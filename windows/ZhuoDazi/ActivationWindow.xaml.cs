using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ZhuoDazi.Services;

namespace ZhuoDazi;

public partial class ActivationWindow : Window
{
    private const string AllowedCharacters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private readonly LicenseService _licenses;
    private readonly bool _replacingExisting;
    private bool _normalizing;
    private string _wechatId = "wcl_lcw627";
    private string? _xianyuUrl;
    private readonly RemoteConfigService _remoteConfig;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private bool _refreshing;

    public ActivationWindow(
        LicenseService licenses,
        bool replacingExisting = false,
        string? initialStatus = null,
        bool trialEnded = false)
    {
        _licenses = licenses;
        _replacingExisting = replacingExisting;
        _remoteConfig = new RemoteConfigService(new SettingsStore(licenses.DataDirectory));
        InitializeComponent();
        DeviceCodeText.Text = licenses.InstallationId;
        SyncActivationButton.IsEnabled = !replacingExisting;
        ApplyContactConfiguration();
        ContinueButton.Content = replacingExisting ? "取消" : "先留下桌宠";
        if (replacingExisting)
        {
            Title = "更换激活码";
            HeadingText.Text = "更换激活码";
            SubtitleText.Text = "输入新的激活码后，当前设备会更新绑定记录。";
        }
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
        _refreshTimer.Tick += async (_, _) => await RefreshActivationAsync();
        Loaded += async (_, _) =>
        {
            _refreshTimer.Start();
            await RefreshActivationAsync();
        };
        Closed += (_, _) =>
        {
            _refreshTimer.Stop();
            _lifetime.Cancel();
        };
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
        System.Windows.Clipboard.SetText(_wechatId);
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

    private void CopyDeviceCode_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Clipboard.SetText(_licenses.InstallationId);
        StatusText.Text = "设备码已复制，可发给作者指定此设备激活。";
    }

    private async void SyncActivation_Click(object sender, RoutedEventArgs e)
        => await RefreshActivationAsync(true);

    private async Task RefreshActivationAsync(bool showStatus = false)
    {
        if (_refreshing || _lifetime.IsCancellationRequested) return;
        _refreshing = true;
        try
        {
            await _remoteConfig.RefreshAsync(_lifetime.Token);
            if (_lifetime.IsCancellationRequested) return;
            ApplyContactConfiguration();
            if (!_replacingExisting)
            {
                await _licenses.CheckTrialAsync(_lifetime.Token);
                if (_lifetime.IsCancellationRequested) return;
                if (_licenses.IsActivated)
                {
                    DialogResult = true;
                    return;
                }
                if (showStatus) StatusText.Text = "后台尚未为此设备激活，请核对设备码后稍后重试。";
            }
        }
        catch
        {
            if (showStatus && !_lifetime.IsCancellationRequested) StatusText.Text = "暂时无法同步，请检查网络后重试。";
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void ApplyContactConfiguration()
    {
        var config = _remoteConfig.Current;
        ContactTitleText.Text = _replacingExisting ? config.ActivationReplacementTitle : config.ActivationTitle;
        ContactDescriptionText.Text = _replacingExisting
            ? config.ActivationReplacementDescription : config.ActivationDescription;
        _wechatId = config.WechatId;
        WeChatIdText.Text = _wechatId;
        CopyWeChatButton.IsEnabled = !string.IsNullOrWhiteSpace(_wechatId);
        _xianyuUrl = Uri.TryCreate(config.XianyuUrl, UriKind.Absolute, out var link)
            && link.Scheme == Uri.UriSchemeHttps ? link.AbsoluteUri : null;
        XianyuButton.Visibility = _xianyuUrl is null ? Visibility.Collapsed : Visibility.Visible;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            using var stream = config.ActivationQrCodeDataUrl.StartsWith("data:image/png;base64,", StringComparison.Ordinal)
                && config.ActivationQrCodeDataUrl.Length <= 350_000
                ? new MemoryStream(Convert.FromBase64String(config.ActivationQrCodeDataUrl[22..])) : null;
            if (stream is not null) image.StreamSource = stream;
            else image.UriSource = new Uri("pack://application:,,,/ZhuoDazi;component/assets/contact-author-wechat.png");
            image.DecodePixelWidth = 336;
            image.EndInit();
            image.Freeze();
            ActivationQrImage.Source = image;
        }
        catch
        {
            ActivationQrImage.Source = new BitmapImage(new Uri("pack://application:,,,/ZhuoDazi;component/assets/contact-author-wechat.png"));
        }
    }
}
