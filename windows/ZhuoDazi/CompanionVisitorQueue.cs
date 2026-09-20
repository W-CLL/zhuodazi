using System.IO;
using ZhuoDazi.Services;

namespace ZhuoDazi;

internal sealed class CompanionVisitorQueue
{
    private readonly LinkedList<CompanionVisit> _visits = new();
    private readonly HashSet<string> _known = new(StringComparer.Ordinal);
    private CancellationTokenSource? _activeCancellation;
    private bool _showing;
    private bool _preserveActive;
    private bool _stopAfterCurrent;
    private CompanionInboxStore? _inbox;
    private long _generation;

    public bool IsShowing => _showing;
    public bool HasPending => _visits.Count > 0;

    public void Restore(CompanionInboxStore inbox)
    {
        if (_inbox?.Identity != inbox.Identity)
        {
            PauseActive();
            _generation++;
            _visits.Clear();
            _known.Clear();
            _inbox = inbox;
        }
        try { Enqueue(inbox.Pending()); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // Startup and account switching remain usable. The store still rejects mutations
            // until its existing manifest can be read, preserving acknowledged pending receipts.
        }
    }

    public void Enqueue(IEnumerable<CompanionVisit> visits)
    {
        foreach (var visit in visits)
            if (_inbox?.IsCompleted(visit.Id) != true && _known.Add(visit.Id)) _visits.AddLast(visit);
    }

    public void EnqueueFirst(CompanionVisit visit)
    {
        if (_inbox?.IsCompleted(visit.Id) != true && _known.Add(visit.Id)) _visits.AddFirst(visit);
    }

    public void CloseActive()
    {
        _preserveActive = false;
        _stopAfterCurrent = true;
        _activeCancellation?.Cancel();
    }

    public void PauseActive()
    {
        _preserveActive = true;
        _stopAfterCurrent = true;
        _activeCancellation?.Cancel();
    }

    public Task ShowQueuedAsync(Func<bool> canShow, Func<CompanionVisit, CancellationToken, Task> present, bool oneOnly = false)
    {
        if (_showing || _visits.Count == 0 || !canShow()) return Task.CompletedTask;
        return ShowQueuedInternalAsync(canShow, present, oneOnly);
    }

    private async Task ShowQueuedInternalAsync(Func<bool> canShow, Func<CompanionVisit, CancellationToken, Task> present, bool oneOnly)
    {
        if (_showing) return;
        _showing = true;
        _stopAfterCurrent = false;
        try
        {
            while (_visits.First is { } next && canShow())
            {
                var visit = next.Value;
                var generation = _generation;
                var inbox = _inbox;
                _visits.RemoveFirst();
                _preserveActive = false;
                using var cancellation = new CancellationTokenSource();
                _activeCancellation = cancellation;
                try { await present(visit, cancellation.Token); }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                catch { _preserveActive = true; _stopAfterCurrent = true; }
                finally
                {
                    _activeCancellation = null;
                    if (generation == _generation && _preserveActive) _visits.AddFirst(visit);
                    else if (generation == _generation)
                    {
                        _known.Remove(visit.Id);
                        if (inbox?.Contains(visit.Id) == true) inbox.Complete(visit.Id);
                        else try { File.Delete(visit.FilePath); } catch { }
                    }
                }
                if (_stopAfterCurrent || oneOnly) break;
            }
        }
        finally { _showing = false; }
    }
}
