using System.Diagnostics;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ZhuoDazi.Models;
using WpfMessageBox = System.Windows.MessageBox;

namespace ZhuoDazi;

public partial class PetMarketView : System.Windows.Controls.UserControl, IDisposable
{
    private readonly AppController _controller;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Stopwatch _serverClock = Stopwatch.StartNew();
    private readonly List<PetMarketCardViewModel> _cards = [];
    private readonly List<PetMarketOrder> _orders = [];
    private readonly HashSet<string> _automaticInstallInProgress = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _automaticInstallErrors = new(StringComparer.Ordinal);
    private CancellationTokenSource? _viewSession;
    private ICollectionView? _catalogView;
    private PetMarketOrdersWindow? _ordersWindow;
    private PetMarketCatalog? _catalog;
    private PetMarketProduct? _product;
    private PetMarketOrder? _order;
    private DateTimeOffset _serverAtSync = DateTimeOffset.UtcNow;
    private DateTimeOffset _nextPollAt = DateTimeOffset.MinValue;
    private string _filter = "all";
    private int _previewIndex;
    private int _previewRequestId;
    private int _thumbnailGeneration;
    private int _busyGeneration;
    private string? _cachedQrDataUrl;
    private string? _orderHistoryConnectionWarning;
    private BitmapImage? _cachedQrImage;
    private bool _busy;
    private bool _polling;
    private bool _closed;
    private bool _viewLoaded;
    private bool _catalogRefreshNeeded;
    private bool _rebuildingCatalog;
    private bool _orderPollingUnavailable;
    private bool _hasCachedQrDecode;
    private bool _cachedQrDecodeSucceeded;

    internal PetMarketView(AppController controller)
    {
        InitializeComponent();
        _controller = controller;
        _timer.Tick += Timer_Tick;
    }

    private DateTimeOffset ServerNow => _serverAtSync + _serverClock.Elapsed;
    private CancellationToken ViewToken => _viewSession?.Token ?? _lifetime.Token;

    private async void View_Loaded(object sender, RoutedEventArgs e)
    {
        if (_closed) return;
        _viewLoaded = true;
        BeginViewSession();
        EnvironmentBadge.Visibility = _controller.IsFullAppPreview ? Visibility.Visible : Visibility.Collapsed;
        PreviewEnvironmentBadge.Visibility = EnvironmentBadge.Visibility;
        _timer.Start();
        if (_catalog is null || _catalogRefreshNeeded) await LoadCatalogAsync(_product?.Id);
        else
        {
            ApplyCatalogFilter(_product?.Id);
            ShowProduct();
            ApplyOrder();
            StartThumbnailLoading();
        }
        if (_controller.HasActivatedMarketIdentity)
            await RefreshOrderHistoryAsync(ViewToken, showError: false);
    }

    private void View_Unloaded(object sender, RoutedEventArgs e)
    {
        _viewLoaded = false;
        _previewRequestId++;
        _thumbnailGeneration++;
        _busyGeneration++;
        _busy = false;
        CloseOrdersWindow();
        EndViewSession();
    }

    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        _timer.Stop();
        _timer.Tick -= Timer_Tick;
        CloseOrdersWindow();
        EndViewSession();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private void BeginViewSession()
    {
        if (_viewSession is { IsCancellationRequested: false }) return;
        _viewSession?.Dispose();
        _viewSession = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
    }

    private void EndViewSession()
    {
        if (_viewSession is null) return;
        _viewSession.Cancel();
        _viewSession.Dispose();
        _viewSession = null;
    }

