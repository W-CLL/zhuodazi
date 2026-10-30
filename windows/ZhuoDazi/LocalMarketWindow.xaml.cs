using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ZhuoDazi.Services;
using WpfMessageBox = System.Windows.MessageBox;

namespace ZhuoDazi;

public partial class LocalMarketWindow : Window
{
    private readonly LocalMarketConfig _config;
    private readonly LocalMarketClient _client;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Stopwatch _serverClock = Stopwatch.StartNew();
    private DateTimeOffset _serverAtSync = DateTimeOffset.UtcNow;
    private DateTimeOffset _nextPollAt = DateTimeOffset.MinValue;
    private LocalMarketOrder? _order;
    private bool _ready;
    private bool _hasQr;
    private bool _busy;
    private bool _polling;
    private bool _closed;
    private bool _uiInitialized;

    internal LocalMarketWindow(LocalMarketConfig config)
    {
        InitializeComponent();
        _config = config;
        _client = new LocalMarketClient(config);
        AdminSection.Visibility = string.IsNullOrWhiteSpace(config.AdminToken) ? Visibility.Collapsed : Visibility.Visible;
        _timer.Tick += Timer_Tick;
        _uiInitialized = true;
        UpdatePaymentMethod();
    }

    private DateTimeOffset ServerNow => _serverAtSync + _serverClock.Elapsed;
    private bool IsPersonalTransferMode => PaymentMethodBox.SelectedIndex == 1;

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_config.QrImagePath))
        {
            try { LoadQr(_config.QrImagePath); }
            catch { QrFileText.Text = "配置的收款码无法读取，请重新选择 PNG / JPG 图片。"; }
        }
        await ConnectAsync();
        if (!_closed) _timer.Start();
    }

    private async Task ConnectAsync()
    {
        _busy = true;
        RefreshButtons();
        try
        {
            await _client.CheckHealthAsync(_lifetime.Token);
            var catalog = await _client.GetCatalogAsync(_lifetime.Token);
            if (_closed) return;
            ProductTitleText.Text = catalog.ProductTitle;
            SyncClock();
            _ready = true;
            ConnectionText.Text = $"本机服务已连接 · {_config.BaseUrl}";
            if (_order?.Id != catalog.CurrentOrder?.Id) DownloadPathText.Visibility = Visibility.Collapsed;
            _order = catalog.CurrentOrder;
            if (_order is not null)
            {
                ApplyOrder();
                if (_order.Status == "paid") StatusText.Text = "已恢复本地已确认订单，可以直接下载测试包，无需再次付款。";
                else if (_order.Status == "pending_payment" && _order.ExpiresAt > ServerNow)
                    StatusText.Text = $"已恢复待付款订单，沿用短码 {_order.Reference.ToUpperInvariant()} 和原截止时间。";
            }
            else
            {
                PaymentPanel.Visibility = Visibility.Collapsed;
                PaidPanel.Visibility = Visibility.Collapsed;
                OrderInfoText.Text = string.Empty;
                StatusText.Text = ReadyHint();
            }
        }
        catch (Exception error)
        {
            _ready = false;
            if (!_closed) ConnectionText.Text = SafeError(error);
        }
        finally { _busy = false; RefreshButtons(); }
    }

    private async void Reconnect_Click(object sender, RoutedEventArgs e) => await ConnectAsync();

    private void PaymentMethod_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiInitialized) return;
        UpdatePaymentMethod();
        if (_order is null || _order.Status is "expired" or "canceled") StatusText.Text = ReadyHint();
        else if (_order.Status == "pending_payment") ApplyOrder();
        RefreshButtons();
    }

    private string ReadyHint() => IsPersonalTransferMode
        ? "好友转账无需收款码。先下单取得短码，再在微信发起 0.01 元转账。"
        : _hasQr ? "收款码已就绪，可以创建 0.01 元测试订单。" : "请先选择你生成的 0.01 元收款码。";

    private void UpdatePaymentMethod()
    {
        var transfer = IsPersonalTransferMode;
        ChooseQrButton.Visibility = transfer ? Visibility.Collapsed : Visibility.Visible;
        QrFileText.Visibility = transfer ? Visibility.Collapsed : Visibility.Visible;
        QrVisualPanel.Visibility = transfer ? Visibility.Collapsed : Visibility.Visible;
        TransferInstructionsPanel.Visibility = transfer ? Visibility.Visible : Visibility.Collapsed;
        PaymentMethodHintText.Text = transfer
            ? "在微信向当前收款方发起 0.01 元转账，转账说明直接填订单短码。匹配成功后自动开放测试包下载。"
            : "选择收款码图片后下单，扫码时填写订单短码。";
        ReferencePromptText.Text = transfer ? "转账说明请直接填写以下短码" : "付款前，请在微信备注中输入";
        PaymentInstructionsText.Text = transfer
            ? "识别到金额与短码匹配的转账后，本地服务自动确认订单并开放测试包下载。15 分钟未匹配时关闭此面板。"
            : "到账核对后将开放下载。15 分钟未支付，订单自动过期并关闭此收款面板。";
    }

    private void ChooseQr_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 0.01 元本地测试收款码",
            Filter = "收款码图片 (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
            Multiselect = false,
            CheckFileExists = true
        };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            LoadQr(picker.FileName);
            StatusText.Text = "收款码已就绪。请确认此图片对应本次 0.01 元测试。";
        }
        catch { StatusText.Text = "图片无法读取，请选择本机 PNG / JPG 图片（不超过 10 MB）。"; }
        RefreshButtons();
    }

    private void LoadQr(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal) || extension is not (".png" or ".jpg" or ".jpeg") ||
            !File.Exists(path) || new FileInfo(path).Length is <= 0 or > 10 * 1024 * 1024)
            throw new InvalidOperationException("收款码图片无效。");
        using var source = File.OpenRead(path);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = 900;
        bitmap.StreamSource = source;
        bitmap.EndInit();
        bitmap.Freeze();
        QrImage.Source = bitmap;
        _hasQr = true;
        QrFileText.Text = $"已选择：{Path.GetFileName(path)}";
    }

    private async void CreateOrder_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !_ready || (!IsPersonalTransferMode && !_hasQr)) return;
        _busy = true;
        RefreshButtons();
        try
        {
            _order = await _client.CreateOrderAsync(_lifetime.Token);
            SyncClock();
            DownloadPathText.Visibility = Visibility.Collapsed;
            ApplyOrder();
        }
        catch (Exception error) { ShowError(error); }
        finally { _busy = false; RefreshButtons(); }
    }

    private async void CancelOrder_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _order is null || _order.Status != "pending_payment") return;
        _busy = true;
        RefreshButtons();
        try
        {
            _order = await _client.CancelOrderAsync(_order.Id, _lifetime.Token);
            SyncClock();
            ApplyOrder();
        }
        catch (Exception error) { ShowError(error); }
        finally { _busy = false; RefreshButtons(); }
    }

    private void CopyReference_Click(object sender, RoutedEventArgs e)
    {
        if (_order is null) return;
        try
        {
            System.Windows.Clipboard.SetText(_order.Reference);
            StatusText.Text = IsPersonalTransferMode
                ? "短码已复制到本机。请在微信转账说明中直接填写该短码，金额为 0.01 元。"
                : "短码已复制到本机。手机扫码后，请在确认付款前填写这个短码。";
        }
        catch { StatusText.Text = "剪贴板暂不可用，请手动输入付款短码。"; }
    }

    private async void Timer_Tick(object? sender, EventArgs e)
    {
        if (_closed) return;
        UpdateCountdown();
        if (_order is null || _order.Status != "pending_payment" || _polling || _busy || ServerNow < _nextPollAt) return;
        _nextPollAt = ServerNow.AddSeconds(3);
        _polling = true;
        var orderId = _order.Id;
        try
        {
            var updated = await _client.GetOrderAsync(orderId, _lifetime.Token);
            SyncClock();
            if (_order?.Id == orderId && _order.Status == "pending_payment")
            {
                var changed = _order.Status != updated.Status;
                _order = updated;
                if (changed) ApplyOrder();
                else UpdateCountdown();
            }
        }
        catch (Exception error)
        {
            if (!_closed) StatusText.Text = $"{SafeError(error)} 正在等待重新核对订单状态。";
        }
        finally { _polling = false; RefreshButtons(); }
    }

    private void ApplyOrder()
    {
        if (_order is null || _closed) return;
        ReferenceText.Text = _order.Reference.ToUpperInvariant();
        OrderInfoText.Text = $"订单：{_order.Id}　·　备注码：{_order.Reference.ToUpperInvariant()}　·　金额 ¥{_order.AmountFen / 100m:F2}";
        PaymentPanel.Visibility = _order.Status == "pending_payment" && _order.ExpiresAt > ServerNow ? Visibility.Visible : Visibility.Collapsed;
        PaidPanel.Visibility = _order.Status == "paid" ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = _order.Status switch
        {
            "paid" => "订单已确认，可以下载测试资源包。",
            "expired" => "15 分钟内未确认付款，订单已过期，收款面板已关闭。可以重新下单。",
            "canceled" => "订单已取消。可以重新下单。",
            _ => IsPersonalTransferMode
                ? "请在微信向当前收款方发起 ¥0.01 转账，转账说明直接填写上方 5 位短码。"
                : "请扫码付款 ¥0.01，并在付款前填写上方 5 位备注码。"
        };
        _nextPollAt = ServerNow.AddSeconds(3);
        UpdateCountdown();
        RefreshButtons();
    }

    private void UpdateCountdown()
    {
        if (_order is null || _order.Status != "pending_payment") return;
        var remaining = _order.ExpiresAt - ServerNow;
        if (remaining <= TimeSpan.Zero)
        {
            PaymentPanel.Visibility = Visibility.Collapsed;
            CountdownText.Text = "付款时间已结束";
            StatusText.Text = "付款时间已结束，收款面板已关闭；正在核对订单最终状态。";
            return;
        }
        var totalSeconds = (int)Math.Ceiling(remaining.TotalSeconds);
        CountdownText.Text = $"剩余 {totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _order?.Status != "paid") return;
        _busy = true;
        RefreshButtons();
        try
        {
            DownloadPathText.Text = await _client.DownloadAsync(_order, _lifetime.Token);
            DownloadPathText.Visibility = Visibility.Visible;
            StatusText.Text = "测试包已保存到独立本地目录，未自动安装或执行。";
        }
        catch (Exception error) { ShowError(error); }
        finally { _busy = false; RefreshButtons(); }
    }

    private async void RefreshEvents_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        RefreshButtons();
        try
        {
            var events = await _client.GetPaymentEventsAsync(_lifetime.Token);
            PaymentEventsList.ItemsSource = events;
            AdminStatusText.Text = events.Count == 0 ? "尚未收到支付记录，请确认本地监听已启动。" : $"共 {events.Count} 条记录。好友转账匹配后自动确认订单；收款码通知仍需核对到账。";
        }
        catch (Exception error) { if (!_closed) AdminStatusText.Text = SafeError(error); }
        finally { _busy = false; RefreshButtons(); }
    }

    private void PaymentEvents_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PaymentEventsList.SelectedItem is LocalMarketPaymentEvent candidate && candidate.IsPersonalTransfer)
            AdminStatusText.Text = candidate.ReviewStatus == "approved"
                ? "好友转账 · 订单已确认。订单状态会自动刷新并开放测试包下载。"
                : "好友转账 · 待核对。当前记录尚未满足服务端匹配规则，请核对短码、金额和订单有效期。";
        else if (PaymentEventsList.SelectedItem is LocalMarketPaymentEvent receipt && receipt.Category == "receipt")
            AdminStatusText.Text = receipt.ReviewStatus == "approved" ? "此收款码通知已人工确认。" : "收款码通知仍需在微信核实实际到账、金额和备注后确认。";
        else if (PaymentEventsList.SelectedItem is LocalMarketPaymentEvent)
            AdminStatusText.Text = "此支付记录类型尚未识别，请核对来源记录。";
        RefreshButtons();
    }

    private async void ConfirmPayment_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _order is null || PaymentEventsList.SelectedItem is not LocalMarketPaymentEvent candidate || !candidate.CanConfirm) return;
        if (!candidate.Reference.Equals(_order.Reference, StringComparison.OrdinalIgnoreCase) || candidate.AmountFen != _order.AmountFen)
        {
            AdminStatusText.Text = "选中候选的备注码或金额与当前订单不一致，不能确认。";
            return;
        }
        var expiredNote = _order.Status is "expired" or "canceled" || _order.ExpiresAt <= ServerNow
            ? "\n此订单已经过期或取消，请特别确认这是该原订单的迟到付款。" : string.Empty;
        var confirmation = $"请先在微信核实已实际收到 ¥0.01，备注为 {_order.Reference.ToUpperInvariant()}。";
        if (WpfMessageBox.Show(this,
                $"{confirmation}\n\n选中记录：{candidate}\n{expiredNote}\n确认后将为当前本地测试订单开放下载。",
                "仅本地测试：人工确认到账", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        _busy = true;
        RefreshButtons();
        try
        {
            await _client.ConfirmPaymentAsync(candidate.Id, _order.Id, _lifetime.Token);
            _order = await _client.GetOrderAsync(_order.Id, _lifetime.Token);
            SyncClock();
            ApplyOrder();
            AdminStatusText.Text = "已记录本地人工核对结果。";
        }
        catch (Exception error) { if (!_closed) AdminStatusText.Text = SafeError(error); }
        finally { _busy = false; RefreshButtons(); }
    }

    private void SyncClock()
    {
        if (!_client.ServerTime.HasValue) return;
        _serverAtSync = _client.ServerTime.Value;
        _serverClock.Restart();
    }

    private void RefreshButtons()
    {
        if (_closed || !_uiInitialized) return;
        CreateOrderButton.IsEnabled = !_busy && _ready && (IsPersonalTransferMode || _hasQr) && _order?.Status is not ("pending_payment" or "paid");
        PaymentMethodBox.IsEnabled = !_busy && _order?.Status != "paid";
        ChooseQrButton.IsEnabled = !_busy && (_order?.Status != "pending_payment" || !_hasQr);
        ReconnectButton.IsEnabled = !_busy;
        CancelOrderButton.IsEnabled = !_busy && _order?.Status == "pending_payment";
        DownloadButton.IsEnabled = !_busy && _order?.Status == "paid";
        RefreshEventsButton.IsEnabled = !_busy && _ready;
        ConfirmPaymentButton.IsEnabled = !_busy && _ready && _order is not null && _order.Status != "paid" &&
            PaymentEventsList.SelectedItem is LocalMarketPaymentEvent candidate && candidate.CanConfirm &&
            candidate.Reference.Equals(_order.Reference, StringComparison.OrdinalIgnoreCase) && candidate.AmountFen == _order.AmountFen;
        ConfirmPaymentButton.Visibility = PaymentEventsList.SelectedItem is LocalMarketPaymentEvent selected && selected.Category == "receipt"
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowError(Exception error)
    {
        if (!_closed) StatusText.Text = SafeError(error);
    }

    private static string SafeError(Exception error) => error switch
    {
        OperationCanceledException => "本机服务响应超时，请稍后重试。",
        HttpRequestException => "暂时无法连接本机测试服务，请检查本地服务是否运行。",
        InvalidOperationException => error.Message,
        IOException or UnauthorizedAccessException => "本地文件读写失败，请检查测试目录权限。",
        _ => "本地测试操作未完成，请检查服务状态后重试。"
    };

    private void Window_Closed(object? sender, EventArgs e)
    {
        _closed = true;
        _timer.Stop();
        _lifetime.Cancel();
        _client.Dispose();
        _lifetime.Dispose();
    }
}
