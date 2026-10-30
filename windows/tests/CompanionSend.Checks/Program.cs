using ZhuoDazi.Services;

var assertions = 0;
void Require(bool condition, string message)
{
    assertions++;
    if (!condition) throw new InvalidOperationException(message);
}

async Task RequireThrows<T>(Func<Task> action, string message) where T : Exception
{
    try { await action(); }
    catch (T) { assertions++; return; }
    throw new InvalidOperationException(message);
}

// All callbacks below are local fakes: no application, dialog, message, or network is started.
foreach (var online in new bool?[] { true, false, null })
foreach (var approved in new[] { false, true })
{
    var gate = new CompanionSendGate();
    var events = new List<string>();
    var sent = await gate.SendAsync(
        _ => { events.Add("refresh"); return Task.FromResult(online); },
        status =>
        {
            Require(status == online, "Unknown presence must reach confirmation unchanged.");
            events.Add("confirm");
            return approved;
        },
        _ => { events.Add("send"); return Task.CompletedTask; },
        () => true);
    var expectedSent = online == true || approved;
    var expectedEvents = online == true ? new[] { "refresh", "send" }
        : approved ? new[] { "refresh", "confirm", "send" } : new[] { "refresh", "confirm" };
    Require(sent == expectedSent, "Declining must not report a successful send.");
    Require(events.SequenceEqual(expectedEvents), "Refresh and explicit confirmation must precede sending.");
    Require(!gate.IsBusy, "A completed or declined send must release the gate.");
}

var retryGate = new CompanionSendGate();
var failureSent = 0;
await RequireThrows<IOException>(() => retryGate.SendAsync(
    _ => throw new IOException("Simulated presence failure."),
    _ => throw new InvalidOperationException("Refresh failure must not open a dialog."),
    _ => { failureSent++; return Task.CompletedTask; }, () => true), "Refresh errors must propagate.");
Require(failureSent == 0 && !retryGate.IsBusy, "A failed refresh must not send and must release the gate.");
Require(await retryGate.SendAsync(_ => Task.FromResult<bool?>(true), _ => false,
    _ => { failureSent++; return Task.CompletedTask; }, () => true), "The next attempt must work after refresh failure.");
Require(failureSent == 1, "Only the successfully refreshed attempt may send.");

var concurrentGate = new CompanionSendGate();
var pendingRefresh = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
var concurrentSends = 0;
var firstSend = concurrentGate.SendAsync(_ => pendingRefresh.Task, _ => true,
    _ => { concurrentSends++; return Task.CompletedTask; }, () => true);
Require(concurrentGate.IsBusy, "Presence refresh must lock every send entry point.");
var duplicate = await concurrentGate.SendAsync(
    _ => throw new InvalidOperationException("A concurrent attempt must not refresh."),
    _ => throw new InvalidOperationException("A concurrent attempt must not open a dialog."),
    _ => throw new InvalidOperationException("A concurrent attempt must not send."), () => true);
Require(!duplicate && concurrentSends == 0, "Concurrent clicks must be ignored without reporting success.");
pendingRefresh.SetResult(false);
Require(await firstSend && concurrentSends == 1 && !concurrentGate.IsBusy,
    "The original confirmed attempt must send exactly once and release the gate.");

var nestedGate = new CompanionSendGate();
var nestedSends = 0;
var nested = false;
Require(await nestedGate.SendAsync(_ => Task.FromResult<bool?>(false), _ =>
{
    // A modal dialog pumps UI events; a second entry point must still stay locked.
    nested = nestedGate.SendAsync(_ => Task.FromResult<bool?>(true), _ => true,
        _ => { nestedSends++; return Task.CompletedTask; }, () => true).GetAwaiter().GetResult();
    return true;
}, _ => { nestedSends++; return Task.CompletedTask; }, () => true), "The first dialog may approve its send.");
Require(!nested && nestedSends == 1, "Modal reentrancy must not create a second send.");

foreach (var stopDuringConfirmation in new[] { false, true })
{
    var gate = new CompanionSendGate();
    var allowed = true;
    var count = 0;
    var result = await gate.SendAsync(
        _ => { if (!stopDuringConfirmation) allowed = false; return Task.FromResult<bool?>(false); },
        _ => { allowed = false; return true; },
        _ => { count++; return Task.CompletedTask; }, () => allowed);
    Require(!result && count == 0 && !gate.IsBusy,
        "Exiting, activation loss, or account changes must prevent a pending send.");
}

foreach (var cancelDuringConfirmation in new[] { false, true })
{
    var gate = new CompanionSendGate();
    using var cancellation = new CancellationTokenSource();
    var count = 0;
    await RequireThrows<OperationCanceledException>(() => gate.SendAsync(
        _ => { if (!cancelDuringConfirmation) cancellation.Cancel(); return Task.FromResult<bool?>(false); },
        _ => { cancellation.Cancel(); return true; },
        _ => { count++; return Task.CompletedTask; }, () => true, cancellation.Token),
        "Cancellation before upload must propagate.");
    Require(count == 0 && !gate.IsBusy, "Cancellation must release the gate without uploading.");
}

var uploadFailureGate = new CompanionSendGate();
await RequireThrows<IOException>(() => uploadFailureGate.SendAsync(_ => Task.FromResult<bool?>(true), _ => true,
    _ => throw new IOException("Simulated send failure."), () => true), "Send errors must propagate.");
Require(!uploadFailureGate.IsBusy, "Send failure must release the gate for retry.");

var refreshedGate = new CompanionSendGate();
var refreshes = 0;
var confirmations = 0;
Task<bool?> Refresh(CancellationToken _) => Task.FromResult<bool?>(++refreshes == 1);
bool Confirm(bool? _) { confirmations++; return false; }
Require(await refreshedGate.SendAsync(Refresh, Confirm, _ => Task.CompletedTask, () => true),
    "The first online attempt must send directly.");
Require(!await refreshedGate.SendAsync(Refresh, Confirm, _ => Task.CompletedTask, () => true),
    "The next attempt must use fresh presence even if the previous attempt was online.");
Require(refreshes == 2 && confirmations == 1, "Presence must be refreshed for each independent attempt.");

Console.WriteLine($"Companion send checks passed: {assertions} assertions; no GUI or network used.");
