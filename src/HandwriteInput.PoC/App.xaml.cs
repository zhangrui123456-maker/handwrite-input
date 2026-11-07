using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace HandwriteInput.PoC;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 全局兜底：任何未捕获异常都不再让进程“卡死关闭”，而是记日志并给出可退出提示
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Log("UI 线程未处理异常：" + e.Exception);
        try
        {
            MessageBox.Show("程序发生错误，已记录到日志，程序将继续运行。\n\n" +
                            e.Exception.Message, "数位板手写输入",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch { /* 提示失败不影响主流程 */ }
        e.Handled = true; // 阻止崩溃
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Logger.Log("AppDomain 未处理异常：" + (e.ExceptionObject as Exception)?.ToString());
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Logger.Log("未观察的任务异常：" + e.Exception);
        e.SetObserved();
    }
}
