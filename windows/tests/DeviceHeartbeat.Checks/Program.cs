global using System.IO;
using System.Collections.Concurrent;
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
        await CheckExpiredIdentityAndFailures();
        await CheckSingleFlightSuspendAndDisposal();
        CheckThrottleAndPeriodicRetry();
        Console.WriteLine($"Device heartbeat checks passed ({_checks} assertions); no production requests or app windows.");
    }

    private static async Task CheckExpiredIdentityAndFailures()
    {
        var directory = Path.Combine(Path.GetTempPath(), "zhuodazi-heartbeat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string? trialInstallation = null;
        string? trialCredential = null;
        var heartbeatRequests = 0;
        var status = HttpStatusCode.OK;
        var throwNetwork = false;
        var blockUntilCancelled = false;
        var licenseId = Guid.NewGuid().ToString();
        var handler = new StubHandler(async (request, token) =>
        {
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            if (request.RequestUri!.AbsolutePath == "/api/trial")
            {
                trialInstallation = body.RootElement.GetProperty("installationId").GetString();
                trialCredential = body.RootElement.GetProperty("credential").GetString();
                return Json(HttpStatusCode.OK, "{\"allowed\":false,\"remainingSeconds\":0}");
            }
            if (request.RequestUri.AbsolutePath == "/api/activate")
                return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { licenseId, deviceCount = 1 }));
            Require(request.RequestUri.AbsolutePath == "/api/device/heartbeat", "Activity uses only the dedicated endpoint.");
            Require(request.RequestUri.Scheme == "https" && request.Method == HttpMethod.Post, "Identity is sent over HTTPS POST.");
            Require(request.Headers.Authorization is null, "Heartbeat must not borrow premium authorization.");
            Require(request.Headers.GetValues("X-DeskPet-Platform").Single() == "windows", "Platform header is present.");
            Require(request.Headers.GetValues("X-DeskPet-Architecture").Single() == "x64", "Architecture header is present.");
            Require(request.Headers.GetValues("X-DeskPet-Version").Single() == UpdateService.CurrentVersion, "Version header is present.");
            Require(body.RootElement.GetProperty("installationId").GetString() == trialInstallation, "Heartbeat retains the registered installation.");
            Require(body.RootElement.GetProperty("credential").GetString() == trialCredential, "Heartbeat proves the saved identity without replacing it.");
            Require(body.RootElement.GetProperty("appVersion").GetString() == UpdateService.CurrentVersion, "Payload has appVersion.");
            heartbeatRequests++;
            if (throwNetwork) throw new HttpRequestException("fixture network failure");
            if (blockUntilCancelled) await Task.Delay(Timeout.Infinite, token);
            return Json(status, "{\"ok\":true,\"serverTime\":\"2026-09-28T00:00:00Z\"}");
        });
        DeskPetHttp.HandlerFactory = () => handler;
        try
        {
            using var licenses = new LicenseService(directory);
            var trial = await licenses.CheckTrialAsync();
            Require(!trial.Allowed && !licenses.HasPremiumAccess, "Fixture is an expired registered trial.");
            var savedBefore = File.ReadAllBytes(Path.Combine(directory, "license.dat"));
            foreach (var result in new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized })
            {
                status = result;
                await licenses.SendHeartbeatAsync();
                Require(!licenses.HasPremiumAccess, "Heartbeat cannot grant an expired trial premium access.");
                Require(savedBefore.SequenceEqual(File.ReadAllBytes(Path.Combine(directory, "license.dat"))), "Heartbeat response never rewrites license state.");
            }
            throwNetwork = true;
            await licenses.SendHeartbeatAsync();
            throwNetwork = false;
            Require(!licenses.HasPremiumAccess, "Network failure does not affect entitlement state.");
            blockUntilCancelled = true;
            using var cancellation = new CancellationTokenSource();
            var send = licenses.SendHeartbeatAsync(cancellation.Token);
            cancellation.Cancel();
            await send.WaitAsync(TimeSpan.FromSeconds(2));
            blockUntilCancelled = false;
            await licenses.ActivateAsync("ABCDEF");
            status = HttpStatusCode.Unauthorized;
            await licenses.SendHeartbeatAsync();
            Require(licenses.IsActivated && licenses.LicenseId == licenseId, "A heartbeat 401 cannot revoke the existing local license.");
            Require(heartbeatRequests == 6, "Expired, cancelled and activated identities reached the heartbeat transport.");
        }
        finally
        {
            DeskPetHttp.HandlerFactory = null;
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task CheckSingleFlightSuspendAndDisposal()
    {
        var clock = new ManualClock();
        var requests = new ConcurrentQueue<PendingSend>();
        var active = 0;
        var maximumActive = 0;
        var calls = 0;
        var loop = new DeviceHeartbeatLoop(async token =>
        {
            var pending = new PendingSend(token);
            requests.Enqueue(pending);
            Interlocked.Increment(ref calls);
            maximumActive = Math.Max(maximumActive, Interlocked.Increment(ref active));
            try { await pending.Completion.Task.WaitAsync(token); }
            finally { Interlocked.Decrement(ref active); }
        }, clock);
        loop.Start();
        Require(calls == 1, "Start sends immediately.");
        loop.IdentityRefreshed(); loop.IdentityRefreshed();
        clock.Advance(TimeSpan.FromSeconds(60));
        Require(calls == 1, "Periodic and identity triggers cannot overlap an in-flight request.");
        Require(requests.TryDequeue(out var first), "First request exists.");
        first!.Completion.SetResult();
        await WaitFor(() => Volatile.Read(ref calls) == 2);
        Require(calls == 2, "Several identity refreshes coalesce into one follow-up request.");
        Require(requests.TryDequeue(out var second), "Second request exists.");
        loop.Suspend();
        Require(second!.Token.IsCancellationRequested, "Suspend cancels the pending HTTP request.");
        clock.Advance(TimeSpan.FromMinutes(10));
        Require(calls == 2, "Suspended app emits no timer heartbeat.");
        loop.Resume();
        await WaitFor(() => Volatile.Read(ref calls) == 3);
        Require(requests.TryDequeue(out var third), "Resume request exists.");
        Require(!third!.Token.IsCancellationRequested, "Resume uses a fresh cancellation token.");
        Require(maximumActive == 1, "At most one request runs throughout suspend/resume.");
        loop.Dispose();
        Require(third.Token.IsCancellationRequested, "Exit cancels the outstanding request.");
        clock.Advance(TimeSpan.FromMinutes(2)); loop.Resume(); loop.IdentityRefreshed();
        Require(calls == 3, "Disposed loop never starts again.");
    }

    private static void CheckThrottleAndPeriodicRetry()
    {
        var clock = new ManualClock();
        var calls = 0;
        using var loop = new DeviceHeartbeatLoop(_ =>
        {
            calls++;
            return Task.FromException(new HttpRequestException("fixture"));
        }, clock);
        loop.Start();
        loop.Resume(); loop.Resume();
        Require(calls == 1, "Repeated foreground callbacks are throttled.");
        clock.Advance(TimeSpan.FromSeconds(59));
        Require(calls == 1, "A failed heartbeat waits for its next scheduled minute.");
        clock.Advance(TimeSpan.FromSeconds(1));
        Require(calls == 2, "A failure does not stop the 60-second heartbeat.");
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static async Task WaitFor(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition()) await Task.Delay(5, timeout.Token);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Interlocked.Increment(ref _checks);
    }

    private sealed record PendingSend(CancellationToken Token)
    {
        internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _timestamp;
        private ManualTimer? _timer;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => _timer = new ManualTimer(this, callback, state, dueTime, period);
        internal void Advance(TimeSpan duration)
        {
            Interlocked.Add(ref _timestamp, duration.Ticks);
            _timer?.FireDue();
        }

        private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state, TimeSpan due, TimeSpan period) : ITimer
        {
            private long _due = due == Timeout.InfiniteTimeSpan ? long.MaxValue : clock.GetTimestamp() + due.Ticks;
            private TimeSpan _period = period;
            public bool Change(TimeSpan dueTime, TimeSpan nextPeriod)
            {
                _due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : clock.GetTimestamp() + dueTime.Ticks;
                _period = nextPeriod;
                return true;
            }
            internal void FireDue()
            {
                if (_due > clock.GetTimestamp()) return;
                _due = _period == Timeout.InfiniteTimeSpan ? long.MaxValue : clock.GetTimestamp() + _period.Ticks;
                callback(state);
            }
            public void Dispose() => _due = long.MaxValue;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}

namespace ZhuoDazi.Services
{
    internal static class UpdateService { internal const string CurrentVersion = "0.0.0"; }
}
