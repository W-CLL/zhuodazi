using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ZhuoDazi.Services;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args is ["--server", var port, var log, var mode])
        {
            await RunServerAsync(int.Parse(port), log, mode);
            return 0;
        }
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "fixture-settings.json");
        var settings = File.Exists(settingsPath)
            ? JsonSerializer.Deserialize<FixtureSettings>(File.ReadAllText(settingsPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!
            : new FixtureSettings();
        if (args is ["--prepare-uninstall"])
        {
            if (settings.FailPreparation) return 21;
            var executable = Environment.ProcessPath!;
            var installDirectory = Path.GetDirectoryName(Path.GetDirectoryName(executable)!)!;
            return await UninstallProcessService.StopInstalledProcessesAsync(executable, Path.Combine(installDirectory, "ZhuoDazi.exe")) ? 0 : 21;
        }
        if (args is ["--uninstall-feedback", var completionPath])
        {
            if (settings.Choice == "cancel") return 20;
            if (settings.Choice is not ("skip" or "confirm")) return 21;
            // Synthetic one-time receipt only. This fixture never reads a real
            // license, submits feedback, or connects to the production server.
            File.WriteAllBytes(completionPath, JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 1,
                eventId = Guid.NewGuid().ToString("D"),
                eventType = "uninstall_completed",
                intentEventId = Guid.NewGuid().ToString("D"),
                completionToken = new string('T', 43),
                occurredAt = "__POST_UNINSTALL_UTC__"
            }));
            return 0;
        }
        if (args is ["--keep-alive", var readyPath])
        {
            using var signal = settings.IgnoreExitSignal ? null
                : new EventWaitHandle(false, EventResetMode.AutoReset, UninstallProcessService.ExitSignalName(Environment.ProcessPath!));
            File.WriteAllText(readyPath, Environment.ProcessId.ToString());
            if (signal is not null) return signal.WaitOne(TimeSpan.FromMinutes(3)) ? 17 : 42;
            await Task.Delay(TimeSpan.FromMinutes(3));
            return 42;
        }
        return 21;
    }

    private static async Task RunServerAsync(int port, string logPath, string mode)
    {
        // A loopback socket avoids URL ACL changes required by HttpListener on
        // some CI hosts. No production address is reachable from this fixture.
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        File.WriteAllText(logPath + ".ready", "ready");
        var requestIndex = 0;
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var headerBytes = new List<byte>();
            var single = new byte[1];
            while (headerBytes.Count < 16384)
            {
                if (await stream.ReadAsync(single, timeout.Token) == 0) throw new IOException("Incomplete HTTP headers.");
                headerBytes.Add(single[0]);
                if (headerBytes.Count >= 4 && headerBytes[^4] == 13 && headerBytes[^3] == 10 && headerBytes[^2] == 13 && headerBytes[^1] == 10) break;
            }
            var headers = Encoding.ASCII.GetString(headerBytes.ToArray()).Split("\r\n");
            if (!headers[0].StartsWith("POST /api/device/install-events HTTP/", StringComparison.Ordinal))
                throw new IOException("Unexpected fixture request path or method.");
            var contentLengthLine = headers.First(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
            var contentLength = int.Parse(contentLengthLine.Split(':', 2)[1].Trim());
            if (contentLength is <= 0 or > 32768) throw new IOException("Unexpected fixture payload size.");
            var body = new byte[contentLength];
            await stream.ReadExactlyAsync(body, timeout.Token);
            using var document = JsonDocument.Parse(body);
            var payload = document.RootElement;
            if (payload.GetProperty("eventType").GetString() != "uninstall_completed") throw new IOException("Unexpected fixture event.");
            var eventId = payload.GetProperty("eventId").GetString();
            var occurredAt = payload.GetProperty("occurredAt").GetString();
            requestIndex++;
            var status = mode == "unauthorized" ? 401 : mode == "retry" && requestIndex == 1 ? 503 : 200;
            var response = JsonSerializer.SerializeToUtf8Bytes(new
            {
                ok = status == 200,
                eventId = mode == "wrong-id" ? Guid.NewGuid().ToString("D") : eventId,
                ignoredAsStale = mode == "stale",
                deviceState = mode == "stale" ? "restored" : "uninstall_reported"
            });
            await File.AppendAllTextAsync(logPath, JsonSerializer.Serialize(new
            {
                requestIndex, eventId, occurredAt, status,
                userAgent = headers.FirstOrDefault(line => line.StartsWith("User-Agent:", StringComparison.OrdinalIgnoreCase))
            }) + Environment.NewLine);
            var responseHeaders = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Fixture\r\nContent-Type: application/json\r\nContent-Length: {response.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(responseHeaders, timeout.Token);
            await stream.WriteAsync(response, timeout.Token);
        }
    }

    private sealed class FixtureSettings
    {
        public string Choice { get; set; } = "skip";
        public bool FailPreparation { get; set; }
        public bool IgnoreExitSignal { get; set; }
    }
}
