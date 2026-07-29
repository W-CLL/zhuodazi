using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZhuoDazi.Controls;
using ZhuoDazi.Services;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        CheckGifLibraryRandomization();
        CheckPartialGifComposition();
        CheckNetworkErrorsDoNotExposeServiceAddress();
        CheckManagedEd25519Verification();
        var reproductionPath = Environment.GetEnvironmentVariable("ZHUODAZI_GIF_REPRO_PATH");
        if (!string.IsNullOrWhiteSpace(reproductionPath))
        {
            int? maximumDimension = int.TryParse(Environment.GetEnvironmentVariable("ZHUODAZI_GIF_REPRO_MAX_DIMENSION"), out var value)
                ? value
                : null;
            CheckPartialGifComposition(reproductionPath, requireRetainedPixels: false, maximumDimension);
        }
        Console.WriteLine("Windows checks passed.");
    }

    private static void CheckManagedEd25519Verification()
    {
        var publicKey = Convert.FromHexString(
            "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a");
        var signature = Convert.FromHexString(
            "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e06522490155" +
            "5fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b");

        Require(Ed25519SignatureVerifier.Verify(publicKey, [], signature),
            "The managed Ed25519 verifier rejected a valid signature.");
        signature[0] ^= 0x01;
        Require(!Ed25519SignatureVerifier.Verify(publicKey, [], signature),
            "The managed Ed25519 verifier accepted a tampered signature.");
    }

    private static void CheckNetworkErrorsDoNotExposeServiceAddress()
    {
        const string serviceAddress = "8.134.130.155";
        var error = new HttpRequestException($"The SSL connection to {serviceAddress} could not be established.");
        var message = NetworkConnectionErrors.Format(error, "连接超时");

        Require(!message.Contains(serviceAddress, StringComparison.Ordinal),
            "A network error exposed the update service address.");
        Require(message.Contains("直接网络", StringComparison.Ordinal),
            "A network error did not explain that the app uses the computer's direct network.");
    }

    private static void CheckGifLibraryRandomization()
    {
        var files = Enumerable.Range(1, 100).Select(index => $"pet-{index:000}.gif").ToArray();
        var library = new GifLibraryService();

        var firstCycle = TakeCycle(library, files);
        Require(firstCycle.Distinct(StringComparer.OrdinalIgnoreCase).Count() == files.Length,
            "The first cycle repeated a GIF before exhausting the library.");

        var secondCycle = TakeCycle(library, files, firstCycle[^1]);
        Require(secondCycle[0] != firstCycle[^1],
            "The first GIF of a new cycle repeated the previous cycle's final GIF.");
        Require(secondCycle.Distinct(StringComparer.OrdinalIgnoreCase).Count() == files.Length,
            "The second cycle repeated a GIF before exhausting the library.");

        var single = new[] { "only.gif" };
        Require(library.Pick(single, single[0]) == single[0],
            "A one-item library did not return its only GIF.");

        var changed = new[] { "new-a.gif", "new-b.gif", "new-c.gif" };
        var changedSelections = TakeCycle(library, changed);
        Require(changedSelections.All(changed.Contains),
            "A selection from the previous library survived a pool change.");
        Require(changedSelections.Distinct(StringComparer.OrdinalIgnoreCase).Count() == changed.Length,
            "The changed library repeated a GIF before exhausting its pool.");
    }

    private static void CheckPartialGifComposition(string? gifPath = null, bool requireRetainedPixels = true, int? maximumDimension = null)
    {
        gifPath ??= Path.Combine(AppContext.BaseDirectory, "assets", "partial-frame.gif");
        using var stream = File.OpenRead(gifPath);
        var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var sourceFrames = decoder.Frames.ToArray();
        var composedFrames = GifFrameCompositor.Compose(sourceFrames, maximumDimension);
        var canvasWidth = sourceFrames[0].PixelWidth;
        var canvasHeight = sourceFrames[0].PixelHeight;
        var (expectedWidth, expectedHeight) = GetExpectedCanvasSize(canvasWidth, canvasHeight, maximumDimension);

        Require(composedFrames.Count == sourceFrames.Length, "GIF composition dropped frames.");
        Require(composedFrames.All(frame => frame.PixelWidth == expectedWidth && frame.PixelHeight == expectedHeight),
            "Composed GIF frames do not preserve the logical canvas size.");

        var scaledFrames = GifFrameCompositor.Compose(sourceFrames, 48);
        Require(scaledFrames.All(frame => Math.Max(frame.PixelWidth, frame.PixelHeight) <= 48),
            "Scaled GIF frames exceeded their requested display size.");

        if (!requireRetainedPixels) return;

        var hasRetainedPixels = Enumerable.Range(1, sourceFrames.Length - 1)
            .Where(index => IsPartialFrame(sourceFrames[index], canvasWidth, canvasHeight)
                && ReadDisposal(sourceFrames[index - 1]) == 1)
            .Any(index => CountOpaquePixels(composedFrames[index]) > CountOpaquePixels(sourceFrames[index]));
        Require(hasRetainedPixels, "A partial GIF frame was displayed without the previous frame's pixels.");
    }

    private static string[] TakeCycle(GifLibraryService library, IReadOnlyList<string> files, string? previous = null)
    {
        var selections = new string[files.Count];
        for (var index = 0; index < selections.Length; index++)
        {
            selections[index] = library.Pick(files, previous)
                ?? throw new InvalidOperationException("Selection unexpectedly returned null.");
            Require(previous is null || files.Count == 1
                || !string.Equals(selections[index], previous, StringComparison.OrdinalIgnoreCase),
                "Two consecutive selections returned the same GIF.");
            previous = selections[index];
        }
        return selections;
    }

    private static bool IsPartialFrame(BitmapFrame frame, int canvasWidth, int canvasHeight)
    {
        if (frame.Metadata is not BitmapMetadata metadata) return false;
        return Convert.ToInt32(metadata.GetQuery("/imgdesc/Width")) < canvasWidth
            || Convert.ToInt32(metadata.GetQuery("/imgdesc/Height")) < canvasHeight;
    }

    private static int ReadDisposal(BitmapFrame frame)
    {
        if (frame.Metadata is not BitmapMetadata metadata) return 0;
        return Convert.ToInt32(metadata.GetQuery("/grctlext/Disposal"));
    }

    private static int CountOpaquePixels(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[checked(converted.PixelWidth * converted.PixelHeight * 4)];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        return pixels.Where((_, index) => index % 4 == 3 && pixels[index] != 0).Count();
    }

    private static (int Width, int Height) GetExpectedCanvasSize(int sourceWidth, int sourceHeight, int? maximumDimension)
    {
        if (maximumDimension is not > 0 || Math.Max(sourceWidth, sourceHeight) <= maximumDimension)
            return (sourceWidth, sourceHeight);

        var scale = maximumDimension.Value / (double)Math.Max(sourceWidth, sourceHeight);
        return (
            Math.Max(1, (int)Math.Round(sourceWidth * scale, MidpointRounding.AwayFromZero)),
            Math.Max(1, (int)Math.Round(sourceHeight * scale, MidpointRounding.AwayFromZero)));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
