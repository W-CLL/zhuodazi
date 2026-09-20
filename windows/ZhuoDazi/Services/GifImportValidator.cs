using System.Buffers.Binary;
using System.IO;
using System.Windows.Media.Imaging;

namespace ZhuoDazi.Services;

internal static class GifImportValidator
{
    internal const long MaximumBytes = 8 * 1024 * 1024;
    internal const int MaximumDimension = 2048;

    internal static void Validate(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || !path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请选择有效的 GIF 文件。");
        if (info.Length is < 10 or > MaximumBytes)
            throw new InvalidOperationException("GIF 不能为空，且不能超过 8 MB。");
        using var input = File.OpenRead(path);
        Span<byte> header = stackalloc byte[10];
        input.ReadExactly(header);
        if (!header[..6].SequenceEqual("GIF87a"u8) && !header[..6].SequenceEqual("GIF89a"u8))
            throw new InvalidOperationException("文件内容不是 GIF，请选择真正的 GIF 动图。");
        var width = BinaryPrimitives.ReadUInt16LittleEndian(header[6..8]);
        var height = BinaryPrimitives.ReadUInt16LittleEndian(header[8..10]);
        if (width is 0 or > MaximumDimension || height is 0 or > MaximumDimension)
            throw new InvalidOperationException("GIF 宽高都不能超过 2048 像素。");
        input.Position = 0;
        try
        {
            var decoder = new GifBitmapDecoder(input, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (decoder.Frames.Count == 0) throw new InvalidOperationException("GIF 没有可显示的画面。");
        }
        catch (Exception error) when (error is not InvalidOperationException)
        {
            throw new InvalidOperationException("GIF 文件损坏或无法读取，请换一个文件。", error);
        }
    }
}
