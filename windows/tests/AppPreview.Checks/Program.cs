using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using ZhuoDazi.Services;

var root = Path.Combine(Path.GetTempPath(), "ZhuoDazi-AppPreview-Checks", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var preview = new AppPreviewEnvironment(new Uri("http://127.0.0.1:18766/"), root);
    Check(preview.DataDirectory == Path.Combine(root, "app-preview"), "Preview has its own child data store.");
    Check(Directory.Exists(preview.DataDirectory), "Preview storage is created locally.");
    foreach (var invalid in new[]
    {
        "https://127.0.0.1:18766/", "http://localhost:18766/", "http://[::1]:18766/",
        "http://example.com/", "http://127.0.0.1:18766/api/", "http://user@127.0.0.1:18766/",
        "http://127.0.0.1:18766/?query=1", "http://127.0.0.1:18766/#fragment"
    })
        ExpectInvalid(() => new AppPreviewEnvironment(new Uri(invalid), root), "Invalid origin rejected.");
    foreach (var invalidPath in new[]
    {
        "relative-preview", @"\\localhost\share\preview", Path.GetPathRoot(root)!,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "poko-desktop-pet"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "poko-desktop-pet", "preview")
    })
        ExpectInvalid(() => new AppPreviewEnvironment(preview.Origin, invalidPath), "Unsafe data directory rejected.");

    var transport = new RecordingHandler();
    using (var client = new HttpClient(preview.CreateGuardedHandler(transport)))
    {
        using var allowed = await client.GetAsync("http://127.0.0.1:18766/api/pet-market/catalog");
        Check(allowed.StatusCode == HttpStatusCode.OK && transport.Requests == 1, "Configured local origin is allowed.");
        foreach (var blocked in new[]
        {
            "https://desktoppet.online/api/analytics/events", "https://example.com/api/device/heartbeat",
            "http://127.0.0.1:18767/api/pet-market/catalog", "https://127.0.0.1:18766/",
            "http://localhost:18766/", "http://[::1]:18766/", "http://user@127.0.0.1:18766/",
            "http://127.0.0.1:18766/#fragment"
        })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, blocked) { Content = new StringContent("preview-only") };
            await ExpectBlocked(() => client.SendAsync(request));
        }
        using var hostOverride = new HttpRequestMessage(HttpMethod.Get, preview.Origin);
        hostOverride.Headers.Host = "example.com";
        await ExpectBlocked(() => client.SendAsync(hostOverride));
        Check(transport.Requests == 1, "Blocked requests never reach a transport or open a socket.");
    }

    // Exercise the real socket transport against a disposable loopback server.
    // A redirect to an external URL must be returned unchanged, never followed.
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var localOrigin = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");
    var localPreview = new AppPreviewEnvironment(localOrigin, Path.Combine(root, "transport"));
    var reply = Task.Run(async () =>
    {
        using var accepted = await listener.AcceptTcpClientAsync();
        using var stream = accepted.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, false, leaveOpen: true);
        while (await reader.ReadLineAsync() is { Length: > 0 }) { }
        var response = Encoding.ASCII.GetBytes("HTTP/1.1 302 Found\r\nLocation: https://example.com/blocked\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(response);
    });
    using (var localClient = new HttpClient(localPreview.CreateHandler()) { Timeout = TimeSpan.FromSeconds(5) })
    using (var response = await localClient.GetAsync(localOrigin))
        Check(response.StatusCode == HttpStatusCode.Found, "Redirects are not followed outside the configured loopback origin.");
    await reply.WaitAsync(TimeSpan.FromSeconds(5));
    listener.Stop();
    Console.WriteLine("App preview boundary checks passed: isolated storage, origin allowlist, no production dispatch, host-header guard, loopback transport, and no redirects.");
}
finally
{
    // This exact, freshly created test directory contains only disposable check data.
    Directory.Delete(root, recursive: true);
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void ExpectInvalid(Action action, string message)
{
    try { action(); }
    catch (InvalidOperationException) { return; }
    throw new InvalidOperationException(message);
}

static async Task ExpectBlocked(Func<Task<HttpResponseMessage>> action)
{
    try { using var response = await action(); }
    catch (HttpRequestException) { return; }
    throw new InvalidOperationException("An unapproved preview request reached the transport.");
}

internal sealed class RecordingHandler : HttpMessageHandler
{
    internal int Requests { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
