using System.Windows.Input;

namespace HandwriteInput.PoC;

internal static class KeyNames
{
    public static string Name(int vk) => vk switch
    {
        0x20 => "空格 Space",
        0x11 => "Ctrl",
        0x10 => "Shift",
        0x12 => "Alt",
        0x14 => "CapsLock",
        0x09 => "Tab",
        0x1B => "Esc",
        0x0D => "回车 Enter",
        0x71 => "F2",
        0x72 => "F3",
        0x73 => "F4",
        0x74 => "F5",
        0x75 => "F6",
        0x76 => "F7",
        0x77 => "F8",
        0x78 => "F9",
        0x79 => "F10",
        _ => KeyInterop.KeyFromVirtualKey(vk).ToString(),
    };

    public static bool IsCtrl(int vk) => vk is 0x11 or 0xA2 or 0xA3;
    public static bool IsShift(int vk) => vk is 0x10 or 0xA0 or 0xA1;
    public static bool IsAlt(int vk) => vk is 0x12 or 0xA4 or 0xA5;
    public static bool IsWin(int vk) => vk is 0x5B or 0x5C;
}
