package com.zhuodazi.android;

import android.content.Context;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;

import org.json.JSONObject;

import java.util.Collections;
import java.util.IdentityHashMap;
import java.util.Set;
import java.util.concurrent.Executor;
import java.util.concurrent.Executors;

/** Tracks an open app or running pet independently of feature authorization. */
final class DeviceActivityService {
    static final long INTERVAL_MS = 60_000L;
    static final long MINIMUM_INTERVAL_MS = 15_000L;
    private static DeviceActivityService shared;

    interface Clock { long now(); }
    interface Scheduler {
        void postDelayed(Runnable task, long delayMillis);
        void remove(Runnable task);
    }
    interface Heartbeat { void send() throws Exception; }

    private final Clock clock;
    private final Scheduler scheduler;
    private final Executor executor;
    private final Heartbeat heartbeat;
    private final Set<Object> owners = Collections.newSetFromMap(new IdentityHashMap<>());
    private final Runnable tick = this::trySend;
    private boolean attempted;
    private boolean inFlight;
    private long lastAttemptAt;

    static synchronized void start(Context context, Object owner) {
        if (shared == null) {
            Context application = context.getApplicationContext();
            Handler handler = new Handler(Looper.getMainLooper());
            Scheduler scheduler = new Scheduler() {
                @Override public void postDelayed(Runnable task, long delayMillis) {
                    handler.postDelayed(task, delayMillis);
                }
                @Override public void remove(Runnable task) { handler.removeCallbacks(task); }
            };
            shared = new DeviceActivityService(SystemClock::elapsedRealtime, scheduler,
                Executors.newSingleThreadExecutor(), () -> sendHeartbeat(application));
        }
        shared.started(owner);
    }

    static synchronized void stop(Object owner) {
        if (shared != null) shared.stopped(owner);
    }

    DeviceActivityService(Clock clock, Scheduler scheduler, Executor executor, Heartbeat heartbeat) {
        this.clock = clock;
        this.scheduler = scheduler;
        this.executor = executor;
        this.heartbeat = heartbeat;
    }

    synchronized void started(Object owner) {
        if (!owners.add(owner) || inFlight) return;
        schedule(attempted ? Math.max(0, MINIMUM_INTERVAL_MS - (clock.now() - lastAttemptAt)) : 0);
    }

    synchronized void stopped(Object owner) {
        owners.remove(owner);
        if (owners.isEmpty()) scheduler.remove(tick);
    }

    private synchronized void trySend() {
        if (owners.isEmpty() || inFlight) return;
        long elapsed = attempted ? clock.now() - lastAttemptAt : MINIMUM_INTERVAL_MS;
        if (elapsed < MINIMUM_INTERVAL_MS) {
            schedule(MINIMUM_INTERVAL_MS - elapsed);
            return;
        }
        inFlight = true;
        attempted = true;
        lastAttemptAt = clock.now();
        executor.execute(() -> {
            try {
                synchronized (DeviceActivityService.this) {
                    if (owners.isEmpty()) return;
                }
                heartbeat.send();
            } catch (Exception ignored) {
                // Offline, unknown devices and expired credentials must not interrupt the app.
            } finally {
                completed();
            }
        });
    }

    private synchronized void completed() {
        inFlight = false;
        if (!owners.isEmpty()) schedule(Math.max(0, INTERVAL_MS - (clock.now() - lastAttemptAt)));
    }

    private void schedule(long delayMillis) {
        scheduler.remove(tick);
        scheduler.postDelayed(tick, delayMillis);
    }

    static JSONObject heartbeatBody(SecureLicenseStore.LicenseRecord record, String appVersion) throws Exception {
        return new JSONObject().put("installationId", record.installationId)
            .put("credential", record.credential).put("appVersion", appVersion);
    }

    private static void sendHeartbeat(Context context) throws Exception {
        SecureLicenseStore.LicenseRecord record = new SecureLicenseStore(context).record();
        JSONObject response = NetworkClient.json(context, "POST", DeskPetApi.DEVICE_HEARTBEAT,
            heartbeatBody(record, NetworkClient.appVersion(context)), null, NetworkClient.Auth.NONE);
        if (!response.optBoolean("ok")) throw new IllegalStateException("Device heartbeat was not accepted");
    }
}
