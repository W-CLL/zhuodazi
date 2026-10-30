namespace ZhuoDazi.Services;

/// <summary>Refreshes presence before each send and serializes confirmation across all entry points.</summary>
public sealed class CompanionSendGate
{
    private int _busy;
    public bool IsBusy => Volatile.Read(ref _busy) != 0;

    public async Task<bool> SendAsync(
        Func<CancellationToken, Task<bool?>> refreshOnline,
        Func<bool?, bool> confirm,
        Func<CancellationToken, Task> send,
        Func<bool> canContinue,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!canContinue()) return false;
            var online = await refreshOnline(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!canContinue()) return false;
            if (online != true && !confirm(online)) return false;
            cancellationToken.ThrowIfCancellationRequested();
            if (!canContinue()) return false;
            await send(cancellationToken);
            return true;
        }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }
}
