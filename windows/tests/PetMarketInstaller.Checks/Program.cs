using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
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
        var files = Enumerable.Range(0, PetMarketInstaller.MaximumGifFiles)
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
        Require(result.Files.Count == PetMarketInstaller.MaximumGifFiles,
            "The handle-consistency package must still install all verified files.");
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
        IReadOnlyDictionary<string, string>? manifestHashOverrides = null)
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
            WriteEntry(archive, "pet-market.json", JsonSerializer.SerializeToUtf8Bytes(manifest));
            foreach (var file in files) WriteEntry(archive, file.Key, file.Value);
            addEntries?.Invoke(archive);
        }
        var bytes = File.ReadAllBytes(path);
        return new Package(path, bytes.LongLength, Sha256(bytes));
    }

    private static void WriteEntry(ZipArchive archive, string path, byte[] bytes)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
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
