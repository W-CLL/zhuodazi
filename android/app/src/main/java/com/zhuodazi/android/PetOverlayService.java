package com.zhuodazi.android;

import android.animation.ValueAnimator;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.graphics.PixelFormat;
import android.graphics.Point;
import android.graphics.drawable.Drawable;
import android.os.Build;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.provider.Settings;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.VelocityTracker;
import android.view.ViewConfiguration;
import android.view.WindowManager;
import android.view.animation.DecelerateInterpolator;

import java.util.Random;

public final class PetOverlayService extends Service {
    static final String ACTION_START = "com.zhuodazi.android.START";
    static final String ACTION_STOP = "com.zhuodazi.android.STOP";
    static final String ACTION_REFRESH = "com.zhuodazi.android.REFRESH";
    static final String ACTION_NEXT = "com.zhuodazi.android.NEXT";
    static final String ACTION_SAY = "com.zhuodazi.android.SAY";

    private static final String CHANNEL_ID = "pet_overlay";
    private static final int NOTIFICATION_ID = 2107;
    private static final int REQUEST_CONTENT = 101;
    private static final int REQUEST_STOP = 102;
    private static final int REQUEST_NEXT = 103;
    private static final int REQUEST_SAY = 104;

    private final Handler handler = new Handler(Looper.getMainLooper());
    private final Random random = new Random();
    private WindowManager windowManager;
    private WindowManager.LayoutParams windowParams;
    private PetOverlayView overlay;
    private SettingsStore settings;
    private PetRepository pets;
    private WordRepository words;
    private ValueAnimator movementAnimator;
    private VelocityTracker velocityTracker;
    private float touchDownX;
    private float touchDownY;
    private int windowDownX;
    private int windowDownY;
    private boolean dragging;
    private long lastTapAt;
    private int facing = 1;
    private String currentPet = "";
    private int touchSlop;
    private long bubbleVersion;
    private int motionVersion;

    private final Runnable wanderTask = new Runnable() {
        @Override public void run() {
            if (overlay != null && settings.movement() && !dragging) wander();
            scheduleWander();
        }
    };

    private final Runnable interactionTask = new Runnable() {
        @Override public void run() {
            if (overlay != null && settings.interactions() && !dragging) say("idle", "我在这里陪你。", 5000);
            scheduleInteraction();
        }
    };

    private final Runnable petSwitchTask = new Runnable() {
        @Override public void run() {
            if (overlay != null && settings.randomPet()) selectPet(pets.randomPet(currentPet), true);
            schedulePetSwitch();
        }
    };

    @Override public void onCreate() {
        super.onCreate();
        settings = new SettingsStore(this);
        pets = new PetRepository(this, settings);
        words = new WordRepository(this, settings);
        windowManager = (WindowManager) getSystemService(WINDOW_SERVICE);
        touchSlop = ViewConfiguration.get(this).getScaledTouchSlop();
        createNotificationChannel();
        startAsForeground();
    }

    @Override public int onStartCommand(Intent intent, int flags, int startId) {
        String action = intent == null ? ACTION_START : intent.getAction();
        if (ACTION_STOP.equals(action)) {
            stopSelf();
            return START_NOT_STICKY;
        }
        if (!Settings.canDrawOverlays(this)) {
            settings.setRunning(false);
            stopSelf();
            return START_NOT_STICKY;
        }
        if (overlay == null) createOverlay();
        if (ACTION_NEXT.equals(action)) nextPet();
        else if (ACTION_SAY.equals(action)) say("happy", "今天也一起加油。", 5000);
        else if (ACTION_REFRESH.equals(action)) refreshOverlay();
        settings.setRunning(true);
        return START_STICKY;
    }

    @Override public IBinder onBind(Intent intent) { return null; }

    @Override public void onDestroy() {
        handler.removeCallbacksAndMessages(null);
        cancelMovement();
        if (overlay != null) {
            try { windowManager.removeView(overlay); } catch (Exception ignored) { }
            overlay = null;
        }
        settings.setRunning(false);
        super.onDestroy();
    }

