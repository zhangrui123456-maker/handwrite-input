using System.Collections.Generic;
using System.Linq;

namespace HandwriteInput.PoC.Profile;

/// <summary>
/// 识别后处理：用个人档案对识别结果做「逐字纠正 + 词典加权」。
/// 这是「选择性字体适配」的落地——不改模型，用校准阶段收集的纠正对修正用户常写错的字。
/// </summary>
internal static class PersonalAdapter
{
    /// <summary>
    /// 对识别文本套用个人纠正。策略：
    /// 1. 先把文本按字符切分（中文逐字、英文/数字按连续段），逐个命中纠正对。
    /// 2. 命中的字符替换为正确字。
    /// 3. 用个人词典对「同样长度的纠正候选」做加权（当前简化为直接纠正对优先）。
    /// </summary>
    public static string Apply(string text, PersonalProfile profile)
    {
        if (string.IsNullOrEmpty(text) || profile.Corrections.Count == 0)
            return text;

        // 同一「错字」可能有多个正确候选，按出现次数多者优先
        var wrongToRight = new Dictionary<string, string>();
        foreach (var c in profile.Corrections
                     .Where(x => x.Wrong.Length == 1 && x.Right.Length == 1)
                     .OrderByDescending(x => x.Count))
        {
            wrongToRight.TryAdd(c.Wrong, c.Right);
        }

        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var ch in text)
        {
            string s = ch.ToString();
            sb.Append(wrongToRight.TryGetValue(s, out var rep) ? rep : s);
        }
        return sb.ToString();
    }

    /// <summary>从「识别原稿 + 用户校对稿」提取纠正对与词典词，并入档案。</summary>
    public static void LearnCorrections(PersonalProfile profile, string recognized, string corrected)
    {
        if (string.IsNullOrEmpty(recognized) || string.IsNullOrEmpty(corrected))
            return;

        // 按字对齐：两者长度相等时逐字比对；不等时只提取「同位置不同字」的对
        int n = System.Math.Min(recognized.Length, corrected.Length);
        for (int i = 0; i < n; i++)
        {
            char r = recognized[i], c = corrected[i];
            if (r != c && !char.IsWhiteSpace(r) && !char.IsWhiteSpace(c))
            {
                AddOrBump(profile.Corrections, r.ToString(), c.ToString());
            }
        }

        // 词典：把校对稿里非空白的连续词段加入（简单按空白切分）
        foreach (var token in corrected.Split(' ', '\t'))
        {
            var t = token.Trim();
            if (t.Length >= 2 && !profile.Lexicon.Contains(t))
                profile.Lexicon.Add(t);
        }
    }

    private static void AddOrBump(List<CorrectionEntry> list, string wrong, string right)
    {
        var existing = list.FirstOrDefault(x => x.Wrong == wrong && x.Right == right);
        if (existing != null)
        {
            existing.Count++;
        }
        else
        {
            list.Add(new CorrectionEntry { Wrong = wrong, Right = right, Count = 1 });
        }
    }
}
