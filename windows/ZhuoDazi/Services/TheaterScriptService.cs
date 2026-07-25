using System.Text.Json;
using ZhuoDazi.Models;

namespace ZhuoDazi.Services;

public static class TheaterScriptService
{
    private const int MaxFileBytes = 256 * 1024;

    public static TheaterScriptDefinition Parse(string filePath)
    {
        if (!File.Exists(filePath) || !filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请选择有效的小剧场 JSON 剧本。");
        if (new FileInfo(filePath).Length > MaxFileBytes)
            throw new InvalidOperationException("小剧场剧本不能超过 256 KB。");

        using var document = JsonDocument.Parse(File.ReadAllText(filePath));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("scenes", out var scenesElement)
            || scenesElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("剧本必须包含 scenes 数组。");

        var scenes = new List<TheaterSceneDefinition>();
        foreach (var item in scenesElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var main = Clean(ReadString(item, "main", "actorA"));
            var companion = Clean(ReadString(item, "companion", "actorB"));
            if (main.Length == 0 || companion.Length == 0) continue;
            scenes.Add(new TheaterSceneDefinition { Main = main, Companion = companion });
            if (scenes.Count == 5) break;
        }
        if (scenes.Count < 3)
            throw new InvalidOperationException("剧本至少需要 3 组有效的 main/companion 对白。");

        var name = root.TryGetProperty("name", out var nameElement) ? CleanName(nameElement.ToString()) : string.Empty;
        if (name.Length == 0) name = CleanName(Path.GetFileNameWithoutExtension(filePath));
        return new TheaterScriptDefinition { Name = name.Length > 0 ? name : "小剧场剧本", Scenes = scenes };
    }

    private static string ReadString(JsonElement element, string primaryName, string aliasName)
    {
        if (element.TryGetProperty(primaryName, out var value) && value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? string.Empty;
        if (element.TryGetProperty(aliasName, out value) && value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? string.Empty;
        return string.Empty;
    }

    private static string Clean(string value)
    {
        var clean = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length <= 60 ? clean : clean[..60];
    }

    private static string CleanName(string value)
    {
        var clean = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length <= 40 ? clean : clean[..40];
    }
}
