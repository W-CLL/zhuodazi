using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using ZhuoDazi.Services;

namespace ZhuoDazi;

public partial class ActivationWindow : Window
{
    private const string AllowedCharacters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const string AuthorWeChat = "wcl_lcw627";
    private readonly LicenseService _licenses;
    private readonly bool _replacingExisting;
    private bool _normalizing;
    private string? _xianyuUrl;

    public ActivationWindow(
        LicenseService licenses,
        bool replacingExisting = false,
        string? initialStatus = null,
        bool trialEnded = false)
    {
        _licenses = licenses;
        _replacingExisting = replacingExisting;
        InitializeComponent();
        ContinueButton.Content = replacingExisting ? "取消" : "先留下桌宠";
        if (trialEnded)
        {
            Title = "完整体验结束，基础陪伴继续";
            HeadingText.Text = "七天体验结束啦";
            SubtitleText.Text = "桌搭子不会离开。刚才试过的玩法想接着用，填激活码就好；先留下桌宠也完全没问题。";
            WebsiteButton.Content = "去官网看看玩法";
            ContinueButton.Content = "继续基础陪伴";
            ActivateButton.Content = "解锁完整功能";
            ActivateButton.Width = 118;
            TrialSummary.Visibility = Visibility.Visible;
        }
        StatusText.Text = initialStatus ?? string.Empty;
        ContentRendered += (_, _) => CodeTextBox.Focus();
        Loaded += async (_, _) => await LoadXianyuLinkAsync();
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
            await _licenses.ActivateAsync(CodeTextBox.Text, _replacingExisting);
            DialogResult = true;
        }
        catch (TaskCanceledException)
        {
            StatusText.Text = "连接激活服务超时，请稍后重试。";
        }
        catch (HttpRequestException)
        {
            StatusText.Text = "无法连接激活服务，请检查网络后重试。";
        }
        catch (InvalidOperationException error)
        {
            StatusText.Text = error.Message;
        }
        catch
        {
            StatusText.Text = "激活码验证失败，请稍后重试。";
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

    private void CopyWeChat_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Clipboard.SetText(AuthorWeChat);
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
        StatusText.Text = "微信号已复制，备注「桌搭子」即可。";
    }

    private void Xianyu_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_xianyuUrl)) return;
        Process.Start(new ProcessStartInfo(_xianyuUrl) { UseShellExecute = true });
    }

    private void Website_Click(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo("https://desktoppet.online/") { UseShellExecute = true });

    private void Continue_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private async Task LoadXianyuLinkAsync()
    {
        try
        {
            using var client = DeskPetHttp.CreateClient(TimeSpan.FromSeconds(8));
            using var response = await client.GetAsync(DeskPetApi.SiteSettings);
            if (!response.IsSuccessStatusCode) return;
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await System.Text.Json.JsonDocument.ParseAsync(stream);
            if (!document.RootElement.TryGetProperty("xianyuUrl", out var urlElement)) return;
            var url = urlElement.GetString();
            if (string.IsNullOrWhiteSpace(url)) return;
            _xianyuUrl = url;
            XianyuButton.Visibility = Visibility.Visible;
        }
        catch
        {
        }
    }
}
