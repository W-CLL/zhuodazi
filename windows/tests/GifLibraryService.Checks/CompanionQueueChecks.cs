using System.IO;
using System.Text.Json;
using ZhuoDazi;
using ZhuoDazi.Services;

internal static class CompanionQueueChecks
{
    public static async Task Run()
    {
        (string Name, Func<string, Task> Check)[] checks =
        [
            ("restart preserves order and full messages", CheckRestart),
            ("quiet or hidden state retains pending visits", CheckSuppressed),
            ("pause preserves the active visit at the front", CheckPause),
            ("duplicate polls do not replace or replay receipts", CheckDuplicates),
            ("completed receipts survive restart without replay", CheckCompleted),
            ("identity switch isolates an active visit", CheckIdentitySwitch),
            ("first activation migration is durable and idempotent", CheckTrialMigration),
            ("unexpected presenter cancellation retains the receipt", CheckUnexpectedCancellation),
            ("out-of-scope manifest paths are ignored", CheckInvalidManifest),
            ("corrupt manifests are preserved and block writes", CheckCorruptManifest),
            ("temporary read failures never overwrite receipts", CheckLockedManifest),
            ("manual presentation consumes only one queued visit", CheckOneOnly)
        ];
        var root = Path.Combine(Path.GetTempPath(), "ZhuoDazi-CompanionQueueChecks", Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var (name, check) in checks)
            {
                await check(Path.Combine(root, Guid.NewGuid().ToString("N")));
                Console.WriteLine($"Companion queue check passed: {name}.");
            }
            Console.WriteLine($"Companion queue checks passed: {checks.Length} scenarios.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static async Task CheckRestart(string root)
    {
        var inbox = new CompanionInboxStore(root, "trial:installation-a");
        var original = Seed(inbox, "first", "second", "third");
        var restored = new CompanionInboxStore(root, inbox.Identity);
        Require(restored.Pending().SequenceEqual(original), "A restart lost visit order, sender, path, or full message.");
        var queue = new CompanionVisitorQueue();
        queue.Restore(restored);
        var shown = await Drain(queue);
        Require(shown.SequenceEqual(original), "Restored visits did not appear in their received order.");
        Require(restored.Pending().Count == 0 && !queue.HasPending, "Completed visits remained pending.");
        Require(original.All(v => !File.Exists(v.FilePath)), "Completed GIF files were not removed.");
    }

    private static async Task CheckSuppressed(string root)
    {
        var inbox = new CompanionInboxStore(root, "license:a");
        var original = Seed(inbox, "first", "second", "third");
        var queue = new CompanionVisitorQueue();
        queue.Restore(inbox);
        var called = false;
        await queue.ShowQueuedAsync(() => false, (_, _) => { called = true; return Task.CompletedTask; });
        Require(!called && queue.HasPending, "A suppressed queue started presentation or lost pending state.");
        Require(new CompanionInboxStore(root, inbox.Identity).Pending().SequenceEqual(original),
            "Quiet or hidden state consumed durable receipts.");
    }

    private static async Task CheckPause(string root)
    {
        var inbox = new CompanionInboxStore(root, "license:a");
        var original = Seed(inbox, "first", "second", "third");
        var queue = new CompanionVisitorQueue();
        queue.Restore(inbox);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = queue.ShowQueuedAsync(() => true, (visit, token) =>
        {
            Require(visit.Id == "first", "The wrong visit was shown before pausing.");
            started.SetResult();
            return Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        await started.Task;
        queue.PauseActive();
        await active;
        Require(!queue.IsShowing && inbox.Pending().Count == 3, "Pausing consumed the active receipt.");
        Require((await Drain(queue)).SequenceEqual(original), "Resume did not keep the paused visit at the front.");
    }

    private static async Task CheckDuplicates(string root)
    {
        var inbox = new CompanionInboxStore(root, "license:a");
        var original = Seed(inbox, "one")[0];
        inbox.SaveDownloaded(original with { SenderName = "replacement", Message = "short replacement" });
        var queue = new CompanionVisitorQueue();
        queue.Restore(inbox);
        queue.Enqueue(inbox.Pending());
        queue.Enqueue(inbox.Pending());
        queue.Restore(inbox);
        var shown = await Drain(queue);
        Require(shown.Count == 1 && shown[0] == original, "Repeated polls duplicated a receipt or overwrote its metadata.");
    }

    private static async Task CheckCompleted(string root)
    {
        var inbox = new CompanionInboxStore(root, "license:a");
        var original = Seed(inbox, "one")[0];
        var queue = new CompanionVisitorQueue();
        queue.Restore(inbox);
        await Drain(queue);
        var restored = new CompanionInboxStore(root, inbox.Identity);
        Require(restored.Contains(original.Id) && restored.IsCompleted(original.Id) && restored.Pending().Count == 0,
            "The completed receipt tombstone was lost on restart.");
        restored.SaveDownloaded(original with { Message = "server repeated after failed acknowledge" });
        queue = new CompanionVisitorQueue();
        queue.Restore(restored);
        queue.Enqueue([original]);
        queue.EnqueueFirst(original);
        Require((await Drain(queue)).Count == 0, "A completed delivery replayed after a duplicate server response.");
    }

    private static async Task CheckIdentitySwitch(string root)
    {
        var accountA = new CompanionInboxStore(root, "license:a");
        var accountB = new CompanionInboxStore(root, "license:b");
        var originalA = Seed(accountA, "shared-id")[0];
        var originalB = Seed(accountB, "shared-id")[0];
        Require(accountA.DirectoryPath != accountB.DirectoryPath && originalA.FilePath != originalB.FilePath,
            "Different identities shared an inbox or GIF path.");
        var queue = new CompanionVisitorQueue();
        queue.Restore(accountA);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = queue.ShowQueuedAsync(() => true, (_, token) =>
        {
            started.SetResult();
            return Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        await started.Task;
        queue.Restore(accountB);
        await active;
        var shown = await Drain(queue);
        Require(shown.Count == 1 && shown[0] == originalB,
            "The active old account visit was inserted into the new account queue.");
        Require(accountA.Pending().Single() == originalA && !accountA.IsCompleted(originalA.Id),
            "Switching accounts consumed the previous account's pending receipt.");
        Require(accountB.Pending().Count == 0, "The new account receipt was not completed.");
        queue.Restore(accountA);
        Require((await Drain(queue)).Single() == originalA, "Returning to the previous account lost its pending receipt.");
    }

    private static async Task CheckTrialMigration(string root)
    {
        var trial = new CompanionInboxStore(root, "trial:installation-a");
        var original = Seed(trial, "first", "second", "third");
        var activated = new CompanionInboxStore(root, "license:first");
        var incomplete = Seed(activated, "first")[0];
        File.Delete(incomplete.FilePath); // Retry an interrupted migration with a missing destination GIF.
        activated.ImportPendingFrom(trial);
        activated.ImportPendingFrom(trial);
        var restored = new CompanionInboxStore(root, activated.Identity);
        var migrated = restored.Pending();
        Require(trial.Pending().Count == 0 && original.All(v => trial.IsCompleted(v.Id)),
            "Successful first activation left duplicate pending trial receipts.");
        Require(migrated.Count == 3 && migrated.Select(v => (v.Id, v.SenderName, v.Message))
            .SequenceEqual(original.Select(v => (v.Id, v.SenderName, v.Message))),
            "First activation lost or duplicated visit metadata.");
        Require(migrated.All(v => v.FilePath == restored.GifPath(v.Id) && File.Exists(v.FilePath)),
            "Migration did not save GIFs in the activated identity directory.");
        var queue = new CompanionVisitorQueue();
        queue.Restore(restored);
        Require((await Drain(queue)).Count == 3, "Migrated receipts could not be presented after restart.");
    }

    private static async Task CheckUnexpectedCancellation(string root)
    {
        var inbox = new CompanionInboxStore(root, "license:a");
        var original = Seed(inbox, "one")[0];
        var queue = new CompanionVisitorQueue();
        queue.Restore(inbox);
        await queue.ShowQueuedAsync(() => true, (_, _) => throw new OperationCanceledException("View disappeared."));
        Require(queue.HasPending && inbox.Pending().Single() == original,
            "An unexpected presenter cancellation consumed an unseen receipt.");
        Require((await Drain(queue)).Single() == original, "A cancelled presentation could not be retried.");
    }

    private static Task CheckInvalidManifest(string root)
    {
        var inbox = new CompanionInboxStore(root, "license:a");
        Directory.CreateDirectory(inbox.DirectoryPath);
        var outside = Path.Combine(root, "outside.gif");
        File.WriteAllBytes(outside, [71, 73, 70, 56, 57, 97]);
        File.WriteAllText(Path.Combine(inbox.DirectoryPath, "inbox.json"), JsonSerializer.Serialize(new[]
        {
            new { Visit = new CompanionVisit("outside", "sender", outside, "private"), ReceivedAt = DateTimeOffset.UtcNow,
                Completed = false, CompletedAt = (DateTimeOffset?)null }
        }));
        Require(inbox.Pending().Count == 0 && !inbox.Contains("outside"), "A manifest imported a file outside its identity directory.");
        Require(File.Exists(outside), "Rejecting a malformed manifest deleted an unrelated file.");
        return Task.CompletedTask;
    }

    private static async Task CheckOneOnly(string root)
    {
        var inbox = new CompanionInboxStore(root, "license:a");
        var original = Seed(inbox, "first", "second", "third");
        var queue = new CompanionVisitorQueue();
        queue.Restore(inbox);
        var shown = new List<CompanionVisit>();
        await queue.ShowQueuedAsync(() => true, (visit, _) => { shown.Add(visit); return Task.CompletedTask; }, oneOnly: true);
        Require(shown.Count == 1 && shown[0] == original[0] && inbox.Pending().Count == 2 && queue.HasPending,
            "Manual presentation consumed more than its single allowed visit.");
    }

    private static Task CheckCorruptManifest(string root)
    {
        var inbox = new CompanionInboxStore(root, "license:a");
        var original = Seed(inbox, "one")[0];
        var manifest = Path.Combine(inbox.DirectoryPath, "inbox.json");
        var saved = File.ReadAllText(manifest);
        const string corrupt = "[{\"Visit\": {\"Id\": \"interrupted";
        File.WriteAllText(manifest, corrupt);
        var queue = new CompanionVisitorQueue();
        queue.Restore(inbox); // A corrupt receipt file must not prevent application startup.
        RequireThrows<InvalidDataException>(() => inbox.SaveDownloaded(original with { Id = "new" }),
            "A new download overwrote a corrupt inbox as though it were empty.");
        RequireThrows<InvalidDataException>(() => inbox.Complete(original.Id),
            "Completing a visit overwrote a corrupt inbox.");
        Require(File.ReadAllText(manifest) == corrupt && File.Exists(original.FilePath),
            "The corrupt manifest or acknowledged pending GIF was modified.");
        File.WriteAllText(manifest, saved);
        queue.Restore(inbox);
        Require(queue.HasPending && inbox.Pending().Single() == original,
            "Restoring the valid manifest did not recover the preserved receipt.");
        return Task.CompletedTask;
    }

    private static Task CheckLockedManifest(string root)
    {
        var inbox = new CompanionInboxStore(root, "license:a");
        var original = Seed(inbox, "one")[0];
        var manifest = Path.Combine(inbox.DirectoryPath, "inbox.json");
        var saved = File.ReadAllText(manifest);
        var queue = new CompanionVisitorQueue();
        using (var locked = new FileStream(manifest, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            queue.Restore(inbox);
            RequireThrows<IOException>(() => inbox.SaveDownloaded(original with { Id = "new" }),
                "A temporary manifest read failure overwrote acknowledged pending receipts.");
        }
        Require(File.ReadAllText(manifest) == saved && File.Exists(original.FilePath),
            "A temporarily locked manifest or its pending GIF was modified.");
        queue.Restore(inbox);
        Require(queue.HasPending && inbox.Pending().Single() == original,
            "Releasing a temporary manifest lock did not recover the original receipt.");
        return Task.CompletedTask;
    }

    private static CompanionVisit[] Seed(CompanionInboxStore inbox, params string[] ids)
    {
        Directory.CreateDirectory(inbox.DirectoryPath);
        return ids.Select(id =>
        {
            var visit = new CompanionVisit(id, "完整昵称 " + id, inbox.GifPath(id), inbox.Identity + " 第一行留言。\n第二行包含完整内容：" + new string('访', 100));
            File.WriteAllBytes(visit.FilePath, [71, 73, 70, 56, 57, 97, 1, 0, 1, 0]);
            inbox.SaveDownloaded(visit);
            return visit;
        }).ToArray();
    }

    private static async Task<List<CompanionVisit>> Drain(CompanionVisitorQueue queue)
    {
        var shown = new List<CompanionVisit>();
        await queue.ShowQueuedAsync(() => true, (visit, _) => { shown.Add(visit); return Task.CompletedTask; });
        return shown;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireThrows<TException>(Action action, string message) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException(message);
    }
}
