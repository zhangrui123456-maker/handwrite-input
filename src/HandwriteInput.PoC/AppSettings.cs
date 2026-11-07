using System;
using System.IO;
using System.Text.Json;

namespace HandwriteInput.PoC;

public sealed class AppSettings
{
    public int ToggleKeyCode { get; set; } = 0x20; // VK_SPACE
    public bool Ctrl { get; set; }
    public bool Shift { get; set; }
    public bool Alt { get; set; }
    public bool Win { get; set; }
    public bool HoldMode { get; set; } = true;     // true=按住书写/松开识别，false=按一下切换
    public bool AutoPaste { get; set; } = true;     // 默认：识别后自动粘贴到光标处
    public string Provider { get; set; } = "auto";  // 推理执行器：auto=优先DML回退CPU，dml=强制DML(GPU)，cpu=强制CPU
    public string InkColor { get; set; } = "#142E7A"; // 书写墨迹颜色（识别栅格化时仍强制转黑，不影响模型）
    public double InkThickness { get; set; } = 4.0;  // 书写墨迹粗细（像素）
    public string StatusTheme { get; set; } = "light"; // 顶部提示配色：light=浅色白底，dark=半透明黑底

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HandwriteInput", "Config");

    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var s = JsonSerializer.Deserialize<AppSettings>(json);
                if (s != null) return s;
            }
        }
        catch
        {
            // 读取失败则回退默认值
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 保存失败不致命
        }
    }
}
