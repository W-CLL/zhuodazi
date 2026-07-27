using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ZhuoDazi.Controls;

public sealed class AnimatedGifPlayer : IDisposable
{
    private readonly System.Windows.Controls.Image _target;
    private readonly DispatcherTimer _timer;
    private IReadOnlyList<BitmapSource> _frames = [];
    private IReadOnlyList<TimeSpan> _delays = [];
    private int _index;

    public AnimatedGifPlayer(System.Windows.Controls.Image target)
    {
        _target = target;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += OnTick;
    }

    public bool Load(string? filePath)
    {
        Stop();
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;
        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(filePath));
            var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var sourceFrames = decoder.Frames.ToArray();
            _frames = GifFrameCompositor.Compose(sourceFrames, GetMaximumDimension());
            _delays = sourceFrames.Select(ReadDelay).ToArray();
            if (_frames.Count == 0) return false;
            _index = 0;
            _target.Source = _frames[0];
            if (_frames.Count > 1)
            {
                _timer.Interval = _delays[0];
                _timer.Start();
            }
            return true;
        }
        catch
        {
            Stop();
            return false;
        }
    }

    public void Stop()
    {
        _timer.Stop();
        _frames = [];
        _delays = [];
        _target.Source = null;
        _index = 0;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_frames.Count < 2) return;
        _index = (_index + 1) % _frames.Count;
        _target.Source = _frames[_index];
        _timer.Interval = _delays[_index];
    }

    private static TimeSpan ReadDelay(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata metadata && metadata.GetQuery("/grctlext/Delay") is ushort delay)
                return TimeSpan.FromMilliseconds(Math.Max(20, delay * 10));
        }
        catch { }
        return TimeSpan.FromMilliseconds(100);
    }

    private int? GetMaximumDimension()
    {
        var parent = _target.Parent as FrameworkElement;
        var width = FirstValidDimension(_target.ActualWidth, _target.Width, parent?.ActualWidth, parent?.Width);
        var height = FirstValidDimension(_target.ActualHeight, _target.Height, parent?.ActualHeight, parent?.Height);
        if (width is null && height is null) return null;

        var dpiScale = Math.Max(1, VisualTreeHelper.GetDpi(_target).DpiScaleX);
        return Math.Max(1, (int)Math.Ceiling(Math.Max(width ?? 0, height ?? 0) * dpiScale * 1.5));
    }

    private static double? FirstValidDimension(params double?[] dimensions)
    {
        foreach (var dimension in dimensions)
        {
            if (dimension is not { } value || value <= 0 || double.IsNaN(value) || double.IsInfinity(value)) continue;
            return value;
        }
        return null;
    }

    public void Dispose() => Stop();
}
