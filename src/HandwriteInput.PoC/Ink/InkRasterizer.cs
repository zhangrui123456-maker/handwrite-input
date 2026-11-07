using System.Windows;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ocv = OpenCvSharp;

namespace HandwriteInput.PoC.Ink;

/// <summary>
/// 把 WPF 笔迹（StrokeCollection）栅格化成 OpenCvSharp 位图，
/// 供 PaddleOCR 检测+识别。白底黑字，与识别模型的训练输入一致。
/// </summary>
internal static class InkRasterizer
{
    /// <summary>
    /// 渲染笔迹为 BGR 位图（白底黑字）。只裁到笔迹包围盒 + 内边距，
    /// 避免整屏渲染；超出上限时等比缩小。无有效笔迹返回 null。
    /// </summary>
    public static Ocv.Mat? RenderStrokesToMat(StrokeCollection strokes)
    {
        var bounds = strokes.GetBounds();
        if (bounds.IsEmpty || bounds.Width < 1 || bounds.Height < 1)
            return null;

        const int pad = 16;
        const double maxContent = 1968.0;

        double scale = 1.0;
        double maxSide = Math.Max(bounds.Width, bounds.Height);
        if (maxSide > maxContent)
            scale = maxContent / maxSide;

        int width = (int)Math.Ceiling(bounds.Width * scale + 2 * pad);
        int height = (int)Math.Ceiling(bounds.Height * scale + 2 * pad);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));

            var tg = new TransformGroup();
            tg.Children.Add(new ScaleTransform(scale, scale));
            tg.Children.Add(new TranslateTransform(pad - bounds.Left * scale, pad - bounds.Top * scale));
            dc.PushTransform(tg);

            foreach (var stroke in strokes)
            {
                var da = stroke.DrawingAttributes.Clone();
                da.Color = Colors.Black; // 确保黑色笔迹，白底
                stroke.Draw(dc, da);
            }
            dc.Pop();
        }

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        int stride = width * 4;
        var pixels = new byte[height * stride];
        rtb.CopyPixels(pixels, stride, 0);

        var mat4 = Ocv.Mat.FromPixelData(height, width, Ocv.MatType.CV_8UC4, pixels);
        var bgr = new Ocv.Mat();
        Ocv.Cv2.CvtColor(mat4, bgr, Ocv.ColorConversionCodes.BGRA2BGR);
        mat4.Dispose();
        return bgr;
    }
}
