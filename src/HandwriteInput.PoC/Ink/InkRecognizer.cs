using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Ink;
using Windows.Foundation;
using Windows.UI.Input.Inking;

namespace HandwriteInput.PoC.Ink;

internal static class InkRecognizer
{
    // 识别引擎加载很慢（尤其中文），必须缓存复用，不能每次 new
    private static readonly object Gate = new();
    private static readonly InkStrokeBuilder Builder = new();
    private static InkRecognizerContainer? _container;

    static InkRecognizer()
    {
        Builder.SetDefaultDrawingAttributes(new InkDrawingAttributes
        {
            Color = Windows.UI.Color.FromArgb(255, 0, 0, 0),
            PenTip = PenTipShape.Circle,
            Size = new Size(2, 2),
            IgnorePressure = true,
            FitToCurve = true,
        });
    }

    private static InkRecognizerContainer Container
    {
        get
        {
            lock (Gate)
            {
                return _container ??= new InkRecognizerContainer();
            }
        }
    }

    public static async Task<(string Text, IReadOnlyList<string> Candidates)> RecognizeAsync(StrokeCollection strokes)
    {
        var container = new InkStrokeContainer();
        foreach (var stroke in strokes)
        {
            var pts = stroke.StylusPoints.Select(sp => new Point(sp.X, sp.Y));
            container.AddStroke(Builder.CreateStroke(pts));
        }

        var recognizerContainer = Container;
        var results = await recognizerContainer.RecognizeAsync(container, InkRecognitionTarget.All);

        var candidates = new List<string>();
        foreach (var r in results)
        {
            var c = r.GetTextCandidates();
            candidates.Add(c.Count > 0 ? c[0] : string.Empty);
        }

        string text = JoinAndNormalize(candidates);
        return (text, candidates);
    }

    /// <summary>
    /// 识别器把一行按词/字拆成多个片段，逐段拼接时不能在汉字之间插空格，
    /// 否则「你好世界」会变成「你 好 世 界」。规则：仅当空格两侧都是汉字时去掉该空格；
    /// 汉字与英文/数字之间、英文词之间保留空格。
    /// </summary>
    internal static string JoinAndNormalize(IReadOnlyList<string> parts)
    {
        string joined = string.Join(" ", parts.Where(c => c.Length > 0));
        var sb = new StringBuilder(joined.Length);
        for (int i = 0; i < joined.Length; i++)
        {
            char c = joined[i];
            bool isSpace = c == ' ' || c == '　'; // ASCII 空格或全角空格
            if (isSpace && i > 0 && i + 1 < joined.Length
                && IsCjk(joined[i - 1]) && IsCjk(joined[i + 1]))
            {
                continue; // 去掉两个汉字之间的空格
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static bool IsCjk(char c) =>
        (c >= '㐀' && c <= '䶿') ||   // CJK 扩展 A
        (c >= '一' && c <= '鿿') ||   // CJK 基本区（常用汉字）
        (c >= '豈' && c <= '﫿') ||   // CJK 兼容汉字
        (c >= '぀' && c <= 'ヿ') ||   // 日文假名
        (c >= '가' && c <= '힯') ||   // 韩文音节
        (c >= '　' && c <= '〿');     // CJK 标点（、。，等）
}
