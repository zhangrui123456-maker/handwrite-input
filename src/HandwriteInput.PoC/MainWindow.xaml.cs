using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HandwriteInput.PoC.Ink;
using HandwriteInput.PoC.Interop;

namespace HandwriteInput.PoC;

public partial class MainWindow : Window
{
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    private const int VK_F8 = 0x77;
    private const int VK_F9 = 0x78;
    private const int VK_F10 = 0x79;

    private readonly KeyboardHook _hook = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly Profile.PersonalProfile _profile = Profile.ProfileStore.Load();
    private TrayIcon? _tray;
    private Ink.PaddleOcrRecognizer? _ocr;
    private bool _writing;
    private bool _keyDown;
    private int _strokeCount;
    private int _writingGeneration; // 书写代次：每次进入书写态 +1，用于识别完成后不误清新笔画
    private KeyboardSimulator.FocusState _focusBeforeWriting;

    private Hotkey ToggleHotkey => Hotkey.FromSettings(_settings);

    private Ink.PaddleOcrRecognizer Ocr => _ocr ??= Ink.PaddleOcrRecognizer.Create(_settings.Provider);

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;

        _hook.KeyChanged += OnKeyChanged;
        ApplyInkSettings();

        InkSurface.StrokeCollected += (_, _) =>
        {
            _strokeCount = InkSurface.Strokes.Count;
            Logger.Log($"笔迹采集 StrokeCollected count={_strokeCount} writing={_writing}");
            if (_writing)
            {
                UpdateStatus($"书写中… 笔迹 {_strokeCount} 条  （松开「{ToggleHotkey.Format()}」识别）",
                    Color.FromRgb(0x1B, 0x5E, 0x20));
            }
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        _hook.SuppressHotkey = ToggleHotkey;
        bool hookOk = _hook.Install();
        SetClickThrough(true);

        _tray = new TrayIcon(ToggleHotkey.Format());
        _tray.SettingsRequested += ShowSettings;
        _tray.CalibrationRequested += ShowCalibration;
        _tray.ExitRequested += () => Close();

        Logger.Log($"启动成功 hookInstalled={hookOk} toggleKey=0x{_settings.ToggleKeyCode:X} " +
                   $"mods=C{_settings.Ctrl}S{_settings.Shift}A{_settings.Alt}W{_settings.Win} holdMode={_settings.HoldMode} trayCreated=true");

        UpdateStatus(DefaultHint());

        if (Environment.GetCommandLineArgs().Contains("--selftest", StringComparer.OrdinalIgnoreCase))
        {
            _ = RunSelfTestAndExitAsync();
        }

        if (Environment.GetCommandLineArgs().Contains("--ocr-selftest", StringComparer.OrdinalIgnoreCase))
        {
            _ = RunOcrSelfTestAndExitAsync();
        }

        if (Environment.GetCommandLineArgs().Contains("--raster-selftest", StringComparer.OrdinalIgnoreCase))
        {
            _ = RunRasterSelfTestAndExitAsync();
        }

        if (Environment.GetCommandLineArgs().Contains("--profile-selftest", StringComparer.OrdinalIgnoreCase))
        {
            _ = RunProfileSelfTestAndExitAsync();
        }
    }

