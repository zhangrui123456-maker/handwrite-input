using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using HandwriteInput.PoC;

namespace HandwriteInput.PoC.Interop;

internal sealed class KeyboardHook : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;

    public event Action<KeyEvent>? KeyChanged;

    /// <summary>需要全局吞掉（不传递给目标应用）的组合键；null 表示不吞任何键。</summary>
    public Hotkey? SuppressHotkey { get; set; }

    private bool _ctrl, _shift, _alt, _win;

    public KeyboardHook()
    {
        _proc = HookCallback;
    }

    public bool Install()
    {
        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _proc,
            NativeMethods.GetModuleHandle(null),
            0);
        bool ok = _hookId != IntPtr.Zero;
        Debug.WriteLine($"[KeyboardHook] installed hHook=0x{_hookId.ToInt64():X} (0 表示失败)");
        return ok;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN
                      or NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
            {
                var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                bool isDown = msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
                int vk = (int)info.vkCode;

                // 维护原始修饰键状态
                if (KeyNames.IsCtrl(vk)) _ctrl = isDown;
                if (KeyNames.IsShift(vk)) _shift = isDown;
                if (KeyNames.IsAlt(vk)) _alt = isDown;
                if (KeyNames.IsWin(vk)) _win = isDown;

                var evt = new KeyEvent
                {
                    VkCode = vk,
                    IsDown = isDown,
                    Ctrl = _ctrl && !KeyNames.IsCtrl(vk),
                    Shift = _shift && !KeyNames.IsShift(vk),
                    Alt = _alt && !KeyNames.IsAlt(vk),
                    Win = _win && !KeyNames.IsWin(vk),
                };

                KeyChanged?.Invoke(evt);

                if (SuppressHotkey != null && SuppressHotkey.Matches(evt))
                {
                    return (IntPtr)1; // 吞掉组合键主键
                }
            }
        }
        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }
}
