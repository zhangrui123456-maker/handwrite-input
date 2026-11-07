using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using HandwriteInput.PoC.Interop;

namespace HandwriteInput.PoC.Ink;

internal static class TextPositionProbe
{
    public static string ProbeAtCursor()
    {
        var lines = new List<string>();
        NativeMethods.GetCursorPos(out var p);
        lines.Add($"cursor=({p.X},{p.Y})");

        var hwnd = NativeMethods.WindowFromPoint(p);
        lines.Add($"hwnd=0x{hwnd.ToInt64():X}");

        try
        {
            var el = AutomationElement.FromPoint(new Point(p.X, p.Y));
            if (el == null)
            {
                lines.Add("AutomationElement.FromPoint -> null");
                return string.Join("\n", lines);
            }

            lines.Add($"el: {el.Current.ControlType.ProgrammaticName} | name='{el.Current.Name}' | class='{el.Current.ClassName}' | enabled={el.Current.IsEnabled}");

            var textEl = FindTextPattern(el);
            if (textEl == null)
            {
                lines.Add("未找到 TextPattern 元素，尝试 EM_CHARFROMPOS（Win32 Edit）");
                long caret = NativeMethods.SendMessage(hwnd, NativeMethods.EM_CHARFROMPOS, IntPtr.Zero, ref p).ToInt64();
                lines.Add($"EM_CHARFROMPOS -> 字符索引={caret & 0xFFFF}  行={caret >> 16}");
                return string.Join("\n", lines);
            }

            var tp = textEl.GetCurrentPattern(TextPattern.Pattern) as TextPattern;
            var range = tp!.RangeFromPoint(new Point(p.X, p.Y));
            if (range == null)
            {
                lines.Add("RangeFromPoint -> null");
                return string.Join("\n", lines);
            }

            string snippet = range.GetText(30)
                .Replace("\r", "⏎").Replace("\n", "⏎").Replace("\t", "→");
            lines.Add($"RangeFromPoint 文本: \"{snippet}\"");

            var rects = range.GetBoundingRectangles();
            if (rects.Length > 0)
            {
                var r = rects[0];
                lines.Add($"定位竖线坐标: x={r.X:F0} y={r.Y:F0} w={r.Width:F0} h={r.Height:F0}");
                lines.Add("=> 可在该 x,y 处画竖线（L1 可行性验证通过）");
            }
            else
            {
                lines.Add("GetBoundingRectangles 为空（该控件不支持精确光标定位）");
            }
        }
        catch (Exception ex)
        {
            lines.Add($"探测异常: {ex.GetType().Name}: {ex.Message}");
        }

        return string.Join("\n", lines);
    }

    private static AutomationElement? FindTextPattern(AutomationElement start)
    {
        var el = start;
        for (int i = 0; i < 10 && el != null; i++)
        {
            if (el.TryGetCurrentPattern(TextPattern.Pattern, out _))
            {
                return el;
            }
            el = TreeWalker.ControlViewWalker.GetParent(el);
        }
        return null;
    }
}