    /// <summary>验证个人适配层：纠正对提取 + 逐字纠正逻辑（纯逻辑，不依赖识别引擎）。</summary>
    private async Task RunProfileSelfTestAndExitAsync()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[PROFILE-SELFTEST]");
        var failures = new List<string>();
        try
        {
            var profile = new Profile.PersonalProfile();

            // 1. 学习：识别「你好世用」校对成「你好世界」→ 应学到 用→界
            Profile.PersonalAdapter.LearnCorrections(profile, "你好世用", "你好世界");
            if (profile.Corrections.Count != 1 ||
                profile.Corrections[0].Wrong != "用" ||
                profile.Corrections[0].Right != "界")
            {
                failures.Add($"学习纠正对失败：{string.Join("|", profile.Corrections.Select(c => c.Wrong + "->" + c.Right))}");
            }

            // 2. 应用：识别「你好世用」→ 应纠正为「你好世界」
            string applied = Profile.PersonalAdapter.Apply("你好世用", profile);
            if (applied != "你好世界")
            {
                failures.Add($"纠正失败：期望'你好世界' 实际'{applied}'");
            }

            // 3. 词典：无空格文本按整体 token 存入（中文分词留待后续，当前词典按空白切分）
            if (!profile.Lexicon.Contains("你好世界"))
            {
                failures.Add($"词典提取失败：{string.Join("|", profile.Lexicon)}");
            }

            // 4. 空档案不应改变文本
            var empty = new Profile.PersonalProfile();
            if (Profile.PersonalAdapter.Apply("你好世界", empty) != "你好世界")
            {
                failures.Add("空档案不应改变文本");
            }

            // 5. 同字多候选按次数优先
            var multi = new Profile.PersonalProfile();
            multi.Corrections.Add(new Profile.CorrectionEntry { Wrong = "用", Right = "界", Count = 1 });
            multi.Corrections.Add(new Profile.CorrectionEntry { Wrong = "用", Right = "解", Count = 5 });
            if (Profile.PersonalAdapter.Apply("用", multi) != "解")
            {
                failures.Add("多候选应按次数优先取'解'");
            }

            sb.AppendLine(failures.Count == 0 ? "OK" : $"FAIL ({failures.Count} 处)");
            foreach (var f in failures) sb.AppendLine("  " + f);
        }
        catch (Exception ex)
        {
            sb.AppendLine("EXCEPTION: " + ex);
        }
        finally
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(AppContext.BaseDirectory, "profile-selftest.log"),
                sb.ToString());
            await Task.Delay(200);
            Application.Current.Shutdown();
        }
    }

    /// <summary>
    /// 验证「WPF 笔迹 → 栅格化位图 → PaddleOCR」整条链路（即主流程路径）。
    /// 用合成的汉字笔画（十、三）构造 StrokeCollection，栅格化后识别并保存 PNG 供人工核对。
    /// </summary>
    private async Task RunRasterSelfTestAndExitAsync()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[RASTER-SELFTEST]");
        try
        {
            string outDir = AppContext.BaseDirectory;
            var cases = new (string Name, StrokeCollection Strokes)[]
            {
                ("shi", MakeShapeStrokes("shi")),
                ("san", MakeShapeStrokes("san")),
            };

            foreach (var (name, strokes) in cases)
            {
                using var mat = Ink.InkRasterizer.RenderStrokesToMat(strokes);
                if (mat is null)
                {
                    sb.AppendLine($"{name} -> 栅格化返回 null");
                    continue;
                }
                string png = System.IO.Path.Combine(outDir, $"raster-{name}.png");
                OpenCvSharp.Cv2.ImWrite(png, mat);
                string text = Ocr.Recognize(mat);
                sb.AppendLine($"{name} -> '{text}'  (bmp={mat.Width}x{mat.Rows}, saved {System.IO.Path.GetFileName(png)})");
            }
            sb.AppendLine("[RASTER-SELFTEST] DONE");
        }
        catch (Exception ex)
        {
            sb.AppendLine("FAIL: " + ex);
        }
        finally
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(AppContext.BaseDirectory, "raster-selftest.log"),
                sb.ToString());
            await Task.Delay(200);
            Application.Current.Shutdown();
        }
    }

    /// <summary>合成可识别汉字的大笔画：十（横+竖）、三（三条横）。</summary>
    private static StrokeCollection MakeShapeStrokes(string which)
    {
        var da = new DrawingAttributes { Color = Colors.Black, Width = 6, Height = 6 };
        var strokes = new StrokeCollection();
        if (which == "shi")
        {
            strokes.Add(new Stroke(new StylusPointCollection(new[] { new Point(30, 60), new Point(190, 60) }), da));
            strokes.Add(new Stroke(new StylusPointCollection(new[] { new Point(110, 20), new Point(110, 100) }), da));
        }
        else // san: 三条横线
        {
            strokes.Add(new Stroke(new StylusPointCollection(new[] { new Point(30, 40), new Point(190, 40) }), da));
            strokes.Add(new Stroke(new StylusPointCollection(new[] { new Point(30, 80), new Point(190, 80) }), da));
            strokes.Add(new Stroke(new StylusPointCollection(new[] { new Point(30, 120), new Point(190, 120) }), da));
        }
        return strokes;
    }

    private async Task RunOcrSelfTestAndExitAsync()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[OCR-SELFTEST]");
        try
        {
            string baseDir = AppContext.BaseDirectory;
            string imgDir = System.IO.Path.Combine(baseDir, "models", "testimg");
            using var rec = new Ink.PaddleOcrRecognizer(
                System.IO.Path.Combine(baseDir, "models", "det", "inference.onnx"),
                System.IO.Path.Combine(baseDir, "models", "rec", "inference.onnx"),
                System.IO.Path.Combine(baseDir, "models", "rec", "ppocrv6_dict.txt"),
                _settings.Provider);
            sb.AppendLine($"provider={rec.ProviderName}");

            var files = System.IO.Directory.GetFiles(imgDir, "*.png")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
            {
                using var mat = OpenCvSharp.Cv2.ImRead(f, OpenCvSharp.ImreadModes.Color);
                string text = rec.Recognize(mat);
                sb.AppendLine($"{System.IO.Path.GetFileName(f)} -> '{text}'");
            }
            sb.AppendLine("[OCR-SELFTEST] DONE");
        }
        catch (Exception ex)
        {
            sb.AppendLine("FAIL: " + ex);
        }
        finally
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(AppContext.BaseDirectory, "ocr-selftest.log"),
                sb.ToString());
            await Task.Delay(200);
            Application.Current.Shutdown();
        }
    }

    private async Task RunSelfTestAndExitAsync()
    {
        try
        {
            var (text, candidates) = await InkRecognizer.RecognizeAsync(SelfTestStrokes());
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(AppContext.BaseDirectory, "selftest.log"),
                $"[SELFTEST] OK\ntext='{text}'\ncandidates={string.Join(" | ", candidates)}");
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(AppContext.BaseDirectory, "selftest.log"),
                $"[SELFTEST] FAIL\n{ex.GetType().Name}: {ex.Message}\n{ex}");
        }
        finally
        {
            RunNormalizeSelfTest();
            await Task.Delay(200);
            Application.Current.Shutdown();
        }
    }

    /// <summary>
    /// 验证多字/中英混合拼接规则（纯逻辑，不依赖识别引擎）：
    /// 汉字之间不插空格，中英之间、英文词之间保留空格。
    /// </summary>
    private static bool RunNormalizeSelfTest()
    {
        var cases = new (string[] Input, string Expected)[]
        {
            (new[] { "你", "好", "世", "界" }, "你好世界"),         // 单字拆分 → 无空格
            (new[] { "你好", "世界" }, "你好世界"),                 // 按词拆分 → 无空格
            (new[] { "hello", "world" }, "hello world"),            // 英文词 → 保留空格
            (new[] { "你", "好", "world", "123" }, "你好 world 123"), // 中英数字混合
            (new[] { "A", "B", "C" }, "A B C"),                     // 单个英文字母 → 保留空格
            (new[] { "你好", "世界", "2026" }, "你好世界 2026"),      // 中文+数字
            (new[] { "写", "test", "好" }, "写 test 好"),           // 中文夹英文
        };

        var failures = new List<string>();
        foreach (var (input, expected) in cases)
        {
            string got = Ink.InkRecognizer.JoinAndNormalize(input);
            if (got != expected)
            {
                failures.Add($"  in=[{string.Join("|", input)}] expected='{expected}' got='{got}'");
            }
        }

        var header = $"[NORMALIZE-SELFTEST] {(failures.Count == 0 ? "OK" : $"FAIL ({failures.Count} 处不一致)")}";
        var body = string.Join("\n", failures);
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(AppContext.BaseDirectory, "normalize-selftest.log"),
            header + (body.Length > 0 ? "\n" + body : ""));

        Logger.Log(header + (failures.Count > 0 ? $"\n{body}" : ""));
        return failures.Count == 0;
    }

    private string DefaultHint()
    {
        string mode = _settings.HoldMode ? "按住书写，松开识别" : "按一下开始，再按一下结束并识别";
        return $"书写切换键：【{ToggleHotkey.Format()}】（{mode}）  |  F10 设置  |  F8 探测竖线  |  右下角托盘图标可设置/退出";
    }

    private void ApplyInkSettings()
    {
        // 墨迹外观：从设置读取颜色与粗细（识别栅格化时仍会强制转黑，不影响模型输入）
        Color ink;
        try
        {
            ink = (Color)ColorConverter.ConvertFromString(_settings.InkColor);
        }
        catch
        {
            ink = Color.FromRgb(0x14, 0x2E, 0x7A);
        }

        double thick = _settings.InkThickness;
        if (thick <= 0 || thick > 30) thick = 4.0;

        InkSurface.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = ink,
            Width = thick,
            Height = thick,
            FitToCurve = true,
        };
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _hook.Dispose();
        _tray?.Dispose();
        _ocr?.Dispose();
    }

    private void OnKeyChanged(KeyEvent e)
    {
        bool noMods = !e.Ctrl && !e.Shift && !e.Alt && !e.Win;

        if (e.IsDown && noMods && e.VkCode == VK_F10)
        {
            Dispatcher.BeginInvoke(new Action(ShowSettings));
            return;
        }
        if (e.IsDown && noMods && e.VkCode == VK_F8)
        {
            Dispatcher.BeginInvoke(new Action(ShowProbe));
            return;
        }
        if (e.IsDown && noMods && e.VkCode == VK_F9)
        {
            Dispatcher.BeginInvoke(new Action(async () => await RunSelfTestAsync()));
            return;
        }

        if (e.VkCode == ToggleHotkey.KeyCode)
        {
            Dispatcher.BeginInvoke(new Action(() => HandleToggleKey(e)));
        }
    }

    private void HandleToggleKey(KeyEvent e)
    {
        bool primaryDown = e.IsDown;
        bool primaryUp = !e.IsDown;
        bool fullMatch = ToggleHotkey.Matches(e);

        if (_settings.HoldMode)
        {
            // 按住模式：完整组合按下 → 进入；主键抬起 → 退出（不要求修饰键仍在，容错先松修饰键的情况）
            if (fullMatch && primaryDown && !_keyDown)
            {
                _keyDown = true;
                EnterWritingMode();
            }
            else if (primaryUp)
            {
                _keyDown = false;
                ExitWritingMode();
            }
        }
        else
        {
            // 锁定模式：完整组合按下 → 切换；主键抬起 → 复位
            if (fullMatch && primaryDown && !_keyDown)
            {
                _keyDown = true;
                if (_writing) ExitWritingMode();
                else EnterWritingMode();
            }
            else if (primaryUp)
            {
                _keyDown = false;
            }
        }
    }

    private void EnterWritingMode()
    {
        if (_writing) return;
        _writing = true;
        _writingGeneration++;
        _strokeCount = 0;
        _focusBeforeWriting = KeyboardSimulator.CaptureFocus();
        // 停止可能还在运行的淡出动画，恢复墨迹完全不透明
        InkSurface.BeginAnimation(OpacityProperty, null);
        InkSurface.Opacity = 1.0;
        InkSurface.Strokes.Clear();
        SetClickThrough(false);
        Logger.Log($"进入书写模式 gen={_writingGeneration}（前台=0x{_focusBeforeWriting.ForegroundWindow.ToInt64():X} 焦点=0x{_focusBeforeWriting.FocusWindow.ToInt64():X}）");
        UpdateStatus($"书写中… 笔迹 0 条  （松开「{ToggleHotkey.Format()}」识别）",
            Color.FromRgb(0x1B, 0x5E, 0x20));
    }

    private void ExitWritingMode()
    {
        if (!_writing) return;
        _writing = false;
        SetClickThrough(true);
        var strokes = new StrokeCollection(InkSurface.Strokes);
        int gen = _writingGeneration;
        Logger.Log($"退出书写模式 gen={gen}，笔迹 {strokes.Count} 条");
        UpdateStatus("识别中…");
        _ = RecognizeAndShowAsync(strokes, gen);
    }

    private async Task RecognizeAndShowAsync(StrokeCollection strokes, int generation)
    {
        try
        {
            if (strokes.Count == 0)
            {
                UpdateStatus("未采集到笔迹。请在书写态（按住切换键）用鼠标/数位板书写。");
                return;
            }

            // 栅格化 + ONNX 推理较重，放后台线程，避免卡 UI
            var sw = Stopwatch.StartNew();
            string text = await Task.Run(() =>
            {
                using var mat = InkRasterizer.RenderStrokesToMat(strokes);
                if (mat is null) return string.Empty;
                return Ocr.Recognize(mat);
            });

            // 套用个人适配层：纠正用户常写错的字
            string raw = text;
            text = Profile.PersonalAdapter.Apply(text, _profile);
            if (text != raw)
            {
                Logger.Log($"适配层纠正：'{raw}' -> '{text}'");
            }

            // 墨迹先淡出再清空，形成「漂浮字迹」的收尾观感。
            // 但若这期间用户已开始新一轮书写（代次变了），不能清空 —— 会误清新笔画。
            FadeOutInk(generation);
            Logger.Log($"识别完成 gen={generation} text='{text}' 耗时={sw.ElapsedMilliseconds}ms");

            if (string.IsNullOrWhiteSpace(text))
            {
                UpdateStatus("识别结果为空（可再写清楚一点重试）");
                return;
            }

            // 剪贴板写入：先写一次、失败后台重试，不阻塞主流程（与淡出动画并行）。
            // 无论是否自动粘贴都写入（手动 Ctrl+V 依赖它）。
            SafeClipboard.SetTextBackground(text);
            Logger.Log($"识别后文字已入剪贴板 text='{text}'");

            // 识别文字统一在顶部状态栏显示，不再弹文字卡片
            if (_settings.AutoPaste)
            {
                UpdateStatus($"识别结果：{text}（已自动粘贴）");
                AutoPaste();
            }
            else
            {
                UpdateStatus($"识别结果：{text}（已复制，可手动 Ctrl+V）");
            }
        }
        catch (Exception ex)
        {
            // 清空也要尊重代次：识别失败的旧墨迹若已被新一轮书写覆盖，不能清空
            if (generation == _writingGeneration)
            {
                InkSurface.Strokes.Clear();
            }
            Logger.Log("识别失败：" + ex);
            UpdateStatus("识别失败：" + ex.Message);
            Debug.WriteLine(ex.ToString());
        }
    }

    /// <summary>让墨迹先淡出再清空（约 260ms）。若期间已进入新一轮书写，则不淡出、不清空。</summary>
    private void FadeOutInk(int generation)
    {
        // 若当前已经进入新一轮书写（代次已变），本次识别的墨迹早已被 EnterWritingMode 清掉，
        // 不该再去动 InkSurface，否则会把新笔画淡出/清空。
        if (generation != _writingGeneration || InkSurface.Strokes.Count == 0)
        {
            return;
        }

        var anim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
        };
        anim.Completed += (_, _) =>
        {
            // 动画结束再校验一次代次：240ms 内用户也可能开始新一轮书写
            if (generation == _writingGeneration)
            {
                InkSurface.Strokes.Clear();
            }
            InkSurface.Opacity = 1.0;
        };
        InkSurface.BeginAnimation(OpacityProperty, anim);
    }

    private void AutoPaste()
    {
        // 恢复书写前的前台窗口和焦点，再模拟 Ctrl+V
        KeyboardSimulator.RestoreFocus(_focusBeforeWriting);
        // 给前台切换一点时间
        var t = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200),
        };
        t.Tick += (_, _) =>
        {
            t.Stop();
            KeyboardSimulator.PressCtrlV();
            Logger.Log("自动粘贴完成");
        };
        t.Start();
    }

    private async Task RunSelfTestAsync()
    {
        try
        {
            var (text, candidates) = await InkRecognizer.RecognizeAsync(SelfTestStrokes());
            UpdateStatus($"F9 自测：text='{text}'  candidates={string.Join(" | ", candidates)}");
        }
        catch (Exception ex)
        {
            UpdateStatus("F9 自测失败：" + ex.Message);
        }
    }

    private static StrokeCollection SelfTestStrokes()
    {
        var horizontal = new StylusPointCollection(new[] { new Point(20, 50), new Point(180, 50) });
        var vertical = new StylusPointCollection(new[] { new Point(100, 10), new Point(100, 90) });
        return new StrokeCollection { new Stroke(horizontal), new Stroke(vertical) };
    }

    private void ShowProbe()
    {
        UpdateStatus("定位探测：\n" + TextPositionProbe.ProbeAtCursor());
    }

    private void ShowSettings()
    {
        var w = new SettingsWindow(_settings) { Owner = this };
        if (w.ShowDialog() == true)
        {
            _hook.SuppressHotkey = ToggleHotkey;
            // 执行器可能变了，重建识别引擎
            _ocr?.Dispose();
            _ocr = null;
            // 墨迹外观可能变了，重新应用
            ApplyInkSettings();
            UpdateStatus(DefaultHint());
        }
    }

    private void ShowCalibration()
    {
        var w = new CalibrationWindow(_profile) { Owner = this };
        w.ShowDialog();
        UpdateStatus(DefaultHint());
    }

    private void UpdateStatus(string text, Color? accent = null)
    {
        StatusText.Text = text;

        bool dark = string.Equals(_settings.StatusTheme, "dark", StringComparison.OrdinalIgnoreCase);
        if (dark)
        {
            // 半透明黑底：白色文字，无描边
            StatusPanel.Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x20, 0x20, 0x20));
            StatusPanel.BorderBrush = Brushes.Transparent;
            StatusText.Foreground = new SolidColorBrush(Colors.White);
        }
        else
        {
            // 浅色白底：语义色只作用于文字与描边
            var fg = accent ?? Color.FromRgb(0x2F, 0x34, 0x37);
            var border = accent is null
                ? Color.FromRgb(0xEA, 0xEA, 0xEA)
                : Color.FromArgb(0x99, accent.Value.R, accent.Value.G, accent.Value.B);
            StatusPanel.Background = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF));
            StatusPanel.BorderBrush = new SolidColorBrush(border);
            StatusText.Foreground = new SolidColorBrush(fg);
        }
    }

    private void SetClickThrough(bool through)
    {
        NativeMethods.SetExStyle(this, NativeMethods.WS_EX_TRANSPARENT, through);
        NativeMethods.SetExStyle(this, NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW, true);

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }
}
