using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ZhuoDazi.Controls;

public sealed class AnimatedGifPlayer : IDisposable
{
    private readonly System.Windows.Controls.Image _target;
    private readonly DispatcherTimer _timer;
    private IReadOnlyList<BitmapFrame> _frames = [];
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
            _frames = decoder.Frames.Select(BitmapFrame.Create).ToArray();
            _delays = decoder.Frames.Select(ReadDelay).ToArray();
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

    public void Dispose() => Stop();
}
