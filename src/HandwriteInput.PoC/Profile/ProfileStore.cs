using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace HandwriteInput.PoC.Profile;

/// <summary>个人适配档案的读写，存放于 %APPDATA%\HandwriteInput\Profile\profile.json。</summary>
internal static class ProfileStore
{
    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HandwriteInput", "Profile");

    private static string FilePath => Path.Combine(Dir, "profile.json");

    public static PersonalProfile Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var p = JsonSerializer.Deserialize<PersonalProfile>(json);
                if (p != null) return p;
            }
        }
        catch
        {
            // 读取失败回退空档案
        }
        return new PersonalProfile();
    }

    public static void Save(PersonalProfile profile)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // 保存失败不致命
        }
    }
}
