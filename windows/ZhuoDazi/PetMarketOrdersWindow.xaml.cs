using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ZhuoDazi.Models;

namespace ZhuoDazi;

internal sealed record PetMarketOrderInstallPresentation(string Text, bool Installed, bool InProgress, bool CanRetry);

internal sealed class PetMarketOrderRow
{
    internal required PetMarketOrder Order { get; init; }
    public string ProductTitle { get; init; } = string.Empty;
    public string StatusLabel { get; init; } = string.Empty;
    public string AmountLabel { get; init; } = string.Empty;
    public string CreatedLabel { get; init; } = string.Empty;
    internal PetMarketOrderInstallPresentation Install { get; init; } = new(string.Empty, false, false, false);
}

public partial class PetMarketOrdersWindow : Window
{
    private PetMarketOrderRow? _selected;
    private string? _displayedOrderId;
    private DateTimeOffset _serverNow = DateTimeOffset.UtcNow;
    private string? _cachedQrDataUrl;
    private BitmapImage? _cachedQrImage;
    private bool _hasCachedQrDecode;
    private bool _cachedQrDecodeSucceeded;
    private bool _updatingOrders;

    internal event Action<PetMarketOrder>? ProductRequested;
    internal event Action<PetMarketOrder>? RetryInstallRequested;

    internal PetMarketOrdersWindow()
    {
        InitializeComponent();
    }

    internal void UpdateOrders(
        IReadOnlyList<PetMarketOrder> orders,
        DateTimeOffset serverNow,
        Func<PetMarketOrder, string> productTitle,
        Func<PetMarketOrder, PetMarketOrderInstallPresentation> installPresentation)
    {
        _serverNow = serverNow;
        var selectedId = _selected?.Order.Id;
        var rows = orders
            .OrderByDescending(order => order.CreatedAt)
            .Select(order => new PetMarketOrderRow
            {
                Order = order,
                ProductTitle = productTitle(order),
                StatusLabel = StatusLabel(order, serverNow),
                AmountLabel = AmountLabel(order),
                CreatedLabel = FormatTime(order.CreatedAt),
                Install = installPresentation(order)
            })
            .ToList();
        var selected = rows.FirstOrDefault(row => row.Order.Id == selectedId) ?? rows.FirstOrDefault();
        _updatingOrders = true;
        try
        {
            OrderList.ItemsSource = rows;
            EmptyPanel.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            OrderList.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            OrderList.SelectedItem = selected;
        }
        finally { _updatingOrders = false; }
        ShowOrder(selected);
    }

