using System.Windows;

namespace ZhuoDazi.Controls;

public sealed class AnimatedGifImage : System.Windows.Controls.Image
{
    public static readonly DependencyProperty FilePathProperty = DependencyProperty.Register(
        nameof(FilePath),
        typeof(string),
        typeof(AnimatedGifImage),
        new PropertyMetadata(null, OnFilePathChanged));

    private readonly AnimatedGifPlayer _player;
    private string? _loadedPath;
    private Window? _host;

    public AnimatedGifImage()
    {
        _player = new AnimatedGifPlayer(this);
        Loaded += (_, _) =>
        {
            _host = Window.GetWindow(this);
            if (_host is not null) _host.StateChanged += HostStateChanged;
            RefreshPlayback();
        };
        Unloaded += (_, _) =>
        {
            if (_host is not null) _host.StateChanged -= HostStateChanged;
            _host = null;
            StopPlayback();
        };
        IsVisibleChanged += (_, _) => RefreshPlayback();
    }

    public string? FilePath
    {
        get => (string?)GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    private static void OnFilePathChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var image = (AnimatedGifImage)sender;
        image.RefreshPlayback();
    }

    private void RefreshPlayback()
    {
        if (!IsLoaded || !IsVisible || _host?.WindowState == WindowState.Minimized || string.IsNullOrWhiteSpace(FilePath))
        {
            StopPlayback();
            return;
        }
        if (_loadedPath is not null && string.Equals(_loadedPath, FilePath, StringComparison.OrdinalIgnoreCase)) return;
        _loadedPath = _player.Load(FilePath) ? FilePath : null;
    }

    private void StopPlayback()
    {
        _player.Stop();
        _loadedPath = null;
    }

    private void HostStateChanged(object? sender, EventArgs e) => RefreshPlayback();
}
