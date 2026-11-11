using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZhuoDazi.Controls;

// Preserve exact composed pixels, but keep inactive frames compressed instead of
// retaining a separate 32-bit WPF bitmap for every frame in every visible GIF.
internal sealed class GifPlaybackFrames
{
    private readonly List<StoredFrame> _frames = [];
    private readonly byte[] _pixels;
    private readonly int _stride;
    internal WriteableBitmap Bitmap { get; }
    internal int Count => _frames.Count;
    internal long StoredBytes => _frames.Sum(frame => (long)frame.Bytes.Length);

    private GifPlaybackFrames(BitmapSource first)
    {
        _stride = checked(first.PixelWidth * 4);
        _pixels = new byte[checked(_stride * first.PixelHeight)];
        Bitmap = new WriteableBitmap(first.PixelWidth, first.PixelHeight, 96, 96, PixelFormats.Pbgra32, null);
    }

    internal static GifPlaybackFrames? Create(IEnumerable<BitmapSource> composedFrames)
    {
        GifPlaybackFrames? result = null;
        byte[]? scratch = null;
        foreach (var source in composedFrames)
        {
            result ??= new GifPlaybackFrames(source);
            if (source.PixelWidth != result.Bitmap.PixelWidth || source.PixelHeight != result.Bitmap.PixelHeight
                || source.Format != PixelFormats.Pbgra32)
                throw new InvalidDataException("GIF frames must use the same premultiplied canvas.");
            scratch ??= new byte[BrotliEncoder.GetMaxCompressedLength(result._pixels.Length)];
            source.CopyPixels(result._pixels, result._stride, 0);
            var compressed = BrotliEncoder.TryCompress(result._pixels, scratch, out var length, quality: 1, window: 22)
                && length < result._pixels.Length;
            result._frames.Add(compressed
                ? new StoredFrame(scratch.AsSpan(0, length).ToArray(), true)
                : new StoredFrame((byte[])result._pixels.Clone(), false));
        }
        result?.Show(0);
        return result;
    }

    internal void Show(int index)
    {
        var frame = _frames[index];
        if (frame.Compressed)
        {
            if (!BrotliDecoder.TryDecompress(frame.Bytes, _pixels, out var written) || written != _pixels.Length)
                throw new InvalidDataException("GIF frame could not be decoded.");
        }
        else
        {
            frame.Bytes.CopyTo(_pixels, 0);
        }
        Bitmap.WritePixels(new Int32Rect(0, 0, Bitmap.PixelWidth, Bitmap.PixelHeight), _pixels, _stride, 0);
    }

    private sealed record StoredFrame(byte[] Bytes, bool Compressed);
}
