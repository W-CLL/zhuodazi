package com.zhuodazi.android;

import org.junit.Test;
import java.io.*;
import java.nio.file.*;
import static org.junit.Assert.*;

public class PendingVisitQueueTest {
    private PendingVisitQueue.Entry entry(Path directory, String id) throws Exception {
        Path gif = directory.resolve(id + ".gif");
        Files.write(gif, new byte[]{'G', 'I', 'F'});
        return new PendingVisitQueue.Entry(id, "来访者" + id, "你好", gif.toString());
    }

    @Test public void threeHiddenVisitsSurviveRestartAndPlayInOrder() throws Exception {
        Path directory = Files.createTempDirectory("visit-inbox-test");
        File file = directory.resolve("inbox.bin").toFile();
        PendingVisitQueue queue = new PendingVisitQueue(file);
        for (String id : new String[]{"one", "two", "three"}) queue.enqueue(entry(directory, id));
        PendingVisitQueue restarted = new PendingVisitQueue(file);
        assertEquals(3, restarted.pending().size());
        assertEquals("one", restarted.pending().get(0).id());
        restarted.complete("one");
        assertFalse(Files.exists(directory.resolve("one.gif")));
        assertEquals("two", new PendingVisitQueue(file).pending().get(0).id());
        restarted.complete("two"); restarted.complete("three");
        assertTrue(new PendingVisitQueue(file).pending().isEmpty());
    }

    @Test public void repeatedDownloadAndReceiptRetryDoNotReplayViewedVisits() throws Exception {
        Path directory = Files.createTempDirectory("visit-dedupe-test");
        PendingVisitQueue queue = new PendingVisitQueue(directory.resolve("inbox.bin").toFile());
        PendingVisitQueue.Entry visit = entry(directory, "same");
        queue.enqueue(visit); queue.enqueue(visit);
        assertEquals(1, queue.pending().size());
        queue.complete("same"); queue.enqueue(visit);
        assertTrue(queue.pending().isEmpty());
        assertTrue(queue.contains("same"));
    }

    @Test public void activityAndServiceInstancesCannotOverwriteEachOther() throws Exception {
        Path directory = Files.createTempDirectory("visit-concurrent-test");
        File file = directory.resolve("inbox.bin").toFile();
        PendingVisitQueue first = new PendingVisitQueue(file), second = new PendingVisitQueue(file);
        first.enqueue(entry(directory, "first")); second.enqueue(entry(directory, "second"));
        first.complete("first");
        assertEquals("second", second.pending().get(0).id());
    }

    @Test public void corruptInboxFailsClosedInsteadOfDeletingPendingData() throws Exception {
        Path file = Files.createTempFile("visit-corrupt-test", ".bin");
        byte[] original = new byte[]{1, 2, 3};
        Files.write(file, original);
        try {
            new PendingVisitQueue(file.toFile()).enqueue(new PendingVisitQueue.Entry("id", "name", "", "file.gif"));
            fail("corrupt inbox must stop acknowledgement");
        } catch (IOException expected) { assertArrayEquals(original, Files.readAllBytes(file)); }
    }

    @Test public void accountSwitchKeepsPendingAndCompletedReceiptsIsolated() throws Exception {
        Path directory = Files.createTempDirectory("visit-account-isolation");
        PendingVisitQueue first = PendingVisitQueue.forOwner(directory.toFile(), "license:first");
        PendingVisitQueue second = PendingVisitQueue.forOwner(directory.toFile(), "license:second");
        first.enqueue(entry(directory, "first-unread"));
        first.enqueue(entry(directory, "viewed")); first.complete("viewed");
        assertTrue(second.pending().isEmpty());
        assertFalse(second.contains("viewed"));
        // A request captured before a switch can only reference the original account's inbox.
        first.enqueue(entry(directory, "late-first-result"));
        second.enqueue(entry(directory, "second-unread"));
        assertEquals(1, second.pending().size());
        assertEquals("second-unread", second.pending().get(0).id());
        assertEquals(2, PendingVisitQueue.forOwner(directory.toFile(), "license:first").pending().size());
    }

    @Test public void firstActivationPreservesUnviewedTrialVisitsAndReceiptDedupe() throws Exception {
        Path directory = Files.createTempDirectory("visit-trial-migration");
        String trial = "trial:installation-one", activated = "license:first";
        PendingVisitQueue source = PendingVisitQueue.forOwner(directory.toFile(), trial);
        source.enqueue(entry(directory, "trial-unread"));
        source.enqueue(entry(directory, "trial-viewed")); source.complete("trial-viewed");
        PendingVisitQueue.migrateForActivation(directory.toFile(), trial, activated);
        PendingVisitQueue destination = PendingVisitQueue.forOwner(directory.toFile(), activated);
        assertEquals("trial-unread", destination.pending().get(0).id());
        assertTrue(destination.contains("trial-viewed"));
        assertTrue(Files.isRegularFile(Path.of(destination.pending().get(0).path())));
        // Repeating after a crash before credential save is idempotent and leaves the original intact.
        PendingVisitQueue.migrateForActivation(directory.toFile(), trial, activated);
        assertEquals(1, destination.pending().size());
        assertEquals(1, source.pending().size());
        PendingVisitQueue.finishTrialMigration(directory.toFile(), trial);
        assertTrue(source.pending().isEmpty());
        assertTrue(Files.isRegularFile(Path.of(destination.pending().get(0).path())));
        assertTrue(PendingVisitQueue.forOwner(directory.toFile(), "trial:other-installation").pending().isEmpty());
    }

    @Test public void switchingBetweenActivatedAccountsNeverMigratesVisits() throws Exception {
        Path directory = Files.createTempDirectory("visit-license-switch");
        PendingVisitQueue source = PendingVisitQueue.forOwner(directory.toFile(), "license:first");
        source.enqueue(entry(directory, "private-visit"));
        PendingVisitQueue.migrateForActivation(directory.toFile(), "license:first", "license:second");
        PendingVisitQueue.finishTrialMigration(directory.toFile(), "license:first");
        assertTrue(PendingVisitQueue.forOwner(directory.toFile(), "license:second").pending().isEmpty());
        assertEquals("private-visit", source.pending().get(0).id());
    }
}
