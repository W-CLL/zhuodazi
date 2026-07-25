using System.Text.Json;

namespace ZhuoDazi.Services;

public static class InteractionWordsService
{
    public static readonly string[] Actions =
    [
        "idle", "grab", "switch", "happy", "angry", "confused",
        "shy", "sleepy", "surprised", "cheer", "sad", "calm",
        "chase", "dodge", "bounce", "theater_open", "theater_reply",
        "theater_middle", "theater_middle_reply", "theater_challenge",
        "theater_challenge_reply", "theater_twist", "theater_twist_reply", "theater_finish"
    ];

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["idle"] = "idle", ["待机"] = "idle",
        ["grab"] = "grab", ["抓我"] = "grab", ["抓取"] = "grab",
        ["switch"] = "switch", ["切换"] = "switch",
        ["happy"] = "happy", ["开心"] = "happy",
        ["angry"] = "angry", ["生气"] = "angry",
        ["confused"] = "confused", ["疑惑"] = "confused",
        ["shy"] = "shy", ["害羞"] = "shy",
        ["sleepy"] = "sleepy", ["困倦"] = "sleepy", ["困"] = "sleepy",
        ["surprised"] = "surprised", ["惊讶"] = "surprised",
        ["cheer"] = "cheer", ["加油"] = "cheer",
        ["sad"] = "sad", ["难过"] = "sad", ["伤心"] = "sad",
        ["calm"] = "calm", ["安静"] = "calm",
        ["chase"] = "chase", ["追逐"] = "chase",
        ["dodge"] = "dodge", ["躲避"] = "dodge",
        ["bounce"] = "bounce", ["反弹"] = "bounce",
        ["theater_open"] = "theater_open", ["小剧场开场"] = "theater_open",
        ["theater_reply"] = "theater_reply", ["小剧场回应"] = "theater_reply",
        ["theater_middle"] = "theater_middle", ["小剧场中场"] = "theater_middle",
        ["theater_middle_reply"] = "theater_middle_reply", ["小剧场中场回应"] = "theater_middle_reply",
        ["theater_challenge"] = "theater_challenge", ["小剧场动作"] = "theater_challenge",
        ["theater_challenge_reply"] = "theater_challenge_reply", ["小剧场动作回应"] = "theater_challenge_reply",
        ["theater_twist"] = "theater_twist", ["小剧场反转"] = "theater_twist",
        ["theater_twist_reply"] = "theater_twist_reply", ["小剧场反转回应"] = "theater_twist_reply",
        ["theater_finish"] = "theater_finish", ["小剧场收尾"] = "theater_finish"
    };

    public static Dictionary<string, List<string>> Parse(string filePath)
    {
        var content = File.ReadAllText(filePath);
        var words = Path.GetExtension(filePath).Equals(".txt", StringComparison.OrdinalIgnoreCase)
            ? ParseText(content) : ParseJson(content);
        if (words.Sum(item => item.Value.Count) == 0) throw new InvalidOperationException("词包中没有识别到可用台词。");
        return words;
    }

    private static Dictionary<string, List<string>> ParseJson(string content)
    {
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        if (root.TryGetProperty("reactions", out var reactions)) root = reactions;
        else if (root.TryGetProperty("words", out var words)) root = words;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("JSON 词包格式无效。");
        var result = NewDictionary();
        foreach (var property in root.EnumerateObject())
        {
            var action = Resolve(property.Name);
            if (action is null) continue;
            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in property.Value.EnumerateArray()) Add(result, action, item.ToString());
            }
            else Add(result, action, property.Value.ToString());
        }
        return result;
    }

    private static Dictionary<string, List<string>> ParseText(string content)
    {
        var result = NewDictionary();
        var section = "idle";
        foreach (var raw in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//")) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = Resolve(line[1..^1]) ?? section;
                continue;
            }
            var separator = line.IndexOfAny(['|', '=', '：']);
            if (separator is > 0 and < 32 && Resolve(line[..separator].Trim()) is { } tagged)
                Add(result, tagged, line[(separator + 1)..]);
            else Add(result, section, line);
        }
        return result;
    }

    private static Dictionary<string, List<string>> NewDictionary() => new(StringComparer.OrdinalIgnoreCase);
    private static string? Resolve(string value) => Aliases.GetValueOrDefault(value.Trim());

    private static void Add(Dictionary<string, List<string>> target, string action, string value)
    {
        var clean = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (clean.Length > 60) clean = clean[..60];
        if (clean.Length == 0) return;
        if (!target.TryGetValue(action, out var items)) target[action] = items = [];
        if (items.Count < 50 && !items.Contains(clean)) items.Add(clean);
    }
}
