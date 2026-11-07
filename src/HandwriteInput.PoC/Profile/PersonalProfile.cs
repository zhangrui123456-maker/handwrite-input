using System.Collections.Generic;

namespace HandwriteInput.PoC.Profile;

/// <summary>一条「识别错字 → 正确字」的纠正规则，来自校准阶段用户校对。</summary>
public sealed class CorrectionEntry
{
    public string Wrong { get; set; } = "";   // 模型识别出的字（单字）
    public string Right { get; set; } = "";   // 用户本意要的字（单字）
    public int Count { get; set; } = 1;       // 出现次数，越高越可信
}

/// <summary>
/// 个人适配档案。不是重训模型，而是识别后处理用的个人数据：
/// 纠正对（字形错→正确字）、个人词典（常用词/字）、个人语料（留作后续 n-gram 重排）。
/// </summary>
public sealed class PersonalProfile
{
    public int Version { get; set; } = 1;
    public List<CorrectionEntry> Corrections { get; set; } = new();
    public List<string> Lexicon { get; set; } = new();
    public List<string> Corpus { get; set; } = new();
}
