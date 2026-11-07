using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HandwriteInput.PoC;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private bool _capturing;
    private bool _capCtrl, _capShift, _capAlt, _capWin;
    private string _customColor = ""; // 用户通过取色器选择的自定义色（十六进制）

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        ThicknessSlider.ValueChanged += (_, _) =>
            ThicknessText.Text = ((int)ThicknessSlider.Value).ToString();
        RefreshKeyText();
        HoldRadio.IsChecked = _settings.HoldMode;
        ToggleRadio.IsChecked = !_settings.HoldMode;
        AutoPasteCheck.IsChecked = _settings.AutoPaste;
        ProviderBox.SelectedIndex = _settings.Provider.ToLowerInvariant() switch
        {
            "dml" => 1,
            "cpu" => 2,
            _ => 0,
        };
        ThicknessSlider.Value = _settings.InkThickness <= 0 || _settings.InkThickness > 10 ? 4.0 : _settings.InkThickness;
        ThicknessText.Text = ((int)ThicknessSlider.Value).ToString();
        SelectInkColor(_settings.InkColor);

        // 顶部提示配色
        bool dark = string.Equals(_settings.StatusTheme, "dark", StringComparison.OrdinalIgnoreCase);
        StatusLightRadio.IsChecked = !dark;
        StatusDarkRadio.IsChecked = dark;
    }

    private void SelectInkColor(string colorHex)
    {
        var hex = colorHex?.Trim() ?? "";
        var radios = new[] { InkDark, InkBlack, InkRed, InkGreen, InkBlue };
        bool matched = false;
        foreach (var rb in radios)
        {
            bool hit = string.Equals(rb.Tag as string, hex, StringComparison.OrdinalIgnoreCase);
            rb.IsChecked = hit;
            if (hit) matched = true;
        }

        if (matched)
        {
            _customColor = "";
            CustomColorButton.Background = new SolidColorBrush(Colors.White);
            CustomColorLabel.Text = "自定义";
            CustomColorLabel.Foreground = (Brush)FindResource("TextMutedBrush");
        }
        else
        {
            // 非预设色 → 视为自定义
            _customColor = string.IsNullOrEmpty(hex) ? "#142E7A" : hex;
            SetCustomSwatch(_customColor);
        }
    }

    private void SetCustomSwatch(string hex)
    {
        _customColor = hex;
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            CustomColorButton.Background = new SolidColorBrush(c);
            CustomColorLabel.Text = "自定义 " + hex.ToUpperInvariant();
            CustomColorLabel.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#346538"));
        }
        catch
        {
            CustomColorButton.Background = new SolidColorBrush(Colors.White);
            CustomColorLabel.Text = "自定义";
        }
    }

    private string SelectedInkColor()
    {
        // 自定义色优先（用户选了取色器）
        if (!string.IsNullOrEmpty(_customColor))
            return _customColor;

        var rb = new[] { InkDark, InkBlack, InkRed, InkGreen, InkBlue }.FirstOrDefault(r => r.IsChecked == true);
        return (rb?.Tag as string) ?? "#142E7A";
    }

    private void InkColor_Checked(object sender, RoutedEventArgs e)
    {
        // 点选预设色板：清掉自定义态
        if (sender is RadioButton rb && rb.IsChecked == true)
        {
            _customColor = "";
            CustomColorButton.Background = new SolidColorBrush(Colors.White);
            CustomColorLabel.Text = "自定义";
            CustomColorLabel.Foreground = (Brush)FindResource("TextMutedBrush");
        }
    }

    private void CustomColor_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
        };
        // 预选当前颜色
        string cur = SelectedInkColor();
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(cur);
            dlg.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
        }
        catch { /* 忽略预选失败 */ }

        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            string hex = "#" + dlg.Color.R.ToString("X2") + dlg.Color.G.ToString("X2") + dlg.Color.B.ToString("X2");
            foreach (var rb in new[] { InkDark, InkBlack, InkRed, InkGreen, InkBlue })
                rb.IsChecked = false;
            SetCustomSwatch(hex);
        }
    }

    private void RefreshKeyText()
    {
        CurrentKeyText.Text = "当前：" + Hotkey.FromSettings(_settings).Format();
    }

    private void CaptureKey_Click(object sender, RoutedEventArgs e)
    {
        _capturing = true;
        _capCtrl = _capShift = _capAlt = _capWin = false;
        CaptureKeyButton.Content = "请按下新快捷键…";
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_capturing)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        // 捕获组合键：先记录修饰键，直到按下非修饰主键才完成
        bool isModifier = e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.System or Key.LWin or Key.RWin;

        if (e.Key is Key.LeftCtrl or Key.RightCtrl) _capCtrl = true;
        if (e.Key is Key.LeftShift or Key.RightShift) _capShift = true;
        if (e.Key is Key.LeftAlt or Key.RightAlt or Key.System) _capAlt = true;
        if (e.Key is Key.LWin or Key.RWin) _capWin = true;

        e.Handled = true;

        if (!isModifier)
        {
            int vk = KeyInterop.VirtualKeyFromKey(e.Key);
            if (vk == 0) return; // 无法映射的主键，继续等待
            _settings.ToggleKeyCode = vk;
            _settings.Ctrl = _capCtrl;
            _settings.Shift = _capShift;
            _settings.Alt = _capAlt;
            _settings.Win = _capWin;
            _capturing = false;
            CaptureKeyButton.Content = "点击后按下新快捷键";
            RefreshKeyText();
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.HoldMode = HoldRadio.IsChecked == true;
        _settings.AutoPaste = AutoPasteCheck.IsChecked == true;
        _settings.Provider = ProviderBox.SelectedIndex switch
        {
            1 => "dml",
            2 => "cpu",
            _ => "auto",
        };
        _settings.InkColor = SelectedInkColor();
        _settings.InkThickness = ThicknessSlider.Value;
        _settings.StatusTheme = StatusDarkRadio.IsChecked == true ? "dark" : "light";

        _settings.Save();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Calibrate_Click(object sender, RoutedEventArgs e)
    {
        var profile = Profile.ProfileStore.Load();
        var w = new CalibrationWindow(profile) { Owner = this };
        w.ShowDialog();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }
}
