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
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.Random;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.function.Consumer;

public final class PetOverlayService extends Service {
    static final String ACTION_START = "com.zhuodazi.android.START";
    static final String ACTION_STOP = "com.zhuodazi.android.STOP";
    static final String ACTION_REFRESH = "com.zhuodazi.android.REFRESH";
    static final String ACTION_NEXT = "com.zhuodazi.android.NEXT";
    static final String ACTION_INTERACT = "com.zhuodazi.android.INTERACT";
    static final String ACTION_REACT = "com.zhuodazi.android.REACT";
    static final String ACTION_SEND_COMPANION = "com.zhuodazi.android.SEND_COMPANION";
    static final String ACTION_SHOW = "com.zhuodazi.android.SHOW";
    static final String ACTION_HIDE = "com.zhuodazi.android.HIDE";
    static final String ACTION_CLICK_THROUGH = "com.zhuodazi.android.CLICK_THROUGH";
    static final String EXTRA_REACTION = "reaction";

    private static final int BOTTOM_GUARD_DP = 48;

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
    private InteractionContentService interactionContent;
    private CompanionService companions;
    private ValueAnimator movementAnimator;
    private VelocityTracker velocityTracker;
    private float touchDownX;
    private float touchDownY;
    private int windowDownX;
    private int windowDownY;
    private boolean dragging;
    private boolean companionBusy;
    private boolean interactionBusy;
    private boolean interactionSyncBusy;
    private boolean interactionExpanded;
    private int baseWindowWidth;
    private int baseWindowHeight;
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
                startRandomInteraction(false);
            } else {
                scheduleInteraction();
            }
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
        interactionContent = new InteractionContentService(this, licenses);
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
        } else if (ACTION_REACT.equals(action)) {
            settings.setPetHidden(false);
            settings.setClickThrough(false);
            if (overlay == null) createOverlay(false);
            else applyTouchMode();
            react(intent.getStringExtra(EXTRA_REACTION));
        } else if (ACTION_INTERACT.equals(action)) {
            settings.setPetHidden(false);
            settings.setClickThrough(false);
            if (overlay == null) createOverlay(false);
            else applyTouchMode();
            startRandomInteraction(true);
        } else if (ACTION_HIDE.equals(action)) {
            hidePet();
        } else if (ACTION_CLICK_THROUGH.equals(action)) {
            enableClickThrough();
        } else {
            if (overlay == null && !settings.petHidden()) createOverlay(false);
            if (ACTION_NEXT.equals(action)) nextPet();
            else if (ACTION_SEND_COMPANION.equals(action)) sendToCompanion();
            else if (ACTION_REFRESH.equals(action)) refreshOverlay();
        }
        settings.setRunning(true);
        updateNotification();
        scheduleCompanionPoll();
        warmInteractionContent();
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
        int width = Math.max(petSize + dp(12), dp(128));
        int height = petSize + dp(64);
        baseWindowWidth = width;
        baseWindowHeight = height;
        interactionExpanded = false;
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
        windowParams.y = SettingsStore.clamp(settings.positionY(bounds.y / 2), 0, maxWindowY(bounds, height));
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
        interactionBusy = false;
        interactionExpanded = false;
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
            case PetOverlayView.MENU_INTERACT -> startRandomInteraction(true);
            case PetOverlayView.MENU_SEND -> sendToCompanion();
            case PetOverlayView.MENU_NEXT -> nextPet();
            case PetOverlayView.MENU_CLICK_THROUGH -> enableClickThrough();
            case PetOverlayView.MENU_HIDE -> hidePet();
            default -> { }
        }
    }

    private void react(String reaction) {
        String action = switch (reaction == null ? "" : reaction) {
            case "cheer", "calm", "sleepy", "surprised", "sad", "happy" -> reaction;
            default -> "happy";
        };
        String fallback = switch (action) {
            case "cheer" -> "再坚持一下，我在旁边给你加油！";
            case "calm" -> "先慢慢呼吸，我们把节奏找回来。";
            case "sleepy" -> "眼睛休息一下，我替你守着桌面。";
            case "surprised" -> "今天会不会突然有一件小好事？";
            case "sad" -> "不开心也没关系，我先陪你待一会儿。";
            default -> "碰到我啦，今天也一起加油。";
        };
        say(action, fallback, 5200);
    }

    private void startRandomInteraction(boolean manual) {
        handler.removeCallbacks(interactionTask);
        if (!licenses.hasPremiumAccess()) {
            if (manual) sayText("体验或正式激活后可以使用随机趣味互动。", 5200);
            scheduleInteraction();
            return;
        }
        if (overlay == null || dragging || (!manual && (settings.clickThrough() || !settings.interactions()))) {
            scheduleInteraction();
            return;
        }
        if (interactionBusy) {
            if (manual) sayText("先把眼前这个互动完成吧。", 3600);
            return;
        }
        interactionBusy = true;
        cancelMovement();
        boolean moodDue = interactionContent.isMoodPromptDue();
        if (moodDue && (interactionContent.cachedCount() == 0 || random.nextInt(4) == 0)) {
            showMoodInteraction();
            return;
        }
        InteractionContentService.Item item = interactionContent.takeNextContent();
        if (item != null) {
            showContentInteraction(item);
            warmInteractionContent();
            return;
        }
        sayText("正在找点有趣的内容…", 5000);
        networkExecutor.execute(() -> {
            Exception failure = null;
            try { interactionContent.refillOnline(); }
            catch (Exception error) { failure = error; }
            InteractionContentService.Item loaded = interactionContent.takeNextContent();
            Exception finalFailure = failure;
            handler.post(() -> {
                if (overlay == null) {
                    finishInteraction();
                } else if (loaded != null) {
                    showContentInteraction(loaded);
                } else if (moodDue) {
                    showMoodInteraction();
                } else {
                    if (manual) sayText(finalFailure == null
                        ? "趣味内容正在补货，稍后再来找我吧。"
                        : "线上内容暂时不可用，稍后再试。", 5200);
                    finishInteraction();
                }
            });
        });
    }

    private void showMoodInteraction() {
        interactionContent.markMoodPrompted();
        showOverlayInteraction("随手问候", "今天心情怎么样？", Arrays.asList(
            new PetOverlayView.InteractionChoice("开心", "happy"),
            new PetOverlayView.InteractionChoice("还可以", "okay"),
            new PetOverlayView.InteractionChoice("不咋地", "low")
        ), mood -> {
            if (mood == null) {
                finishInteraction();
                return;
            }
            interactionContent.recordMood(mood);
            String response = switch (mood) {
                case "happy" -> "那就把这份开心多留一会儿。";
                case "low" -> "先不用硬撑，我在这儿陪你一会儿。";
                default -> "平平稳稳也很好，慢慢来。";
            };
            finishInteraction();
            sayText(response, 5600);
        });
    }

    private void showContentInteraction(InteractionContentService.Item item) {
        if ("joke".equals(item.type)) {
            showOverlayInteraction("冷笑话时间", item.prompt,
                Arrays.asList(new PetOverlayView.InteractionChoice("看答案", "reveal", true)), choice -> {
                    if (choice == null) {
                        finishInteraction();
                        return;
                    }
                    interactionContent.recordJoke(item);
                    showAnswerInteraction("答案", formatContentAnswer(item), null);
                });
            return;
        }
        if ("tip".equals(item.type) || "care".equals(item.type)) {
            String title = "tip".equals(item.type) ? "生活小贴士" : "关心你一下";
            String button = "tip".equals(item.type) ? "记下了" : "我知道了";
            showOverlayInteraction(title, item.prompt + "\n\n" + formatContentAnswer(item),
                Arrays.asList(new PetOverlayView.InteractionChoice(button, "done", true)),
                choice -> finishInteraction());
            return;
        }
        String title = switch (item.type) {
            case "math" -> "来道数学题";
            case "riddle" -> "脑筋急转弯";
            default -> "趣味知识";
        };
        if (!item.choices.isEmpty()) {
            List<PetOverlayView.InteractionChoice> choices = new ArrayList<>();
            for (String choice : item.choices)
                choices.add(new PetOverlayView.InteractionChoice(choice, choice));
            showOverlayInteraction(title, item.prompt, choices, selected -> {
                if (selected == null) {
                    finishInteraction();
                    return;
                }
                boolean correct = answersMatch(selected, item.answer);
                interactionContent.recordQuiz(item, correct);
                showAnswerInteraction(correct ? "答对了" : "答案揭晓",
                    formatContentAnswer(item), correct);
            });
            return;
        }
        showOverlayInteraction(title, item.prompt,
            Arrays.asList(new PetOverlayView.InteractionChoice("查看答案", "reveal", true)), choice -> {
                if (choice == null) {
                    finishInteraction();
                    return;
                }
                showOverlayInteraction("答案", formatContentAnswer(item), Arrays.asList(
                    new PetOverlayView.InteractionChoice("答对了", "correct", true),
                    new PetOverlayView.InteractionChoice("没答对", "wrong")
                ), result -> {
                    if (result != null) interactionContent.recordQuiz(item, "correct".equals(result));
                    finishInteraction();
                });
            });
    }

    private void showAnswerInteraction(String title, String message, Boolean correct) {
        String button = Boolean.TRUE.equals(correct) ? "收下这分" : "知道了";
        showOverlayInteraction(title, message,
            Arrays.asList(new PetOverlayView.InteractionChoice(button, "done", true)),
            choice -> finishInteraction());
    }

    private void showOverlayInteraction(String title, String message,
                                        List<PetOverlayView.InteractionChoice> choices,
                                        Consumer<String> callback) {
        if (overlay == null) {
            finishInteraction();
            return;
        }
        expandInteractionWindow();
        overlay.showInteraction(title, message, choices, callback::accept);
        overlay.post(this::fitInteractionWindowToContent);
    }

    private void finishInteraction() {
        if (overlay != null) overlay.hideInteraction();
        collapseInteractionWindow();
        interactionBusy = false;
        scheduleInteraction();
        warmInteractionContent();
        if (interactionContent.shouldFlush() && !interactionSyncBusy) {
            interactionSyncBusy = true;
            networkExecutor.execute(() -> {
                try { interactionContent.flushEvents(); } catch (Exception ignored) { }
                finally { interactionSyncBusy = false; }
            });
        }
    }

    private void warmInteractionContent() {
        if (!interactionContent.needsRefill() || interactionSyncBusy) return;
        interactionSyncBusy = true;
        networkExecutor.execute(() -> {
            try { interactionContent.refillOnline(); } catch (Exception ignored) { }
            finally { interactionSyncBusy = false; }
        });
    }

    private String formatContentAnswer(InteractionContentService.Item item) {
        String answer = item.answer.trim().isEmpty() ? "答案暂缺" : item.answer.trim();
        String explanation = item.explanation.trim();
        return explanation.isEmpty() || explanation.equals(answer)
            ? answer : answer + "\n" + explanation;
    }

    private boolean answersMatch(String selected, String answer) {
        return selected.trim().equalsIgnoreCase(answer.trim());
    }

    private void expandInteractionWindow() {
        if (interactionExpanded || overlay == null || windowParams == null) return;
        Point bounds = screenBounds();
        int width = Math.max(baseWindowWidth, Math.min(dp(196), bounds.x - dp(12)));
        int height = Math.min(baseWindowHeight + dp(300),
            Math.max(baseWindowHeight, bounds.y - dp(BOTTOM_GUARD_DP)));
        interactionExpanded = true;
        resizeWindowAnchored(width, height);
    }

    private void fitInteractionWindowToContent() {
        if (!interactionExpanded || overlay == null || windowParams == null
            || !overlay.isInteractionVisible()) return;
        Point bounds = screenBounds();
        int petSize = Math.max(0, baseWindowHeight - dp(64));
        // Pull the pet slightly into the card's lower edge so both read as one interaction unit.
        int desiredHeight = overlay.interactionCardHeight() + petSize - dp(16);
        int height = SettingsStore.clamp(desiredHeight, baseWindowHeight,
            Math.max(baseWindowHeight, bounds.y - dp(BOTTOM_GUARD_DP)));
        resizeWindowAnchored(windowParams.width, height);
    }

    private void collapseInteractionWindow() {
        if (!interactionExpanded || overlay == null || windowParams == null) return;
        interactionExpanded = false;
        resizeWindowAnchored(baseWindowWidth, baseWindowHeight);
    }

    private void resizeWindowAnchored(int width, int height) {
        if (overlay == null || windowParams == null) return;
        int centerX = windowParams.x + windowParams.width / 2;
        int bottom = windowParams.y + windowParams.height;
        Point bounds = screenBounds();
        windowParams.width = width;
        windowParams.height = height;
        windowParams.x = SettingsStore.clamp(centerX - width / 2, 0, Math.max(0, bounds.x - width));
        windowParams.y = SettingsStore.clamp(bottom - height, 0, maxWindowY(bounds, height));
        try {
            windowManager.updateViewLayout(overlay, windowParams);
            overlay.requestLayout();
        } catch (Exception ignored) { }
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
        if (overlay.isInteractionVisible()) return true;
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
        int maxY = maxWindowY(bounds, windowParams.height);
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
                int maxY = maxWindowY(bounds, windowParams.height);
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
        windowParams.y = SettingsStore.clamp(y, 0, maxWindowY(bounds, windowParams.height));
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

    private int maxWindowY(Point bounds, int windowHeight) {
        return Math.max(0, bounds.y - windowHeight - dp(BOTTOM_GUARD_DP));
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
