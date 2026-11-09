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
    private bool _configurationRefreshing;
    private readonly FullAppPaymentClient? _payments;
    private bool _creatingPayment;
    private bool _paymentOfferRefreshing;
    private bool _paymentAvailable;

    public ActivationWindow(
        LicenseService licenses,
        bool replacingExisting = false,
        string? initialStatus = null,
        bool trialEnded = false)
        : this(licenses, replacingExisting, initialStatus, trialEnded, null)
    {
    }

    internal ActivationWindow(LicenseService licenses, bool replacingExisting, string? initialStatus,
        bool trialEnded, RemoteConfigService? remoteConfig, FullAppPaymentClient? payments = null)
    {
        _licenses = licenses;
        _replacingExisting = replacingExisting;
        _remoteConfig = remoteConfig ?? new RemoteConfigService(new SettingsStore(licenses.DataDirectory));
        InitializeComponent();
        _payments = replacingExisting ? null : payments;
        AlipayActivationPanel.Visibility = _payments is null ? Visibility.Collapsed : Visibility.Visible;
        if (_payments is not null)
        {
            SubtitleText.Visibility = Visibility.Collapsed;
            ExistingCodeLabel.Text = "使用激活码";
            DeviceActivationPanel.Visibility = Visibility.Collapsed;
            ManualActivationContactPanel.Visibility = Visibility.Collapsed;
            WebsiteButton.Visibility = Visibility.Collapsed;
        }
        Activated += async (_, _) => { if (_payments is not null) await RefreshActivationAsync(); };
        DeviceCodeText.Text = licenses.InstallationId;
        SyncActivationButton.IsEnabled = !replacingExisting;
        ApplyContactConfiguration();
        _remoteConfig.Changed += OnRemoteConfigurationChanged;
        ContinueButton.Content = replacingExisting ? "取消" : "先留下桌宠";
        if (_payments is not null)
        {
            ContinueButton.Content = "稍后";
            ActivateButton.Content = "激活";
        }
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
        if (_payments is not null)
        {
            Title = "激活桌搭子";
            HeadingText.Text = "激活桌搭子";
            TrialSummary.Visibility = Visibility.Collapsed;
            MinHeight = 440;
            Height = 480;
        }
        StatusText.Text = initialStatus ?? string.Empty;
        ContentRendered += (_, _) => CodeTextBox.Focus();
        _refreshTimer.Tick += async (_, _) => await RefreshCardAsync();
        Loaded += async (_, _) =>
        {
            _refreshTimer.Start();
            await RefreshCardAsync();
        };
        Closed += (_, _) =>
        {
            _refreshTimer.Stop();
            _lifetime.Cancel();
            _remoteConfig.Changed -= OnRemoteConfigurationChanged;
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

    private async void AlipayActivation_Click(object sender, RoutedEventArgs e)
    {
        if (_payments is null || _creatingPayment) return;
        _creatingPayment = true;
        AlipayActivationButton.IsEnabled = false;
        try
        {
            await _licenses.CheckTrialAsync(_lifetime.Token);
            if (_licenses.IsActivated) { DialogResult = true; return; }
            var catalog = await _payments.CatalogAsync(_lifetime.Token);
            AlipayActivationButton.Content = $"支付宝支付 · ¥{catalog.Activation.AmountFen / 100m:F2}";
            await _payments.OpenAsync(true, null, _lifetime.Token);
            StatusText.Text = "等待付款";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { StatusText.Text = error is InvalidOperationException ? error.Message : "暂时无法创建订单，请稍后重试。"; }
        finally { _creatingPayment = false; AlipayActivationButton.IsEnabled = _paymentAvailable; }
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
        => await RefreshCardAsync(true);

    private Task RefreshCardAsync(bool showStatus = false)
        => Task.WhenAll(RefreshContactConfigurationAsync(), RefreshActivationAsync(showStatus), RefreshPaymentOfferAsync());

    private async Task RefreshPaymentOfferAsync()
    {
        if (_payments is null || _paymentOfferRefreshing || _lifetime.IsCancellationRequested) return;
        _paymentOfferRefreshing = true;
        try
        {
            var catalog = await _payments.CatalogAsync(_lifetime.Token);
            if (_lifetime.IsCancellationRequested) return;
            AlipayProductTitle.Text = catalog.Activation.Title;
            _paymentAvailable = catalog.Provider.Configured;
            AlipayActivationButton.Content = _paymentAvailable
                ? $"支付宝支付 · ¥{catalog.Activation.AmountFen / 100m:F2}" : "暂时无法支付";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch
        {
            _paymentAvailable = false;
            AlipayActivationButton.Content = "暂时无法支付";
        }
        finally
        {
            _paymentOfferRefreshing = false;
            AlipayActivationButton.IsEnabled = _paymentAvailable && !_creatingPayment;
        }
    }

    private async Task RefreshContactConfigurationAsync()
    {
        if (_configurationRefreshing || _lifetime.IsCancellationRequested) return;
        _configurationRefreshing = true;
        try { await _remoteConfig.RefreshAsync(_lifetime.Token); }
        finally { _configurationRefreshing = false; }
    }

    private void OnRemoteConfigurationChanged()
    {
        if (_lifetime.IsCancellationRequested) return;
        if (Dispatcher.CheckAccess()) ApplyContactConfiguration();
        else _ = Dispatcher.InvokeAsync(() =>
        {
            if (!_lifetime.IsCancellationRequested) ApplyContactConfiguration();
        });
    }

    private async Task RefreshActivationAsync(bool showStatus = false)
    {
        if (_refreshing || _lifetime.IsCancellationRequested) return;
        _refreshing = true;
        try
        {
            if (!_replacingExisting)
            {
                if (_payments is not null) await _payments.RefreshActivationOrdersAsync(_lifetime.Token);
                await _licenses.CheckTrialAsync(_lifetime.Token);
                if (_lifetime.IsCancellationRequested) return;
                if (_licenses.IsActivated)
                {
                    DialogResult = true;
                    return;
                }
                if (showStatus) StatusText.Text = _payments is null
                    ? "后台尚未为此设备激活，请核对设备码后稍后重试。" : "尚未激活";
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
