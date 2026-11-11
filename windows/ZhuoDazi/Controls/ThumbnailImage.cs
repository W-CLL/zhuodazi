using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZhuoDazi.Controls;

/// <summary>Small static catalog covers, decoded only while their page is visible.</summary>
public sealed class ThumbnailImage : System.Windows.Controls.Image
{
    public static readonly DependencyProperty FilePathProperty = DependencyProperty.Register(
        nameof(FilePath), typeof(string), typeof(ThumbnailImage),
        new PropertyMetadata(null, (sender, _) => ((ThumbnailImage)sender).RefreshImage()));
    private Window? _host;
    private string? _loadedPath;

    public string? FilePath
    {
        get => (string?)GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    public ThumbnailImage()
    {
        Loaded += (_, _) =>
        {
            _host = Window.GetWindow(this);
            if (_host is not null) _host.StateChanged += HostStateChanged;
            RefreshImage();
        };
        Unloaded += (_, _) =>
        {
            if (_host is not null) _host.StateChanged -= HostStateChanged;
            _host = null;
            ReleaseImage();
        };
        IsVisibleChanged += (_, _) => RefreshImage();
    }

    private void HostStateChanged(object? sender, EventArgs e) => RefreshImage();

    private void RefreshImage()
    {
        if (!IsLoaded || !IsVisible || _host?.WindowState == WindowState.Minimized || string.IsNullOrWhiteSpace(FilePath))
        {
            ReleaseImage();
            return;
        }
        if (string.Equals(_loadedPath, FilePath, StringComparison.OrdinalIgnoreCase)) return;
        ReleaseImage();
        try
        {
            using var stream = File.OpenRead(FilePath);
            var metadata = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            var limit = (int)Math.Ceiling(160 * Math.Max(1, VisualTreeHelper.GetDpi(this).DpiScaleX));
            var landscape = metadata.PixelWidth >= metadata.PixelHeight;
            var dimension = Math.Min(limit, Math.Max(metadata.PixelWidth, metadata.PixelHeight));
            stream.Position = 0;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            if (landscape) bitmap.DecodePixelWidth = dimension;
            else bitmap.DecodePixelHeight = dimension;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            Source = bitmap;
            _loadedPath = FilePath;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException
            or ArgumentException or FileFormatException or System.Runtime.InteropServices.COMException)
        {
            ReleaseImage();
        }
    }

    private void ReleaseImage() { Source = null; _loadedPath = null; }
}
