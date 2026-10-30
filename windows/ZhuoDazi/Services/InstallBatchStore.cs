using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZhuoDazi.Services;

internal sealed class InstallBatchStore
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    internal string DataDirectory { get; }
    internal string MarkerPath => Path.Combine(DataDirectory, "install-batch.json");

    internal InstallBatchStore(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "poko-desktop-pet");
    }

    internal InstallBatchRecord GetOrCreate()
    {
        var existing = LoadExisting();
        if (existing is not null) return existing;

        var createdAt = DateTimeOffset.UtcNow;
        var value = new InstallBatchRecord
        {
            SchemaVersion = CurrentSchemaVersion,
            Id = Guid.NewGuid().ToString("D"),
            CreatedAt = createdAt,
            InstalledStartEventId = Guid.NewGuid().ToString("D")
        };
        Save(value);
        return value;
    }

    internal InstallBatchRecord? LoadExisting()
    {
        try
        {
            if (!File.Exists(MarkerPath)) return null;
            var bytes = File.ReadAllBytes(MarkerPath);
            if (bytes.Length is 0 or > 16 * 1024) return null;
            var value = JsonSerializer.Deserialize<InstallBatchRecord>(bytes, JsonOptions);
            return IsValid(value) ? value : null;
        }
        catch
        {
            return null;
        }
    }

    internal bool MarkInstalledStartReported(InstallBatchRecord expected, DateTimeOffset reportedAt)
    {
        var current = LoadExisting();
        if (current is null
            || !current.Id.Equals(expected.Id, StringComparison.OrdinalIgnoreCase)
            || !current.InstalledStartEventId.Equals(expected.InstalledStartEventId, StringComparison.OrdinalIgnoreCase))
            return false;

        current.InstalledStartReportedAt = reportedAt.ToUniversalTime();
        Save(current);
        expected.InstalledStartReportedAt = current.InstalledStartReportedAt;
        return true;
    }

    private void Save(InstallBatchRecord value)
    {
        Directory.CreateDirectory(DataDirectory);
        var temporaryPath = $"{MarkerPath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporaryPath, JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions));
            File.Move(temporaryPath, MarkerPath, true);
        }
        finally
        {
            try { File.Delete(temporaryPath); } catch { }
        }
    }

    private static bool IsValid(InstallBatchRecord? value)
    {
        if (value is null || value.SchemaVersion != CurrentSchemaVersion) return false;
        if (!Guid.TryParseExact(value.Id, "D", out _) || !Guid.TryParseExact(value.InstalledStartEventId, "D", out _))
            return false;
        if (value.CreatedAt == default || value.CreatedAt > DateTimeOffset.UtcNow.AddDays(1)) return false;
        return value.InstalledStartReportedAt is null
            || value.InstalledStartReportedAt.Value <= DateTimeOffset.UtcNow.AddDays(1);
    }
}

internal sealed class InstallBatchRecord
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("installedStartEventId")]
    public string InstalledStartEventId { get; set; } = string.Empty;

    [JsonPropertyName("installedStartReportedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? InstalledStartReportedAt { get; set; }
}
