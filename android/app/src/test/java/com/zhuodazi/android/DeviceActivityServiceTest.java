package com.zhuodazi.android;

import org.json.JSONObject;
import org.junit.Test;

import java.io.IOException;
import java.util.ArrayDeque;
import java.util.Queue;
import java.util.UUID;
import java.util.concurrent.Executor;

import static org.junit.Assert.*;

public class DeviceActivityServiceTest {
    private static final class Runtime implements DeviceActivityService.Clock, DeviceActivityService.Scheduler, Executor {
        long now;
        long due;
        Runnable scheduled;
        final Queue<Runnable> network = new ArrayDeque<>();
        int sent;
        boolean fail;
        final DeviceActivityService service = new DeviceActivityService(this, this, this, () -> {
            sent++;
            if (fail) throw new IOException("offline or unauthorized");
        });

        @Override public long now() { return now; }
        @Override public void postDelayed(Runnable task, long delayMillis) {
            scheduled = task;
            due = now + delayMillis;
        }
        @Override public void remove(Runnable task) {
            if (scheduled == task) scheduled = null;
        }
        @Override public void execute(Runnable task) { network.add(task); }

        void advance(long millis) {
            now += millis;
            if (scheduled != null && due <= now) {
                Runnable ready = scheduled;
                scheduled = null;
                ready.run();
            }
        }
        void finishRequest() { network.remove().run(); }
    }

    @Test public void foregroundOrOverlayKeepsOneMinuteHeartbeatAndStoppingBothCancelsIt() {
        Runtime runtime = new Runtime();
        Object activity = new Object();
        Object overlay = new Object();
        runtime.service.started(activity);
        runtime.service.started(overlay);
        runtime.advance(0);
        assertEquals(1, runtime.network.size());
        runtime.finishRequest();
        assertEquals(1, runtime.sent);
        assertEquals(60_000L, runtime.due);
        runtime.service.stopped(activity);
        runtime.advance(60_000L);
        runtime.finishRequest();
        assertEquals(2, runtime.sent);
        runtime.service.stopped(overlay);
        assertNull(runtime.scheduled);
        runtime.advance(600_000L);
        assertTrue(runtime.network.isEmpty());
    }

    @Test public void quickResumeIsThrottledButLaterResumeSendsImmediately() {
        Runtime runtime = new Runtime();
        Object activity = new Object();
        runtime.service.started(activity);
        runtime.advance(0);
        runtime.finishRequest();
        runtime.service.stopped(activity);
        runtime.advance(1_000L);
        runtime.service.started(activity);
        assertEquals(15_000L, runtime.due);
        runtime.advance(13_999L);
        assertTrue(runtime.network.isEmpty());
        runtime.advance(1);
        runtime.finishRequest();
        assertEquals(2, runtime.sent);
        runtime.service.stopped(activity);
        runtime.advance(100_000L);
        runtime.service.started(activity);
        assertEquals(runtime.now, runtime.due);
        runtime.advance(0);
        runtime.finishRequest();
        assertEquals(3, runtime.sent);
    }

    @Test public void multipleLifecyclesCannotStartOverlappingRequests() {
        Runtime runtime = new Runtime();
        Object oldActivity = new Object();
        Object replacement = new Object();
        Object overlay = new Object();
        runtime.service.started(oldActivity);
        runtime.advance(0);
        runtime.advance(30_000L);
        runtime.service.started(replacement);
        runtime.service.started(overlay);
        runtime.service.stopped(oldActivity);
        runtime.service.stopped(overlay);
        runtime.advance(60_000L);
        assertEquals(1, runtime.network.size());
        runtime.finishRequest();
        assertEquals(1, runtime.sent);
        assertNotNull(runtime.scheduled);
        runtime.advance(0);
        runtime.finishRequest();
        assertEquals(2, runtime.sent);
        runtime.service.stopped(replacement);
        assertNull(runtime.scheduled);
    }

    @Test public void workQueuedBeforeTheAppAndPetStopDoesNotSendOrReschedule() {
        Runtime runtime = new Runtime();
        Object activity = new Object();
        runtime.service.started(activity);
        runtime.advance(0);
        runtime.service.stopped(activity);
        runtime.finishRequest();
        assertEquals(0, runtime.sent);
        assertNull(runtime.scheduled);
    }

    @Test public void serviceRestartWithTheSameOwnerResumesAfterItsStoppedRequestCompletes() {
        Runtime runtime = new Runtime();
        Object overlay = new Object();
        runtime.service.started(overlay);
        runtime.advance(0);
        runtime.service.stopped(overlay);
        runtime.finishRequest();
        assertEquals(0, runtime.sent);
        assertNull(runtime.scheduled);
        runtime.service.started(overlay);
        runtime.advance(15_000L);
        runtime.finishRequest();
        assertEquals(1, runtime.sent);
        assertEquals(75_000L, runtime.due);
        runtime.service.stopped(overlay);
        assertNull(runtime.scheduled);
    }

    @Test public void failuresAreSilentAndRetriedAtTheNormalInterval() {
        Runtime runtime = new Runtime();
        runtime.fail = true;
        runtime.service.started(new Object());
        runtime.advance(0);
        runtime.finishRequest();
        assertEquals(60_000L, runtime.due);
        runtime.advance(60_000L);
        runtime.fail = false;
        runtime.finishRequest();
        assertEquals(2, runtime.sent);
        assertEquals(120_000L, runtime.due);
    }

    @Test public void trialAndActivatedIdentitiesUseTheSameMinimalHeartbeatPayload() throws Exception {
        JSONObject stored = new JSONObject().put("version", 1).put("installationId", "a".repeat(32))
            .put("credential", "b".repeat(43));
        SecureLicenseStore.LicenseRecord record = SecureLicenseStore.LicenseRecord.fromJson(stored);
        for (String licenseId : new String[]{"", UUID.randomUUID().toString()}) {
            record.licenseId = licenseId;
            JSONObject body = DeviceActivityService.heartbeatBody(record, "test-version");
            assertEquals(3, body.length());
            assertEquals(record.installationId, body.getString("installationId"));
            assertEquals(record.credential, body.getString("credential"));
            assertEquals("test-version", body.getString("appVersion"));
            assertFalse(body.has("trialExpiresAt"));
            assertFalse(body.has("licenseId"));
        }
    }
}
