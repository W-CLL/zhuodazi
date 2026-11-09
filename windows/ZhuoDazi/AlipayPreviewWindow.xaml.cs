using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ZhuoDazi.Services;

namespace ZhuoDazi;

internal sealed record AlipayPreviewSavedOrder(string OrderId, string? CheckoutUrl);

public partial class AlipayPreviewWindow : Window
{
    private readonly AlipayPreviewConfig _configuration;
    private readonly AlipayPreviewClient _client;
    private readonly CancellationTokenSource _closing = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly AlipayPreviewOrderResponse?[] _orders = new AlipayPreviewOrderResponse?[2];
    private readonly AlipayPreviewCatalog?[] _catalogs = new AlipayPreviewCatalog?[2];
    private bool _ready, _busy;
    private int _generation;
    private int Mode => ModeTabs.SelectedIndex == 1 ? 1 : 0;
    private AlipayPreviewIdentity Identity(int mode) => mode == 0 ? _configuration.ActivationIdentity : _configuration.ShopIdentity;
    private string OrderPath(int mode) => Path.Combine(_configuration.StorageDirectory, mode == 0 ? "activation-order.json" : "shop-order.json");

    internal AlipayPreviewWindow(AlipayPreviewConfig configuration)
    {
        _configuration = configuration; _client = new(configuration);
        InitializeComponent();
        _timer.Tick += async (_, _) => { if (!_busy && _orders[Mode] is not null) await RefreshAsync(catalog: false); };
        _ready = true;
    }
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        foreach (var mode in new[] { 0, 1 })
        {
            try
            {
                if (File.Exists(OrderPath(mode)) && new FileInfo(OrderPath(mode)).Length < 16 * 1024)
                {
                    var saved = JsonSerializer.Deserialize<AlipayPreviewSavedOrder>(File.ReadAllText(OrderPath(mode)), AlipayPreviewConfig.JsonOptions);
                    if (saved is not null && Guid.TryParse(saved.OrderId, out _))
                    {
                        if (saved.CheckoutUrl is not null) AlipayPreviewClient.ValidateCheckoutUrl(saved.CheckoutUrl, saved.OrderId);
                        _orders[mode] = new(new AlipayPreviewOrder { Id = saved.OrderId }, saved.CheckoutUrl, null);
                    }
                }
            }
            catch { /* A malformed local preview state is ignored; it never reaches a production store. */ }
        }
        await RefreshAsync(); _timer.Start();
    }
    private void Window_Closed(object? sender, EventArgs e) { _ready = false; _timer.Stop(); _closing.Cancel(); _client.Dispose(); _closing.Dispose(); }
    private async void Window_Activated(object? sender, EventArgs e) { if (_ready && IsLoaded && !_busy) await RefreshAsync(catalog: false); }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void ModeTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || !ReferenceEquals(e.OriginalSource, ModeTabs)) return;
        _generation++; Render(); await RefreshAsync();
    }
    private void ProductList_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_ready) Render(); }

    private async Task RefreshAsync(bool catalog = true)
    {
        if (!_ready || _busy) return;
        var mode = Mode; var generation = _generation;
        SetBusy(true);
        try
        {
            if (catalog || _catalogs[mode] is null)
            {
                _catalogs[mode] = await _client.CatalogAsync(Identity(mode), _closing.Token);
                if (mode == Mode && mode == 1)
                {
                    var selection = (ProductList.SelectedItem as AlipayPreviewProduct)?.Id;
                    ProductList.ItemsSource = _catalogs[mode]!.Products;
                    ProductList.SelectedItem = _catalogs[mode]!.Products.FirstOrDefault(p => p.Id == selection) ?? _catalogs[mode]!.Products.FirstOrDefault();
                }
            }
            if (_orders[mode] is { } old)
            {
                var response = await _client.OrderAsync(Identity(mode), old.Order.Id, _closing.Token);
                _orders[mode] = response with { CheckoutUrl = response.CheckoutUrl ?? old.CheckoutUrl };
            }
            if (mode == Mode && generation == _generation) StatusText.Text = "已读取本地服务状态。返回此窗口会自动刷新，也可以点击右上角刷新。";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (_ready && mode == Mode) StatusText.Text = error.Message; }
        finally { if (_ready) { SetBusy(false); Render(); } }
    }
    private async void Activate_Click(object sender, RoutedEventArgs e) => await CreateOrderAsync(activation: true);
    private async void Buy_Click(object sender, RoutedEventArgs e) => await CreateOrderAsync(activation: false);
    private async Task CreateOrderAsync(bool activation)
    {
        if (_busy || (activation ? Mode != 0 : Mode != 1)) return;
        var mode = Mode; var selected = ProductList.SelectedItem as AlipayPreviewProduct;
        if (!activation && selected is null) return;
        SetBusy(true); StatusText.Text = "正在创建本地订单…";
        try
        {
            var response = await _client.CreateOrderAsync(Identity(mode), activation, selected?.Id, _closing.Token);
            _orders[mode] = response;
            // Save only order recovery data in the dedicated preview directory. No credentials or activation codes are written.
            File.WriteAllText(OrderPath(mode), JsonSerializer.Serialize(new AlipayPreviewSavedOrder(response.Order.Id, response.CheckoutUrl), AlipayPreviewConfig.JsonOptions));
            Render(); OpenCheckout(response);
            StatusText.Text = "商品网页已打开。核对商品后，在网页点击付款并前往支付宝；完成后返回窗口查看交付状态。";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (_ready) StatusText.Text = error.Message; }
        finally { if (_ready) { SetBusy(false); Render(); } }
    }
    private void OpenCheckout_Click(object sender, RoutedEventArgs e)
    {
        try { if (_orders[Mode] is { } response) OpenCheckout(response); }
        catch (Exception error) { StatusText.Text = error.Message; }
    }
    private static void OpenCheckout(AlipayPreviewOrderResponse response)
    {
        var uri = AlipayPreviewClient.ValidateCheckoutUrl(response.CheckoutUrl ?? string.Empty, response.Order.Id);
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
    private void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ActivationCode.Text)) { System.Windows.Clipboard.SetText(ActivationCode.Text); StatusText.Text = "已复制本地测试订单交付的激活码。"; }
    }
    private void SetBusy(bool value) { _busy = value; RefreshButton.IsEnabled = !value; ModeTabs.IsEnabled = !value; Render(); }
    private void Render()
    {
        if (!_ready) return;
        var catalog = _catalogs[Mode];
        ProviderText.Text = catalog?.Provider is { } provider
            ? provider.Configured ? "支付宝服务已配置，订单和付款状态以服务端返回为准。" : $"支付宝尚未配置：{provider.Reason ?? "需要商户配置"}。可先查看网页订单。"
            : "正在读取本地支付服务…";
        if (_catalogs[0]?.Activation is { } activation)
        {
            ActivationDescription.Text = activation.Description; ActivationPrice.Text = $"¥{activation.AmountFen / 100m:F2}";
        }
        var selected = ProductList.SelectedItem as AlipayPreviewProduct;
        var activationIdentityActivated = _catalogs[0]?.Activated == true || _orders[0]?.Order.IsActivationDelivered == true;
        ActivateButton.Content = activationIdentityActivated ? "此本地身份已激活" : "激活桌搭子 →";
        ActivateButton.IsEnabled = !_busy && _catalogs[0] is not null && !activationIdentityActivated;
        BuyButton.Visibility = Mode == 1 ? Visibility.Visible : Visibility.Collapsed;
        BuyButton.Content = selected is null ? "选择桌宠后前往网页 →" : $"购买 {selected.PriceLabel} · 去网页查看 →";
        BuyButton.IsEnabled = !_busy && selected is not null;
        if (_orders[Mode] is not { } response)
        {
            OrderTitle.Text = Mode == 0 ? _catalogs[0]?.Activation.Title ?? "激活桌搭子" : selected?.Title ?? "先选择一只桌宠";
            OrderPrice.Text = Mode == 0 ? ActivationPrice.Text : selected?.PriceLabel ?? "";
            OrderStatus.Text = "还没有创建订单"; OrderDetail.Text = "创建后会在浏览器打开商品确认页。";
            OpenCheckoutButton.IsEnabled = false; CodePanel.Visibility = Visibility.Collapsed; return;
        }
        var order = response.Order;
        var delivery = order.FulfillmentStatus ?? order.DeliveryStatus;
        OrderTitle.Text = order.Title; OrderPrice.Text = $"¥{order.AmountFen / 100m:F2}";
        OrderStatus.Text = order.Status switch
        {
            "paid" when delivery == "fulfilled" => "已付款 · 已交付",
            "paid" when delivery == "failed" => "已付款 · 交付待重试",
            "paid" => "已付款 · 等待交付状态同步", "fulfilled" => "已付款 · 已交付", "pending_payment" => "等待付款", "pending" => "等待付款",
            "expired" => "订单已过期", "canceled" => "订单已取消", "closed" => "订单已关闭", "failed" => "订单未完成", _ => order.Status
        };
        var code = order.IsActivationDelivered ? order.ActivationCode ?? response.ActivationCode : null;
        OrderDetail.Text = $"订单号：{order.Id}\n" + (order.ExpiresAt == default ? "" : $"有效期至：{order.ExpiresAt.ToLocalTime():MM-dd HH:mm:ss}\n")
            + (string.IsNullOrWhiteSpace(delivery) ? "" : $"交付状态：{delivery}\n")
            + (order.ActivationDeliveryMessage ?? (order.Kind == "pet" && order.Status is "paid" or "fulfilled"
                ? "桌宠权益由本地服务交付，当前预览窗口不改动正式桌宠。" : "付款结果以服务端确认为准。"));
        CodePanel.Visibility = !string.IsNullOrWhiteSpace(code) ? Visibility.Visible : Visibility.Collapsed;
        ActivationCode.Text = code ?? "";
        OpenCheckoutButton.IsEnabled = !_busy && response.CheckoutUrl is not null;
    }
}
