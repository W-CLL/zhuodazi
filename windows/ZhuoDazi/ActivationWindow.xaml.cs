using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using ZhuoDazi.Services;

namespace ZhuoDazi;

public partial class ActivationWindow : Window
{
    private const string AllowedCharacters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private readonly LicenseService _licenses;
    private readonly bool _replacingExisting;
    private bool _normalizing;

    public ActivationWindow(LicenseService licenses, bool replacingExisting = false)
    {
        _licenses = licenses;
        _replacingExisting = replacingExisting;
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
            StatusText.Text = "请输入完整的 6 位邀请码。";
            CodeTextBox.Focus();
            return;
        }
        SetBusy(true);
        StatusText.Text = string.Empty;
        try
        {
            await _licenses.ActivateAsync(CodeTextBox.Text, _replacingExisting);
            DialogResult = true;
        }
        catch (TaskCanceledException)
        {
            StatusText.Text = "连接邀请服务超时，请稍后重试。";
        }
        catch (HttpRequestException)
        {
            StatusText.Text = "无法连接邀请服务，请检查网络后重试。";
        }
        catch (InvalidOperationException error)
        {
            StatusText.Text = error.Message;
        }
        catch
        {
            StatusText.Text = "邀请码验证失败，请稍后重试。";
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

    private void ContactToggleButton_Click(object sender, RoutedEventArgs e)
    {
        var show = ContactPanel.Visibility != Visibility.Visible;
        ContactPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ContactToggleButton.Content = show ? "收起联系方式  ‹" : "没有邀请码？联系作者获取  ›";
        Height = show ? 735 : 500;
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
