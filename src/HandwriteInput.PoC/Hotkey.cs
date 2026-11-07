using System.Collections.Generic;

namespace HandwriteInput.PoC;

/// <summary>一次键盘事件的快照；修饰键状态表示「除当前键自身外的已按下修饰键」。</summary>
public readonly struct KeyEvent
{
    public int VkCode { get; init; }
    public bool IsDown { get; init; }
    public bool Ctrl { get; init; }
    public bool Shift { get; init; }
    public bool Alt { get; init; }
    public bool Win { get; init; }
}

/// <summary>一个组合快捷键：修饰键 + 主键。</summary>
public sealed class Hotkey
{
    public int KeyCode { get; set; }
    public bool Ctrl { get; set; }
    public bool Shift { get; set; }
    public bool Alt { get; set; }
    public bool Win { get; set; }

    public static Hotkey FromSettings(AppSettings s) => new()
    {
        KeyCode = s.ToggleKeyCode,
        Ctrl = s.Ctrl,
        Shift = s.Shift,
        Alt = s.Alt,
        Win = s.Win,
    };

    /// <summary>精确匹配：主键一致且修饰键完全一致（多余修饰键不算匹配）。</summary>
    public bool Matches(KeyEvent e) =>
        e.VkCode == KeyCode && e.Ctrl == Ctrl && e.Shift == Shift && e.Alt == Alt && e.Win == Win;

    public string Format()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Shift) parts.Add("Shift");
        if (Alt) parts.Add("Alt");
        if (Win) parts.Add("Win");
        parts.Add(KeyNames.Name(KeyCode));
        return string.Join(" + ", parts);
    }
}
