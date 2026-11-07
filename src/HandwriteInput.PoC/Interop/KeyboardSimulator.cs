using System;
using System.Runtime.InteropServices;

namespace HandwriteInput.PoC.Interop;

/// <summary>
/// 用 SendInput 模拟键盘输入，以及前台窗口/焦点窗口的捕获与恢复。
/// 注意：INPUT 结构的 union 必须包含最大的 MOUSEINPUT 成员，
/// 否则 64 位下结构体大小错误，SendInput 会失败（这是“自动粘贴不生效”的根因）。
/// </summary>
internal static class KeyboardSimulator
{
    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_V = 0x56;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION U;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint cInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    /// <summary>捕获当前前台窗口与其线程内的焦点窗口，用于书写后精确恢复。</summary>
    public struct FocusState
    {
        public IntPtr ForegroundWindow;
        public IntPtr FocusWindow;
    }

    private static INPUT Key(ushort vk, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        U = new INPUTUNION
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = 0,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            }
        }
    };

    /// <summary>模拟 Ctrl+V，把当前剪贴板内容粘贴到当前焦点光标处。</summary>
    public static void PressCtrlV()
    {
        var inputs = new[]
        {
            Key(VK_CONTROL, 0),
            Key(VK_V, 0),
            Key(VK_V, KEYEVENTF_KEYUP),
            Key(VK_CONTROL, KEYEVENTF_KEYUP),
        };
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent == 0)
        {
            Logger.Log($"SendInput 失败，GetLastError={Marshal.GetLastWin32Error()}");
        }
    }

    public static FocusState CaptureFocus()
    {
        var state = new FocusState { ForegroundWindow = GetForegroundWindow() };
        if (state.ForegroundWindow != IntPtr.Zero)
        {
            uint tid = GetWindowThreadProcessId(state.ForegroundWindow, out _);
            var gti = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
            if (GetGUIThreadInfo(tid, ref gti))
            {
                state.FocusWindow = gti.hwndFocus;
            }
        }
        return state;
    }

    public static void RestoreFocus(FocusState state)
    {
        if (state.ForegroundWindow == IntPtr.Zero) return;
        try
        {
            uint targetThread = GetWindowThreadProcessId(state.ForegroundWindow, out _);
            uint currentThread = GetCurrentThreadId();

            if (targetThread != currentThread)
                AttachThreadInput(currentThread, targetThread, true);

            SetForegroundWindow(state.ForegroundWindow);

            if (state.FocusWindow != IntPtr.Zero)
                SetFocus(state.FocusWindow);

            if (targetThread != currentThread)
                AttachThreadInput(currentThread, targetThread, false);
        }
        catch
        {
            // 失败不致命
        }
    }
}