    internal void SetConnectionWarning(string? message)
    {
        ConnectionWarningText.Text = message ?? string.Empty;
        ConnectionWarningText.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OrderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingOrders) ShowOrder(OrderList.SelectedItem as PetMarketOrderRow);
    }

    private void ShowOrder(PetMarketOrderRow? row)
    {
        var nextOrderId = row?.Order.Id;
        var orderChanged = !string.Equals(_displayedOrderId, nextOrderId, StringComparison.Ordinal);
        var detailOffset = DetailScrollViewer.VerticalOffset;
        _selected = row;
        _displayedOrderId = nextOrderId;
        SelectOrderText.Visibility = row is null ? Visibility.Visible : Visibility.Collapsed;
        DetailScrollViewer.Visibility = row is null ? Visibility.Collapsed : Visibility.Visible;
        if (row is null) return;

        var order = row.Order;
        ProductTitleText.Text = row.ProductTitle;
        OrderStatusText.Text = StatusLabel(order, _serverNow);
        AmountText.Text = AmountLabel(order);
        ReferenceText.Text = string.IsNullOrWhiteSpace(order.PaymentReference)
            ? "不可用" : order.PaymentReference.ToUpperInvariant();
        ReferencePanel.Visibility = string.IsNullOrWhiteSpace(order.PaymentReference)
            ? Visibility.Collapsed : Visibility.Visible;

        PaidSuccessPanel.Visibility = order.Status == "paid" ? Visibility.Visible : Visibility.Collapsed;
        InstallStatusText.Text = row.Install.Text;
        RetryInstallButton.Visibility = order.Status == "paid" && row.Install.CanRetry
            ? Visibility.Visible : Visibility.Collapsed;
        RetryInstallButton.IsEnabled = !row.Install.InProgress;

        var payment = order.Payment;
        RecipientNameText.Text = string.IsNullOrWhiteSpace(payment?.RecipientName) ? "未提供" : payment.RecipientName;
        RecipientAccountText.Text = payment?.RecipientAccount ?? string.Empty;
        RecipientAccountRow.Visibility = string.IsNullOrWhiteSpace(payment?.RecipientAccount)
            ? Visibility.Collapsed : Visibility.Visible;
        PaymentInstructionsText.Text = payment?.Instructions ?? string.Empty;
        PaymentInstructionsText.Visibility = string.IsNullOrWhiteSpace(payment?.Instructions)
            ? Visibility.Collapsed : Visibility.Visible;
        QrImage.Source = TryGetQrImage(payment, out var qrImage) ? qrImage : null;
        QrPanel.Visibility = QrImage.Source is null ? Visibility.Collapsed : Visibility.Visible;

        var warning = PendingWarning(order, payment, QrImage.Source is not null, _serverNow);
        PaymentWarningText.Text = warning;
        PaymentWarningPanel.Visibility = string.IsNullOrWhiteSpace(warning)
            ? Visibility.Collapsed : Visibility.Visible;

        OrderIdText.Text = order.Id;
        VersionText.Text = string.IsNullOrWhiteSpace(order.ProductVersion) ? "-" : order.ProductVersion;
        CreatedAtText.Text = FormatTime(order.CreatedAt);
        ExpiresAtText.Text = FormatTime(order.ExpiresAt);
        ExpiresAtRow.Visibility = order.ExpiresAt == default ? Visibility.Collapsed : Visibility.Visible;
        PaidAtText.Text = order.PaidAt is { } paidAt ? FormatTime(paidAt) : string.Empty;
        PaidAtRow.Visibility = order.PaidAt is null ? Visibility.Collapsed : Visibility.Visible;
        CanceledAtText.Text = order.CanceledAt is { } canceledAt ? FormatTime(canceledAt) : string.Empty;
        CanceledAtRow.Visibility = order.CanceledAt is null ? Visibility.Collapsed : Visibility.Visible;
        if (orderChanged) DetailScrollViewer.ScrollToTop();
        else DetailScrollViewer.ScrollToVerticalOffset(detailOffset);
    }

    private void CopyReference_Click(object sender, RoutedEventArgs e)
        => CopyText(_selected?.Order.PaymentReference);

    private void CopyRecipient_Click(object sender, RoutedEventArgs e)
        => CopyText(_selected?.Order.Payment?.RecipientAccount);

    private static void CopyText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        try { System.Windows.Clipboard.SetText(value); } catch { }
    }

    private void OpenProduct_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not null) ProductRequested?.Invoke(_selected.Order);
    }

    private void RetryInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not null) RetryInstallRequested?.Invoke(_selected.Order);
    }

    private static string StatusLabel(PetMarketOrder order, DateTimeOffset serverNow)
        => order.Status switch
        {
            "paid" => "已付款",
            "canceled" => "已取消",
            "expired" => "已过期",
            "pending_payment" when order.ExpiresAt <= serverNow => "已过期",
            "pending_payment" => "待付款",
            _ => "状态待确认"
        };

    private static string AmountLabel(PetMarketOrder order)
        => string.Equals(order.Currency, "CNY", StringComparison.OrdinalIgnoreCase)
            ? $"¥{order.AmountFen / 100m:0.00}"
            : $"{order.Currency} {order.AmountFen / 100m:0.00}";

    private static string FormatTime(DateTimeOffset value)
        => value == default ? "-" : value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    private static string PendingWarning(
        PetMarketOrder order, PetMarketPaymentInfo? payment, bool qrImageAvailable, DateTimeOffset serverNow)
    {
        if (order.Status != "pending_payment" || order.ExpiresAt <= serverNow) return string.Empty;
        if (payment is null) return "当前未取得收款信息，请等待连接恢复后再付款。";
        if (!payment.Enabled || !payment.Available || !payment.ListenerOnline)
            return "付款核对暂不可用，请等待恢复后再付款。";
        if (payment.IsPersonalTransfer && !payment.HasRecipient)
            return "收款人信息不完整，请等待恢复后再付款。";
        if (payment.IsCollectionQr && !qrImageAvailable)
            return "收款二维码无法显示，请等待恢复后再付款。";
        return string.Empty;
    }

    private static bool TryCreateQrImage(PetMarketPaymentInfo? payment, out BitmapImage? image)
    {
        image = null;
        if (payment is not { IsCollectionQr: true } || !payment.TryGetQrCodeBytes(out var bytes)) return false;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 300;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0) return false;
            bitmap.Freeze();
            image = bitmap;
            return true;
        }
        catch { return false; }
    }

    private bool TryGetQrImage(PetMarketPaymentInfo? payment, out BitmapImage? image)
    {
        image = null;
        if (payment is not { IsCollectionQr: true }) return false;
        var dataUrl = payment.QrCodeDataUrl ?? string.Empty;
        if (_hasCachedQrDecode && string.Equals(_cachedQrDataUrl, dataUrl, StringComparison.Ordinal))
        {
            image = _cachedQrImage;
            return _cachedQrDecodeSucceeded;
        }
        _hasCachedQrDecode = true;
        _cachedQrDataUrl = dataUrl;
        _cachedQrImage = null;
        _cachedQrDecodeSucceeded = TryCreateQrImage(payment, out var decodedImage);
        if (_cachedQrDecodeSucceeded) _cachedQrImage = decodedImage;
        image = _cachedQrImage;
        return _cachedQrDecodeSucceeded;
    }
}
