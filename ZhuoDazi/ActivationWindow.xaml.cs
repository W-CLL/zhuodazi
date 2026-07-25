using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using ZhuoDazi.Services;

namespace ZhuoDazi;

public partial class ActivationWindow : Window
{
    private const string AllowedCharacters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private readonly LicenseService _licenses;
    private bool _normalizing;

    public ActivationWindow(LicenseService licenses)
    {
        _licenses = licenses;
        InitializeComponent();
        ContentRendered += (_, _) => CodeTextBox.Focus();
    }

    private void CodeTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_normalizing) return;
        var normalized = new string(CodeTextBox.Text.ToUpperInvariant()
            .Where(character => AllowedCharacters.Contains(character)).Take(6).ToArray());
        if (normalized == CodeTextBox.Text) return;
        _normalizing = true;
        CodeTextBox.Text = normalized;
        CodeTextBox.CaretIndex = normalized.Length;
        _normalizing = false;
    }

    private async void ActivateButton_Click(object sender, RoutedEventArgs e)
    {
        if (CodeTextBox.Text.Length != 6)
        {
            StatusText.Text = "请输入完整的 6 位激活码。";
            CodeTextBox.Focus();
            return;
        }
        SetBusy(true);
        StatusText.Text = string.Empty;
        try
        {
            await _licenses.ActivateAsync(CodeTextBox.Text);
            DialogResult = true;
        }
        catch (TaskCanceledException)
        {
            StatusText.Text = "连接激活服务器超时，请稍后重试。";
        }
        catch (HttpRequestException)
        {
            StatusText.Text = "无法连接激活服务器，请检查网络后重试。";
        }
        catch (InvalidOperationException error)
        {
            StatusText.Text = error.Message;
        }
        catch
        {
            StatusText.Text = "激活失败，请稍后重试。";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        ActivateButton.IsEnabled = !busy;
        CodeTextBox.IsEnabled = !busy;
        ActivationProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
