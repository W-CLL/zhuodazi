using System.Drawing;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using WpfControls = System.Windows.Controls;
using DrawingFontStyle = System.Drawing.FontStyle;
using DrawingPoint = System.Drawing.Point;
using Forms = System.Windows.Forms;
using Media = System.Windows.Media;
using ZhuoDazi.Interop;

namespace ZhuoDazi;

public partial class FakeAdWindow : Window
{
    private static readonly Color SurfaceColor = Color.FromArgb(18, 18, 18);
    private static readonly Color ChromeColor = Color.FromArgb(36, 36, 36);
    private readonly DispatcherTimer _watchTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Forms.Panel _surface = new();
    private readonly Forms.Panel _videoHost = new();
    private readonly Forms.Label _dropHint = new();
    private readonly Window _adOverlay = new();
    private readonly WpfControls.TextBlock _adTitle = new();
    private readonly WpfControls.TextBlock _adSubtitle = new();
    private readonly Forms.Button _adAction = new();
    private readonly Forms.ToolTip _toolTips = new();
    private nint _embeddedWindow;
    private NativeMethods.EmbeddedWindowState? _embeddedState;
    private readonly Dictionary<nint, NativeMethods.NativeRect> _douyinBounds = [];
    private readonly HashSet<nint> _movedDouyinWindows = [];
    private bool _compact;
    private bool _closing;

    private static readonly AdSkin[] Skins =
    [
        new("新客专享红包", "最高 88 元，今日名额有限", "马上领取"),
        new("免费领7天会员", "学习 / 影视 / 音乐任选", "马上领取"),
        new("限时秒杀 · 仅剩2小时", "爆款办公低至 3 折", "立即抢购")
    ];

