using System.Net.Http;
using System.Text.Json;

namespace ZhuoDazi.Services;

public sealed class AnalyticsService : IDisposable
{
    private const string EventsUrl = DeskPetApi.AnalyticsEvents;
    private readonly LicenseService _licenses;
    private readonly HttpClient _httpClient = DeskPetHttp.CreateClient(TimeSpan.FromSeconds(12));
    private readonly string _firstLaunchMarkerPath;
    private bool _disposed;

    public AnalyticsService(LicenseService licenses)
    {
        _licenses = licenses;
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "poko-desktop-pet");
        Directory.CreateDirectory(dataDirectory);
        _firstLaunchMarkerPath = Path.Combine(dataDirectory, "analytics-first-launch.marker");
    }

    public async Task TrackStartupAsync()
    {
        if (_disposed) return;
        var occurredAt = DateTimeOffset.UtcNow.ToString("O");
        var types = File.Exists(_firstLaunchMarkerPath)
            ? new[] { "app_session_start", "app_daily_active" }
            : new[] { "app_first_launch", "app_session_start", "app_daily_active" };
        var events = types.Select(type => CreateEvent(type, occurredAt)).ToArray();
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(new { events });
            for (var attempt = 0; attempt < 3; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, EventsUrl)
                {
                    Content = new ByteArrayContent(payload)
                };
                request.Content.Headers.ContentType = new("application/json") { CharSet = "utf-8" };
                request.Headers.UserAgent.ParseAdd($"ZhuoDazi/{UpdateService.CurrentVersion}");
                request.Headers.TryAddWithoutValidation("X-DeskPet-Platform", "windows");
                request.Headers.TryAddWithoutValidation("X-DeskPet-Architecture", "x64");
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                if (response.IsSuccessStatusCode)
                {
                    if (!File.Exists(_firstLaunchMarkerPath))
                        await File.WriteAllTextAsync(_firstLaunchMarkerPath, occurredAt);
                    return;
                }
                if (attempt < 2) await Task.Delay(TimeSpan.FromSeconds(attempt + 1));
            }
        }
        catch
        {
            // Metrics must never prevent the desktop pet from starting.
        }
    }

    private object CreateEvent(string type, string occurredAt) => new
    {
        eventId = $"windows-{Guid.NewGuid():N}",
        type,
        installationId = _licenses.InstallationId,
        platform = "windows",
        architecture = "x64",
        version = UpdateService.CurrentVersion,
        occurredAt
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }
}
