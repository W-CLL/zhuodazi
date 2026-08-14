package com.zhuodazi.android;

import android.animation.ValueAnimator;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.graphics.ImageDecoder;
import android.graphics.PixelFormat;
import android.graphics.Point;
import android.graphics.drawable.AnimatedImageDrawable;
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

import java.io.File;
import java.util.List;
import java.util.Random;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public final class PetOverlayService extends Service {
    static final String ACTION_START = "com.zhuodazi.android.START";
    static final String ACTION_STOP = "com.zhuodazi.android.STOP";
    static final String ACTION_REFRESH = "com.zhuodazi.android.REFRESH";
    static final String ACTION_NEXT = "com.zhuodazi.android.NEXT";
    static final String ACTION_INTERACT = "com.zhuodazi.android.INTERACT";
    static final String ACTION_SEND_COMPANION = "com.zhuodazi.android.SEND_COMPANION";
    static final String ACTION_SHOW = "com.zhuodazi.android.SHOW";
    static final String ACTION_HIDE = "com.zhuodazi.android.HIDE";
    static final String ACTION_CLICK_THROUGH = "com.zhuodazi.android.CLICK_THROUGH";

    private static final String CHANNEL_ID = "pet_overlay";
    private static final String VISITOR_CHANNEL_ID = "companion_visits";
    private static final int NOTIFICATION_ID = 2107;
    private static final int VISITOR_NOTIFICATION_ID = 2108;
    private static final int REQUEST_CONTENT = 101;
    private static final int REQUEST_STOP = 102;
    private static final int REQUEST_NEXT = 103;
    private static final int REQUEST_INTERACT = 104;
    private static final int REQUEST_SHOW = 105;
    private static final int REQUEST_HIDE = 106;

    private final Handler handler = new Handler(Looper.getMainLooper());
    private final Random random = new Random();
    private final ExecutorService networkExecutor = Executors.newSingleThreadExecutor();
    private WindowManager windowManager;
    private WindowManager.LayoutParams windowParams;
    private PetOverlayView overlay;
    private WindowManager.LayoutParams visitorParams;
    private PetOverlayView visitorOverlay;
    private SettingsStore settings;
    private PetRepository pets;
    private WordRepository words;
    private LicenseService licenses;
    private CompanionService companions;
    private ValueAnimator movementAnimator;
    private VelocityTracker velocityTracker;
    private float touchDownX;
    private float touchDownY;
    private int windowDownX;
    private int windowDownY;
    private boolean dragging;
    private boolean companionBusy;
    private int facing = 1;
    private String currentPet = "";
    private int touchSlop;
    private long bubbleVersion;
    private int motionVersion;
    private CompanionService.Visit pendingVisit;
    private File visitorFile;

    private final Runnable wanderTask = new Runnable() {
        @Override public void run() {
            if (overlay != null && settings.movement() && !dragging && !settings.clickThrough()) wander();
            scheduleWander();
        }
    };

    private final Runnable interactionTask = new Runnable() {
        @Override public void run() {
            if (overlay != null && settings.interactions() && licenses.hasPremiumAccess() && !dragging) {
                say("idle", "我在这里陪你。", 5000);
            }
            scheduleInteraction();
        }
    };

    private final Runnable petSwitchTask = new Runnable() {
        @Override public void run() {
            if (overlay != null && settings.randomPet()) selectPet(pets.randomPet(currentPet), true);
            schedulePetSwitch();
        }
    };

    private final Runnable companionPollTask = new Runnable() {
        @Override public void run() {
            if (!licenses.isActivated() || companionBusy) {
                scheduleCompanionPoll();
                return;
            }
            companionBusy = true;
            networkExecutor.execute(() -> {
                try {
                    List<CompanionService.Visit> visits = companions.receive();
                    handler.post(() -> {
                        for (CompanionService.Visit visit : visits) receiveVisit(visit);
                    });
                } catch (Exception ignored) {
                    // Polling failures are silent; direct user actions still report errors.
                } finally {
                    companionBusy = false;
                    handler.post(PetOverlayService.this::scheduleCompanionPoll);
                }
            });
        }
    };

    @Override public void onCreate() {
        super.onCreate();
        settings = new SettingsStore(this);
        pets = new PetRepository(this, settings);
        words = new WordRepository(this, settings);
        licenses = new LicenseService(this);
        companions = new CompanionService(this, licenses, pets);
        windowManager = (WindowManager) getSystemService(WINDOW_SERVICE);
        touchSlop = ViewConfiguration.get(this).getScaledTouchSlop();
        createNotificationChannels();
        startAsForeground();
    }

    @Override public int onStartCommand(Intent intent, int flags, int startId) {
        String action = intent == null || intent.getAction() == null ? ACTION_START : intent.getAction();
        if (ACTION_STOP.equals(action)) {
            stopSelf();
            return START_NOT_STICKY;
        }
        if (!Settings.canDrawOverlays(this)) {
            settings.setRunning(false);
            stopSelf();
            return START_NOT_STICKY;
        }

        if (ACTION_START.equals(action) || ACTION_SHOW.equals(action)) {
            settings.setPetHidden(false);
            settings.setClickThrough(false);
            if (overlay == null) createOverlay(true);
            else applyTouchMode();
            if (pendingVisit != null) {
                CompanionService.Visit visit = pendingVisit;
                pendingVisit = null;
                showVisitor(visit);
            }
        } else if (ACTION_HIDE.equals(action)) {
            hidePet();
        } else if (ACTION_CLICK_THROUGH.equals(action)) {
            enableClickThrough();
        } else {
            if (overlay == null && !settings.petHidden()) createOverlay(false);
            if (ACTION_NEXT.equals(action)) nextPet();
            else if (ACTION_INTERACT.equals(action)) say("happy", "碰到我啦！", 4800);
            else if (ACTION_SEND_COMPANION.equals(action)) sendToCompanion();
            else if (ACTION_REFRESH.equals(action)) refreshOverlay();
        }
        settings.setRunning(true);
        updateNotification();
        scheduleCompanionPoll();
        return START_STICKY;
    }

    @Override public IBinder onBind(Intent intent) { return null; }

    @Override public void onDestroy() {
        handler.removeCallbacksAndMessages(null);
        cancelMovement();
        removeVisitor(false);
        removeMainOverlay(true);
        networkExecutor.shutdownNow();
        settings.setRunning(false);
        super.onDestroy();
    }

    private void createOverlay(boolean announce) {
        if (settings.petHidden() || overlay != null) return;
        int petSize = dp(settings.sizeDp());
        int width = Math.max(petSize + dp(24), dp(304));
        int height = petSize + dp(96);
        overlay = new PetOverlayView(this, petSize, width, height);
        int touchFlag = settings.clickThrough() ? WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE : 0;
        windowParams = new WindowManager.LayoutParams(
            width, height,
            WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE
                | WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS
                | WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL
                | touchFlag,
            PixelFormat.TRANSLUCENT);
        windowParams.gravity = Gravity.TOP | Gravity.START;
        Point bounds = screenBounds();
        windowParams.x = SettingsStore.clamp(settings.positionX(bounds.x - width), 0, Math.max(0, bounds.x - width));
        windowParams.y = SettingsStore.clamp(settings.positionY(bounds.y / 2), 0, Math.max(0, bounds.y - height));
        overlay.setOnTouchListener((view, event) -> handleTouch(event));
        overlay.setMenuListener(this::handleMenuAction);
        windowManager.addView(overlay, windowParams);
        currentPet = pets.selectedPet();
        loadCurrentPet();
        if (announce) say("idle", "我来啦，点一下可以打开快捷菜单。", 4800);
        restartSchedules();
    }

    private void refreshOverlay() {
        if (overlay == null) return;
        int previousX = windowParams.x;
        int previousY = windowParams.y;
        removeMainOverlay(false);
        settings.savePosition(previousX, previousY);
        createOverlay(false);
    }

    private void removeMainOverlay(boolean savePosition) {
        if (overlay == null) return;
        if (savePosition && windowParams != null) settings.savePosition(windowParams.x, windowParams.y);
        try { windowManager.removeView(overlay); } catch (Exception ignored) { }
        overlay = null;
        windowParams = null;
        handler.removeCallbacks(wanderTask);
        handler.removeCallbacks(interactionTask);
        handler.removeCallbacks(petSwitchTask);
        cancelMovement();
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

    private void nextPet() { selectPet(pets.nextPet(currentPet), true); }

    private void handleMenuAction(String action) {
        switch (action) {
            case PetOverlayView.MENU_INTERACT -> say("happy", "今天也一起加油。", 4800);
            case PetOverlayView.MENU_SEND -> sendToCompanion();
            case PetOverlayView.MENU_NEXT -> nextPet();
            case PetOverlayView.MENU_CLICK_THROUGH -> enableClickThrough();
            case PetOverlayView.MENU_HIDE -> hidePet();
            default -> { }
        }
    }

    private void hidePet() {
        settings.setPetHidden(true);
        settings.setClickThrough(false);
        removeMainOverlay(true);
        updateNotification();
    }

    private void enableClickThrough() {
        if (overlay == null || windowParams == null) return;
        overlay.hideQuickMenu();
        settings.setClickThrough(true);
        applyTouchMode();
        updateNotification();
    }

    private void applyTouchMode() {
        if (overlay == null || windowParams == null) return;
        if (settings.clickThrough()) windowParams.flags |= WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE;
        else windowParams.flags &= ~WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE;
        try { windowManager.updateViewLayout(overlay, windowParams); } catch (Exception ignored) { }
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
                    overlay.hideQuickMenu();
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
                    overlay.toggleQuickMenu();
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

    private void wander() {
        if (windowParams == null) return;
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
            moveWindow(Math.round(startX + ((targetX - startX) * value)),
                Math.round(startY + ((targetY - startY) * value)));
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
                if (overlay == null || windowParams == null || dragging || version != motionVersion) return;
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
        if (overlay == null || windowParams == null) return;
        Point bounds = screenBounds();
        windowParams.x = SettingsStore.clamp(x, 0, Math.max(0, bounds.x - windowParams.width));
        windowParams.y = SettingsStore.clamp(y, 0, Math.max(0, bounds.y - windowParams.height));
        try { windowManager.updateViewLayout(overlay, windowParams); } catch (Exception ignored) { }
    }

    private void savePosition() {
        if (windowParams != null) settings.savePosition(windowParams.x, windowParams.y);
    }

    private void say(String action, String fallback, long duration) {
        String pack = licenses.hasPremiumAccess() ? settings.wordPack() : "元气夸夸.json";
        sayText(words.reaction(pack, action, fallback), duration);
    }

    private void sayText(String message, long duration) {
        if (overlay == null) return;
        long version = ++bubbleVersion;
        overlay.say(message);
        handler.postDelayed(() -> {
            if (overlay != null && version == bubbleVersion) overlay.hideBubble();
        }, duration);
    }

    private void sendToCompanion() {
        if (!licenses.isActivated()) {
            sayText("搭子联机需要先在“我的”中正式激活。", 5200);
            return;
        }
        if (companionBusy) {
            sayText("正在连接搭子，请稍等。", 3600);
            return;
        }
        companionBusy = true;
        sayText("正在把我发送给搭子…", 5000);
        networkExecutor.execute(() -> {
            try {
                String recipient = companions.sendCurrentGif();
                handler.post(() -> sayText("已经去找 " + recipient + " 啦！", 5200));
            } catch (Exception error) {
                handler.post(() -> sayText("发送失败：" + safeMessage(error), 6500));
            } finally {
                companionBusy = false;
            }
        });
    }

    private void receiveVisit(CompanionService.Visit visit) {
        if (overlay == null || settings.petHidden() || settings.clickThrough()) {
            if (pendingVisit != null) pendingVisit.file().delete();
            pendingVisit = visit;
            showVisitorNotification(visit.senderName());
        } else {
            showVisitor(visit);
        }
    }

    private void showVisitor(CompanionService.Visit visit) {
        removeVisitor(true);
        try {
            Drawable drawable = ImageDecoder.decodeDrawable(ImageDecoder.createSource(visit.file()));
            if (drawable instanceof AnimatedImageDrawable animated) {
                animated.setRepeatCount(AnimatedImageDrawable.REPEAT_INFINITE);
                animated.start();
            }
            int petSize = dp(Math.max(120, Math.min(200, settings.sizeDp() - 20)));
            int width = Math.max(petSize + dp(20), dp(230));
            int height = petSize + dp(92);
            visitorOverlay = new PetOverlayView(this, petSize, width, height);
            visitorFile = visit.file();
            visitorOverlay.setPet(drawable, 1f, false, -facing);
            visitorOverlay.say(visit.senderName() + " 来串门啦！");
            visitorParams = new WindowManager.LayoutParams(width, height,
                WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE
                    | WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE
                    | WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS,
                PixelFormat.TRANSLUCENT);
            visitorParams.gravity = Gravity.TOP | Gravity.START;
            Point bounds = screenBounds();
            int mainX = windowParams == null ? bounds.x / 2 : windowParams.x;
            visitorParams.x = mainX < bounds.x / 2 ? Math.max(0, bounds.x - width - dp(8)) : dp(8);
            visitorParams.y = windowParams == null ? bounds.y / 2 : windowParams.y;
            windowManager.addView(visitorOverlay, visitorParams);
            getSystemService(NotificationManager.class).cancel(VISITOR_NOTIFICATION_ID);
            handler.postDelayed(() -> removeVisitor(true), 10_000);
        } catch (Exception error) {
            visit.file().delete();
            showVisitorNotification(visit.senderName());
        }
    }

    private void removeVisitor(boolean deleteFile) {
        if (visitorOverlay != null) {
            try { windowManager.removeView(visitorOverlay); } catch (Exception ignored) { }
            visitorOverlay = null;
            visitorParams = null;
        }
        if (deleteFile && visitorFile != null) {
            visitorFile.delete();
        }
        visitorFile = null;
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
        if (!settings.movement() || overlay == null || settings.clickThrough()) return;
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
        if (!settings.interactions() || overlay == null || !licenses.hasPremiumAccess()) return;
        int minimumMinutes;
        int additionalMinutes;
        switch (settings.interactionMode()) {
            case "quiet" -> { minimumMinutes = 60; additionalMinutes = 60; }
            case "lively" -> { minimumMinutes = 10; additionalMinutes = 20; }
            default -> { minimumMinutes = 30; additionalMinutes = 30; }
        }
        long delay = (minimumMinutes + random.nextInt(additionalMinutes + 1)) * 60_000L;
        handler.postDelayed(interactionTask, delay);
    }

    private void schedulePetSwitch() {
        handler.removeCallbacks(petSwitchTask);
        if (!settings.randomPet() || overlay == null) return;
        handler.postDelayed(petSwitchTask, settings.randomPetInterval() * 1000L);
    }

    private void scheduleCompanionPoll() {
        handler.removeCallbacks(companionPollTask);
        if (licenses.isActivated() && settings.running()) handler.postDelayed(companionPollTask, 30_000L);
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

    private int dp(int value) { return Math.round(value * getResources().getDisplayMetrics().density); }

    private void createNotificationChannels() {
        NotificationChannel service = new NotificationChannel(
            CHANNEL_ID, getString(R.string.notification_channel), NotificationManager.IMPORTANCE_LOW);
        service.setDescription("保持悬浮桌宠运行，并提供快捷控制");
        getSystemService(NotificationManager.class).createNotificationChannel(service);
        NotificationChannel visitor = new NotificationChannel(
            VISITOR_CHANNEL_ID, "搭子来访", NotificationManager.IMPORTANCE_DEFAULT);
        visitor.setDescription("搭子发送桌宠时通知你");
        getSystemService(NotificationManager.class).createNotificationChannel(visitor);
    }

    private void startAsForeground() {
        Notification notification = buildServiceNotification();
        if (Build.VERSION.SDK_INT >= 34) {
            startForeground(NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE);
        } else {
            startForeground(NOTIFICATION_ID, notification);
        }
    }

    private void updateNotification() {
        getSystemService(NotificationManager.class).notify(NOTIFICATION_ID, buildServiceNotification());
    }

    private Notification buildServiceNotification() {
        PendingIntent content = PendingIntent.getActivity(this, REQUEST_CONTENT,
            new Intent(this, MainActivity.class), PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        Notification.Builder builder = new Notification.Builder(this, CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_pet)
            .setContentTitle(getString(R.string.notification_title))
            .setContentIntent(content)
            .setOngoing(true)
            .setCategory(Notification.CATEGORY_SERVICE);
        if (settings.petHidden()) {
            builder.setContentText("桌宠已隐藏，搭子接收仍在运行")
                .addAction(action("显示桌宠", REQUEST_SHOW, ACTION_SHOW))
                .addAction(action("完全退出", REQUEST_STOP, ACTION_STOP));
        } else if (settings.clickThrough()) {
            builder.setContentText("桌宠已穿透，触摸会传给下层应用")
                .addAction(action("恢复触摸", REQUEST_SHOW, ACTION_SHOW))
                .addAction(action("隐藏", REQUEST_HIDE, ACTION_HIDE))
                .addAction(action("完全退出", REQUEST_STOP, ACTION_STOP));
        } else {
            builder.setContentText("单击桌宠打开互动菜单，拖动可以换位置")
                .addAction(action("换一只", REQUEST_NEXT, ACTION_NEXT))
                .addAction(action("隐藏", REQUEST_HIDE, ACTION_HIDE))
                .addAction(action("完全退出", REQUEST_STOP, ACTION_STOP));
        }
        return builder.build();
    }

    private Notification.Action action(String title, int requestCode, String action) {
        return new Notification.Action.Builder(R.drawable.ic_pet, title,
            servicePendingIntent(requestCode, action)).build();
    }

    private void showVisitorNotification(String sender) {
        PendingIntent show = servicePendingIntent(REQUEST_SHOW, ACTION_SHOW);
        Notification notification = new Notification.Builder(this, VISITOR_CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_pet)
            .setContentTitle(sender + " 的桌宠来串门了")
            .setContentText("点一下显示桌宠并查看来访")
            .setContentIntent(show)
            .setAutoCancel(true)
            .addAction(new Notification.Action.Builder(R.drawable.ic_pet, "查看", show).build())
            .build();
        getSystemService(NotificationManager.class).notify(VISITOR_NOTIFICATION_ID, notification);
    }

    private PendingIntent servicePendingIntent(int requestCode, String action) {
        Intent intent = new Intent(this, PetOverlayService.class).setAction(action);
        return PendingIntent.getService(this, requestCode, intent,
            PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
    }

    private static String safeMessage(Exception error) {
        String message = error.getMessage();
        return message == null || message.trim().isEmpty() ? "网络服务暂时不可用" : message;
    }
}
