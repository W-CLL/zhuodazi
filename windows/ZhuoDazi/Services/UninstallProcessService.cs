using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ZhuoDazi.Services;

internal static class UninstallProcessService
{
    private const uint QueryLimitedInformation = 0x1000;
    private const uint Synchronize = 0x00100000;
    private const uint Terminate = 0x0001;
    private const uint WaitObjectSignaled = 0;
    private const uint WaitTimeout = 258;
    private const int ProcessNoLongerExists = 87;
    private static readonly TimeSpan GracefulTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan TerminationTimeout = TimeSpan.FromSeconds(3);

    internal static string ExitSignalName(string executablePath)
    {
        var normalized = NormalizeExecutablePath(executablePath).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return "Local\\ZhuoDazi.Uninstall.Exit." + hash;
    }

    // The caller must prevent new launches until removal finishes. Never use a
    // process-name-only kill: another installation may have the same filename.
    internal static async Task<bool> StopInstalledProcessesAsync(
        string currentExecutablePath,
        string? legacyExecutablePath = null,
        CancellationToken cancellationToken = default)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            NormalizeExecutablePath(currentExecutablePath)
        };
        if (!string.IsNullOrWhiteSpace(legacyExecutablePath))
            paths.Add(NormalizeExecutablePath(legacyExecutablePath));

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryFindMatchingProcesses(paths, out var processes)) return false;
            if (processes.Count == 0) return true;

            foreach (var path in paths)
            {
                try
                {
                    using var signal = EventWaitHandle.OpenExisting(ExitSignalName(path));
                    signal.Set();
                }
                catch (WaitHandleCannotBeOpenedException) { } // Older versions have no exit signal.
                catch (UnauthorizedAccessException) { } // Exact-path termination remains the fallback.
            }

            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < GracefulTimeout)
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                if (!TryFindMatchingProcesses(paths, out processes)) return false;
                if (processes.Count == 0) return true;
            }

            cancellationToken.ThrowIfCancellationRequested();
            foreach (var processId in processes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryTerminateMatchingProcess(processId, paths)) return false;
            }

            deadline.Restart();
            do
            {
                if (!TryFindMatchingProcesses(paths, out processes)) return false;
                if (processes.Count == 0) return true;
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            } while (deadline.Elapsed < TerminationTimeout);
            return false;
        }
        catch (OperationCanceledException) { return false; }
    }

    private static bool TryFindMatchingProcesses(HashSet<string> paths, out List<int> matches)
    {
        matches = [];
        var names = paths.Select(Path.GetFileNameWithoutExtension).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            Process[] candidates;
            try { candidates = Process.GetProcessesByName(name); }
            catch { return false; }
            try
            {
                foreach (var candidate in candidates)
                {
                    var processId = candidate.Id;
                    if (processId == Environment.ProcessId) continue;
                    using var handle = OpenProcess(QueryLimitedInformation | Synchronize, false, processId);
                    if (handle.IsInvalid)
                    {
                        if (Marshal.GetLastWin32Error() == ProcessNoLongerExists) continue;
                        return false; // An unreadable candidate cannot safely be ruled out.
                    }
                    var wait = WaitForSingleObject(handle, 0);
                    if (wait == WaitObjectSignaled) continue;
                    if (wait != WaitTimeout) return false;
                    if (!TryReadExecutablePath(handle, out var path))
                    {
                        if (WaitForSingleObject(handle, 0) == WaitObjectSignaled) continue;
                        return false;
                    }
                    if (paths.Contains(path)) matches.Add(processId);
                }
            }
            finally
            {
                foreach (var candidate in candidates) candidate.Dispose();
            }
        }
        return true;
    }

    private static bool TryTerminateMatchingProcess(int processId, HashSet<string> paths)
    {
        if (processId == Environment.ProcessId) return true;
        using var handle = OpenProcess(QueryLimitedInformation | Synchronize | Terminate, false, processId);
        if (handle.IsInvalid) return Marshal.GetLastWin32Error() == ProcessNoLongerExists;
        if (WaitForSingleObject(handle, 0) == WaitObjectSignaled) return true;
        if (!TryReadExecutablePath(handle, out var path))
            return WaitForSingleObject(handle, 0) == WaitObjectSignaled;
        if (!paths.Contains(path)) return true; // The PID may have been reused after enumeration.

        // Query and terminate through the same handle so PID reuse cannot target
        // a different process between the final path check and termination.
        return TerminateProcess(handle, 0)
            || WaitForSingleObject(handle, 0) == WaitObjectSignaled;
    }

    private static bool TryReadExecutablePath(SafeProcessHandle process, out string path)
    {
        var buffer = new StringBuilder(32768);
        var length = buffer.Capacity;
        path = string.Empty;
        if (!QueryFullProcessImageName(process, 0, buffer, ref length)) return false;
        try { path = NormalizeExecutablePath(buffer.ToString()); return true; }
        catch { return false; }
    }

    private static string NormalizeExecutablePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase)) path = "\\\\" + path[8..];
        else if (path.StartsWith("\\\\?\\", StringComparison.Ordinal)) path = path[4..];
        if (!Path.IsPathFullyQualified(path) || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("卸载进程路径必须是应用程序的完整路径。", nameof(path));
        return Path.GetFullPath(path);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder executableName, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);
}
