using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace ZhuoDazi.Services;

internal sealed record PetMarketInstallResult(
    string ProductId,
    string Version,
    string Title,
    string Directory,
    IReadOnlyList<string> Files);

internal static class PetMarketInstaller
{
    internal const long MaximumArchiveBytes = 64 * 1024 * 1024;
    internal const long MaximumUncompressedBytes = 64 * 1024 * 1024;
    internal const int MaximumManifestBytes = 256 * 1024;
    internal const int MaximumGifFiles = 80;

    private const int MaximumArchiveEntries = 256;
    private const int MaximumCompressionRatio = 100;
    private const int UnixFileTypeMask = 0xF000;
    private const int UnixSymbolicLink = 0xA000;
    private const string ManifestName = "pet-market.json";
    private static readonly SemaphoreSlim InstallLock = new(1, 1);

    internal static async Task<PetMarketInstallResult> InstallAsync(
        string zipPath,
        string targetDataDirectory,
        string expectedProductId,
        string expectedVersion,
        long expectedSizeBytes,
        string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        ValidateSafeComponent(expectedProductId, "商品 ID");
        ValidateSafeComponent(expectedVersion, "商品版本");
        var normalizedExpectedHash = NormalizeSha256(expectedSha256, "资源包 SHA-256");
        if (expectedSizeBytes is <= 0 or > MaximumArchiveBytes)
            throw new InvalidOperationException("资源包大小无效或超过 64 MB。");

        var archivePath = Path.GetFullPath(zipPath);
        var dataDirectory = Path.GetFullPath(targetDataDirectory);
        var archiveInfo = new FileInfo(archivePath);
        if (!archiveInfo.Exists || archiveInfo.Length != expectedSizeBytes)
            throw new InvalidOperationException("资源包大小与服务器记录不一致。");
        if ((archiveInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("资源包不能是符号链接或重解析点。");

        await InstallLock.WaitAsync(cancellationToken);
        try
        {
            await using var input = new FileStream(
                archivePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (input.Length != expectedSizeBytes)
                throw new InvalidOperationException("资源包大小与服务器记录不一致。");
            var archiveHash = await HashStreamAsync(input, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(archiveHash),
                    Convert.FromHexString(normalizedExpectedHash)))
                throw new InvalidOperationException("资源包 SHA-256 与服务器记录不一致。");
            input.Position = 0;
            return await InstallVerifiedArchiveAsync(
                input,
                dataDirectory,
                expectedProductId,
                expectedVersion,
                cancellationToken);
        }
        finally
        {
            InstallLock.Release();
        }
    }

    private static async Task<PetMarketInstallResult> InstallVerifiedArchiveAsync(
        FileStream input,
        string dataDirectory,
        string expectedProductId,
        string expectedVersion,
        CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        var index = IndexArchive(archive);
        var manifestBytes = await ReadManifestAsync(index.Manifest, cancellationToken);
        var manifest = ParseManifest(manifestBytes);
        if (!string.Equals(manifest.ProductId, expectedProductId, StringComparison.Ordinal)
            || !string.Equals(manifest.Version, expectedVersion, StringComparison.Ordinal))
            throw new InvalidOperationException("资源包清单与请求的商品或版本不匹配。");

        ValidateArchiveAgainstManifest(index, manifest);

        var marketDirectory = Path.Combine(dataDirectory, "pet-market");
        var productDirectory = SafeChildPath(marketDirectory, manifest.ProductId);
        var destinationDirectory = SafeChildPath(productDirectory, manifest.Version);
        System.IO.Directory.CreateDirectory(dataDirectory);
        EnsureOrdinaryDirectory(dataDirectory, "数据目录");
        System.IO.Directory.CreateDirectory(marketDirectory);
        EnsureOrdinaryDirectory(marketDirectory, "桌宠市场目录");
        System.IO.Directory.CreateDirectory(productDirectory);
        EnsureOrdinaryDirectory(productDirectory, "商品安装目录");

        var stagingDirectory = Path.Combine(
            productDirectory,
            $".{manifest.Version}.{Guid.NewGuid():N}.installing");
        System.IO.Directory.CreateDirectory(stagingDirectory);
        EnsureOrdinaryDirectory(stagingDirectory, "商品临时安装目录");
        var installedFiles = new List<string>(manifest.Files.Count);

        try
        {
            var stagedManifest = SafeChildPath(stagingDirectory, ManifestName);
            await File.WriteAllBytesAsync(stagedManifest, manifestBytes, cancellationToken);

            foreach (var file in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = SafeChildPath(stagingDirectory, file.Path);
                var parent = Path.GetDirectoryName(destination)
                    ?? throw new InvalidOperationException("资源包文件路径无效。");
                System.IO.Directory.CreateDirectory(parent);
                await ExtractGifAsync(index.Files[file.Path], file, destination, cancellationToken);
                GifImportValidator.Validate(destination);
                installedFiles.Add(destination);
            }

            CommitDirectory(stagingDirectory, destinationDirectory);
            installedFiles = manifest.Files
                .Select(file => SafeChildPath(destinationDirectory, file.Path))
                .ToList();
            return new PetMarketInstallResult(
                manifest.ProductId,
                manifest.Version,
                manifest.Title,
                destinationDirectory,
                installedFiles);
        }
        finally
        {
            if (System.IO.Directory.Exists(stagingDirectory))
                System.IO.Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    private static ArchiveIndex IndexArchive(ZipArchive archive)
    {
        if (archive.Entries.Count is 0 or > MaximumArchiveEntries)
            throw new InvalidOperationException("资源包文件数量无效或过多。");

        ZipArchiveEntry? manifest = null;
        var files = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        var caseInsensitiveNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.Ordinal);
        long totalUncompressed = 0;
        long totalCompressed = 0;

        foreach (var entry in archive.Entries)
        {
            RejectUnsafeEntryAttributes(entry);
            var directoryEntry = entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.Name.Length == 0;
            var normalizedName = ValidateRelativePath(entry.FullName, directoryEntry);
            if (!caseInsensitiveNames.Add(normalizedName))
                throw new InvalidOperationException("资源包包含大小写重复的文件或目录。");

            if (directoryEntry)
            {
                directories.Add(normalizedName);
                continue;
            }

            checked
            {
                totalUncompressed += entry.Length;
                totalCompressed += entry.CompressedLength;
            }
            if (totalUncompressed > MaximumUncompressedBytes)
                throw new InvalidOperationException("资源包解压后超过 64 MB。");
            if (entry.Length > 1024 * 1024
                && entry.Length > Math.Max(1, entry.CompressedLength) * MaximumCompressionRatio)
                throw new InvalidOperationException("资源包压缩比异常，可能是解压炸弹。");

            if (string.Equals(normalizedName, ManifestName, StringComparison.Ordinal))
            {
                if (manifest is not null) throw new InvalidOperationException("资源包只能包含一个根清单。");
                if (entry.Length is <= 0 or > MaximumManifestBytes)
                    throw new InvalidOperationException("资源包清单为空或超过 256 KB。");
                manifest = entry;
                continue;
            }

            if (!normalizedName.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("资源包除根清单外只能包含 GIF 文件。");
            if (entry.Length is < 10 or > GifImportValidator.MaximumBytes)
                throw new InvalidOperationException("资源包中的 GIF 为空或超过 8 MB。");
            files.Add(normalizedName, entry);
        }

        if (manifest is null) throw new InvalidOperationException("资源包缺少根清单 pet-market.json。");
        if (totalUncompressed > Math.Max(1024 * 1024, totalCompressed * MaximumCompressionRatio))
            throw new InvalidOperationException("资源包总压缩比异常，可能是解压炸弹。");
        return new ArchiveIndex(manifest, files, directories);
    }

    private static void ValidateArchiveAgainstManifest(ArchiveIndex index, PackageManifest manifest)
    {
        if (manifest.Files.Count != index.Files.Count)
            throw new InvalidOperationException("资源包包含清单未列出的文件或缺少清单文件。");

        var usedDirectories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in manifest.Files)
        {
            if (!index.Files.TryGetValue(file.Path, out var entry))
                throw new InvalidOperationException("资源包包含清单未列出的文件或缺少清单文件。");
            if (entry.Length != file.SizeBytes)
                throw new InvalidOperationException($"资源包文件大小与清单不一致：{file.Path}");
            AddParentDirectories(file.Path, usedDirectories);
        }

        if (index.Directories.Any(directory => !usedDirectories.Contains(directory)))
            throw new InvalidOperationException("资源包包含清单未使用的额外目录。");
    }

    private static async Task ExtractGifAsync(
        ZipArchiveEntry entry,
        ManifestFile file,
        string destination,
        CancellationToken cancellationToken)
    {
        await using var source = entry.Open();
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        long written = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            written += read;
            if (written > file.SizeBytes || written > GifImportValidator.MaximumBytes)
                throw new InvalidOperationException($"资源包文件解压大小异常：{file.Path}");
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        await output.FlushAsync(cancellationToken);
        if (written != file.SizeBytes)
            throw new InvalidOperationException($"资源包文件大小与清单不一致：{file.Path}");
        var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actualHash),
                Convert.FromHexString(file.Sha256)))
            throw new InvalidOperationException($"资源包文件 SHA-256 与清单不一致：{file.Path}");
    }

