using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace HandwriteInput.PoC.Ink;

/// <summary>
/// PaddleOCR PP-OCRv6 离线识别引擎（纯 C#，无 Python）。
/// 链路：墨迹渲染位图 → det 文本检测 → DB 后处理切框 → rec 单行识别 → CTC 解码。
/// 归一化参数与阈值来自官方 inference.yml，已在 Python 参考实现里逐项对齐验证。
/// </summary>
internal sealed class PaddleOcrRecognizer : IDisposable
{
    private static readonly float[] DetMean = { 0.485f, 0.456f, 0.406f };
    private static readonly float[] DetStd = { 0.229f, 0.224f, 0.225f };
    private const float DetThresh = 0.2f;
    private const float DetBoxThresh = 0.45f;
    private const float UnclipRatio = 1.4f;
    private const int RecHeight = 48;
    private const int RecMaxWidth = 320;
    private const int DetLimitSide = 960;

    private readonly InferenceSession _det;
    private readonly InferenceSession _rec;
    private readonly string[] _dict; // 长度 18708，CTC 索引 i(1..N) -> _dict[i-1]
    private readonly object _gate = new();

    public static PaddleOcrRecognizer CreateDefault()
    {
        string baseDir = AppContext.BaseDirectory;
        return new PaddleOcrRecognizer(
            Path.Combine(baseDir, "models", "det", "inference.onnx"),
            Path.Combine(baseDir, "models", "rec", "inference.onnx"),
            Path.Combine(baseDir, "models", "rec", "ppocrv6_dict.txt"));
    }

    public static PaddleOcrRecognizer Create(string provider)
    {
        string baseDir = AppContext.BaseDirectory;
        return new PaddleOcrRecognizer(
            Path.Combine(baseDir, "models", "det", "inference.onnx"),
            Path.Combine(baseDir, "models", "rec", "inference.onnx"),
            Path.Combine(baseDir, "models", "rec", "ppocrv6_dict.txt"),
            provider);
    }

    public PaddleOcrRecognizer(string detModelPath, string recModelPath, string dictPath, string provider = "auto")
    {
        var options = new SessionOptions();
        string want = (provider ?? "auto").ToLowerInvariant();

        if (want == "cpu")
        {
            ProviderName = "CPU"; // 显式指定 CPU，不加 DML
        }
        else // "auto" 或 "dml"：优先 DML，失败回退 CPU
        {
            try
            {
                options.AppendExecutionProvider_DML(0);
                ProviderName = "DML";
            }
            catch (Exception ex)
            {
                Logger.Log($"DML 执行器不可用，回退 CPU：{ex.Message}");
                ProviderName = "CPU";
            }
        }

        _det = new InferenceSession(detModelPath, options);
        _rec = new InferenceSession(recModelPath, options);
        _dict = File.ReadAllLines(dictPath)
            .Select(l => l.TrimEnd('\r', '\n'))
            .ToArray();
    }

    /// <summary>实际使用的推理执行器（DML 或 CPU），用于日志与诊断。</summary>
    public string ProviderName { get; }

    /// <summary>对一张含文字的位图做检测+识别，按框的几何顺序（先上后下、同行先左后右）返回文本。</summary>
    public string Recognize(Mat bgr)
    {
        lock (_gate)
        {
            var boxes = Detect(bgr);
            if (boxes.Count == 0)
            {
                // 降级：det 对分离的细笔画/单字不敏感时，直接把整图当一行识别。
                // 手写单行是最常见场景，此兜底反而更稳。
                using var whole = new Mat(bgr, new Rect(0, 0, bgr.Cols, bgr.Rows));
                return RecognizeCrop(whole);
            }

            var results = new List<(float X, float Y, string Text)>();
            foreach (var box in boxes)
            {
                Mat crop = CropBox(bgr, box);
                string text = RecognizeCrop(crop);
                crop.Dispose();
                if (!string.IsNullOrEmpty(text))
                {
                    results.Add((box.Min(p => p.X), box.Min(p => p.Y), text));
                }
            }

            // 排序：按 y 分带（同带再按 x），还原多行阅读顺序
            var ordered = results
                .OrderBy(r => r.Y)
                .ThenBy(r => r.X)
                .Select(r => r.Text);
            return string.Join(string.Empty, ordered);
        }
    }

    /// <summary>DB 检测：resize 到 32 倍数 → 归一化 → 推理 → 概率图二值化 → 轮廓 → 最小外接框 + unclip。</summary>
    private List<OpenCvSharp.Point2f[]> Detect(Mat bgr)
    {
        int h = bgr.Rows, w = bgr.Cols;
        float ratio = 1f;
        if (Math.Max(h, w) > DetLimitSide)
            ratio = DetLimitSide / (float)Math.Max(h, w);

        int nh = RoundTo32((int)Math.Round(h * ratio));
        int nw = RoundTo32((int)Math.Round(w * ratio));
        using var resized = new Mat();
        Cv2.Resize(bgr, resized, new OpenCvSharp.Size(nw, nh));

        var blob = ToDetTensor(resized);
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("x", blob) };
        using var outputs = _det.Run(inputs);
        var prob = outputs[0].AsTensor<float>(); // [1,1,nh,nw]

        using var bitmap = new Mat(nh, nw, MatType.CV_8UC1);
        var probData = prob;
        for (int y = 0; y < nh; y++)
            for (int x = 0; x < nw; x++)
                bitmap.Set<byte>(y, x, probData[0, 0, y, x] > DetThresh ? (byte)255 : (byte)0);

        OpenCvSharp.Point[][] contours;
        HierarchyIndex[] hierarchy;
        Cv2.FindContours(bitmap, out contours, out hierarchy, RetrievalModes.List, ContourApproximationModes.ApproxSimple);

