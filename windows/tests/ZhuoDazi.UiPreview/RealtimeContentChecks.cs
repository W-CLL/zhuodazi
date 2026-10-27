using System.IO;
using System.Net;
using System.Text.Json;
using ZhuoDazi.Models;
using ZhuoDazi.Services;

internal static class RealtimeContentChecks
{
    public static async Task<int> Run(string output)
    {
        Program.Stage("realtime service checks: migration and signed fetch");
        var assertions = 0;
        void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            assertions++;
        }
        async Task RequireFailure(Func<Task> action, string message)
        {
            var failed = false;
            try { await action(); }
            catch (InvalidOperationException) { failed = true; }
            Require(failed, message);
        }

        var store = new SettingsStore(Path.Combine(output, "realtime-selection-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(store.DataDirectory);
        // Write the old on-disk format directly; the current writer deliberately discards its pool.
        File.WriteAllText(store.InteractionsPath, JsonSerializer.Serialize(new InteractionCacheDocument
        {
            LastPromptType = "math",
            Items = [RealtimeContentHandler.Question("math.legacy-cache")],
            Shown = [new() { Id = "trivia.already-shown", ShownAt = DateTimeOffset.UtcNow.ToString("O") }],
            PendingEvents = [new() { Type = "content_shown", ContentId = "trivia.already-shown" }]
        }));
        using var license = new LicenseService(store.DataDirectory);
        await license.CheckTrialAsync();
        using var handler = new RealtimeContentHandler();
        using var service = new InteractionService(store, license, handler, handler.PublicKey);
        Require(handler.BatchRequests.Count == 0, "Constructing the service must not prefetch a question pool.");
        Require(service.PendingEventCount == 1, "Migrating the legacy pool preserves pending event uploads.");
        var cache = new InteractionCacheStore(store.InteractionsPath);
        Require(cache.Load().Items.Count == 0 && cache.Load().Shown.Count == 1,
            "Legacy questions are removed while the displayed-content history survives migration.");

        var first = RealtimeContentHandler.Question("math.live-first");
        handler.Items = [first];
        var fetched = await service.FetchNextContentAsync(["math", "trivia", "riddle"]);
        Require(fetched?.Id == first.Id && handler.BatchRequests.Count == 1,
            "The first question comes from one newly signed HTTP response.");
        Require(handler.BatchRequests[0].Limit == 1 && handler.BatchRequests[0].Types.ToHashSet().SetEquals(["math", "trivia", "riddle"]),
            "Quiz requests ask for exactly one item and only the quiz categories.");
        Require(handler.BatchRequests[0].Types[^1] == "math", "The last displayed type is tried after other requested types.");
        Require(handler.BatchRequests[0].ExcludeIds.Contains("trivia.already-shown")
            && !handler.BatchRequests[0].ExcludeIds.Contains("math.legacy-cache"),
            "History is excluded, while undisplayed legacy cached questions remain eligible online.");
        Require(service.PendingEventCount == 1 && cache.Load().Shown.All(item => item.Id != first.Id),
            "Fetching without displaying must not record a content_shown event or deduplication history.");
        Require(cache.Load().Items.Count == 0, "A live response must never recreate a local question pool.");

        service.RecordContentShown(fetched!);
        Require(service.PendingEventCount == 2 && cache.Load().Shown.Any(item => item.Id == first.Id),
            "Displaying the verified question adds history and exactly one pending shown event.");
        var second = RealtimeContentHandler.Question("riddle.live-second", "riddle", "【刚更新的线上模拟题】第二道题");
        handler.Items = [first, second];
        var next = await service.FetchNextContentAsync(["math", "trivia", "riddle"]);
        Require(next?.Id == second.Id && handler.BatchRequests.Count == 2,
            "A subsequent interaction makes a new request and receives the current server content.");
        Require(handler.BatchRequests[1].ExcludeIds.Contains(first.Id),
            "The next request excludes content already displayed even before events are uploaded.");

        handler.Items = [];
        Require(await service.FetchNextContentAsync() is null && handler.BatchRequests.Count == 3,
            "An empty signed response returns no item and never falls back to legacy or previously fetched content.");
        Require(handler.BatchRequests[2].Types.ToHashSet().SetEquals(["joke", "math", "trivia", "riddle", "tip", "care"]),
            "General interactions request all six supported online categories.");

        handler.Items = [RealtimeContentHandler.Question("joke.wrong-type", "joke")];
        handler.IgnoreTypeFilter = true;
        Require(await service.FetchNextContentAsync(["math", "trivia", "riddle"]) is null,
            "A signed response outside the requested quiz categories must never become a quiz.");
        handler.IgnoreTypeFilter = false;
        handler.Items = [second];
        handler.CorruptSignature = true;
        await RequireFailure(async () => { await service.FetchNextContentAsync(); },
            "An invalid signature must fail without returning a cached question.");
        handler.CorruptSignature = false;
        handler.FailureStatus = HttpStatusCode.ServiceUnavailable;
        await RequireFailure(async () => { await service.FetchNextContentAsync(); },
            "An HTTP error must fail without returning cached or previous content.");
        handler.FailureStatus = null;
        handler.FailConnection = true;
        await RequireFailure(async () => { await service.FetchNextContentAsync(); },
            "A connection failure must fail without returning a cached question.");
        handler.FailConnection = false;
        Require(handler.BatchRequests.Count == 7 && handler.BatchRequests.All(request => request.Limit == 1),
            "Every successful, empty and failed interaction makes one limit=1 request with no batch refill or retry loop.");
        Require(service.PendingEventCount == 2 && cache.Load().Shown.Count == 2,
            "Empty responses, unshown results and failures do not add history or shown events.");

        using var cancellation = new CancellationTokenSource();
        Program.Stage("realtime service checks: cancelling in-flight request");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.BeforeContentResponse = async token =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        };
        var pending = service.FetchNextContentAsync(cancellationToken: cancellation.Token);
        var cancelled = false;
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            try { await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { cancelled = true; }
        }
        finally
        {
            cancellation.Cancel();
            handler.BeforeContentResponse = null;
            try { await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
        }
        Require(cancelled && service.PendingEventCount == 2,
            "Cancelling an in-flight live request releases it without recording a displayed question.");
        Require((await service.FetchNextContentAsync())?.Id == second.Id,
            "A cancelled request releases the network gate so the next interaction can fetch normally.");

        Program.Stage("realtime service checks: concurrent profile synchronization");
        var profileStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProfile = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.BeforeProfileResponse = async token =>
        {
            profileStarted.TrySetResult();
            await releaseProfile.Task.WaitAsync(token);
        };
        var profileSync = service.SyncProfileAsync("standard", true);
        try
        {
            await profileStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var duringProfileSync = await service.FetchNextContentAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Require(duringProfileSync?.Id == second.Id && !profileSync.IsCompleted,
                "A live question request proceeds while background profile synchronization is still blocked.");
        }
        finally
        {
            releaseProfile.TrySetResult();
            handler.BeforeProfileResponse = null;
            await profileSync.WaitAsync(TimeSpan.FromSeconds(5));
        }

        File.WriteAllText(Path.Combine(output, "realtime-content-results.txt"),
            $"PASS: {assertions} real-time content service assertions; signed in-memory HTTP only.\n");
        Program.Stage($"realtime service checks passed ({assertions} assertions)");
        return assertions;
    }
}
