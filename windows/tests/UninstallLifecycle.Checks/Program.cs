global using System.IO;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ZhuoDazi.Services;

internal static class Program
{
    private static int _checks;

    private static async Task Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "zhuodazi-uninstall-lifecycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            CheckBatchPersistence(root);
            CheckExistingIdentityOnly(root);
            await CheckInstalledStartRetryAsync(root);
            await CheckIntentAndCompletionPayloadAsync(root);
            CheckFeedbackChoices();
            CheckInstallerContract();
            Console.WriteLine($"Uninstall lifecycle checks passed ({_checks} assertions); no app windows, uninstaller, or production requests.");
        }
        finally
        {
            DeskPetHttp.HandlerFactory = null;
            Directory.Delete(root, recursive: true);
        }
    }

    private static void CheckBatchPersistence(string root)
    {
        var directory = Path.Combine(root, "batch-persistence");
        var store = new InstallBatchStore(directory);
        var first = store.GetOrCreate();
        var second = store.GetOrCreate();
        Require(first.Id == second.Id && first.InstalledStartEventId == second.InstalledStartEventId,
            "Ordinary launches and in-place upgrades preserve one installation batch and one retry event ID.");
        Require(first.InstalledStartReportedAt is null && File.Exists(store.MarkerPath),
            "A new marker is unreported until an online start succeeds.");
        var json = JsonDocument.Parse(File.ReadAllBytes(store.MarkerPath)).RootElement;
        Require(json.GetProperty("schemaVersion").GetInt32() == 1
                && Guid.TryParse(json.GetProperty("id").GetString(), out _)
                && Guid.TryParse(json.GetProperty("installedStartEventId").GetString(), out _),
            "The marker contains only bounded versioned batch metadata.");
    }

    private static void CheckExistingIdentityOnly(string root)
    {
        var missingDirectory = Path.Combine(root, "missing-identity");
        Directory.CreateDirectory(missingDirectory);
        Require(LicenseService.TryOpenExisting(missingDirectory) is null
                && !File.Exists(Path.Combine(missingDirectory, "license.dat")),
            "The uninstall helper never mints or persists a replacement identity.");

        var identityDirectory = Path.Combine(root, "existing-identity");
        string installationId;
        using (var created = new LicenseService(identityDirectory)) installationId = created.InstallationId;
        using var reopened = LicenseService.TryOpenExisting(identityDirectory);
        Require(reopened is not null && reopened.InstallationId == installationId,
            "The helper can reopen the existing DPAPI-protected identity without changing it.");
    }

    private static async Task CheckInstalledStartRetryAsync(string root)
    {
        var directory = Path.Combine(root, "start-retry");
        var store = new InstallBatchStore(directory);
        var batch = store.GetOrCreate();
        var identity = new InstallEventIdentity(new string('a', 32), new string('B', 43));
        var requests = new List<JsonElement>();
        var attempt = 0;
        DeskPetHttp.HandlerFactory = () => new StubHandler(async (request, token) =>
        {
            var document = JsonDocument.Parse(await request.Content!.ReadAsByteArrayAsync(token));
            requests.Add(document.RootElement.Clone());
            document.Dispose();
            attempt++;
            if (attempt == 1) throw new HttpRequestException("offline fixture");
            return Json(HttpStatusCode.OK, new
            {
                ok = true,
                eventId = requests[^1].GetProperty("eventId").GetString(),
                duplicate = false,
                deviceState = "installed"
            });
        });
        using var service = new UninstallFeedbackService();
        Require(!await service.ReportInstalledStartAsync(identity, store, batch),
            "A failed online-start request remains pending and never blocks the app.");
        Require(store.LoadExisting()?.InstalledStartReportedAt is null,
            "Offline startup does not mark the batch as reported.");
        Require(await service.ReportInstalledStartAsync(identity, store, batch),
            "A later online launch can finish the same start report.");
        Require(requests.Count == 2
                && requests[0].GetProperty("eventId").GetString() == requests[1].GetProperty("eventId").GetString()
                && requests[0].GetProperty("installBatchId").GetString() == batch.Id,
            "Startup retry reuses the original event and batch IDs for server idempotency.");
        Require(store.LoadExisting()?.InstalledStartReportedAt is not null,
            "Only a successful server response marks the local start as reported.");
        var requestCount = requests.Count;
        Require(await service.ReportInstalledStartAsync(identity, store, batch) && requests.Count == requestCount,
            "A reported batch does not emit redundant startup events.");
    }

    private static async Task CheckIntentAndCompletionPayloadAsync(string root)
    {
        var directory = Path.Combine(root, "intent");
        var batch = new InstallBatchStore(directory).GetOrCreate();
        var identity = new InstallEventIdentity(new string('c', 32), new string('D', 43));
        var requests = new List<JsonElement>();
        var token = new string('T', 43);
        DeskPetHttp.HandlerFactory = () => new StubHandler(async (request, cancellationToken) =>
        {
            var document = JsonDocument.Parse(await request.Content!.ReadAsByteArrayAsync(cancellationToken));
            requests.Add(document.RootElement.Clone());
            var eventId = document.RootElement.GetProperty("eventId").GetString();
            document.Dispose();
            return Json(HttpStatusCode.OK, new { ok = true, eventId, completionToken = token, duplicate = false });
        });
        using var service = new UninstallFeedbackService();
        var skipped = await service.SubmitIntentAsync(identity, batch, UninstallFeedbackChoice.Skip);
        var explained = await service.SubmitIntentAsync(identity, batch,
            UninstallFeedbackChoice.Create("other", "准备重装系统"));
        Require(skipped is not null && explained is not null && requests.Count == 2,
            "Skip and confirmed feedback both create an authenticated intent needed for trusted completion.");
        Require(requests[0].GetProperty("reason").ValueKind == JsonValueKind.Null
                && requests[0].GetProperty("comment").GetString() == string.Empty,
            "Skip sends no fabricated reason or comment.");
        Require(requests[1].GetProperty("reason").GetString() == "other"
                && requests[1].GetProperty("comment").GetString() == "准备重装系统"
                && requests[1].GetProperty("installationId").GetString() == identity.InstallationId,
            "Confirmed feedback sends the selected reason through the existing device identity.");

        var completionPath = Path.Combine(directory, "completion.json");
        UninstallFeedbackService.WriteCompletionPayloadTemplate(completionPath, explained!);
        var completionBytes = File.ReadAllBytes(completionPath);
        var completionText = Encoding.UTF8.GetString(completionBytes);
        var completion = JsonDocument.Parse(completionBytes).RootElement;
        Require(completion.GetProperty("eventType").GetString() == "uninstall_completed"
                && completion.GetProperty("intentEventId").GetString() == explained!.EventId
                && completion.GetProperty("completionToken").GetString() == token,
            "The post-uninstall payload carries the one-time intent token and a stable completion event ID.");
        Require(completion.GetProperty("occurredAt").GetString() == UninstallFeedbackService.CompletionTimestampPlaceholder,
            "The controlled post-delete stage supplies the actual completion timestamp.");
        Require(!completionText.Contains("installationId", StringComparison.Ordinal)
                && !completionText.Contains("credential", StringComparison.Ordinal)
                && !completionText.Contains(identity.Credential, StringComparison.Ordinal),
            "The temporary completion file never contains the permanent device credential.");

        DeskPetHttp.HandlerFactory = () => new StubHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("offline fixture")));
        using var offlineService = new UninstallFeedbackService();
        Require(await offlineService.SubmitIntentAsync(identity, batch, UninstallFeedbackChoice.Skip) is null,
            "Network failure returns no completion token and never blocks the uninstall choice.");

        DeskPetHttp.HandlerFactory = () => new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new UnknownLengthContent(40 * 1024)
        }));
        using var oversizedService = new UninstallFeedbackService();
        Require(await oversizedService.SubmitIntentAsync(identity, batch, UninstallFeedbackChoice.Skip) is null,
            "A response without Content-Length is still rejected at the streaming size limit.");
    }

    private static void CheckFeedbackChoices()
    {
        Require(UninstallFeedbackChoice.Create(null, null) == UninstallFeedbackChoice.Skip,
            "A confirmed uninstall can omit its optional reason.");
        foreach (var reason in new[] { "not_often_used", "resource_usage", "experience_issue", "temporary_pause", "other" })
            Require(UninstallFeedbackChoice.Create(reason, reason == "other" ? "说明" : null).Reason == reason,
                $"Reason {reason} is accepted.");
        RequireThrows(() => UninstallFeedbackChoice.Create("experience_issue", "不应附加"),
            "Only the other reason accepts an explanation.");
        RequireThrows(() => UninstallFeedbackChoice.Create("unknown", null),
            "Unknown reasons are rejected before transmission.");
    }

    private static void CheckInstallerContract()
    {
        var installerPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "installer", "ZhuoDazi.iss"));
        var installer = File.ReadAllText(installerPath);
        Require(installer.Contains("function InitializeUninstall: Boolean", StringComparison.Ordinal)
                && installer.Contains("--uninstall-feedback", StringComparison.Ordinal)
                && installer.Contains("ResultCode = UninstallCanceledExitCode", StringComparison.Ordinal),
            "The uninstaller invokes the focused helper and honors its distinct cancel result.");
        var postStage = installer.IndexOf("else if CurUninstallStep = usPostUninstall", StringComparison.Ordinal);
        var completionCall = postStage < 0 ? -1 : installer.IndexOf("TryReportUninstallCompleted;", postStage, StringComparison.Ordinal);
        var markerDelete = postStage < 0
            ? -1
            : installer.IndexOf("DeleteCapturedInstallBatchMarker;", postStage, StringComparison.Ordinal);
        Require(postStage >= 0 && completionCall > postStage && markerDelete > completionCall,
            "Completion reporting and batch deletion exist only in the post-uninstall stage.");
        Require(installer.Contains("CompletionTimestampPlaceholder", StringComparison.Ordinal)
                && installer.Contains("WinHttp.WinHttpRequest.5.1", StringComparison.Ordinal)
                && installer.Contains("Request.Option(6) := False", StringComparison.Ordinal)
                && installer.Contains("Request.SetTimeouts", StringComparison.Ordinal),
            "The post stage supplies its own timestamp and uses bounded, non-redirecting best-effort HTTP.");
        Require(installer.Contains("if not FileExists(ExpandConstant('{app}\\app\\ZhuoDazi.exe')) then", StringComparison.Ordinal),
            "A remaining main executable prevents an unverified completion report.");
        Require(installer.Contains("CaptureInstallBatchMarker;", StringComparison.Ordinal)
                && installer.Contains("DeleteCapturedInstallBatchMarker;", StringComparison.Ordinal)
                && installer.Contains("CurrentBatch = UninstallBatchSnapshot", StringComparison.Ordinal),
            "The post stage clears only the unchanged batch marker captured for this uninstall.");
        Require(!installer.Contains("license.dat", StringComparison.OrdinalIgnoreCase)
                && !installer.Contains("settings.json", StringComparison.OrdinalIgnoreCase),
            "The uninstaller does not delete device identity, settings, or entitlement data.");
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object payload)
        => new(status) { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };

    private static void RequireThrows(Action action, string message)
    {
        try { action(); }
        catch (ArgumentException) { Require(true, message); return; }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Interlocked.Increment(ref _checks);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }

    private sealed class UnknownLengthContent(int byteCount) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => stream.WriteAsync(new byte[byteCount]).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}

namespace ZhuoDazi.Services
{
    internal static class UpdateService
    {
        internal const string CurrentVersion = "3.4.4";
    }
}
