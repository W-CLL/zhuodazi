using System.IO.Compression;
using System.Text.Json;
using ZhuoDazi.Models;

namespace ZhuoDazi.Services;

internal sealed record ParsedResourcePack(List<InteractionWordPackDefinition> Words, List<TheaterScriptDefinition> Scripts);

internal static class ResourcePackInstaller
{
    internal static ParsedResourcePack Parse(string archive, ShopResourcePack pack, CancellationToken token)
    {
        // All parsing finishes before settings are mutated. No ZIP path is extracted to disk.
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count > 162) throw new InvalidOperationException("资源包文件数量超出限制。");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var words = new List<InteractionWordPackDefinition>();
        var scripts = new List<TheaterScriptDefinition>();
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            var name = entry.FullName;
            if (name.Contains('\\') || name.StartsWith('/') || name.Contains(':')
                || name.TrimEnd('/').Split('/').Any(part => part is "" or "." or "..")
                || !names.Add(name) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidOperationException("资源包包含不安全或重复的文件路径。");
            if (name.EndsWith('/')) continue;
            total += entry.Length;
            if (entry.Length is <= 0 or > 256 * 1024 || total > 20 * 1024 * 1024)
                throw new InvalidOperationException("资源包解压大小超出限制。");
            var isJson = name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            if (!isJson && !Path.GetFileName(name).Equals("readme.md", StringComparison.OrdinalIgnoreCase)
                && !Path.GetFileName(name).Equals("readme.txt", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("资源包只能包含 JSON 内容和 README 说明。");
            using var input = entry.Open();
            using var memory = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                token.ThrowIfCancellationRequested();
                if (memory.Length + count > entry.Length) throw new InvalidOperationException("资源包内容大小无效。");
                memory.Write(buffer, 0, count);
            }
            if (memory.Length != entry.Length) throw new InvalidOperationException("资源包内容不完整。");
            if (!isJson) continue;
            if (words.Count + scripts.Count >= 80) throw new InvalidOperationException("资源包内容数量超出限制。");
            var json = new System.Text.UTF8Encoding(false, true).GetString(memory.ToArray()).TrimStart('\uFEFF');
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("内容必须是 JSON 对象。");
            var title = document.RootElement.TryGetProperty("name", out var titleElement)
                && titleElement.ValueKind == JsonValueKind.String ? titleElement.GetString() : Path.GetFileNameWithoutExtension(name);
            title = string.IsNullOrWhiteSpace(title) ? pack.Title : title.Trim();
            if (title.Length > 40) title = title[..40];
            if (pack.IsWords)
                words.Add(new InteractionWordPackDefinition { Name = title, Words = InteractionWordsService.ParseContent(json),
                    SourceResourcePackId = pack.Id, SourceResourcePackSha256 = pack.Sha256, Enabled = false });
            else
            {
                var script = TheaterScriptService.ParseContent(json, title);
                script.SourceResourcePackId = pack.Id;
                script.SourceResourcePackSha256 = pack.Sha256;
                script.Enabled = false;
                scripts.Add(script);
            }
        }
        if (words.Count + scripts.Count != pack.FileCount) throw new InvalidOperationException("资源包内容数量与目录不一致，请刷新后重试。");
        return new ParsedResourcePack(words, scripts);
    }
}
