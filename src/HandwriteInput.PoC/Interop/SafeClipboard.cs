using System;
using System.Threading.Tasks;
using System.Windows;

namespace HandwriteInput.PoC.Interop;

/// <summary>
/// 带读回验证的剪贴板写入。
/// Windows 11 的「剪贴板历史/云同步」后台进程会短暂锁住剪贴板：
/// 此时 Clipboard.SetText 可能抛 CLIPBRD_E_CANT_OPEN(0x800401D0)，但数据其实已经写入。
/// 因此这里不以“是否抛异常”作为成功标准，而以“读回内容 == 目标文本”为准。
///
/// 为了不阻塞识别后的主流程，采用「先写一次、失败再后台重试」：
/// 当前线程直接写一次，读回一致即成功；否则排到 UI 线程后台再试若干次，调用方立即返回。
/// </summary>
internal static class SafeClipboard
{
    private const int RetryCount = 5;
    private const int RetryDelayMs = 120;

    /// <summary>写入并读回验证。先写一次；失败则在后台（UI 线程）重试，不阻塞调用方。</summary>
    public static void SetTextBackground(string text)
    {
        if (TrySet(text)) return;
        _ = RetryOnDispatcherAsync(text);
    }

    /// <summary>写一次 + 读回验证。返回“剪贴板里现在是否就是这段文字”。</summary>
    private static bool TrySet(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            Logger.Log($"SafeClipboard.SetText 抛异常: {ex.GetType().Name}: {ex.Message}");
            // 抛异常也可能已写入，继续读回验证，不立即判失败
        }

        return TryReadBack(text);
    }

    private static bool TryReadBack(string expected)
    {
        try
        {
            string? read = Clipboard.GetText();
            return string.Equals(expected, read, StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            Logger.Log($"SafeClipboard 读回异常: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 在 UI 线程后台重试（剪贴板是 STA 组件，必须在 STA/UI 线程操作）。
    /// 每轮间隔约 120ms，最多再试 5 次。
    /// </summary>
    private static async Task RetryOnDispatcherAsync(string text)
    {
        var dispatcher = Application.Current?.Dispatcher;
        for (int i = 1; i <= RetryCount; i++)
        {
            try
            {
                await Task.Delay(RetryDelayMs);
            }
            catch
            {
                return;
            }

            bool ok;
            if (dispatcher != null && !dispatcher.CheckAccess())
                ok = await dispatcher.InvokeAsync(() => TrySet(text));
            else
                ok = TrySet(text);

            if (ok)
            {
                Logger.Log($"SafeClipboard 后台重试第 {i} 次写入成功");
                return;
            }
            Logger.Log($"SafeClipboard 后台重试第 {i} 次仍未写入");
        }
        Logger.Log($"SafeClipboard 后台重试最终失败（{RetryCount} 次），剪贴板持续被占用");
    }
}
