using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ZhuoDazi.Models;
using Brush = System.Windows.Media.Brush;
using RadioButton = System.Windows.Controls.RadioButton;

namespace ZhuoDazi;

public partial class ResourcePackView : System.Windows.Controls.UserControl, IDisposable
{
    private readonly AppController _controller;
    private readonly CancellationTokenSource _lifetime = new();
    private List<ShopResourcePack> _packs = [];
    private string _category = "all";
    private string? _busyPackId;
    private bool? _twoColumnLayout;
    private bool _loaded;
    private bool _loadFailed;
    private bool _busy;
    private bool _disposed;

    internal ResourcePackView(AppController controller)
    {
        _controller = controller;
        InitializeComponent();
    }

    private async void View_Loaded(object sender, RoutedEventArgs e)
    {
        _controller.StateChanged -= Controller_StateChanged;
        _controller.StateChanged += Controller_StateChanged;
        ApplyLayout(ContentViewport.ActualWidth);
        if (!_loaded) await LoadAsync(); else RefreshCards();
    }

    private void View_Unloaded(object sender, RoutedEventArgs e) => _controller.StateChanged -= Controller_StateChanged;
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();
    private void Filter_Changed(object sender, TextChangedEventArgs e) => RefreshCards();

    private void Category_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton button)
            _category = button.Tag?.ToString() ?? "all";
        RefreshCards();
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        _category = "all";
        SearchBox.Text = "";
        AllCategory.IsChecked = true;
        RefreshCards();
        SearchBox.Focus();
    }

    private void View_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyLayout(e.NewSize.Width);

    private void Controller_StateChanged()
    {
        if (_disposed || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(RefreshCards));
            return;
        }
        RefreshCards();
    }

    private void ApplyLayout(double availableWidth)
    {
        if (PackList is null || availableWidth <= 0) return;
        var useTwoColumns = availableWidth >= 760;
        if (_twoColumnLayout == useTwoColumns) return;
        _twoColumnLayout = useTwoColumns;
        PackList.ItemsPanel = (ItemsPanelTemplate)FindResource(
            useTwoColumns ? "TwoColumnItemsPanel" : "OneColumnItemsPanel");
    }

    private async Task LoadAsync()
    {
        if (_busy || _disposed) return;
        _busy = true;
        _busyPackId = null;
        _loadFailed = false;
        RefreshCards();
        StatusText.Text = "正在获取免费内容…";
        try
        {
            _packs = await _controller.LoadResourcePacksAsync(_lifetime.Token);
            _loaded = true;
            StatusText.Text = $"共 {_packs.Count} 个免费资源包。安装不会自动启用。";
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            _loadFailed = true;
            StatusText.Text = FriendlyError(error);
        }
        finally
        {
            _busy = false;
            RefreshCards();
        }
    }

    private void RefreshCards()
    {
        if (PackList is null || SearchBox is null || _disposed) return;
        var query = SearchBox.Text.Trim();
        var visible = _packs.Where(pack => (_category == "all" || pack.Category == _category)
            && (query.Length == 0 || pack.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || pack.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                || pack.CategoryLabel.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();

        PackList.ItemsSource = visible.Select(pack => new ResourceCard(
            pack,
            _controller.IsResourcePackInstalled(pack),
            _controller.IsResourcePackEnabled(pack),
            _busy,
            _busyPackId == pack.Id)).ToList();

        var showLoading = _busy && visible.Count == 0;
        var hasFilters = query.Length > 0 || _category != "all";
        LoadingStatePanel.Visibility = showLoading ? Visibility.Visible : Visibility.Collapsed;
        EmptyStatePanel.Visibility = !_busy && visible.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        PackList.Visibility = visible.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SearchPlaceholder.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearFiltersButton.Visibility = _packs.Count > 0 && hasFilters
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (_loadFailed && !_loaded)
        {
            EmptyStateTitle.Text = "暂时无法获取内容";
            EmptyStateText.Text = "请检查本地后台连接，然后点击刷新重试。";
        }
        else if (_packs.Count == 0)
        {
            EmptyStateTitle.Text = "暂无可下载内容";
            EmptyStateText.Text = "后台上架后会显示在这里。";
        }
        else
        {
            EmptyStateTitle.Text = "没有找到匹配内容";
            EmptyStateText.Text = "换个关键词或分类试试。";
        }

        RefreshButton.IsEnabled = !_busy;
        RefreshButton.Content = _busy && _busyPackId is null ? "刷新中…" : "刷新";
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not System.Windows.Controls.Button { Tag: ShopResourcePack pack }) return;
        _busy = true;
        _busyPackId = pack.Id;
        RefreshCards();
        StatusText.Text = $"正在下载并安装“{pack.Title}”…";
        try
        {
            await _controller.DownloadAndInstallResourcePackAsync(pack, _lifetime.Token);
            StatusText.Text = $"“{pack.Title}”已安装，点击“启用”后才会使用。";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { StatusText.Text = FriendlyError(error); }
        finally
        {
            _busy = false;
            _busyPackId = null;
            RefreshCards();
        }
    }

    private void Enable_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not System.Windows.Controls.Button { Tag: ShopResourcePack pack }) return;
        try
        {
            var enable = !_controller.IsResourcePackEnabled(pack);
            if (_controller.SetResourcePackEnabled(pack, enable))
                StatusText.Text = enable ? $"已启用“{pack.Title}”。" : $"已停用“{pack.Title}”，安装内容已保留。";
        }
        catch (Exception error) { StatusText.Text = FriendlyError(error); }
        RefreshCards();
    }

    private static string FriendlyError(Exception error) => error is InvalidOperationException
        ? error.Message
        : "操作暂未完成，请检查本地后台连接后重试。";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _controller.StateChanged -= Controller_StateChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private sealed record ResourceCard(
        ShopResourcePack Pack,
        bool Installed,
        bool Enabled,
        bool Busy,
        bool CurrentBusy)
    {
        private static readonly Brush WordsBackground = FrozenBrush("#E3F2EB");
        private static readonly Brush WordsForeground = FrozenBrush("#167D6C");
        private static readonly Brush WordsBorder = FrozenBrush("#B4D8CB");
        private static readonly Brush TheaterBackground = FrozenBrush("#FFF0E3");
        private static readonly Brush TheaterForeground = FrozenBrush("#A65D2B");
        private static readonly Brush TheaterBorder = FrozenBrush("#E7C5AA");
        private static readonly Brush NeutralBackground = FrozenBrush("#F1F4F3");
        private static readonly Brush NeutralForeground = FrozenBrush("#5F6C68");
        private static readonly Brush NeutralBorder = FrozenBrush("#D6DEDB");
        private static readonly Brush PendingBackground = FrozenBrush("#FFF4D8");
        private static readonly Brush PendingForeground = FrozenBrush("#765A11");
        private static readonly Brush PendingBorder = FrozenBrush("#E6C875");
        private static readonly Brush EnabledBackground = FrozenBrush("#E2F1EA");
        private static readonly Brush EnabledForeground = FrozenBrush("#126452");
        private static readonly Brush EnabledBorder = FrozenBrush("#9BCDBD");

        public string StateText => !Installed ? "尚未安装" : Enabled ? "已安装 · 已启用" : "已安装 · 待启用";
        public string DownloadLabel => CurrentBusy ? "正在安装…" : "免费安装";
        public string EnableLabel => Enabled ? "停用" : "启用";
        public string MetaText => $"{Pack.FileCount} 个内容 · {FormatSize(Pack.Size)} · {FormatVersion(Pack.Version)}";
        public bool CanDownload => !Installed && !Busy;
        public bool CanEnable => Installed && !Busy;
        public Visibility DownloadVisibility => Installed ? Visibility.Collapsed : Visibility.Visible;
        public Visibility EnableVisibility => Installed ? Visibility.Visible : Visibility.Collapsed;
        public Brush CategoryBackground => Pack.IsWords ? WordsBackground : TheaterBackground;
        public Brush CategoryForeground => Pack.IsWords ? WordsForeground : TheaterForeground;
        public Brush CategoryBorder => Pack.IsWords ? WordsBorder : TheaterBorder;
        public Brush StateBackground => !Installed ? NeutralBackground : Enabled ? EnabledBackground : PendingBackground;
        public Brush StateForeground => !Installed ? NeutralForeground : Enabled ? EnabledForeground : PendingForeground;
        public Brush StateBorder => !Installed ? NeutralBorder : Enabled ? EnabledBorder : PendingBorder;

        private static string FormatSize(long bytes)
        {
            if (bytes <= 0) return "大小未知";
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024d:0.#} KB";
            return $"{bytes / (1024d * 1024d):0.#} MB";
        }

        private static string FormatVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) return "版本未知";
            return $"v{version.Trim().TrimStart('v', 'V')}";
        }

        private static SolidColorBrush FrozenBrush(string color)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
            brush.Freeze();
            return brush;
        }
    }
}