    private void createOverlay() {
        int petSize = dp(settings.sizeDp());
        int width = Math.max(petSize + dp(24), dp(248));
        int height = petSize + dp(96);
        overlay = new PetOverlayView(this, petSize, width, height);
        windowParams = new WindowManager.LayoutParams(
            width, height,
            WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE
                | WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS
                | WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL,
            PixelFormat.TRANSLUCENT);
        windowParams.gravity = Gravity.TOP | Gravity.START;
        Point bounds = screenBounds();
        windowParams.x = SettingsStore.clamp(settings.positionX(bounds.x - width), 0, Math.max(0, bounds.x - width));
        windowParams.y = SettingsStore.clamp(settings.positionY(bounds.y / 2), 0, Math.max(0, bounds.y - height));
        overlay.setOnTouchListener((view, event) -> handleTouch(event));
        windowManager.addView(overlay, windowParams);
        currentPet = pets.selectedPet();
        loadCurrentPet();
        say("idle", "我来啦，拖动我可以换个位置。", 4800);
        restartSchedules();
    }

    private void refreshOverlay() {
        if (overlay == null) return;
        int previousX = windowParams.x;
        int previousY = windowParams.y;
        try { windowManager.removeView(overlay); } catch (Exception ignored) { }
        overlay = null;
        settings.savePosition(previousX, previousY);
        createOverlay();
    }

    private void loadCurrentPet() {
        if (overlay == null || currentPet.isEmpty()) return;
        try {
            Drawable drawable = pets.load(currentPet);
            overlay.setPet(drawable, settings.opacity() / 100f, settings.mirrored(), facing);
        } catch (Exception error) {
            sayText("这个 GIF 暂时打不开，换一个试试。", 5000);
        }
    }

    private void selectPet(String petId, boolean announce) {
        if (petId == null || petId.isEmpty()) return;
        currentPet = petId;
        settings.putString(SettingsStore.ACTIVE_PET, petId);
        loadCurrentPet();
        if (announce) say("switch", "新搭档登场。", 4200);
    }

    private void nextPet() {
        selectPet(pets.nextPet(currentPet), true);
    }

    private boolean handleTouch(MotionEvent event) {
        if (overlay == null) return false;
        switch (event.getActionMasked()) {
            case MotionEvent.ACTION_DOWN -> {
                cancelMovement();
                handler.removeCallbacks(wanderTask);
                dragging = false;
                touchDownX = event.getRawX();
                touchDownY = event.getRawY();
                windowDownX = windowParams.x;
                windowDownY = windowParams.y;
                velocityTracker = VelocityTracker.obtain();
                velocityTracker.addMovement(event);
                return true;
            }
            case MotionEvent.ACTION_MOVE -> {
                if (velocityTracker != null) velocityTracker.addMovement(event);
                float dx = event.getRawX() - touchDownX;
                float dy = event.getRawY() - touchDownY;
                if (!dragging && Math.hypot(dx, dy) > touchSlop) {
                    dragging = true;
                    say("grab", "抓稳啦。", 2500);
                }
                if (dragging) {
                    facing = dx >= 0 ? 1 : -1;
                    overlay.face(facing, settings.mirrored());
                    moveWindow(windowDownX + Math.round(dx), windowDownY + Math.round(dy));
                }
                return true;
            }
            case MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> {
                if (velocityTracker != null) {
                    velocityTracker.addMovement(event);
                    velocityTracker.computeCurrentVelocity(1000, dp(1800));
                }
                if (dragging && event.getActionMasked() == MotionEvent.ACTION_UP) {
                    float velocityX = velocityTracker == null ? 0 : velocityTracker.getXVelocity();
                    float velocityY = velocityTracker == null ? 0 : velocityTracker.getYVelocity();
                    if (Math.hypot(velocityX, velocityY) > dp(180)) startInertia(velocityX, velocityY);
                    else savePosition();
                } else if (event.getActionMasked() == MotionEvent.ACTION_UP) {
                    long now = System.currentTimeMillis();
                    if (now - lastTapAt < 360) openSettings();
                    else say("happy", "碰到我啦！", 4200);
                    lastTapAt = now;
                }
                dragging = false;
                if (velocityTracker != null) velocityTracker.recycle();
                velocityTracker = null;
                scheduleWander();
                return true;
            }
            default -> { return false; }
        }
    }