    private static async Task<byte[]> ReadManifestAsync(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        await using var source = entry.Open();
        using var output = new MemoryStream((int)entry.Length);
        var buffer = new byte[32 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (output.Length + read > MaximumManifestBytes)
                throw new InvalidOperationException("资源包清单超过 256 KB。");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (output.Length != entry.Length)
            throw new InvalidOperationException("资源包清单长度与 ZIP 目录记录不一致。");
        return output.ToArray();
    }

    private static PackageManifest ParseManifest(byte[] bytes)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 12
            });
        }
        catch (JsonException error)
        {
            throw new InvalidOperationException("资源包清单不是有效 JSON。", error);
        }

        using (document)
        {
            var root = document.RootElement;
            RequireObjectProperties(root, ["schemaVersion", "productId", "version", "title", "files"]);
            if (!root.GetProperty("schemaVersion").TryGetInt32(out var schemaVersion) || schemaVersion != 1)
                throw new InvalidOperationException("资源包清单版本不受支持。");
            var productId = RequiredString(root.GetProperty("productId"), "productId", 128);
            var version = RequiredString(root.GetProperty("version"), "version", 64);
            var title = RequiredString(root.GetProperty("title"), "title", 160);
            ValidateSafeComponent(productId, "商品 ID");
            ValidateSafeComponent(version, "商品版本");

            var fileElements = root.GetProperty("files");
            if (fileElements.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("资源包清单 files 必须是数组。");
            var files = new List<ManifestFile>();
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var element in fileElements.EnumerateArray())
            {
                if (files.Count >= MaximumGifFiles)
                    throw new InvalidOperationException("资源包最多包含 80 个 GIF。");
                RequireObjectProperties(element, ["path", "sha256", "sizeBytes"]);
                var path = ValidateRelativePath(RequiredString(element.GetProperty("path"), "files.path", 1024), false);
                if (string.Equals(path, ManifestName, StringComparison.OrdinalIgnoreCase)
                    || !path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("资源包清单只能列出 GIF 文件。");
                if (!paths.Add(path)) throw new InvalidOperationException("资源包清单包含大小写重复的路径。");
                var sha256 = NormalizeSha256(RequiredString(element.GetProperty("sha256"), "files.sha256", 64), "文件 SHA-256");
                if (!element.GetProperty("sizeBytes").TryGetInt64(out var sizeBytes)
                    || sizeBytes is < 10 or > GifImportValidator.MaximumBytes)
                    throw new InvalidOperationException("资源包清单中的 GIF 大小无效。");
                files.Add(new ManifestFile(path, sha256, sizeBytes));
            }
            if (files.Count == 0) throw new InvalidOperationException("资源包清单至少需要一个 GIF。");
            return new PackageManifest(productId, version, title, files);
        }
    }

    private static void RequireObjectProperties(JsonElement element, IReadOnlyCollection<string> expected)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("资源包清单结构无效。");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name) || !expected.Contains(property.Name, StringComparer.Ordinal))
                throw new InvalidOperationException("资源包清单包含重复或未知字段。");
        }
        if (names.Count != expected.Count || expected.Any(name => !names.Contains(name)))
            throw new InvalidOperationException("资源包清单缺少必需字段。");
    }

    private static string RequiredString(JsonElement element, string name, int maximumLength)
    {
        if (element.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException($"资源包清单字段 {name} 必须是字符串。");
        var value = element.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength || value != value.Trim())
            throw new InvalidOperationException($"资源包清单字段 {name} 无效。");
        return value;
    }

    private static string ValidateRelativePath(string rawPath, bool directory)
    {
        if (string.IsNullOrWhiteSpace(rawPath)
            || rawPath.Contains('\\')
            || rawPath.Contains('\0')
            || rawPath.StartsWith("/", StringComparison.Ordinal)
            || rawPath.StartsWith("//", StringComparison.Ordinal)
            || Path.IsPathRooted(rawPath))
            throw new InvalidOperationException("资源包包含不安全的绝对或 UNC 路径。");

        var normalized = directory ? rawPath.TrimEnd('/') : rawPath;
        if (normalized.Length == 0 || (!directory && normalized.EndsWith("/", StringComparison.Ordinal)))
            throw new InvalidOperationException("资源包路径无效。");
        var segments = normalized.Split('/');
        foreach (var segment in segments)
        {
            if (segment.Length == 0 || segment is "." or ".."
                || segment.StartsWith(".", StringComparison.Ordinal)
                || segment.EndsWith(' ') || segment.EndsWith('.')
                || segment.Any(character => character < 32 || "<>:\"|?*".Contains(character))
                || IsReservedWindowsName(segment))
                throw new InvalidOperationException("资源包包含路径穿越、隐藏路径或 Windows 非法路径。");
        }
        return string.Join('/', segments);
    }

    private static bool IsReservedWindowsName(string segment)
    {
        var stem = segment.Split('.', 2)[0];
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || Enumerable.Range(1, 9).Any(number => stem.Equals($"COM{number}", StringComparison.OrdinalIgnoreCase))
            || Enumerable.Range(1, 9).Any(number => stem.Equals($"LPT{number}", StringComparison.OrdinalIgnoreCase));
    }

    private static void RejectUnsafeEntryAttributes(ZipArchiveEntry entry)
    {
        var unixMode = (entry.ExternalAttributes >> 16) & 0xFFFF;
        var windowsAttributes = (FileAttributes)(entry.ExternalAttributes & 0xFFFF);
        if ((unixMode & UnixFileTypeMask) == UnixSymbolicLink
            || (windowsAttributes & (FileAttributes.Hidden | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidOperationException("资源包不能包含符号链接、重解析点或隐藏文件。");
    }

    private static void AddParentDirectories(string path, ISet<string> directories)
    {
        var segments = path.Split('/');
        var current = string.Empty;
        for (var index = 0; index < segments.Length - 1; index++)
        {
            current = current.Length == 0 ? segments[index] : $"{current}/{segments[index]}";
            directories.Add(current);
        }
    }

    private static string SafeChildPath(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root);
        var destination = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("资源包路径超出安装目录。");
        return destination;
    }

    private static void ValidateSafeComponent(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value != value.Trim()
            || !char.IsLetterOrDigit(value[0])
            || value.Any(character => !char.IsLetterOrDigit(character) && character is not '.' and not '_' and not '-'))
            throw new InvalidOperationException($"{name} 不能安全地用作安装目录。");
        if (value.EndsWith('.') || value.EndsWith(' ') || IsReservedWindowsName(value))
            throw new InvalidOperationException($"{name} 不能安全地用作安装目录。");
    }

    private static string NormalizeSha256(string value, string name)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"{name} 无效。");
        return normalized;
    }

    private static async Task<string> HashStreamAsync(FileStream input, CancellationToken cancellationToken)
    {
        input.Position = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void CommitDirectory(string stagingDirectory, string destinationDirectory)
    {
        if (!System.IO.Directory.Exists(destinationDirectory))
        {
            System.IO.Directory.Move(stagingDirectory, destinationDirectory);
            return;
        }

        EnsureOrdinaryDirectory(destinationDirectory, "已安装商品目录");
        var backupDirectory = $"{destinationDirectory}.{Guid.NewGuid():N}.previous";
        System.IO.Directory.Move(destinationDirectory, backupDirectory);
        try
        {
            System.IO.Directory.Move(stagingDirectory, destinationDirectory);
        }
        catch
        {
            if (!System.IO.Directory.Exists(destinationDirectory)
                && System.IO.Directory.Exists(backupDirectory))
                System.IO.Directory.Move(backupDirectory, destinationDirectory);
            throw;
        }

        try { System.IO.Directory.Delete(backupDirectory, recursive: true); }
        catch { /* The new version is committed; a stale installer-owned backup is safer than rollback. */ }
    }

    private static void EnsureOrdinaryDirectory(string path, string name)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"{name} 不是安全的普通目录。");
    }

    private sealed record ManifestFile(string Path, string Sha256, long SizeBytes);
    private sealed record PackageManifest(string ProductId, string Version, string Title, IReadOnlyList<ManifestFile> Files);
    private sealed record ArchiveIndex(
        ZipArchiveEntry Manifest,
        IReadOnlyDictionary<string, ZipArchiveEntry> Files,
        IReadOnlySet<string> Directories);
}
