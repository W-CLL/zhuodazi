using System.Diagnostics;
using System.Runtime.InteropServices;
using ZhuoDazi.Services;

internal static class Program
{
    private static int _checks;

    private static async Task<int> Main(string[] args)
    {
        if (args is ["--fixture", var behavior]) return RunFixture(behavior);
        var root = Path.Combine(Path.GetTempPath(), "zhuodazi-uninstall-process-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var fixtures = new List<Process>();
        try
        {
            var first = CopyFixture(root, "first");
            var second = CopyFixture(root, "first-other");
            Require(UninstallProcessService.ExitSignalName(first)
                    == UninstallProcessService.ExitSignalName(Path.Combine(Path.GetDirectoryName(first)!, ".", Path.GetFileName(first)).ToUpperInvariant()),
                "Equivalent absolute paths and filename casing share an exit signal.");
            Require(UninstallProcessService.ExitSignalName(first) != UninstallProcessService.ExitSignalName(second),
                "Different installation directories use different exit signals.");
            Require(await UninstallProcessService.StopInstalledProcessesAsync(Environment.ProcessPath!),
                "The prepare-uninstall helper excludes its own executable process.");

            var other = await StartFixture(second, "graceful");
            fixtures.Add(other);
            {
                var target = await StartFixture(first, "graceful");
                fixtures.Add(target);
                Require(await UninstallProcessService.StopInstalledProcessesAsync(first), "Graceful shutdown completes.");
                await target.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
                Require(target.ExitCode == 17, "The target received its path-specific signal and exited gracefully.");
                Require(!other.HasExited, "A same-named process in another installation directory stays alive.");
            }

            {
                var target = await StartFixture(first, "ignore");
                fixtures.Add(target);
                var watch = Stopwatch.StartNew();
                Require(await UninstallProcessService.StopInstalledProcessesAsync(first), "An older unresponsive target is terminated.");
                await target.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
                Require(watch.Elapsed >= TimeSpan.FromSeconds(2.8), "The older target receives a bounded graceful-exit interval before force termination.");
                Require(!other.HasExited, "Force termination also leaves the other installation alive.");
            }

            {
                var target = await StartFixture(first, "ignore");
                fixtures.Add(target);
                using var canceled = new CancellationTokenSource();
                canceled.Cancel();
                Require(!await UninstallProcessService.StopInstalledProcessesAsync(first, cancellationToken: canceled.Token)
                        && !target.HasExited,
                    "Cancellation before preparation leaves the target running.");
                using var cancelDuringWait = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
                Require(!await UninstallProcessService.StopInstalledProcessesAsync(first, cancellationToken: cancelDuringWait.Token)
                        && !target.HasExited,
                    "Cancellation during the graceful wait never proceeds to force termination.");
                target.Kill();
                await target.WaitForExitAsync();
            }

            {
                var target = await StartFixture(first, "graceful");
                fixtures.Add(target);
                Require(await UninstallProcessService.StopInstalledProcessesAsync(Path.Combine(root, "missing", Path.GetFileName(first)), first),
                    "A legacy exact executable path can be stopped alongside the current layout.");
                await target.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
                Require(target.ExitCode == 17 && !other.HasExited, "Legacy shutdown preserves unrelated installations.");
            }

            Require(await UninstallProcessService.StopInstalledProcessesAsync(first), "An absent target is already stopped.");
            RequireThrows(() => UninstallProcessService.ExitSignalName("UninstallProcess.Checks.exe"),
                "Relative paths cannot select processes.");
            Console.WriteLine($"Uninstall process checks passed ({_checks} assertions); only disposable fixture processes were stopped, no network or production app access.");
            return 0;
        }
        finally
        {
            foreach (var fixture in fixtures)
            {
                try { if (!fixture.HasExited) { fixture.Kill(); fixture.WaitForExit(3000); } } catch { }
                fixture.Dispose();
            }
            Directory.Delete(root, recursive: true);
        }
    }

    private static int RunFixture(string behavior)
    {
        using var signal = behavior == "graceful"
            ? new EventWaitHandle(false, EventResetMode.AutoReset, UninstallProcessService.ExitSignalName(Environment.ProcessPath!))
            : null;
        Console.WriteLine("ready");
        Console.Out.Flush();
        if (signal is not null) return signal.WaitOne(TimeSpan.FromSeconds(30)) ? 17 : 42;
        Thread.Sleep(TimeSpan.FromSeconds(30));
        return 42;
    }

    private static string CopyFixture(string root, string name)
    {
        var directory = Path.Combine(root, name);
        Directory.CreateDirectory(directory);
        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory))
            File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
        return Path.Combine(directory, "UninstallProcess.Checks.exe");
    }

    private static async Task<Process> StartFixture(string path, string behavior)
    {
        var start = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("--fixture");
        start.ArgumentList.Add(behavior);
        start.Environment["DOTNET_ROOT"] = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        var process = Process.Start(start) ?? throw new InvalidOperationException("Fixture did not start.");
        try
        {
            if (await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)) != "ready")
                throw new InvalidOperationException("Fixture failed: " + await process.StandardError.ReadToEndAsync());
            return process;
        }
        catch
        {
            if (!process.HasExited) process.Kill();
            process.Dispose();
            throw;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }

    private static void RequireThrows(Action action, string message)
    {
        try { action(); }
        catch (ArgumentException) { Require(true, message); return; }
        throw new InvalidOperationException(message);
    }
}
