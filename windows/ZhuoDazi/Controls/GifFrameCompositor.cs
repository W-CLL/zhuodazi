using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZhuoDazi.Controls;

internal static class GifFrameCompositor
{
    public static IReadOnlyList<BitmapSource> Compose(IReadOnlyList<BitmapFrame> sourceFrames, int? maximumDimension = null)
    {
        if (sourceFrames.Count == 0) return [];

        var frameInfos = sourceFrames.Select(frame => new FrameInfo(frame, ReadBounds(frame))).ToArray();
        var sourceCanvasWidth = frameInfos.Max(info => Math.Max(info.Frame.PixelWidth, info.Bounds.X + info.Bounds.Width));
        var sourceCanvasHeight = frameInfos.Max(info => Math.Max(info.Frame.PixelHeight, info.Bounds.Y + info.Bounds.Height));
        var (canvasWidth, canvasHeight) = GetCanvasSize(sourceCanvasWidth, sourceCanvasHeight, maximumDimension);
        var frames = new BitmapSource[sourceFrames.Count];
        BitmapSource? canvas = null;

        for (var index = 0; index < frameInfos.Length; index++)
        {
            var info = frameInfos[index];
            var beforeFrame = canvas;
            var composed = DrawFrame(canvas, info, sourceCanvasWidth, sourceCanvasHeight, canvasWidth, canvasHeight);
            frames[index] = composed;

            // GIF frames can be deltas, so the next canvas depends on this frame's disposal method.
            canvas = ReadDisposal(info.Frame) switch
            {
                2 => ClearBounds(composed, ScaleBounds(info.Bounds, sourceCanvasWidth, sourceCanvasHeight, canvasWidth, canvasHeight), canvasWidth, canvasHeight),
                3 => beforeFrame,
                _ => composed
            };
        }

        return frames;
    }

    private static BitmapSource DrawFrame(
        BitmapSource? canvas,
        FrameInfo info,
        int sourceCanvasWidth,
        int sourceCanvasHeight,
        int canvasWidth,
        int canvasHeight)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var canvasBounds = new Rect(0, 0, canvasWidth, canvasHeight);
            if (canvas is not null) drawing.DrawImage(canvas, canvasBounds);
            drawing.DrawImage(info.Frame, GetDrawBounds(info, sourceCanvasWidth, sourceCanvasHeight, canvasBounds));
        }

        var result = new RenderTargetBitmap(canvasWidth, canvasHeight, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual);
        result.Freeze();
        return result;
    }

    private static BitmapSource ClearBounds(BitmapSource source, Int32Rect bounds, int canvasWidth, int canvasHeight)
    {
        var clipped = new Int32Rect(
            Math.Clamp(bounds.X, 0, canvasWidth),
            Math.Clamp(bounds.Y, 0, canvasHeight),
            Math.Clamp(bounds.Width, 0, canvasWidth - Math.Clamp(bounds.X, 0, canvasWidth)),
            Math.Clamp(bounds.Height, 0, canvasHeight - Math.Clamp(bounds.Y, 0, canvasHeight)));
        if (clipped.Width == 0 || clipped.Height == 0) return source;

        var bitmap = new WriteableBitmap(source);
        var stride = checked(clipped.Width * 4);
        bitmap.WritePixels(clipped, new byte[checked(stride * clipped.Height)], stride, 0);
        bitmap.Freeze();
        return bitmap;
    }

    private static Rect GetDrawBounds(FrameInfo info, int sourceCanvasWidth, int sourceCanvasHeight, Rect canvasBounds)
    {
        return info.Frame.PixelWidth == info.Bounds.Width && info.Frame.PixelHeight == info.Bounds.Height
            ? ToRect(ScaleBounds(info.Bounds, sourceCanvasWidth, sourceCanvasHeight, (int)canvasBounds.Width, (int)canvasBounds.Height))
            : canvasBounds;
    }

    private static (int Width, int Height) GetCanvasSize(int sourceWidth, int sourceHeight, int? maximumDimension)
    {
        if (maximumDimension is not > 0 || Math.Max(sourceWidth, sourceHeight) <= maximumDimension)
            return (sourceWidth, sourceHeight);

        var scale = maximumDimension.Value / (double)Math.Max(sourceWidth, sourceHeight);
        return (
            Math.Max(1, (int)Math.Round(sourceWidth * scale, MidpointRounding.AwayFromZero)),
            Math.Max(1, (int)Math.Round(sourceHeight * scale, MidpointRounding.AwayFromZero)));
    }

    private static Int32Rect ScaleBounds(Int32Rect bounds, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        var left = (int)Math.Floor(bounds.X * targetWidth / (double)sourceWidth);
        var top = (int)Math.Floor(bounds.Y * targetHeight / (double)sourceHeight);
        var right = (int)Math.Ceiling((bounds.X + bounds.Width) * targetWidth / (double)sourceWidth);
        var bottom = (int)Math.Ceiling((bounds.Y + bounds.Height) * targetHeight / (double)sourceHeight);
        return new Int32Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    private static Rect ToRect(Int32Rect bounds) => new(bounds.X, bounds.Y, bounds.Width, bounds.Height);

    private static Int32Rect ReadBounds(BitmapFrame frame)
    {
        var width = frame.PixelWidth;
        var height = frame.PixelHeight;
        var left = 0;
        var top = 0;
        try
        {
            if (frame.Metadata is BitmapMetadata metadata)
            {
                left = ReadMetadataValue(metadata, "/imgdesc/Left", left);
                top = ReadMetadataValue(metadata, "/imgdesc/Top", top);
                width = ReadMetadataValue(metadata, "/imgdesc/Width", width);
                height = ReadMetadataValue(metadata, "/imgdesc/Height", height);
            }
        }
        catch { }

        return new Int32Rect(Math.Max(0, left), Math.Max(0, top), Math.Max(1, width), Math.Max(1, height));
    }

    private static int ReadDisposal(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata metadata)
                return ReadMetadataValue(metadata, "/grctlext/Disposal", 0);
        }
        catch { }
        return 0;
    }

    private static int ReadMetadataValue(BitmapMetadata metadata, string query, int fallback)
    {
        var value = metadata.GetQuery(query);
        return value is null ? fallback : Convert.ToInt32(value);
    }

    private sealed record FrameInfo(BitmapFrame Frame, Int32Rect Bounds);
}