    private void openSettings() {
        Intent intent = new Intent(this, MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        startActivity(intent);
    }

    private void wander() {
        Point bounds = screenBounds();
        int maxX = Math.max(0, bounds.x - windowParams.width);
        int maxY = Math.max(0, bounds.y - windowParams.height);
        int targetX = random.nextInt(maxX + 1);
        int verticalBand = Math.max(dp(80), maxY / 3);
        int targetY = SettingsStore.clamp(windowParams.y + random.nextInt(verticalBand * 2 + 1) - verticalBand, 0, maxY);
        facing = targetX >= windowParams.x ? 1 : -1;
        overlay.face(facing, settings.mirrored());

        int startX = windowParams.x;
        int startY = windowParams.y;
        float distance = (float) Math.hypot(targetX - startX, targetY - startY);
        long duration = Math.max(900, Math.min(4200, Math.round(distance * 4.2f)));
        if ("chaotic".equals(settings.personality())) duration = Math.max(650, duration / 2);
        movementAnimator = ValueAnimator.ofFloat(0f, 1f);
        movementAnimator.setDuration(duration);
        movementAnimator.setInterpolator(new DecelerateInterpolator());
        movementAnimator.addUpdateListener(animation -> {
            float value = (float) animation.getAnimatedValue();
            moveWindow(Math.round(startX + ((targetX - startX) * value)), Math.round(startY + ((targetY - startY) * value)));
        });
        movementAnimator.start();
    }

    private void startInertia(float initialX, float initialY) {
        cancelMovement();
        int version = motionVersion;
        final float[] velocity = { initialX, initialY };
        final long[] lastTime = { System.nanoTime() };
        Runnable physics = new Runnable() {
            @Override public void run() {
                if (overlay == null || dragging || version != motionVersion) return;
                long now = System.nanoTime();
                float dt = Math.min(0.04f, (now - lastTime[0]) / 1_000_000_000f);
                lastTime[0] = now;
                velocity[1] += dp(620) * dt;
                velocity[0] *= Math.pow(0.984, dt * 60);
                velocity[1] *= Math.pow(0.984, dt * 60);

                Point bounds = screenBounds();
                int maxX = Math.max(0, bounds.x - windowParams.width);
                int maxY = Math.max(0, bounds.y - windowParams.height);
                float nextX = windowParams.x + velocity[0] * dt;
                float nextY = windowParams.y + velocity[1] * dt;
                if (nextX <= 0 || nextX >= maxX) {
                    velocity[0] *= -0.68f;
                    nextX = SettingsStore.clamp(Math.round(nextX), 0, maxX);
                }
                if (nextY <= 0 || nextY >= maxY) {
                    velocity[1] *= -0.62f;
                    nextY = SettingsStore.clamp(Math.round(nextY), 0, maxY);
                }
                facing = velocity[0] >= 0 ? 1 : -1;
                overlay.face(facing, settings.mirrored());
                moveWindow(Math.round(nextX), Math.round(nextY));
                if (Math.hypot(velocity[0], velocity[1]) > dp(45) || windowParams.y < maxY - dp(3)) {
                    handler.postDelayed(this, 16);
                } else {
                    savePosition();
                    scheduleWander();
                }
            }
        };
        handler.post(physics);
    }

    private void moveWindow(int x, int y) {
        if (overlay == null) return;
        Point bounds = screenBounds();
        windowParams.x = SettingsStore.clamp(x, 0, Math.max(0, bounds.x - windowParams.width));
        windowParams.y = SettingsStore.clamp(y, 0, Math.max(0, bounds.y - windowParams.height));
        try { windowManager.updateViewLayout(overlay, windowParams); } catch (Exception ignored) { }
    }

    private void savePosition() {
        if (windowParams != null) settings.savePosition(windowParams.x, windowParams.y);
    }

    private void say(String action, String fallback, long duration) {
        sayText(words.reaction(action, fallback), duration);
    }

    private void sayText(String message, long duration) {
        if (overlay == null) return;
        long version = ++bubbleVersion;
        overlay.say(message);
        handler.postDelayed(() -> {
            if (overlay != null && version == bubbleVersion) overlay.hideBubble();
        }, duration);
    }

    private void restartSchedules() {
        handler.removeCallbacks(wanderTask);
        handler.removeCallbacks(interactionTask);
        handler.removeCallbacks(petSwitchTask);
        scheduleWander();
        scheduleInteraction();
        schedulePetSwitch();
    }

    private void scheduleWander() {
        handler.removeCallbacks(wanderTask);
        if (!settings.movement() || overlay == null) return;
        int minimum = switch (settings.personality()) {
            case "shy" -> 9000;
            case "clingy" -> 3500;
            case "chaotic" -> 2200;
            default -> 5000;
        };
        handler.postDelayed(wanderTask, minimum + random.nextInt(Math.max(1000, minimum)));
    }

    private void scheduleInteraction() {
        handler.removeCallbacks(interactionTask);
        if (!settings.interactions() || overlay == null) return;
        int base = switch (settings.interactionMode()) {
            case "quiet" -> 180_000;
            case "lively" -> 45_000;
            default -> 90_000;
        };
        handler.postDelayed(interactionTask, base + random.nextInt(base / 2));
    }

    private void schedulePetSwitch() {
        handler.removeCallbacks(petSwitchTask);
        if (!settings.randomPet() || overlay == null) return;
        handler.postDelayed(petSwitchTask, settings.randomPetInterval() * 1000L);
    }

    private void cancelMovement() {
        motionVersion++;
        if (movementAnimator != null) movementAnimator.cancel();
        movementAnimator = null;
    }

    private Point screenBounds() {
        if (Build.VERSION.SDK_INT >= 30) {
            android.graphics.Rect bounds = windowManager.getCurrentWindowMetrics().getBounds();
            return new Point(bounds.width(), bounds.height());
        }
        Point bounds = new Point();
        windowManager.getDefaultDisplay().getRealSize(bounds);
        return bounds;
    }

    private int dp(int value) {
        return Math.round(value * getResources().getDisplayMetrics().density);
    }

    private void createNotificationChannel() {
        NotificationChannel channel = new NotificationChannel(
            CHANNEL_ID, getString(R.string.notification_channel), NotificationManager.IMPORTANCE_LOW);
        channel.setDescription("保持悬浮桌宠运行，并提供快捷控制");
        getSystemService(NotificationManager.class).createNotificationChannel(channel);
    }

    private void startAsForeground() {
        PendingIntent content = PendingIntent.getActivity(this, REQUEST_CONTENT,
            new Intent(this, MainActivity.class), PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        PendingIntent stop = servicePendingIntent(REQUEST_STOP, ACTION_STOP);
        PendingIntent next = servicePendingIntent(REQUEST_NEXT, ACTION_NEXT);
        PendingIntent say = servicePendingIntent(REQUEST_SAY, ACTION_SAY);
        Notification notification = new Notification.Builder(this, CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_pet)
            .setContentTitle(getString(R.string.notification_title))
            .setContentText("双击桌宠打开设置，拖动后快速松手可以投掷")
            .setContentIntent(content)
            .setOngoing(true)
            .setCategory(Notification.CATEGORY_SERVICE)
            .addAction(new Notification.Action.Builder(R.drawable.ic_pet, "说句话", say).build())
            .addAction(new Notification.Action.Builder(R.drawable.ic_pet, "换一只", next).build())
            .addAction(new Notification.Action.Builder(R.drawable.ic_pet, "收起", stop).build())
            .build();
        if (Build.VERSION.SDK_INT >= 34) {
            startForeground(NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE);
        } else {
            startForeground(NOTIFICATION_ID, notification);
        }
    }

    private PendingIntent servicePendingIntent(int requestCode, String action) {
        Intent intent = new Intent(this, PetOverlayService.class).setAction(action);
        return PendingIntent.getService(this, requestCode, intent,
            PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
    }
}