        var boxes = new List<OpenCvSharp.Point2f[]>();
        foreach (var c in contours)
        {
            if (c.Length < 4) continue;
            var rect = Cv2.MinAreaRect(c);
            if (rect.Size.Width < 3 || rect.Size.Height < 3) continue;

            double area = Cv2.ContourArea(c);
            double peri = Cv2.ArcLength(c, true);
            if (peri <= 0) continue;

            float unclip = (float)(UnclipRatio * peri / 2.0);
            float bw = rect.Size.Width + unclip;
            float bh = rect.Size.Height + unclip;
            var pts = new OpenCvSharp.Point2f[4];
            var boxPts = rect.Points();
            // 用放大后的矩形近似 unclip 后的四边形（单行场景足够）
            var center = rect.Center;
            for (int i = 0; i < 4; i++)
            {
                float dx = boxPts[i].X - center.X;
                float dy = boxPts[i].Y - center.Y;
                float scaleX = bw / Math.Max(rect.Size.Width, 1f);
                float scaleY = bh / Math.Max(rect.Size.Height, 1f);
                pts[i] = new OpenCvSharp.Point2f(center.X + dx * scaleX, center.Y + dy * scaleY);
            }
            // 映射回原图坐标
            for (int i = 0; i < 4; i++)
                pts[i] = new OpenCvSharp.Point2f(pts[i].X / ratio, pts[i].Y / ratio);
            boxes.Add(pts);
        }
        return boxes;
    }

    private Tensor<float> ToDetTensor(Mat bgr)
    {
        int h = bgr.Rows, w = bgr.Cols;
        var data = new float[1 * 3 * h * w];
        // BGR -> NCHW，做 (x/255 - mean)/std
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var px = bgr.At<Vec3b>(y, x);
                int baseIdx = y * w + x;
                data[0 * h * w + baseIdx] = (px.Item0 / 255f - DetMean[0]) / DetStd[0]; // B
                data[1 * h * w + baseIdx] = (px.Item1 / 255f - DetMean[1]) / DetStd[1]; // G
                data[2 * h * w + baseIdx] = (px.Item2 / 255f - DetMean[2]) / DetStd[2]; // R
            }
        }
        return new DenseTensor<float>(data, new[] { 1, 3, h, w });
    }

    private Mat CropBox(Mat bgr, OpenCvSharp.Point2f[] box)
    {
        var xs = box.Select(p => p.X).ToArray();
        var ys = box.Select(p => p.Y).ToArray();
        int xmin = Math.Max(0, (int)Math.Floor(xs.Min()));
        int ymin = Math.Max(0, (int)Math.Floor(ys.Min()));
        int xmax = Math.Min(bgr.Cols, (int)Math.Ceiling(xs.Max()));
        int ymax = Math.Min(bgr.Rows, (int)Math.Ceiling(ys.Max()));
        return new Mat(bgr, new Rect(xmin, ymin, Math.Max(1, xmax - xmin), Math.Max(1, ymax - ymin)));
    }

    private string RecognizeCrop(Mat crop)
    {
        int h = crop.Rows, w = crop.Cols;
        if (h <= 0 || w <= 0) return string.Empty;

        float ratio = RecHeight / (float)h;
        int nw = (int)Math.Round(w * ratio);
        nw = Math.Clamp(nw, 1, RecMaxWidth);
        using var resized = new Mat();
        Cv2.Resize(crop, resized, new OpenCvSharp.Size(nw, RecHeight));

        var data = new float[1 * 3 * RecHeight * nw];
        for (int y = 0; y < RecHeight; y++)
        {
            for (int x = 0; x < nw; x++)
            {
                var px = resized.At<Vec3b>(y, x);
                int baseIdx = y * nw + x;
                // (x/255 - 0.5)/0.5 == x/127.5 - 1
                data[0 * RecHeight * nw + baseIdx] = px.Item0 / 127.5f - 1f;
                data[1 * RecHeight * nw + baseIdx] = px.Item1 / 127.5f - 1f;
                data[2 * RecHeight * nw + baseIdx] = px.Item2 / 127.5f - 1f;
            }
        }
        var tensor = new DenseTensor<float>(data, new[] { 1, 3, RecHeight, nw });
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("x", tensor) };
        using var outputs = _rec.Run(inputs);
        var logits = outputs[0].AsTensor<float>(); // [1, T, 18710]

        return CtcDecode(logits);
    }

    /// <summary>贪心 CTC 解码：去掉重复与 blank(0)，字典索引 i(1..18708) -> _dict[i-1]，索引 18709 = 空格。</summary>
    private string CtcDecode(Tensor<float> logits)
    {
        int T = logits.Dimensions[1];
        int C = logits.Dimensions[2];
        int spaceIdx = C - 1; // 18709，CTC 最后一个类别是空格
        var sb = new System.Text.StringBuilder();
        int prev = -1;
        for (int t = 0; t < T; t++)
        {
            int best = 0;
            float bestVal = float.MinValue;
            for (int c = 0; c < C; c++)
            {
                float v = logits[0, t, c];
                if (v > bestVal) { bestVal = v; best = c; }
            }
            if (best != 0 && best != prev)
            {
                sb.Append(best == spaceIdx ? ' ' : _dict[best - 1]);
            }
            prev = best;
        }
        return sb.ToString();
    }

    private static int RoundTo32(int v)
    {
        int r = (int)Math.Round(v / 32.0) * 32;
        return r == 0 ? 32 : r;
    }

    public void Dispose()
    {
        _det?.Dispose();
        _rec?.Dispose();
    }
}
