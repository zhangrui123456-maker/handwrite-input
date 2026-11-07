using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Ink;
using HandwriteInput.PoC.Ink;
using HandwriteInput.PoC.Profile;

namespace HandwriteInput.PoC;

public partial class CalibrationWindow : Window
{
    private readonly PersonalProfile _profile;
    private PaddleOcrRecognizer? _ocr;
    private string _recognized = "";

    private PaddleOcrRecognizer Ocr => _ocr ??= PaddleOcrRecognizer.CreateDefault();

    public CalibrationWindow(PersonalProfile profile)
    {
        _profile = profile;
        InitializeComponent();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        Ink.Strokes.Clear();
        RecogResultText.Text = "";
        CorrectedBox.Text = "";
        StatusText.Text = "";
    }

    private async void Recognize_Click(object sender, RoutedEventArgs e)
    {
        if (Ink.Strokes.Count == 0)
        {
            StatusText.Text = "请先写一些字。";
            return;
        }

        var strokes = new StrokeCollection(Ink.Strokes);
        RecognizeBtn.IsEnabled = false;
        StatusText.Text = "识别中…";
        try
        {
            string text = await Task.Run(() =>
            {
                using var mat = InkRasterizer.RenderStrokesToMat(strokes);
                if (mat is null) return string.Empty;
                return Ocr.Recognize(mat);
            });
            _recognized = text;
            RecogResultText.Text = text;
            if (CorrectedBox.Text.Length == 0)
            {
                CorrectedBox.Text = text;
            }
            StatusText.Text = string.IsNullOrEmpty(text) ? "未识别出内容，请写清楚一点。" : "识别完成，可在下方修正。";
        }
        catch (Exception ex)
        {
            StatusText.Text = "识别失败：" + ex.Message;
        }
        finally
        {
            RecognizeBtn.IsEnabled = true;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string corrected = CorrectedBox.Text.Trim();
        if (corrected.Length == 0)
        {
            StatusText.Text = "请先填写正确内容再保存。";
            return;
        }

        int before = _profile.Corrections.Count;
        PersonalAdapter.LearnCorrections(_profile, _recognized, corrected);
        ProfileStore.Save(_profile);

        int added = _profile.Corrections.Count - before;
        StatusText.Text = $"已保存 {added} 条纠正对（累计 {_profile.Corrections.Count} 条）。可继续写下一段。";

        Ink.Strokes.Clear();
        RecogResultText.Text = "";
        CorrectedBox.Text = "";
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _ocr?.Dispose();
        base.OnClosed(e);
    }
}
