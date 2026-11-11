using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZhuoDazi.Controls;

internal static class Program
{
    private static object? _retained;

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--measure")
        {
            Measure(args[1], args[2], int.Parse(args[3]), int.Parse(args[4]));
            return;
        }

        var paths = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "assets"), "*.gif")
            .Concat(args).ToArray();
        Require(paths.Length > 20, "Default GIF test assets are missing.");
        long originalBytes = 0, storedBytes = 0;
        var framesChecked = 0;
        var disposalMethods = new HashSet<int>();
        foreach (var path in paths)
        {
            using var input = File.OpenRead(path);
            var source = new GifBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None).Frames;
            foreach (var frame in source)
                if (frame.Metadata is BitmapMetadata metadata && metadata.GetQuery("/grctlext/Disposal") is { } disposal)
                    disposalMethods.Add(Convert.ToInt32(disposal));
            foreach (var size in new[] { 48, 300 })
            {
                var expected = LegacyGifFrameCompositor.Compose(source, size);
                var actual = GifPlaybackFrames.Create(GifFrameCompositor.ComposeFrames(source, size))!;
                Require(actual.Count == expected.Count, "Frame count changed.");
                var frameBytes = actual.Bitmap.PixelWidth * actual.Bitmap.PixelHeight * 4;
                originalBytes += frameBytes * (long)actual.Count;
                storedBytes += actual.StoredBytes + 2L * frameBytes;
                var reference = new byte[frameBytes];
                var pixels = new byte[frameBytes];
                for (var index = 0; index < expected.Count; index++)
                {
                    actual.Show(index);
                    expected[index].CopyPixels(reference, actual.Bitmap.PixelWidth * 4, 0);
                    actual.Bitmap.CopyPixels(pixels, actual.Bitmap.PixelWidth * 4, 0);
                    Require(reference.AsSpan().SequenceEqual(pixels), $"Changed pixels: {Path.GetFileName(path)} frame {index} size {size}.");
                    framesChecked++;
                }
                actual.Show(0);
                expected[0].CopyPixels(reference, actual.Bitmap.PixelWidth * 4, 0);
                actual.Bitmap.CopyPixels(pixels, actual.Bitmap.PixelWidth * 4, 0);
                Require(reference.AsSpan().SequenceEqual(pixels), "Loop restart retained previous pixels.");
            }
        }
        CheckDisposalThree();
        CheckLargeCanvas();
        var image = new System.Windows.Controls.Image { Width = 128, Height = 128 };
        var player = new AnimatedGifPlayer(image);
        Require(player.Load(paths[0]), "Player could not load.");
        Require(image.Source is not null, "Player did not display a frame.");
        player.Stop();
        Require(image.Source is null, "Stop retained the visible bitmap.");
        Require(player.Load(paths[^1]), "Stopped player could not reload.");
        player.Dispose();
        Require(image.Source is null, "Dispose retained the visible bitmap.");
        player.Dispose();
        try { player.Load(paths[0]); throw new Exception("Disposed player restarted."); }
        catch (ObjectDisposedException) { }
        CheckHiddenPreview(paths[0]);
        Console.WriteLine(JsonSerializer.Serialize(new { Files = paths.Length, FramesChecked = framesChecked,
            DisposalMethods = disposalMethods.Order().ToArray(), OriginalFrameBytes = originalBytes,
            NewStoredAndDisplayBytes = storedBytes, SavingPercent = Math.Round(100.0 * (originalBytes - storedBytes) / originalBytes, 2) }));
        Console.WriteLine("GIF pixel compatibility, restart, disposal and lifecycle checks passed.");
    }

    private static void CheckHiddenPreview(string path)
    {
        var preview = new AnimatedGifImage { Width = 64, Height = 64, FilePath = path };
        var panel = new System.Windows.Controls.Grid(); panel.Children.Add(preview);
        var host = new System.Windows.Window { Content = panel, Width = 80, Height = 80,
            Left = -32000, Top = -32000, ShowInTaskbar = false, ShowActivated = false,
            Opacity = 0, WindowStyle = System.Windows.WindowStyle.None };
        try
        {
            Require(preview.Source is null, "Unloaded preview eagerly decoded its GIF.");
            host.Show(); host.UpdateLayout();
            host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            Require(preview.Source is not null, "Visible preview did not start.");
            panel.Visibility = System.Windows.Visibility.Collapsed;
            Require(preview.Source is null, "Hidden tab retained its GIF bitmap.");
            panel.Visibility = System.Windows.Visibility.Visible;
            Require(preview.Source is not null, "Returning to a tab did not restart its preview.");
            host.Hide();
            Require(preview.Source is null, "Hidden window retained its preview bitmap.");
        }
        finally { host.Close(); }
    }

    private static void CheckLargeCanvas()
    {
        // A 2x DPI display must keep the requested resolution, without a
        // low-memory size cap or a different transparency/composition result.
        const int width = 600, height = 420;
        var pixels = new byte[width * height * 4];
        new Random(173).NextBytes(pixels);
        for (var index = 3; index < pixels.Length; index += 4) pixels[index] = 255;
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var frames = new[] { BitmapFrame.Create(image), BitmapFrame.Create(image) };
        foreach (var dimension in new[] { 300, 600 })
        {
            var expected = LegacyGifFrameCompositor.Compose(frames, dimension);
            var actual = GifPlaybackFrames.Create(GifFrameCompositor.ComposeFrames(frames, dimension))!;
            Require(actual.Bitmap.PixelWidth == dimension, "Display resolution was reduced.");
            var left = new byte[actual.Bitmap.PixelWidth * actual.Bitmap.PixelHeight * 4];
            var right = new byte[left.Length];
            foreach (var index in new[] { 1, 0 })
            {
                expected[index].CopyPixels(left, actual.Bitmap.PixelWidth * 4, 0);
                actual.Show(index);
                actual.Bitmap.CopyPixels(right, actual.Bitmap.PixelWidth * 4, 0);
                Require(left.AsSpan().SequenceEqual(right), "High DPI/noisy frame pixels changed.");
            }
        }
    }

    private static void CheckDisposalThree()
    {
        var frames = new List<BitmapFrame>();
        foreach (var (left, top, color, disposal) in new[] {
            (0, 0, Colors.Red, 1), (1, 1, Colors.Blue, 3), (2, 2, Colors.Green, 1), (0, 1, Colors.Yellow, 2), (2, 0, Colors.Black, 1) })
        {
            var pixels = new byte[] { color.B, color.G, color.R, 255 };
            var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 4);
            var metadata = new BitmapMetadata("gif");
            metadata.SetQuery("/imgdesc/Left", (ushort)left);
            metadata.SetQuery("/imgdesc/Top", (ushort)top);
            metadata.SetQuery("/imgdesc/Width", (ushort)1);
            metadata.SetQuery("/imgdesc/Height", (ushort)1);
            metadata.SetQuery("/grctlext/Disposal", (byte)disposal);
            frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
        }
        var actual = GifPlaybackFrames.Create(GifFrameCompositor.ComposeFrames(frames))!;
        var pixelsAfter = new byte[36];
        actual.Show(1); actual.Bitmap.CopyPixels(pixelsAfter, 12, 0);
        Require(pixelsAfter[16] == 255 && pixelsAfter[19] == 255, "Disposal 3 source frame was not drawn.");
        actual.Show(2); actual.Bitmap.CopyPixels(pixelsAfter, 12, 0);
        Require(pixelsAfter[19] == 0 && pixelsAfter[3] == 255 && pixelsAfter[35] == 255, "Disposal 3 did not restore previous canvas.");
        actual.Show(4); actual.Bitmap.CopyPixels(pixelsAfter, 12, 0);
        Require(pixelsAfter[15] == 0 && pixelsAfter[11] == 255, "Disposal 2 did not clear its bounds.");
    }

    private static void Measure(string mode, string path, int size, int copies)
    {
        // Identical GC settling in both isolated benchmark processes measures
        // retained memory; the production player never forces a collection.
        Settle();
        var process = Process.GetCurrentProcess(); process.Refresh();
        var initialPrivate = process.PrivateMemorySize64;
        var initialWorking = process.WorkingSet64;
        var items = new List<object>();
        var stopwatch = Stopwatch.StartNew();
        long cacheBytes = 0;
        int frameCount = 0;
        for (var i = 0; i < copies; i++)
        {
            using var input = mode == "old" ? new MemoryStream(File.ReadAllBytes(path)) : (Stream)File.OpenRead(path);
            var source = new GifBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat,
                mode == "old" ? BitmapCacheOption.OnLoad : BitmapCacheOption.None).Frames;
            frameCount = source.Count;
            if (mode == "old")
            {
                var frames = LegacyGifFrameCompositor.Compose(source, size);
                items.Add(frames);
                cacheBytes += frames.Sum(frame => 4L * frame.PixelWidth * frame.PixelHeight);
            }
            else
            {
                var frames = GifPlaybackFrames.Create(GifFrameCompositor.ComposeFrames(source, size))!;
                items.Add(frames);
                cacheBytes += frames.StoredBytes + 8L * frames.Bitmap.PixelWidth * frames.Bitmap.PixelHeight;
            }
        }
        stopwatch.Stop();
        _retained = items;
        process.Refresh();
        var loadPrivate = process.PrivateMemorySize64;
        Settle(); process.Refresh();
        var retainedPrivate = process.PrivateMemorySize64;
        var retainedWorking = process.WorkingSet64;
        var cpu = Stopwatch.StartNew();
        if (mode == "new")
            foreach (GifPlaybackFrames frames in items)
                for (var loop = 0; loop < 3; loop++)
                    for (var index = 0; index < frames.Count; index++) frames.Show(index);
        cpu.Stop();
        Console.WriteLine(JsonSerializer.Serialize(new { Mode = mode, File = Path.GetFileName(path), Size = size, Copies = copies,
            FrameCount = frameCount, FrameCacheBytes = cacheBytes, LoadMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
            InitialPrivateBytes = initialPrivate, LoadPrivateBytes = loadPrivate, RetainedPrivateBytes = retainedPrivate,
            InitialWorkingBytes = initialWorking, RetainedWorkingBytes = retainedWorking,
            PlaybackMillisecondsPerFrame = mode == "new" ? cpu.Elapsed.TotalMilliseconds / (copies * frameCount * 3) : 0 }));
        GC.KeepAlive(_retained);
    }

    private static void Settle() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