    private async Task LoadCatalogAsync(string? selectProductId = null)
    {
        if (_busy || _closed || _viewSession is null) return;
        var operation = BeginBusy();
        StatusText.Text = "正在加载小铺…";
        var token = ViewToken;
        try
        {
            await FetchCatalogAsync(selectProductId, token, showSuccessStatus: true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { ShowCatalogError(error); }
        finally { EndBusy(operation); }
    }

    private async Task FetchCatalogAsync(string? selectProductId, CancellationToken token, bool showSuccessStatus)
    {
        var session = _viewSession;
        var catalog = await _controller.LoadPetMarketCatalogAsync(token);
        if (_closed || token.IsCancellationRequested || !ReferenceEquals(_viewSession, session)) return;
        _catalog = catalog;
        _catalogRefreshNeeded = false;
        _rebuildingCatalog = true;
        try
        {
            _cards.Clear();
            _cards.AddRange(catalog.Products.Select(product => new PetMarketCardViewModel(product)));
            ProductList.ItemsSource = _cards;
            _catalogView = CollectionViewSource.GetDefaultView(ProductList.ItemsSource);
            _catalogView.Filter = item => item is PetMarketCardViewModel card
                && card.Matches(SearchBox.Text.Trim(), _filter);
            ApplyCatalogFilter(selectProductId ?? _product?.Id ?? catalog.DefaultProductId);
        }
        finally { _rebuildingCatalog = false; }
        if (ProductList.SelectedItem is null)
        {
            _product = null;
            _order = null;
            _orderPollingUnavailable = false;
            ShowProduct();
            ApplyOrder();
        }
        ActivationBanner.Visibility = _controller.HasActivatedMarketIdentity
            ? Visibility.Collapsed : Visibility.Visible;
        if (showSuccessStatus)
            StatusText.Text = catalog.Products.Count == 0 ? "小铺暂时没有可展示的桌宠。" : $"已上架 {catalog.Products.Count} 个桌宠。";
        StartThumbnailLoading();
    }

    private void StartThumbnailLoading()
    {
        if (_viewSession is null || _cards.Count == 0) return;
        var generation = ++_thumbnailGeneration;
        _ = LoadThumbnailsAsync(generation, ViewToken);
    }

    private async Task LoadThumbnailsAsync(int generation, CancellationToken token)
    {
        foreach (var card in _cards.ToArray())
        {
            if (_closed || token.IsCancellationRequested || generation != _thumbnailGeneration) return;
            if (card.ThumbnailPath is not null || card.Product.PreviewFiles.Count == 0) continue;
            try
            {
                var preview = await _controller.DownloadPetMarketPreviewAsync(
                    card.Product, card.Product.PreviewFiles[0], token);
                if (!_closed && !token.IsCancellationRequested && generation == _thumbnailGeneration)
                    card.ThumbnailPath = preview;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch { }
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadCatalogAsync(_product?.Id);
        if (_controller.HasActivatedMarketIdentity)
            await RefreshOrderHistoryAsync(ViewToken, showError: true);
    }

    private void Orders_Click(object sender, RoutedEventArgs e)
    {
        if (_ordersWindow is null)
        {
            _ordersWindow = new PetMarketOrdersWindow { Owner = Window.GetWindow(this) };
            _ordersWindow.ProductRequested += FocusOrderProduct;
            _ordersWindow.RetryInstallRequested += RetryAutomaticInstall;
            _ordersWindow.Closed += (_, _) => _ordersWindow = null;
        }
        UpdateOrdersWindow();
        _ordersWindow.Show();
        if (_ordersWindow.WindowState == WindowState.Minimized) _ordersWindow.WindowState = WindowState.Normal;
        _ordersWindow.Activate();
    }

    private async void Activation_Click(object sender, RoutedEventArgs e)
    {
        if (_controller.ShowActivation(Window.GetWindow(this), "正式激活后可以领取、购买、下载、安装和启用小铺桌宠。"))
        {
            await LoadCatalogAsync(_product?.Id);
            await RefreshOrderHistoryAsync(ViewToken, showError: true);
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyCatalogFilter();

    private void Filter_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.RadioButton { Tag: string filter }) _filter = filter;
        ApplyCatalogFilter();
    }

    private void ApplyCatalogFilter(string? preferredProductId = null)
    {
        if (_catalogView is null) return;
        var selectedId = preferredProductId
            ?? (ProductList.SelectedItem as PetMarketCardViewModel)?.Id
            ?? _product?.Id;
        _catalogView.Refresh();
        var visible = _catalogView.Cast<PetMarketCardViewModel>().ToList();
        EmptyStatePanel.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (visible.Count == 0)
        {
            var hasProducts = _catalog?.Products.Count > 0;
            EmptyStateTitle.Text = hasProducts ? "没有找到桌宠" : "小铺暂时空空的";
            EmptyStateText.Text = hasProducts ? "换个关键词或筛选条件试试。" : "稍后刷新看看有没有新角色。";
            ProductList.SelectedItem = null;
            return;
        }
        var selected = visible.FirstOrDefault(card => card.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase))
            ?? visible.First();
        if (!ReferenceEquals(ProductList.SelectedItem, selected)) ProductList.SelectedItem = selected;
        ProductList.ScrollIntoView(selected);
    }

    private async void ProductList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var next = (ProductList.SelectedItem as PetMarketCardViewModel)?.Product;
        if (_rebuildingCatalog && next is null) return;
        _product = next;
        _order = next is null ? null : FindPendingOrder(next.Id);
        _orderPollingUnavailable = !string.IsNullOrWhiteSpace(_orderHistoryConnectionWarning);
        _previewIndex = 0;
        if (_order is null) PaymentPanel.Visibility = Visibility.Collapsed;
        ShowProduct();
        ApplyOrder();
        await LoadPreviewAsync();
    }

    private async Task LoadPreviewAsync()
    {
        var requestId = ++_previewRequestId;
        var token = ViewToken;
        if (_product is null)
        {
            PreviewImage.FilePath = null;
            PreviewCountText.Text = "0/0";
            PreviousPreviewButton.IsEnabled = false;
            NextPreviewButton.IsEnabled = false;
            return;
        }
        var selected = _product;
        var count = selected.PreviewFiles.Count;
        _previewIndex = count == 0 ? 0 : Math.Clamp(_previewIndex, 0, count - 1);
        var selectedPreviewIndex = _previewIndex;
        PreviewCountText.Text = count == 0 ? "暂无预览" : $"{_previewIndex + 1}/{count}";
        PreviousPreviewButton.IsEnabled = count > 1 && _previewIndex > 0;
        NextPreviewButton.IsEnabled = count > 1 && _previewIndex < count - 1;
        PreviewImage.FilePath = null;
        if (count == 0) return;
        try
        {
            var preview = await _controller.DownloadPetMarketPreviewAsync(
                selected, selected.PreviewFiles[selectedPreviewIndex], token);
            if (!_closed && !token.IsCancellationRequested && requestId == _previewRequestId
                && ReferenceEquals(_product, selected) && _previewIndex == selectedPreviewIndex)
                PreviewImage.FilePath = preview;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch { }
    }

    private async void PreviousPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_previewIndex <= 0) return;
        _previewIndex--;
        await LoadPreviewAsync();
    }

    private async void NextPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_product is null || _previewIndex + 1 >= _product.PreviewFiles.Count) return;
        _previewIndex++;
        await LoadPreviewAsync();
    }

    private void ShowProduct()
    {
        if (_product is null)
        {
            ProductTitleText.Text = "选择一个桌宠";
            ProductPriceText.Text = string.Empty;
            ProductOwnedBadge.Visibility = Visibility.Collapsed;
            DetailTags.ItemsSource = null;
            ProductDescriptionText.Text = string.Empty;
            ProductMetaText.Text = string.Empty;
            RefreshActions();
            return;
        }
        var card = _cards.FirstOrDefault(item => ReferenceEquals(item.Product, _product));
        ProductTitleText.Text = _product.Title;
        ProductPriceText.Text = _product.PriceLabel;
        ProductOwnedBadge.Visibility = _product.Owned ? Visibility.Visible : Visibility.Collapsed;
        DetailTags.ItemsSource = card?.Tags ?? Array.Empty<string>();
        ProductDescriptionText.Text = _product.Description;
        ProductMetaText.Text = $"{_product.AssetCount} 个动作资源 · 版本 {_product.ActiveVersion}"
            + (_product.Status == "unlisted" ? " · 已下架但仍可下载" : string.Empty);
        RefreshActions();
    }

    private async void Claim_Click(object sender, RoutedEventArgs e)
    {
        if (_product is null || _busy) return;
        if (!await EnsureActivatedForPurchaseAsync()) return;
        var selected = _product;
        var token = ViewToken;
        await RunBusyAsync(async () =>
        {
            await _controller.ClaimMarketProductAsync(selected.Id, token);
            await FetchCatalogAsync(selected.Id, token, showSuccessStatus: false);
            StatusText.Text = "已领取，可以下载安装。";
        }, token);
    }

    private async void Buy_Click(object sender, RoutedEventArgs e)
    {
        if (_product is null || _busy) return;
        if (!await EnsureActivatedForPurchaseAsync()) return;
        if (_order is { Status: "pending_payment" } pending && pending.ProductId == _product.Id
            && pending.ExpiresAt > ServerNow)
        {
            ApplyOrder();
            ScrollPaymentIntoView();
            return;
        }
        var paymentUnavailable = NewOrderPaymentUnavailableReason();
        if (!string.IsNullOrWhiteSpace(paymentUnavailable))
        {
            StatusText.Text = paymentUnavailable;
            return;
        }
        var selected = _product;
        var token = ViewToken;
        await RunBusyAsync(async () =>
        {
            var response = await _controller.CreatePetMarketOrderAsync(selected.Id, token);
            if (!ReferenceEquals(_product, selected)) return;
            if (string.IsNullOrWhiteSpace(response.Order.ProductTitle)) response.Order.ProductTitle = selected.Title;
            _order = response.Order;
            UpsertOrder(response.Order);
            _orderPollingUnavailable = false;
            _orderHistoryConnectionWarning = null;
            SyncClock(response.ServerTime);
            _nextPollAt = ServerNow;
            ApplyOrder();
            UpdateOrdersWindow();
            ScrollPaymentIntoView();
            StatusText.Text = response.Reused ? "已恢复未过期订单。" : "订单已创建，请在 15 分钟内完成付款。";
        }, token);
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_product is null || _busy) return;
        var selected = _product;
        var token = ViewToken;
        await RunBusyAsync(async () =>
        {
            var installed = await _controller.DownloadAndInstallMarketProductAsync(selected, token);
            ClearAutomaticInstallErrors(selected.Id);
            StatusText.Text = $"{installed.Title} 已安装。需要时再点击“启用这个桌宠”。";
            ShowProduct();
            UpdateOrdersWindow();
        }, token);
    }

    private async void Use_Click(object sender, RoutedEventArgs e)
    {
        if (_product is null || _busy) return;
        var installation = _controller.FindMarketInstallation(_product.Id);
        if (installation is null) return;
        var token = ViewToken;
        await RunBusyAsync(async () =>
        {
            if (_controller.IsMarketProductEnabled(installation.ProductId))
            {
                _controller.DisableMarketProduct(installation.ProductId);
                StatusText.Text = $"已停用 {installation.Title}，已安装资源保留。";
            }
            else
            {
                await _controller.UseMarketProductAsync(installation, token);
                StatusText.Text = $"已启用 {installation.Title} · 已启用 {_controller.EnabledPetSourceCount}/3 个桌宠来源。";
            }
            ShowProduct();
        }, token);
    }

    private async Task<bool> EnsureActivatedForPurchaseAsync()
    {
        if (_controller.HasActivatedMarketIdentity) return true;
        var activated = _controller.ShowActivation(Window.GetWindow(this),
            "购买或领取桌宠需要先正式激活桌搭子。体验期可以浏览和预览，正式激活后再购买。");
        if (activated)
        {
            await LoadCatalogAsync(_product?.Id);
            StatusText.Text = "已完成激活，请再次点击购买或领取。";
        }
        return false;
    }

    private void CopyReference_Click(object sender, RoutedEventArgs e)
    {
        if (_order is null || string.IsNullOrWhiteSpace(_order.PaymentReference)) return;
        try
        {
            System.Windows.Clipboard.SetText(_order.PaymentReference.ToUpperInvariant());
            StatusText.Text = "付款短码已复制。";
        }
        catch { StatusText.Text = "复制失败，请手动输入短码。"; }
    }

    private void CopyPaymentField_Click(object sender, RoutedEventArgs e)
    {
        if (_order?.Payment is not { } payment
            || sender is not System.Windows.Controls.Button { Tag: string field }) return;
        var value = field == "name" ? payment.RecipientName
            : field == "account" ? payment.RecipientAccount : string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return;
        try
        {
            System.Windows.Clipboard.SetText(value);
            StatusText.Text = field == "name" ? "收款人昵称已复制。" : "收款微信号已复制。";
        }
        catch { StatusText.Text = "复制失败，请手动输入。"; }
    }

    private async void CancelOrder_Click(object sender, RoutedEventArgs e)
    {
        if (_order is null || _busy || _order.Status != "pending_payment") return;
        var requested = _order;
        var token = ViewToken;
        await RunBusyAsync(async () =>
        {
            var canceled = await _controller.CancelPetMarketOrderAsync(requested.Id, token);
            if (_order?.Id != requested.Id || _product?.Id != requested.ProductId) return;
            UpsertOrder(canceled);
            _order = FindPendingOrder(requested.ProductId);
            _orderPollingUnavailable = false;
            ApplyOrder();
            UpdateOrdersWindow();
            StatusText.Text = "订单已取消。";
        }, token);
    }

    private async void Timer_Tick(object? sender, EventArgs e)
    {
        if (_closed) return;
        if (_viewLoaded && _order?.Status == "pending_payment") ApplyOrder();
        if (!_controller.HasActivatedMarketIdentity || _polling || ServerNow < _nextPollAt) return;
        await RefreshOrderHistoryAsync(_lifetime.Token, showError: false);
    }

    private async Task RefreshOrderHistoryAsync(CancellationToken token, bool showError)
    {
        if (_closed || _polling || !_controller.HasActivatedMarketIdentity) return;
        _polling = true;
        _nextPollAt = ServerNow.AddSeconds(3);
        try
        {
            var response = await _controller.ListPetMarketOrdersAsync(token);
            if (_closed || token.IsCancellationRequested) return;
            SyncClock(response.ServerTime);
            MergeOrders(response.Orders);
            _nextPollAt = ServerNow.AddSeconds(HasPendingOrders() ? 3 : 30);
            _orderHistoryConnectionWarning = null;
            _orderPollingUnavailable = false;
            _order = _product is null ? null : FindPendingOrder(_product.Id);
            if (_viewLoaded)
            {
                ApplyOrder();
                RefreshActions();
                UpdateOrdersWindow();
            }
            StartAutomaticInstalls();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (_closed || token.IsCancellationRequested) return;
            _orderHistoryConnectionWarning = "订单记录暂时无法同步，请等待连接恢复。";
            _nextPollAt = ServerNow.AddSeconds(HasPendingOrders() ? 3 : 15);
            _orderPollingUnavailable = _order?.Status == "pending_payment";
            if (_viewLoaded)
            {
                ApplyOrder();
                UpdateOrdersWindow();
                if (showError) StatusText.Text = SafeError(error);
            }
        }
        finally { _polling = false; }
    }

    private void MergeOrders(IEnumerable<PetMarketOrder> orders)
    {
        foreach (var order in orders) UpsertOrder(order, sort: false);
        SortOrders();
    }

    private void UpsertOrder(PetMarketOrder order, bool sort = true)
    {
        var existingIndex = _orders.FindIndex(item => item.Id.Equals(order.Id, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            var existing = _orders[existingIndex];
            order.Payment ??= existing.Payment;
            if (string.IsNullOrWhiteSpace(order.ProductTitle)) order.ProductTitle = existing.ProductTitle;
            _orders[existingIndex] = order;
        }
        else _orders.Add(order);
        if (sort) SortOrders();
    }

    private void SortOrders()
        => _orders.Sort((left, right) =>
        {
            var byCreated = right.CreatedAt.CompareTo(left.CreatedAt);
            return byCreated != 0 ? byCreated : string.CompareOrdinal(right.Id, left.Id);
        });

    private PetMarketOrder? FindPendingOrder(string productId)
        => _orders.FirstOrDefault(order => order.ProductId.Equals(productId, StringComparison.OrdinalIgnoreCase)
            && order.Status == "pending_payment" && order.ExpiresAt > ServerNow);

    private bool HasPendingOrders()
        => _orders.Any(order => order.Status == "pending_payment" && order.ExpiresAt > ServerNow);

    private void StartAutomaticInstalls()
    {
        var candidates = _orders
            .Where(order => order.Status == "paid")
            .GroupBy(order => order.ProductId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(order => order.PaidAt ?? order.CreatedAt)
                .ThenByDescending(order => order.CreatedAt)
                .ThenByDescending(order => order.Id, StringComparer.Ordinal)
                .First())
            .Where(order => !_controller.IsPaidMarketOrderInstalled(order)
                && !_automaticInstallErrors.ContainsKey(order.Id))
            .ToList();
        foreach (var order in candidates)
            _ = EnsurePaidOrderInstalledAsync(order, explicitRetry: false, _lifetime.Token);
    }

    private void RetryAutomaticInstall(PetMarketOrder order)
    {
        if (_closed || order.Status != "paid" || !IsLatestPaidOrderForProduct(order)) return;
        _automaticInstallErrors.Remove(order.Id);
        _ = EnsurePaidOrderInstalledAsync(order, explicitRetry: true, _lifetime.Token);
    }

    private async Task EnsurePaidOrderInstalledAsync(
        PetMarketOrder order, bool explicitRetry, CancellationToken token)
    {
        if (_controller.IsPaidMarketOrderInstalled(order))
        {
            _automaticInstallErrors.Remove(order.Id);
            if (_viewLoaded) UpdateOrdersWindow();
            return;
        }
        if (!IsLatestPaidOrderForProduct(order)
            || (!explicitRetry && _automaticInstallErrors.ContainsKey(order.Id))) return;
        var installKey = AutomaticInstallKey(order);
        if (!_automaticInstallInProgress.Add(installKey)) return;
        _automaticInstallErrors.Remove(order.Id);
        if (_viewLoaded)
        {
            RefreshActions();
            UpdateOrdersWindow();
        }
        try
        {
            var installed = await _controller.EnsurePaidMarketOrderInstalledAsync(order, token);
            if (_closed || token.IsCancellationRequested) return;
            ClearAutomaticInstallErrors(order.ProductId);
            if (_viewLoaded)
            {
                StatusText.Text = $"{installed.Title} 付款验证成功，已自动下载安装；需要使用时请自行启用。";
                if (_product?.Id.Equals(order.ProductId, StringComparison.OrdinalIgnoreCase) == true)
                    ShowProduct();
                var catalogToken = ViewToken;
                try
                {
                    await FetchCatalogAsync(_product?.Id ?? order.ProductId, catalogToken, showSuccessStatus: false);
                }
                catch (OperationCanceledException) when (catalogToken.IsCancellationRequested)
                {
                    _catalogRefreshNeeded = true;
                }
                catch { _catalogRefreshNeeded = true; }
            }
            else _catalogRefreshNeeded = true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (_closed || token.IsCancellationRequested) return;
            var message = SafeError(error);
            _automaticInstallErrors[order.Id] = message;
            if (_viewLoaded)
                StatusText.Text = $"付款已确认，但自动下载安装失败：{message} 请在订单记录中重试。";
        }
        finally
        {
            _automaticInstallInProgress.Remove(installKey);
            if (_viewLoaded)
            {
                RefreshActions();
                UpdateOrdersWindow();
            }
        }
    }

    private static string AutomaticInstallKey(PetMarketOrder order)
        => order.ProductId;

    private bool IsLatestPaidOrderForProduct(PetMarketOrder order)
    {
        if (order.Status != "paid") return false;
        var latest = _orders
            .Where(item => item.Status == "paid"
                && item.ProductId.Equals(order.ProductId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.PaidAt ?? item.CreatedAt)
            .ThenByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        return latest?.Id.Equals(order.Id, StringComparison.Ordinal) == true;
    }

    private void ClearAutomaticInstallErrors(string productId)
    {
        foreach (var orderId in _orders
                     .Where(order => order.ProductId.Equals(productId, StringComparison.OrdinalIgnoreCase))
                     .Select(order => order.Id)
                     .ToList())
            _automaticInstallErrors.Remove(orderId);
    }

    private PetMarketOrderInstallPresentation InstallPresentation(PetMarketOrder order)
    {
        if (order.Status != "paid") return new(string.Empty, false, false, false);
        if (!IsLatestPaidOrderForProduct(order))
            return new("该商品已有更新的已付款订单，此版本仅保留记录。", false, false, false);
        if (_controller.IsPaidMarketOrderInstalled(order))
        {
            var enabled = _controller.IsMarketProductEnabled(order.ProductId);
            return enabled
                ? new("已自动下载安装，当前已启用。", true, false, false)
                : new("已自动下载安装，尚未启用。需要使用时请点击“启用这个桌宠”。", true, false, false);
        }
        var inProgress = _automaticInstallInProgress.Contains(AutomaticInstallKey(order));
        if (inProgress) return new("正在自动下载安装…", false, true, false);
        if (_automaticInstallErrors.TryGetValue(order.Id, out var error))
            return new($"自动下载安装失败：{error}", false, false, true);
        return new("等待自动下载安装。", false, false, true);
    }

    private string ProductTitle(PetMarketOrder order)
        => !string.IsNullOrWhiteSpace(order.ProductTitle)
            ? order.ProductTitle
            : _catalog?.Products.FirstOrDefault(product => product.Id.Equals(
                order.ProductId, StringComparison.OrdinalIgnoreCase))?.Title ?? order.ProductId;

    private void UpdateOrdersWindow()
    {
        if (_ordersWindow is null) return;
        _ordersWindow.UpdateOrders(_orders, ServerNow, ProductTitle, InstallPresentation);
        _ordersWindow.SetConnectionWarning(_orderHistoryConnectionWarning);
    }

    private void FocusOrderProduct(PetMarketOrder order)
    {
        var card = _cards.FirstOrDefault(item => item.Id.Equals(order.ProductId, StringComparison.OrdinalIgnoreCase));
        if (card is null)
        {
            StatusText.Text = $"{ProductTitle(order)} 当前未在商品列表中，订单记录仍会保留。";
            return;
        }
        SearchBox.Text = string.Empty;
        _filter = "all";
        AllFilter.IsChecked = true;
        ApplyCatalogFilter(order.ProductId);
        ProductList.SelectedItem = card;
        ProductList.ScrollIntoView(card);
        if (order.Status == "pending_payment" && order.ExpiresAt > ServerNow)
        {
            _order = order;
            _orderPollingUnavailable = !string.IsNullOrWhiteSpace(_orderHistoryConnectionWarning);
            ApplyOrder();
            ScrollPaymentIntoView();
        }
        Window.GetWindow(this)?.Activate();
    }

    private void CloseOrdersWindow()
    {
        var window = _ordersWindow;
        _ordersWindow = null;
        window?.Close();
    }

    private void ApplyOrder()
    {
        if (_order is null || _order.Status != "pending_payment" || _product?.Id != _order.ProductId)
        {
            PaymentPanel.Visibility = Visibility.Collapsed;
            PaymentQrImage.Source = null;
            return;
        }
        var remaining = _order.ExpiresAt - ServerNow;
        if (remaining <= TimeSpan.Zero)
        {
            PaymentPanel.Visibility = Visibility.Collapsed;
            PaymentQrImage.Source = null;
            StatusText.Text = "订单已到期，正在确认最终状态；如未完成付款可重新下单。";
            CancelOrderButton.IsEnabled = false;
            RefreshActions();
            return;
        }
        PaymentPanel.Visibility = Visibility.Visible;
        PaymentProductText.Text = $"商品：{_product.Title}";
        PaymentAmountText.Text = $"应付金额 ¥{_order.AmountFen / 100m:0.00}";
        PaymentReferenceText.Text = string.IsNullOrWhiteSpace(_order.PaymentReference)
            ? "短码不可用" : _order.PaymentReference.ToUpperInvariant();
        CountdownText.Text = $"剩余 {Math.Max(0, (int)remaining.TotalMinutes):00}:{remaining.Seconds:00}";
        CancelOrderButton.IsEnabled = !_busy;

        var payment = _order.Payment;
        PersonalTransferPanel.Visibility = payment is { IsPersonalTransfer: true, HasRecipient: true }
            ? Visibility.Visible : Visibility.Collapsed;
        PaymentRecipientNameText.Text = payment?.RecipientName ?? string.Empty;
        PaymentRecipientAccountText.Text = payment?.RecipientAccount ?? string.Empty;
        PaymentInstructionsText.Text = payment?.Instructions ?? string.Empty;

        var qrImageAvailable = TryGetQrImage(payment, out var qrImage);
        PaymentQrImage.Source = qrImageAvailable ? qrImage : null;
        QrPaymentPanel.Visibility = payment?.IsCollectionQr == true && qrImageAvailable
            ? Visibility.Visible : Visibility.Collapsed;
        PaymentQrRecipientPanel.Visibility = string.IsNullOrWhiteSpace(payment?.RecipientName)
            ? Visibility.Collapsed : Visibility.Visible;
        PaymentQrRecipientNameText.Text = payment?.RecipientName ?? string.Empty;
        PaymentQrInstructionsText.Text = payment?.Instructions ?? string.Empty;

        var health = PendingOrderPaymentWarning(payment, qrImageAvailable, _orderPollingUnavailable);
        PaymentHealthPanel.Visibility = string.IsNullOrWhiteSpace(health) ? Visibility.Collapsed : Visibility.Visible;
        PaymentHealthText.Text = health;
    }

    private void RefreshActions()
    {
        if (!IsInitialized) return;
        var activated = _controller.HasActivatedMarketIdentity;
        var product = _product;
        var installed = product is null ? null : _controller.FindMarketInstallation(product.Id);
        ProductList.IsEnabled = !_busy;
        SearchBox.IsEnabled = !_busy;
        AllFilter.IsEnabled = !_busy;
        FreeFilter.IsEnabled = !_busy;
        PaidFilter.IsEnabled = !_busy;
        OwnedFilter.IsEnabled = !_busy;
        RefreshButton.IsEnabled = !_busy;
        RefreshButton.Content = _busy ? "处理中…" : "刷新";
        OrdersButton.IsEnabled = activated && !_busy;
        OrdersButton.Content = _orders.Count == 0 ? "订单记录" : $"订单记录 ({_orders.Count})";
        ClaimButton.Visibility = product is { Owned: false, IsFree: true } ? Visibility.Visible : Visibility.Collapsed;
        BuyButton.Visibility = product is { Owned: false, IsFree: false } ? Visibility.Visible : Visibility.Collapsed;
        InstallButton.Visibility = product?.Owned == true ? Visibility.Visible : Visibility.Collapsed;
        UseButton.Visibility = installed is not null ? Visibility.Visible : Visibility.Collapsed;
        ClaimButton.IsEnabled = !_busy && (!activated || product?.Claimable == true);
        var hasPendingOrder = product is not null && _order is { Status: "pending_payment" }
            && _order.ProductId == product.Id && _order.ExpiresAt > ServerNow;
        var paymentUnavailable = NewOrderPaymentUnavailableReason();
        BuyButton.IsEnabled = !_busy && (!activated || hasPendingOrder
            || product?.Purchasable == true && string.IsNullOrWhiteSpace(paymentUnavailable));
        BuyButton.Content = hasPendingOrder ? "去付款" : product is null ? "购买" : $"购买 · {product.PriceLabel}";
        PaymentAvailabilityText.Visibility = product is { Owned: false, IsFree: false }
            && !hasPendingOrder && activated && !string.IsNullOrWhiteSpace(paymentUnavailable)
                ? Visibility.Visible : Visibility.Collapsed;
        PaymentAvailabilityText.Text = paymentUnavailable;
        var automaticInstalling = product is not null && _orders.Any(order => order.Status == "paid"
            && order.ProductId.Equals(product.Id, StringComparison.OrdinalIgnoreCase)
            && _automaticInstallInProgress.Contains(AutomaticInstallKey(order)));
        InstallButton.IsEnabled = activated && !_busy && !automaticInstalling && product?.Downloadable == true;
        InstallButton.Content = automaticInstalling ? "正在下载安装…"
            : installed is null ? "下载并安装" : "重新下载安装";
        UseButton.IsEnabled = activated && !_busy;
        UseButton.Content = installed is not null && _controller.IsMarketProductEnabled(installed.ProductId)
            ? "停用这个桌宠" : "启用这个桌宠";
        ActivationText.Text = activated
            ? "当前设备已正式激活。"
            : "游客和试用可以浏览预览；领取、购买、下载和启用需要正式激活。";
    }

    private string NewOrderPaymentUnavailableReason()
    {
        if (_product is null || _product.IsFree || _product.Owned) return string.Empty;
        var payment = _catalog?.Payment;
        if (payment is null) return "当前服务未提供付款信息，请刷新或更新桌搭子后再试。";
        if (!payment.Enabled)
            return string.IsNullOrWhiteSpace(payment.UnavailableReason) ? "付款服务尚未开放。" : payment.UnavailableReason;
        if (!payment.ListenerOnline)
            return string.IsNullOrWhiteSpace(payment.UnavailableReason)
                ? "付款核对暂时离线，请稍后再试。" : payment.UnavailableReason;
        if (!payment.Available)
            return string.IsNullOrWhiteSpace(payment.UnavailableReason)
                ? "付款服务暂不可用，请稍后再试。" : payment.UnavailableReason;
        if (payment.IsCollectionQr && !TryGetQrImage(payment, out _))
            return "收款二维码无法显示，请稍后刷新。";
        if (!payment.CanCreateOrder) return "收款信息不完整，请稍后刷新。";
        if (!_product.Purchasable) return "当前商品暂不可购买。";
        return string.Empty;
    }

    private static string PendingOrderPaymentWarning(
        PetMarketPaymentInfo? payment, bool qrImageAvailable, bool orderPollingUnavailable)
    {
        if (orderPollingUnavailable)
            return "暂时无法核对订单，请等待连接恢复后再付款。";
        if (payment is null)
            return "订单已保留，但当前服务未返回收款信息。请勿盲目转账，可取消后刷新再试。";
        if (payment.IsPersonalTransfer && !payment.HasRecipient)
            return "订单已保留，但收款人信息不完整。请等待恢复后再付款。";
        if (payment.IsCollectionQr && !qrImageAvailable)
            return "订单已保留，但收款二维码无效。请等待恢复后再付款。";
        if (!payment.Enabled || !payment.Available || !payment.ListenerOnline)
        {
            var reason = string.IsNullOrWhiteSpace(payment.UnavailableReason)
                ? "付款自动核对暂不可用。" : payment.UnavailableReason;
            return $"订单与短码已保留；{reason} 若尚未转账，请先等待恢复。";
        }
        if (!payment.IsPersonalTransfer && !payment.IsCollectionQr)
            return "订单已保留，但付款方式无法识别。请等待恢复后再付款。";
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
            bitmap.DecodePixelWidth = 260;
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

    private void ScrollPaymentIntoView()
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => DetailScrollViewer.ScrollToTop()));
    }

    private int BeginBusy()
    {
        var operation = ++_busyGeneration;
        _busy = true;
        RefreshActions();
        return operation;
    }

    private void EndBusy(int operation)
    {
        if (operation != _busyGeneration) return;
        _busy = false;
        RefreshActions();
    }

    private async Task RunBusyAsync(Func<Task> action, CancellationToken token)
    {
        var operation = BeginBusy();
        try { await action(); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { ShowError(error); }
        finally { EndBusy(operation); }
    }

    private void SyncClock(DateTimeOffset serverTime)
    {
        if (serverTime == default) serverTime = DateTimeOffset.UtcNow;
        _serverAtSync = serverTime;
        _serverClock.Restart();
    }

    private void ShowCatalogError(Exception error)
    {
        if (_closed) return;
        var message = SafeError(error);
        StatusText.Text = message;
        if (_cards.Count != 0) return;
        EmptyStateTitle.Text = "小铺暂时无法加载";
        EmptyStateText.Text = "请检查网络后点击刷新。";
        EmptyStatePanel.Visibility = Visibility.Visible;
    }

    private void ShowError(Exception error)
    {
        if (_closed) return;
        var message = SafeError(error);
        StatusText.Text = message;
        var owner = Window.GetWindow(this);
        if (owner is not null) WpfMessageBox.Show(owner, message, "桌宠小铺", MessageBoxButton.OK, MessageBoxImage.Warning);
        else WpfMessageBox.Show(message, "桌宠小铺", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static string SafeError(Exception error)
        => string.IsNullOrWhiteSpace(error.Message) ? "小铺操作失败，请稍后重试。" : error.Message;
}
