using System;
using System.Drawing;
using System.Windows.Forms;

namespace HandwriteInput.PoC.Interop;

internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;

    public event Action? SettingsRequested;
    public event Action? CalibrationRequested;
    public event Action? ExitRequested;

    public TrayIcon(string? hotkeyText = null)
    {
        var menu = new ContextMenuStrip();
        if (!string.IsNullOrEmpty(hotkeyText))
        {
            menu.Items.Add(new ToolStripMenuItem($"切换键：{hotkeyText}") { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
        }
        menu.Items.Add("设置…", null, (_, _) => SettingsRequested?.Invoke());
        menu.Items.Add("字体适配…", null, (_, _) => CalibrationRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitRequested?.Invoke());

        _icon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "数位板手写输入",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => SettingsRequested?.Invoke();
    }

    /// <summary>优先从本程序 exe 提取内嵌图标，失败回退到系统默认图标。</summary>
    private static Icon LoadAppIcon()
    {
        try
        {
            string? exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exe))
            {
                var icon = Icon.ExtractAssociatedIcon(exe);
                if (icon != null) return icon;
            }
        }
        catch
        {
            // 提取失败走默认图标
        }
        return SystemIcons.Application;
    }

    public void Dispose() => _icon.Dispose();
}
