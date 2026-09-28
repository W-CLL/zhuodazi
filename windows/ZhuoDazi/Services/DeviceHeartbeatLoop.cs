using System.Threading;

namespace ZhuoDazi.Services;

/// <summary>Device activity is independent of premium access, pet visibility and hall membership.</summary>
internal sealed class DeviceHeartbeatLoop : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MinimumGap = TimeSpan.FromSeconds(10);
    private readonly object _gate = new();
    private readonly Func<CancellationToken, Task> _send;
    private readonly TimeProvider _clock;
    private readonly ITimer _timer;
    private CancellationTokenSource _cancellation = new();
    private bool _started;
    private bool _suspended;
    private bool _disposed;
    private bool _sending;
    private bool _identityRefreshPending;
    private long? _lastAttempt;

    internal DeviceHeartbeatLoop(Func<CancellationToken, Task> send, TimeProvider? clock = null)
    {
        _send = send;
        _clock = clock ?? TimeProvider.System;
        _timer = _clock.CreateTimer(_ => Request(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    internal void Start()
    {
        lock (_gate)
        {
            if (_disposed || _started) return;
            _started = true;
            _timer.Change(Interval, Interval);
        }
        Request();
    }

    // Trial registration may finish after the initial request. Coalesce that retry if still in flight.
    internal void IdentityRefreshed() => Request(force: true);

    internal void Suspend()
    {
        lock (_gate)
        {
            if (_disposed || _suspended) return;
            _suspended = true;
            _identityRefreshPending = false;
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _cancellation.Cancel();
        }
    }

    internal void Resume()
    {
        bool wasSuspended;
        lock (_gate)
        {
            if (_disposed || !_started) return;
            wasSuspended = _suspended;
            if (wasSuspended)
            {
                _suspended = false;
                _cancellation.Dispose();
                _cancellation = new CancellationTokenSource();
                _timer.Change(Interval, Interval);
            }
        }
        Request(force: wasSuspended);
    }

    private void Request(bool force = false)
    {
        CancellationToken token;
        lock (_gate)
        {
            if (_disposed || !_started || _suspended) return;
            if (_sending)
            {
                _identityRefreshPending |= force;
                return;
            }
            if (!force && _lastAttempt is { } last && _clock.GetElapsedTime(last) < MinimumGap) return;
            _sending = true;
            _lastAttempt = _clock.GetTimestamp();
            token = _cancellation.Token;
        }
        _ = SendAsync(token);
    }

    private async Task SendAsync(CancellationToken token)
    {
        try { await _send(token).ConfigureAwait(false); }
        catch { }
        finally
        {
            bool again;
            lock (_gate)
            {
                _sending = false;
                again = _identityRefreshPending && !_disposed && !_suspended;
                _identityRefreshPending = false;
            }
            if (again) Request(force: true);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Dispose();
            _cancellation.Cancel();
            _cancellation.Dispose();
        }
    }
}
