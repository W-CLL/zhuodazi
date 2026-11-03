using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using ZhuoDazi.Models;
using ZhuoDazi.Services;

internal static class Program
{
    private static int _assertions;

    [STAThread]
    private static void Main()
    {
        var root = Path.Combine(Path.GetTempPath(), $"zhuodazi-pet-market-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            CheckMultipleGifInstall(root).GetAwaiter().GetResult();
            CheckExpandedPackagesAndPersistence(root).GetAwaiter().GetResult();
            CheckLargeGifPackage(root).GetAwaiter().GetResult();
            CheckTechnicalLimits(root).GetAwaiter().GetResult();
            CheckArchiveTampering(root).GetAwaiter().GetResult();
            CheckArchiveHandleRemainsReadOnly(root).GetAwaiter().GetResult();
            CheckManifestIdentityMismatch(root).GetAwaiter().GetResult();
            CheckTraversalAndStrictEntries(root).GetAwaiter().GetResult();
            CheckFailurePreservesInstalledVersion(root).GetAwaiter().GetResult();
            Console.WriteLine($"PASS: {_assertions} pet market installer assertions.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task CheckMultipleGifInstall(string root)
    {
        var gif = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "assets", "valid.gif"));
        var package = CreatePackage(root, "cat-pack", "1.2.3", "猫咪搭子", new Dictionary<string, byte[]>
        {
            ["idle/hello.gif"] = gif,
            ["work/focus.gif"] = gif
        });
        var dataDirectory = Path.Combine(root, "normal-data");
        var result = await Install(package, dataDirectory, "cat-pack", "1.2.3");
        Require(result.ProductId == "cat-pack" && result.Version == "1.2.3" && result.Title == "猫咪搭子",
            "The installer must return the authenticated manifest identity.");
        Require(result.Directory == Path.Combine(dataDirectory, "pet-market", "cat-pack", "1.2.3"),
            "The installer must use the fixed product/version directory layout.");
        Require(result.Files.Count == 2 && result.Files.All(File.Exists),
            "A valid package must install every listed GIF.");
        Require(File.Exists(Path.Combine(result.Directory, "pet-market.json")),
            "The installed version must retain its verified manifest.");
    }

    private static async Task CheckArchiveTampering(string root)
    {
        var package = CreatePackage(root, "tamper-pack", "1", "篡改测试", OneGif());
        var originalLength = package.SizeBytes;
        var originalHash = package.Sha256;
        await File.AppendAllTextAsync(package.Path, "tampered");
        await RequireThrowsAsync(
            () => PetMarketInstaller.InstallAsync(
                package.Path,
                Path.Combine(root, "tamper-data"),
                "tamper-pack",
                "1",
                originalLength,
                originalHash),
            "A changed ZIP must fail the server-provided size/hash check.");
    }

    private static async Task CheckArchiveHandleRemainsReadOnly(string root)
    {
        var gif = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "assets", "valid.gif"));
        const int fileCount = 100;
        var files = Enumerable.Range(0, fileCount)
            .ToDictionary(index => $"pets/pet-{index:D2}.gif", _ => gif);
        var package = CreatePackage(root, "locked-pack", "1", "同句柄校验", files);
        var install = Install(package, Path.Combine(root, "locked-data"), "locked-pack", "1");
        var writeWasBlocked = false;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!install.IsCompleted && DateTime.UtcNow < deadline)
        {
            try
            {
                using var writer = new FileStream(
                    package.Path,
                    FileMode.Open,
                    FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);
            }
            catch (IOException)
            {
                writeWasBlocked = true;
                break;
            }
            await Task.Yield();
        }
        var result = await install;
        Require(writeWasBlocked,
            "The verified ZIP handle must remain open without write/delete sharing until installation finishes.");
        Require(result.Files.Count == fileCount,
            "The handle-consistency package must still install all verified files.");
    }

    private static async Task CheckExpandedPackagesAndPersistence(string root)
    {
        var gif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==");
        var files = Enumerable.Range(0, 300).ToDictionary(index => $"pets/{index}.gif", _ => gif);
        var package = CreatePackage(root, "many-pets", "1", "完整图鉴", files,
            manifestTransform: bytes => [.. bytes, .. Enumerable.Repeat((byte)' ', 300 * 1024)],
            compression: CompressionLevel.NoCompression);
        var result = await Install(package, Path.Combine(root, "many-data"), "many-pets", "1");
        Require(result.Files.Count == 300 && result.Files.All(File.Exists),
            "A package above 80 GIFs and 256 archive entries must install every file.");
        Require(new FileInfo(Path.Combine(result.Directory, "pet-market.json")).Length > 256 * 1024,
            "Manifests above the previous 256 KB limit must install within the 1 MiB budget.");
        var settings = new AppSettings
        {
            PetMarketInstallations = [new PetMarketInstallation
            {
                ProductId = result.ProductId, Version = result.Version, Title = result.Title,
                Directory = result.Directory, Files = result.Files.ToList(), ManifestSha256 = new string('a', 64)
            }]
        };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        restored.Normalize();
        Require(restored.PetMarketInstallations.Single().Files.SequenceEqual(result.Files),
            "Settings normalization after restart must preserve all installed files beyond 256.");
    }

    private static async Task CheckLargeGifPackage(string root)
    {
        var gif = LargeValidGif(9 * 1024 * 1024);
        var files = Enumerable.Range(0, 8).ToDictionary(index => $"pets/{index}.gif", _ => gif);
        var package = CreatePackage(root, "large-pets", "1", "大图鉴", files,
            compression: CompressionLevel.NoCompression);
        Require(package.SizeBytes > 64 * 1024 * 1024,
            "The regression package must actually exceed the previous 64 MiB archive limit.");
        var result = await Install(package, Path.Combine(root, "large-data"), "large-pets", "1");
        Require(result.Files.Count == 8 && result.Files.All(file => new FileInfo(file).Length == gif.Length),
            "GIFs above 8 MiB and a package above 64 MiB must install with byte integrity intact.");
        await RequireThrowsAsync(() => Task.Run(() => GifImportValidator.Validate(result.Files[0])),
            "Ordinary manual GIF import must retain its existing 8 MiB limit.");
        var wide = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==");
        BinaryPrimitives.WriteUInt16LittleEndian(wide.AsSpan(6), 4096);
        var widePackage = CreatePackage(root, "wide-pet", "1", "宽图", new Dictionary<string, byte[]> { ["wide.gif"] = wide });
        var wideResult = await Install(widePackage, Path.Combine(root, "wide-data"), "wide-pet", "1");
        await RequireThrowsAsync(() => Task.Run(() => GifImportValidator.Validate(wideResult.Files[0])),
            "Manual GIF import must retain 2048 pixels while market GIFs accept 4096.");
        BinaryPrimitives.WriteUInt16LittleEndian(wide.AsSpan(6), 4097);
        var tooWide = CreatePackage(root, "too-wide", "1", "超宽图", new Dictionary<string, byte[]> { ["wide.gif"] = wide });
        await RequireThrowsAsync(() => Install(tooWide, Path.Combine(root, "too-wide-data"), "too-wide", "1"),
            "Market GIF dimensions above 4096 must still be rejected.");
    }

    private static async Task CheckTechnicalLimits(string root)
    {
        var package = CreatePackage(root, "limits", "1", "限制验证", OneGif());
        await RequireThrowsAsync(() => PetMarketInstaller.InstallAsync(package.Path, Path.Combine(root, "limit-data"),
            "limits", "1", PetMarketPackageLimits.MaximumBytes + 1, package.Sha256),
            "An archive above 300 MiB must be rejected before it is read.");

        var largeManifest = CreatePackage(root, "manifest-limit", "1", "清单限制", OneGif(),
            manifestTransform: bytes => [.. bytes, .. Enumerable.Repeat((byte)' ', PetMarketPackageLimits.MaximumManifestBytes)],
            compression: CompressionLevel.NoCompression);
        await RequireThrowsAsync(() => Install(largeManifest, Path.Combine(root, "manifest-limit-data"), "manifest-limit", "1"),
            "A manifest above 1 MiB must be rejected before parsing.");

        var repeatedGif = LargeValidGif(10 * 1024 * 1024, repeatBlockCount: 10);
        var expanded = CreatePackage(root, "expanded-limit", "1", "展开限制",
            Enumerable.Range(0, 31).ToDictionary(index => $"{index}.gif", _ => repeatedGif));
        Require(expanded.SizeBytes < PetMarketPackageLimits.MaximumBytes,
            "The total-expansion regression must fit within the compressed upload budget.");
        try
        {
            await Install(expanded, Path.Combine(root, "expanded-limit-data"), "expanded-limit", "1");
            throw new Exception("An archive expanding past 300 MiB must not install.");
        }
        catch (InvalidOperationException error)
        {
            Require(error.Message.Contains("解压后超过 300 MB", StringComparison.Ordinal),
                "The accumulated expanded-size budget must reject the package before extracting files.");
        }

        var oversizedFile = MutatePackage(package, bytes => SetFirstGifLength(bytes, (uint)PetMarketPackageLimits.MaximumBytes + 1));
        await RequireThrowsAsync(() => Install(oversizedFile, Path.Combine(root, "file-limit-data"), "limits", "1"),
            "A central-directory GIF length above 300 MiB must be rejected before extraction.");
        package = CreatePackage(root, "limits", "1", "ZIP32限制", OneGif());
        var zip64 = MutatePackage(package, bytes =>
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(bytes.Length - 14), ushort.MaxValue);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(bytes.Length - 12), ushort.MaxValue);
        });
        await RequireThrowsAsync(() => Install(zip64, Path.Combine(root, "zip64-data"), "limits", "1"),
            "ZIP64 entry-count sentinels must be rejected even for a small file.");
    }

    private static void SetFirstGifLength(byte[] bytes, uint length)
    {
        var end = bytes.Length - 22;
        var cursor = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(end + 16));
        for (var index = 0; index < 2; index++)
        {
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(cursor + 28));
            var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(cursor + 30));
            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(cursor + 32));
            if (index == 1)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor + 24), length);
                var local = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(cursor + 42));
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(local + 22), length);
            }
            cursor += 46 + nameLength + extraLength + commentLength;
        }
    }

    private static Package MutatePackage(Package package, Action<byte[]> mutate)
    {
        var bytes = File.ReadAllBytes(package.Path);
        mutate(bytes);
        File.WriteAllBytes(package.Path, bytes);
        return package with { SizeBytes = bytes.LongLength, Sha256 = Sha256(bytes) };
    }

    private static byte[] LargeValidGif(int minimumLength, int repeatBlockCount = 1)
    {
        var source = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==");
        using var output = new MemoryStream(minimumLength + 300);
        output.Write(source.AsSpan(0, source.Length - 1));
        output.Write([0x21, 0xfe]);
        var block = new byte[255];
        var random = new Random(78125);
        var blockIndex = 0;
        while (output.Length < minimumLength)
        {
            if (blockIndex++ % repeatBlockCount == 0) random.NextBytes(block);
            output.WriteByte(255);
            output.Write(block);
        }
        output.Write([0, 0x3b]);
        return output.ToArray();
    }

    private static async Task CheckManifestIdentityMismatch(string root)
    {
        var package = CreatePackage(root, "manifest-pack", "2.0", "身份测试", OneGif());
        await RequireThrowsAsync(
            () => Install(package, Path.Combine(root, "identity-data"), "other-pack", "2.0"),
            "A manifest for another product must be rejected.");
        await RequireThrowsAsync(
            () => Install(package, Path.Combine(root, "identity-data"), "manifest-pack", "2.1"),
            "A manifest for another version must be rejected.");
    }

    private static async Task CheckTraversalAndStrictEntries(string root)
    {
        var gif = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "assets", "valid.gif"));
        var traversal = CreatePackage(root, "path-pack", "1", "路径测试", new Dictionary<string, byte[]>
        {
            ["../escape.gif"] = gif
        });
        await RequireThrowsAsync(
            () => Install(traversal, Path.Combine(root, "path-data"), "path-pack", "1"),
            "Path traversal entries must be rejected.");

        var extra = CreatePackage(root, "extra-pack", "1", "额外文件", OneGif(), archive =>
        {
            WriteEntry(archive, "extra.gif", gif);
        });
        await RequireThrowsAsync(
            () => Install(extra, Path.Combine(root, "extra-data"), "extra-pack", "1"),
            "Files not listed in the manifest must be rejected.");

        var duplicate = CreatePackage(root, "case-pack", "1", "大小写", new Dictionary<string, byte[]>
        {
            ["pets/A.gif"] = gif
        }, archive => WriteEntry(archive, "pets/a.gif", gif));
        await RequireThrowsAsync(
            () => Install(duplicate, Path.Combine(root, "case-data"), "case-pack", "1"),
            "Case-insensitive duplicate ZIP entries must be rejected.");

        var symlink = CreatePackage(root, "link-pack", "1", "链接", OneGif(), archive =>
        {
            var entry = archive.CreateEntry("linked.gif");
            entry.ExternalAttributes = (0xA000 | 0x1FF) << 16;
            using var output = entry.Open();
            output.Write(gif);
        });
        await RequireThrowsAsync(
            () => Install(symlink, Path.Combine(root, "link-data"), "link-pack", "1"),
            "Symbolic link entries must be rejected.");

        var oversized = new byte[GifImportValidator.MaximumBytes + 1];
        var bomb = CreatePackage(root, "bomb-pack", "1", "超量", new Dictionary<string, byte[]>
        {
            ["oversized.gif"] = oversized
        });
        await RequireThrowsAsync(
            () => Install(bomb, Path.Combine(root, "bomb-data"), "bomb-pack", "1"),
            "An oversized or highly compressed GIF entry must be rejected before extraction.");
    }

    private static async Task CheckFailurePreservesInstalledVersion(string root)
    {
        var gif = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "assets", "valid.gif"));
        var initial = CreatePackage(root, "stable-pack", "1.0", "稳定版本", new Dictionary<string, byte[]>
        {
            ["stable.gif"] = gif
        });
        var dataDirectory = Path.Combine(root, "stable-data");
        var installed = await Install(initial, dataDirectory, "stable-pack", "1.0");
        var originalGif = await File.ReadAllBytesAsync(installed.Files.Single());
        var originalManifest = await File.ReadAllBytesAsync(Path.Combine(installed.Directory, "pet-market.json"));

        var invalid = CreatePackage(
            root,
            "stable-pack",
            "1.0",
            "损坏替换",
            new Dictionary<string, byte[]> { ["replacement.gif"] = gif },
            manifestHashOverrides: new Dictionary<string, string>
            {
                ["replacement.gif"] = new string('0', 64)
            });
        await RequireThrowsAsync(
            () => Install(invalid, dataDirectory, "stable-pack", "1.0"),
            "A file hash mismatch must fail before replacing the installed version.");
        Require(Directory.Exists(installed.Directory), "A failed replacement must retain the old version directory.");
        Require(File.ReadAllBytes(installed.Files.Single()).SequenceEqual(originalGif),
            "A failed replacement must retain the old GIF bytes.");
        Require(File.ReadAllBytes(Path.Combine(installed.Directory, "pet-market.json")).SequenceEqual(originalManifest),
            "A failed replacement must retain the old manifest.");
    }

    private static Dictionary<string, byte[]> OneGif()
        => new()
        {
            ["pet.gif"] = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "assets", "valid.gif"))
        };

    private static async Task<PetMarketInstallResult> Install(
        Package package,
        string dataDirectory,
        string productId,
        string version)
        => await PetMarketInstaller.InstallAsync(
            package.Path,
            dataDirectory,
            productId,
            version,
            package.SizeBytes,
            package.Sha256);

    private static Package CreatePackage(
        string root,
        string productId,
        string version,
        string title,
        IReadOnlyDictionary<string, byte[]> files,
        Action<ZipArchive>? addEntries = null,
        IReadOnlyDictionary<string, string>? manifestHashOverrides = null,
        Func<byte[], byte[]>? manifestTransform = null,
        CompressionLevel compression = CompressionLevel.Optimal)
    {
        var path = Path.Combine(root, $"package-{Guid.NewGuid():N}.zip");
        var manifest = new
        {
            schemaVersion = 1,
            productId,
            version,
            title,
            files = files.Select(item => new
            {
                path = item.Key,
                sha256 = manifestHashOverrides?.GetValueOrDefault(item.Key) ?? Sha256(item.Value),
                sizeBytes = item.Value.LongLength
            }).ToArray()
        };
        using (var output = File.Create(path))
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
        {
            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
            WriteEntry(archive, "pet-market.json", manifestTransform?.Invoke(manifestBytes) ?? manifestBytes, compression);
            foreach (var file in files) WriteEntry(archive, file.Key, file.Value, compression);
            addEntries?.Invoke(archive);
        }
        var bytes = File.ReadAllBytes(path);
        return new Package(path, bytes.LongLength, Sha256(bytes));
    }

    private static void WriteEntry(ZipArchive archive, string path, byte[] bytes,
        CompressionLevel compression = CompressionLevel.Optimal)
    {
        var entry = archive.CreateEntry(path, compression);
        using var output = entry.Open();
        output.Write(bytes);
    }

    private static string Sha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static async Task RequireThrowsAsync(Func<Task> action, string message)
    {
        try
        {
            await action();
        }
        catch (InvalidOperationException)
        {
            _assertions++;
            return;
        }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _assertions++;
    }

    private sealed record Package(string Path, long SizeBytes, string Sha256);
}