    public FakeAdWindow(AppController _)
    {
        InitializeComponent();
        BuildAdOverlay();
        BuildSurface();
        NativeHost.Child = _surface;
        Loaded += OnLoaded;
        Closed += OnClosed;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void BuildAdOverlay()
    {
        _adOverlay.WindowStyle = WindowStyle.None;
        _adOverlay.ResizeMode = ResizeMode.NoResize;
        _adOverlay.AllowsTransparency = true;
        _adOverlay.Background = Media.Brushes.Transparent;
        _adOverlay.ShowInTaskbar = false;
        _adOverlay.ShowActivated = false;
        _adOverlay.Topmost = true;
        _adOverlay.SizeToContent = SizeToContent.WidthAndHeight;
        _adOverlay.SourceInitialized += (_, _) => NativeMethods.SetClickThrough(_adOverlay, true);

        var copy = new WpfControls.Border
        {
            Background = new Media.SolidColorBrush(Media.Color.FromArgb(132, 0, 0, 0)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(7, 4, 7, 4),
            MaxWidth = 320,
            IsHitTestVisible = false
        };
        var stack = new WpfControls.StackPanel();

        _adTitle.Foreground = Media.Brushes.White;
        _adTitle.FontFamily = new Media.FontFamily("Microsoft YaHei UI");
        _adTitle.FontSize = 17;
        _adTitle.FontWeight = FontWeights.Bold;
        _adTitle.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(_adTitle);

        _adSubtitle.Foreground = new Media.SolidColorBrush(Media.Color.FromRgb(235, 235, 235));
        _adSubtitle.FontFamily = new Media.FontFamily("Microsoft YaHei UI");
        _adSubtitle.FontSize = 12;
        _adSubtitle.Margin = new Thickness(0, 1, 0, 0);
        _adSubtitle.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(_adSubtitle);

        copy.Child = stack;
        _adOverlay.Content = copy;

        LocationChanged += (_, _) => UpdateAdOverlay();
        SizeChanged += (_, _) => UpdateAdOverlay();
        IsVisibleChanged += (_, _) => UpdateAdOverlay();
        StateChanged += (_, _) => UpdateAdOverlay();
    }

    private void BuildSurface()
    {
        _surface.BackColor = SurfaceColor;
        _surface.Dock = Forms.DockStyle.Fill;

        _videoHost.BackColor = SurfaceColor;
        _videoHost.Dock = Forms.DockStyle.Fill;
        _videoHost.BorderStyle = Forms.BorderStyle.FixedSingle;
        _surface.Controls.Add(_videoHost);

        _dropHint.Dock = Forms.DockStyle.Fill;
        _dropHint.Text = "广告素材加载中…";
        _dropHint.TextAlign = ContentAlignment.MiddleCenter;
        _dropHint.ForeColor = Color.FromArgb(210, 210, 210);
        _dropHint.BackColor = SurfaceColor;
        _dropHint.Font = new Font("Microsoft YaHei UI", 11, DrawingFontStyle.Regular);
        _videoHost.Controls.Add(_dropHint);

        var topBar = new Forms.Panel
        {
            Anchor = Forms.AnchorStyles.Top | Forms.AnchorStyles.Left | Forms.AnchorStyles.Right,
            Height = 28,
            BackColor = ChromeColor,
            Cursor = Forms.Cursors.SizeAll
        };
        topBar.MouseDown += (_, e) =>
        {
            if (e.Button != Forms.MouseButtons.Left) return;
            try { DragMove(); } catch (InvalidOperationException) { }
        };
        _surface.Controls.Add(topBar);

        var adTag = new Forms.Label
        {
            AutoSize = true,
            Text = "广告",
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Location = new DrawingPoint(10, 6),
            Font = new Font("Microsoft YaHei UI", 9, DrawingFontStyle.Regular)
        };
        topBar.Controls.Add(adTag);

        var close = new Forms.Button
        {
            Text = "×",
            Dock = Forms.DockStyle.Right,
            Width = 32,
            FlatStyle = Forms.FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = ChromeColor,
            Font = new Font("Microsoft YaHei UI", 11),
            TabStop = false
        };
        close.FlatAppearance.BorderSize = 0;
        close.Click += (_, _) => Close();
        _toolTips.SetToolTip(close, "关闭");
        topBar.Controls.Add(close);

        var hide = CreateChromeButton("—", "隐藏");
        hide.Click += (_, _) => Hide();
        topBar.Controls.Add(hide);

        var resize = CreateChromeButton("▣", "缩小 / 恢复");
        resize.Click += (_, _) => ToggleSize();
        topBar.Controls.Add(resize);

        var move = CreateChromeButton("↕", "按住拖动");
        move.Cursor = Forms.Cursors.SizeAll;
        move.MouseDown += (_, e) =>
        {
            if (e.Button != Forms.MouseButtons.Left) return;
            try { DragMove(); } catch (InvalidOperationException) { }
        };
        topBar.Controls.Add(move);

        _adAction.Text = "马上查看";
        _adAction.Width = 96;
        _adAction.Height = 28;
        _adAction.Anchor = Forms.AnchorStyles.Bottom;
        _adAction.FlatStyle = Forms.FlatStyle.Flat;
        _adAction.FlatAppearance.BorderSize = 0;
        _adAction.BackColor = Color.White;
        _adAction.ForeColor = Color.FromArgb(25, 25, 25);
        _adAction.Font = new Font("Microsoft YaHei UI", 9, DrawingFontStyle.Bold);
        _adAction.Cursor = Forms.Cursors.Hand;
        _adAction.Click += (_, _) => ApplySkin(Skins[Random.Shared.Next(Skins.Length)]);
        _surface.Controls.Add(_adAction);
        _surface.Resize += (_, _) =>
        {
            topBar.Width = _surface.ClientSize.Width;
            _adAction.Location = new DrawingPoint(
                Math.Max(0, (_surface.ClientSize.Width - _adAction.Width) / 2),
                Math.Max(0, _surface.ClientSize.Height - _adAction.Height - 12));
        };
        _videoHost.Resize += (_, _) => ResizeEmbeddedWindow();

        ApplySkin(Skins[Random.Shared.Next(Skins.Length)]);
        _surface.Controls.SetChildIndex(_videoHost, _surface.Controls.Count - 1);
        _adAction.BringToFront();
        topBar.BringToFront();
    }

    private Forms.Button CreateChromeButton(string text, string toolTip)
    {
        var button = new Forms.Button
        {
            Text = text,
            Dock = Forms.DockStyle.Right,
            Width = 32,
            FlatStyle = Forms.FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = ChromeColor,
            Font = new Font("Microsoft YaHei UI", 10),
            TabStop = false
        };
        button.FlatAppearance.BorderSize = 0;
        _toolTips.SetToolTip(button, toolTip);
        return button;
    }

    private void ToggleSize()
    {
        var right = Left + ActualWidth;
        var bottom = Top + ActualHeight;
        _compact = !_compact;
        Width = _compact ? 440 : 560;
        Height = _compact ? 280 : 360;
        Left = Math.Max(SystemParameters.WorkArea.Left, right - Width);
        Top = Math.Max(SystemParameters.WorkArea.Top, bottom - Height);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RestorePosition();
        _adOverlay.Owner = this;
        UpdateAdOverlay();
        _watchTimer.Tick += WatchForDouyin;
        _watchTimer.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closing = true;
        _watchTimer.Stop();
        CloseDouyin();
        if (_adOverlay.IsVisible) _adOverlay.Close();
        NativeHost.Child = null;
        _toolTips.Dispose();
        _surface.Dispose();
    }

    private void WatchForDouyin(object? sender, EventArgs e)
    {
        if (_closing) return;
        if (_embeddedWindow != nint.Zero)
        {
            if (!NativeMethods.IsWindow(_embeddedWindow))
            {
                _embeddedWindow = nint.Zero;
                _embeddedState = null;
                _dropHint.Visible = true;
            }
            else ResizeEmbeddedWindow();
            return;
        }

        var hostRect = _videoHost.RectangleToScreen(_videoHost.ClientRectangle);
        foreach (var candidate in NativeMethods.FindDouyinWindows())
        {
            if (!NativeMethods.GetWindowRect(candidate, out var windowRect)) continue;
            if (!_douyinBounds.TryGetValue(candidate, out var previous))
            {
                _douyinBounds[candidate] = windowRect;
                continue;
            }
            if (windowRect.Left != previous.Left || windowRect.Top != previous.Top
                || windowRect.Right != previous.Right || windowRect.Bottom != previous.Bottom)
            {
                _movedDouyinWindows.Add(candidate);
                _douyinBounds[candidate] = windowRect;
            }
            if (!_movedDouyinWindows.Contains(candidate) || NativeMethods.IsLeftMouseButtonDown()) continue;
            if (!IsDropTarget(windowRect, hostRect)) continue;

            AttachDouyin(candidate);
            return;
        }
    }

    private static bool IsDropTarget(NativeMethods.NativeRect windowRect, Rectangle hostRect)
    {
        var center = new DrawingPoint((windowRect.Left + windowRect.Right) / 2, (windowRect.Top + windowRect.Bottom) / 2);
        if (hostRect.Contains(center)) return true;

        var intersection = Rectangle.Intersect(hostRect, windowRect.ToRectangle());
        return intersection.Width * intersection.Height >= hostRect.Width * hostRect.Height / 3;
    }

    private void AttachDouyin(nint handle)
    {
        var state = NativeMethods.CaptureEmbeddedWindowState(handle);
        if (state is null) return;
        NativeMethods.SetEmbeddedWindowStyle(handle);
        if (!NativeMethods.TrySetParent(handle, _videoHost.Handle, out var error))
        {
            NativeMethods.RestoreEmbeddedWindowStyle(handle, state.Value);
            _movedDouyinWindows.Remove(handle);
            _dropHint.Text = error == 5
                ? "请完全退出抖音并重新打开，再拖入此处"
                : "暂时无法载入抖音窗口，请重新拖入";
            return;
        }

        _embeddedWindow = handle;
        _embeddedState = state;
        _dropHint.Visible = false;
        ResizeEmbeddedWindow();
        NativeMethods.ShowWindow(handle, NativeMethods.SwShowNoActivate);
    }

    private void ResizeEmbeddedWindow()
    {
        if (_embeddedWindow == nint.Zero) return;
        NativeMethods.SetWindowPos(
            _embeddedWindow,
            nint.Zero,
            0,
            0,
            _videoHost.ClientSize.Width,
            _videoHost.ClientSize.Height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
    }

    private void CloseDouyin()
    {
        if (_embeddedWindow == nint.Zero || _embeddedState is null) return;
        var handle = _embeddedWindow;
        var state = _embeddedState.Value;
        _embeddedWindow = nint.Zero;
        _embeddedState = null;
        if (!NativeMethods.IsWindow(handle)) return;

        NativeMethods.SetParent(handle, state.Parent);
        NativeMethods.RestoreEmbeddedWindowStyle(handle, state);
        NativeMethods.SetWindowPos(
            handle,
            nint.Zero,
            state.Bounds.Left,
            state.Bounds.Top,
            state.Bounds.Width,
            state.Bounds.Height,
            NativeMethods.SwpNoActivate);
        NativeMethods.CloseWindow(handle);
    }

    private void ApplySkin(AdSkin skin)
    {
        _adTitle.Text = skin.Title;
        _adSubtitle.Text = skin.Subtitle;
        _adAction.Text = skin.Action;
    }

    private void UpdateAdOverlay()
    {
        if (_closing || !IsLoaded || !IsVisible || WindowState == WindowState.Minimized)
        {
            if (_adOverlay.IsVisible) _adOverlay.Hide();
            return;
        }

        if (!_adOverlay.IsVisible) _adOverlay.Show();
        _adOverlay.Left = Left + 10;
        _adOverlay.Top = Top + 34;
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    private void RestorePosition()
    {
        var area = SystemParameters.WorkArea;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = area.Right - Width - 16;
        Top = area.Bottom - Height - 16;
    }

    private readonly record struct AdSkin(string Title, string Subtitle, string Action);
}
