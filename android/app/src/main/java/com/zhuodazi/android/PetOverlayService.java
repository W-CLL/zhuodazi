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
import android.graphics.Rect;
import android.graphics.Insets;
import android.view.WindowInsets;
import android.graphics.drawable.AnimatedImageDrawable;
import android.graphics.drawable.Drawable;
import android.os.Build;
import android.os.Bundle;
import android.os.ResultReceiver;
import android.os.SystemClock;
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
import java.util.Map;
import java.util.LinkedHashMap;
import java.util.Random;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.function.Consumer;

public final class PetOverlayService extends Service {
    static final String ACTION_CANCEL_REQUEST = "com.zhuodazi.android.CANCEL_REQUEST";
    static final String ACTION_END_SCENE = "com.zhuodazi.android.END_SCENE";
    static final String ACTION_GUIDE = "com.zhuodazi.android.GUIDE";
    static final String EXTRA_RESULT = "action_result";
    static final String ACTION_START = "com.zhuodazi.android.START";
    static final String ACTION_STOP = "com.zhuodazi.android.STOP";
    static final String ACTION_REFRESH = "com.zhuodazi.android.REFRESH";
    static final String ACTION_NEXT = "com.zhuodazi.android.NEXT";
    static final String ACTION_INTERACT = "com.zhuodazi.android.INTERACT";
    static final String ACTION_REACT = "com.zhuodazi.android.REACT";
    static final String ACTION_SEND_COMPANION = "com.zhuodazi.android.SEND_COMPANION";
    static final String ACTION_TRIAL_VISIT = "com.zhuodazi.android.TRIAL_VISIT";
    static final String EXTRA_VISIT_CATEGORY = "visit_category";
    static final String ACTION_SHOW = "com.zhuodazi.android.SHOW";
    static final String ACTION_HIDE = "com.zhuodazi.android.HIDE";
    static final String ACTION_CLICK_THROUGH = "com.zhuodazi.android.CLICK_THROUGH";
    static final String ACTION_THEATER = "com.zhuodazi.android.THEATER";
    static final String EXTRA_REACTION = "reaction";

    private static final int BOTTOM_GUARD_DP = 48;

    private static final String CHANNEL_ID = "pet_overlay";
    private static final String VISITOR_CHANNEL_ID = "companion_visits";
    private static final String REMINDER_CHANNEL_ID = "deskpet_reminders";
    private static final int NOTIFICATION_ID = 2107;
    private static final int VISITOR_NOTIFICATION_ID = 2108;
    private static final int REMINDER_NOTIFICATION_ID = 2109;
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
    private TheaterScriptStore theaterScripts;
    private ReminderStore reminderStore;
    private ValueAnimator movementAnimator;
    private Rect theaterRestoreWindow;
    private TheaterScriptStore.Script currentTheaterScript;
    private int currentTheaterScene;
    private boolean currentTheaterPartner;
    private int currentTheaterTextOffset;
    private boolean menuAbove = true;
    private VelocityTracker velocityTracker;
    private float touchDownX;
    private float touchDownY;
    private int windowDownX;
    private int windowDownY;
    private boolean dragging;
    private boolean companionBusy;
    private boolean trialVisitBusy;
    private boolean theaterActive;
    private boolean theaterVisitor;
    private int theaterVersion;
    private int reminderExpressionVersion;
    private boolean interactionBusy;
    private boolean interactionSyncBusy;
    private boolean interactionExpanded;
    private boolean menuExpanded;
    private int baseWindowWidth;
    private int baseWindowHeight;
    private int facing = 1;
    private String currentPet = "";
    private int touchSlop;
    private long bubbleVersion;
    private int motionVersion;
    private long visitorGeneration;
    private long visitorBubbleVersion;
    private File visitorFile;
    private String visitOwner = "";
    private static PetOverlayService liveService;
    private ResultReceiver pendingInteractionResult;
    private String pendingRequestId = "";
    private long pendingRequestDeadline = Long.MAX_VALUE;
    private int interactionGeneration;
    private int guideGeneration;
    private String guideDemo = "";
    private boolean lastPetLoadSucceeded;
    private boolean pendingPetRefresh;
    private boolean lastQuiet;
    private boolean trialCheckInFlight;
    private long nextTrialCheckAt;
    private boolean destroyed;

    static Map<String, Object> runtimeSnapshot() {
        Map<String, Object> state = new LinkedHashMap<>();
        PetOverlayService live = liveService;
        state.put("guideDemo", live == null ? "" : live.guideDemo);
        state.put("interactionBusy", live != null && live.interactionBusy);
        state.put("theaterActive", live != null && live.theaterActive);
        state.put("petVisible", live != null && live.overlay != null && live.overlay.isAttachedToWindow() && live.settings.running() && live.lastPetLoadSucceeded && !live.settings.petHidden() && !live.settings.clickThrough());
        return state;
    }

    private static void answer(ResultReceiver receiver, String error) {
        if (receiver == null) return;
        Bundle data = new Bundle();
        if (error != null) data.putString("message", error);
        receiver.send(error == null ? 0 : 1, data);
    }

    private void answerInteraction(String error) {
        ResultReceiver receiver = pendingInteractionResult;
        pendingInteractionResult = null;
        pendingRequestId = "";
        pendingRequestDeadline = Long.MAX_VALUE;
        answer(receiver, error);
    }


    private final Runnable wanderTask = new Runnable() {
        @Override public void run() {
            if (!settings.guide().holdsAttention() && overlay != null && settings.movement() && !dragging && !settings.clickThrough() && !theaterActive) wander();
            scheduleWander();
        }
    };

    private final Runnable interactionTask = new Runnable() {
        @Override public void run() {
            if (overlay != null && settings.interactions() && licenses.hasPremiumAccess() && !dragging && !theaterActive) {
                startRandomInteraction(false);
            } else {
                scheduleInteraction();
            }
        }
    };

    private final Runnable petSwitchTask = new Runnable() {
        @Override public void run() {
            if (overlay != null && settings.randomPet() && !theaterActive) selectPet(pets.randomPet(currentPet), true);
            schedulePetSwitch();
        }
    };

    private final Runnable companionPollTask = new Runnable() {
        @Override public void run() {
            if ((!licenses.isActivated() && !licenses.isTrialActive()) || companionBusy) {
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
                    handler.post(PetOverlayService.this::showNextVisit);
                    handler.post(PetOverlayService.this::scheduleCompanionPoll);
                }
            });
        }
    };

    private final Runnable theaterTask = new Runnable() {
        @Override public void run() {
            startTheater(false);
            scheduleTheater();
        }
    };

    private final Runnable reminderTask = this::checkReminders;
    private final Runnable trialCheckTask = this::refreshTrialInBackground;

    @Override public void onCreate() {
        super.onCreate();
        liveService = this;
        settings = new SettingsStore(this);
        pets = new PetRepository(this, settings);
        words = new WordRepository(this, settings);
        licenses = new LicenseService(this);
        interactionContent = new InteractionContentService(this, licenses);
        companions = new CompanionService(this, licenses, pets);
        visitOwner = licenses.visitOwner();
        theaterScripts = new TheaterScriptStore(settings);
        reminderStore = new ReminderStore(settings);
        reminderStore.normalizePast(System.currentTimeMillis());
        lastQuiet = settings.isQuiet();
        windowManager = (WindowManager) getSystemService(WINDOW_SERVICE);
        touchSlop = ViewConfiguration.get(this).getScaledTouchSlop();
        createNotificationChannels();
        startAsForeground();
        if (settings.guide().hasBorrowedVisibility()) {
            settings.guide().dismiss();
            restoreGuideVisibility();
        }
    }

    @Override public int onStartCommand(Intent intent, int flags, int startId) {
        refreshVisitOwner();
        String action = intent == null || intent.getAction() == null ? ACTION_START : intent.getAction();
        ResultReceiver receiver = intent == null ? null : intent.getParcelableExtra(EXTRA_RESULT);
        if (ACTION_CANCEL_REQUEST.equals(action)) {
            String canceledId = intent.getStringExtra("request_id");
            if (canceledId != null && canceledId.equals(pendingRequestId)) {
                interactionGeneration++;
                answerInteraction("操作已取消，可以重新尝试");
                finishInteraction();
            }
            return START_STICKY;
        }
        long deadline = intent == null ? Long.MAX_VALUE : intent.getLongExtra("request_deadline", Long.MAX_VALUE);
        if (SystemClock.elapsedRealtime() >= deadline) {
            answer(receiver, "这次操作已超时，请重新尝试");
            if (!settings.running()) stopSelf();
            return START_NOT_STICKY;
        }
        if (ACTION_STOP.equals(action)) {
            pauseGuide();
            interactionGeneration++;
            answerInteraction("桌宠已完全退出");
            removeVisitor(false);
            removeMainOverlay(true);
            settings.setRunning(false);
            handler.removeCallbacksAndMessages(null);
            answer(receiver, null);
            stopSelf();
            return START_NOT_STICKY;
        }
        if (!Settings.canDrawOverlays(this)) {
            answer(receiver, "请先允许悬浮窗，返回后继续体验");
            settings.setRunning(false);
            stopSelf();
            return START_NOT_STICKY;
        }
        try {
            if (ACTION_GUIDE.equals(action)) {
                handleGuideAction(intent.getStringExtra("guide_action"));
                settings.setRunning(true);
                answer(receiver, null);
            } else {
                boolean needsVisible = ACTION_START.equals(action) || ACTION_SHOW.equals(action)
                    || ACTION_INTERACT.equals(action) || ACTION_THEATER.equals(action)
                    || ACTION_NEXT.equals(action) || ACTION_REACT.equals(action);
                if (needsVisible) {
                    settings.setPetHidden(false);
                    settings.setClickThrough(false);
                    if (overlay == null) createOverlay(ACTION_START.equals(action));
                    else applyTouchMode();
                    if (!lastPetLoadSucceeded) throw new IllegalStateException("这个形象暂时无法显示，请到桌宠页选择内置形象后重试");
                }
                if (ACTION_INTERACT.equals(action) || ACTION_THEATER.equals(action) || ACTION_NEXT.equals(action)) {
                    if (settings.guide().holdsAttention() && settings.guide().step() > 0)
                        throw new IllegalStateException("新手体验进行中，请先完成或点“稍后继续”，再使用普通玩法");
                    if (settings.guide().step() == 0) pauseGuide();
                    if (theaterActive || interactionBusy || dragging)
                        throw new IllegalStateException("请先结束互动或演出");
                }
                if ((ACTION_INTERACT.equals(action) || ACTION_THEATER.equals(action)) && !licenses.hasPremiumAccess())
                    throw new IllegalStateException("体验或正式激活后可使用这个玩法；首页新手演示可以离线观看");
                if (ACTION_INTERACT.equals(action)) {
                    pendingInteractionResult = receiver;
                    pendingRequestId = intent.getStringExtra("request_id");
                    pendingRequestDeadline = deadline;
                    ResultReceiver waiting = receiver;
                    receiver = null;
                    startRandomInteraction(true);
                    handler.postDelayed(() -> {
                        if (waiting == null || pendingInteractionResult != waiting) return;
                        interactionGeneration++;
                        answerInteraction("互动内容还没准备好，请稍后重试");
                        finishInteraction();
                    }, Math.max(0L, deadline - SystemClock.elapsedRealtime()));
                } else if (ACTION_THEATER.equals(action)) {
                    startTheater(true);
                    if (!theaterActive) throw new IllegalStateException("请先结束互动，并准备两个可用形象");
                } else if (ACTION_NEXT.equals(action)) {
                    nextPet();
                    if (!lastPetLoadSucceeded) throw new IllegalStateException("这个形象暂时打不开，请换一个重试");
                } else if (ACTION_END_SCENE.equals(action)) {
                    if (!guideDemo.isEmpty()) stopGuideDemo(false);
                    else {
                        interactionGeneration++;
                        answerInteraction("互动已由你结束");
                        if (theaterActive) finishTheater(false);
                        if (interactionBusy) finishInteraction();
                    }
                } else if (ACTION_REACT.equals(action)) react(intent.getStringExtra(EXTRA_REACTION));
                else if (ACTION_HIDE.equals(action)) hidePet();
                else if (ACTION_CLICK_THROUGH.equals(action)) enableClickThrough();
                else if (ACTION_SEND_COMPANION.equals(action)) sendToCompanion();
                else if (ACTION_TRIAL_VISIT.equals(action)) playTrialVisit(intent.getStringExtra(EXTRA_VISIT_CATEGORY));
                else if (ACTION_REFRESH.equals(action)) refreshOverlay();
                else if (!needsVisible && overlay == null && !settings.petHidden()) createOverlay(false);
                settings.setRunning(true);
                answer(receiver, null);
            }
        } catch (Exception error) {
            answer(receiver, safeMessage(error));
            if (ACTION_INTERACT.equals(action) && receiver == null) answerInteraction(safeMessage(error));
        }
        updateNotification();
        scheduleCompanionPoll();
        scheduleTrialCheck();
        warmInteractionContent();
        scheduleQuietResume();
        return START_STICKY;
    }

    @Override public IBinder onBind(Intent intent) { return null; }

    @Override public void onDestroy() {
        destroyed = true;
        if (liveService == this) liveService = null;
        stopGuideDemo(false);
        restoreGuideVisibility();
        interactionGeneration++;
        answerInteraction("桌宠已退出，可以重新启动后继续");
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
        lastQuiet = settings.isQuiet();
        pets.refreshLibraryCache();
        Point bounds = screenBounds();
        int petSize = fittedPetSize(bounds, settings.sizeDp());
        int width = fitWindowWidth(bounds, Math.max(petSize + dp(12), dp(128)));
        int height = fitWindowHeight(bounds, petSize + dp(64));
        baseWindowWidth = width;
        baseWindowHeight = height;
        interactionExpanded = false;
        menuExpanded = false;
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
        windowParams.x = SettingsStore.clamp(settings.positionX(bounds.x - width), 0, Math.max(0, bounds.x - width));
        windowParams.y = SettingsStore.clamp(settings.positionY(bounds.y / 2), 0, maxWindowY(bounds, height));
        overlay.setOnTouchListener((view, event) -> handleTouch(event));
        overlay.setTrialVisitVisible(licenses.isTrialActive() && settings.trialVisitsEnabled());
        overlay.setMenuListener(action -> {
            overlay.hideQuickMenu();
            collapseMenuWindow();
            handleMenuAction(action);
        });
        windowManager.addView(overlay, windowParams);
        currentPet = pets.selectedPet();
        loadCurrentPet();
        if (announce && !settings.guide().holdsAttention()) {
            say("idle", "我来啦。点一下可以互动，也可以在首页开始新手体验。", 4800);
        }
        restartSchedules();
    }

    private void refreshOverlay() {
        refreshVisitOwner();
        pets.refreshLibraryCache();
        boolean quiet = settings.isQuiet();
        boolean enteredQuiet = quiet && !lastQuiet;
        lastQuiet = quiet;
        if (enteredQuiet) {
            pauseGuide();
            if (theaterActive) finishTheater(false);
            if (interactionBusy) {
                interactionGeneration++;
                answerInteraction("已进入安静模式，可以稍后重新互动");
                finishInteraction();
            }
            removeVisitor(false);
        }
        scheduleQuietResume();
        if (overlay == null) return;
        // A metadata/settings refresh must retain the current interaction listener,
        // guide generation and theater actors. Only explicit exit/hide removes views.
        overlay.setTrialVisitVisible(licenses.isTrialActive() && settings.trialVisitsEnabled());
        overlay.setAppearance(settings.opacity() / 100f, settings.mirrored(), facing);
        applyTouchMode();
        refreshOverlayGeometry();
        pendingPetRefresh = true;
        applyPendingPetRefresh();
        restartSchedules();
        showNextVisit();
    }

    private void applyPendingPetRefresh() {
        if (!pendingPetRefresh || overlay == null || interactionBusy || theaterActive
            || !guideDemo.isEmpty() || dragging) return;
        pendingPetRefresh = false;
        currentPet = pets.selectedPet();
        loadCurrentPet();
    }

    private void updateBaseGeometry() {
        Point bounds = screenBounds();
        int petSize = fittedPetSize(bounds, settings.sizeDp());
        baseWindowWidth = fitWindowWidth(bounds, Math.max(petSize + dp(12), dp(128)));
        baseWindowHeight = fitWindowHeight(bounds, petSize + dp(64));
    }

    private void refreshOverlayGeometry() {
        if (overlay == null || windowParams == null) return;
        if (menuExpanded) { collapseMenuWindow(); updateBaseGeometry(); expandMenuWindow(); return; }
        updateBaseGeometry();
        if (theaterActive && visitorOverlay != null) { placeTheaterPair(screenBounds(), 0); return; }
        Point bounds = screenBounds();
        int petSize = baseWindowHeight - dp(64);
        overlay.setPetSize(petSize);
        int width = baseWindowWidth;
        int height = baseWindowHeight;
        if (interactionExpanded) {
            width = fitWindowWidth(bounds, Math.max(width, dp(196)));
            height = fitWindowHeight(bounds, Math.max(height, windowParams.height));
        } else if (menuExpanded) {
            width = fitWindowWidth(bounds, Math.max(width, overlay.preferredMenuWidth()));
            height = fitWindowHeight(bounds, Math.max(height, overlay.preferredMenuHeight()));
        }
        resizeWindowAnchored(width, height);
        if (interactionExpanded) overlay.post(this::fitInteractionWindowToContent);
    }

    private void removeMainOverlay(boolean savePosition) {
        stopGuideDemo(false);
        interactionGeneration++;
        answerInteraction("桌宠显示状态已改变，请重试");
        if (overlay == null) return;
        restoreTheaterPosition();
        if (savePosition && windowParams != null) settings.savePosition(windowParams.x, windowParams.y);
        if (theaterActive) {
            theaterVersion++;
            theaterActive = false;
            if (theaterVisitor) {
                removeVisitor(false);
                theaterVisitor = false;
            }
        }
        try { windowManager.removeView(overlay); } catch (Exception ignored) { }
        overlay = null;
        windowParams = null;
        handler.removeCallbacks(wanderTask);
        handler.removeCallbacks(interactionTask);
        handler.removeCallbacks(petSwitchTask);
        handler.removeCallbacks(theaterTask);
        cancelMovement();
        interactionBusy = false;
        interactionExpanded = false;
        menuExpanded = false;
    }

    private void loadCurrentPet() {
        reminderExpressionVersion++;
        lastPetLoadSucceeded = false;
        if (overlay == null || currentPet.isEmpty()) return;
        try {
            Drawable drawable = pets.load(currentPet);
            overlay.setPet(drawable, settings.opacity() / 100f, settings.mirrored(), facing);
            lastPetLoadSucceeded = true;
        } catch (Exception error) {
            sayText("这个 GIF 暂时打不开，换一个试试。", 5000);
        }
    }

    private void selectPet(String petId, boolean announce) {
        if (petId == null || petId.isEmpty()) return;
        currentPet = petId;
        settings.putString(SettingsStore.ACTIVE_PET, petId);
        loadCurrentPet();
        if (announce) say("switch", "换班了，上一位把零食吃完就跑。", 4200);
    }

    private void nextPet() { selectPet(pets.nextPet(currentPet), true); }

    private void handleMenuAction(String action) {
        if (settings.guide().holdsAttention() && (PetOverlayView.MENU_NEXT.equals(action)
            || PetOverlayView.MENU_SEND.equals(action))) {
            sayText("先在首页完成新手体验，或选择稍后继续。", 4200);
            return;
        }
        switch (action) {
            case PetOverlayView.MENU_INTERACT -> startRandomInteraction(true);
            case PetOverlayView.MENU_THEATER -> startTheater(true);
            case PetOverlayView.MENU_SEND -> sendToCompanion();
            case PetOverlayView.MENU_GIRLFRIEND_VISIT -> playTrialVisit("girlfriend");
            case PetOverlayView.MENU_FRIEND_VISIT -> playTrialVisit("friend");
            case PetOverlayView.MENU_COMPANION_VISIT -> playTrialVisit("companion");
            case PetOverlayView.MENU_NEXT -> nextPet();
            case PetOverlayView.MENU_CLICK_THROUGH -> explainRecovery(true);
            case PetOverlayView.MENU_HIDE -> explainRecovery(false);
            default -> { }
        }
    }

    private void react(String reaction) {
        String action = switch (reaction == null ? "" : reaction) {
            case "cheer", "calm", "sleepy", "surprised", "sad", "happy" -> reaction;
            default -> "happy";
        };
        String fallback = switch (action) {
            case "cheer" -> "先做完眼前这件，别的以后再编。";
            case "calm" -> "现在不用赢，先坐稳。";
            case "sleepy" -> "困意比需求准时，建议先投降。";
            case "surprised" -> "啊？这个展开我没备案。";
            case "sad" -> "先别装没事，我又不会打分。";
            default -> "碰到我啦。笑一个，别那么正经。";
        };
        sayText(words.reaction(action, fallback), 5200);
    }

    private void startRandomInteraction(boolean manual) {
        if (settings.guide().holdsAttention()) { if (manual) sayText("请在首页继续新手体验，或选择稍后继续。", 4200); return; }
        handler.removeCallbacks(interactionTask);
        if (!manual && settings.isQuiet()) { scheduleInteraction(); return; }
        if (!licenses.hasPremiumAccess()) {
            if (manual) sayText("体验或正式激活后可以使用随机趣味互动。", 5200);
            scheduleInteraction();
            return;
        }
        if (theaterActive) {
            if (manual) sayText("这一场还没演完。", 3600);
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
        int requestedGeneration = ++interactionGeneration;
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
                if (requestedGeneration != interactionGeneration) return;
                if (overlay == null) {
                    answerInteraction("桌宠暂时不可见，请显示后重试");
                    finishInteraction();
                } else if (loaded != null) {
                    showContentInteraction(loaded);
                } else if (moodDue) {
                    showMoodInteraction();
                } else {
                    answerInteraction("趣味内容暂时不可用，请重试；首页新手演示可以离线观看");
                    if (manual) sayText(finalFailure == null
                        ? "暂时没有新内容，稍后再试。"
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
        if (pendingInteractionResult != null && SystemClock.elapsedRealtime() >= pendingRequestDeadline) {
            interactionGeneration++;
            answerInteraction("这次互动已经超时，请重新尝试");
            finishInteraction();
            return;
        }
        if (overlay == null) {
            answerInteraction("桌宠暂时不可见，请重新显示后尝试");
            finishInteraction();
            return;
        }
        expandInteractionWindow();
        overlay.showInteraction(title, message, choices, callback::accept);
        answerInteraction(null);
        overlay.post(this::fitInteractionWindowToContent);
    }

    private void finishInteraction() {
        if (overlay != null) overlay.hideInteraction();
        collapseInteractionWindow();
        interactionBusy = false;
        applyPendingPetRefresh();
        scheduleInteraction();
        showNextVisit();
        warmInteractionContent();
        if (interactionContent.shouldFlush() && !interactionSyncBusy) {
            interactionSyncBusy = true;
            networkExecutor.execute(() -> {
                try { interactionContent.flushEvents(); } catch (Exception ignored) { }
                finally { interactionSyncBusy = false; }
            });
        }
    }

    private void handleGuideAction(String action) throws Exception {
        GuideStore guide = settings.guide();
        if ("dismiss".equals(action)) { pauseGuide(); return; }
        if ("endDemo".equals(action)) { stopGuideDemo("interaction".equals(guideDemo)); return; }
        if ("skip".equals(action)) {
            int step = guide.step();
            stopGuideDemo(false);
            guide.advance(step, true);
            resumeAfterGuide();
            return;
        }
        if ("confirm".equals(action)) {
            if (guide.step() != 1 && guide.step() != 4) throw new IllegalStateException("请体验当前步骤，或选择跳过");
            guide.advance(guide.step(), false);
            resumeAfterGuide();
            return;
        }
        if (theaterActive || interactionBusy || visitorOverlay != null) {
            if (guideDemo.isEmpty()) throw new IllegalStateException("请先结束当前互动或等待来访结束，再开始新手体验");
            throw new IllegalStateException("演示正在进行，可以回应、关闭或点结束演示");
        }
        guide.borrowVisibility(settings.petHidden(), settings.clickThrough());
        settings.setPetHidden(false);
        settings.setClickThrough(false);
        if (overlay == null) createOverlay(false);
        else applyTouchMode();
        if (!lastPetLoadSucceeded) {
            restoreGuideVisibility();
            throw new IllegalStateException("桌宠暂时没有显示，请先选择一个可用形象");
        }
        if ("begin".equals(action) || "replay".equals(action)) guide.begin("replay".equals(action));
        else if (!guide.holdsAttention()) throw new IllegalStateException("请先点继续体验");
        cancelMovement();
        reminderExpressionVersion++;
        loadCurrentPet();
        restartSchedules();
        overlay.hideQuickMenu();
        overlay.hideBubble();
        collapseMenuWindow();
        if ("interaction".equals(action)) {
            if (guide.step() != 2) throw new IllegalStateException("请先完成当前体验步骤");
            startGuideInteraction();
        } else if ("theater".equals(action)) {
            if (guide.step() != 3) throw new IllegalStateException("请先完成当前体验步骤");
            startGuideTheater();
        }
    }

    private void startGuideInteraction() {
        guideDemo = "interaction";
        interactionBusy = true;
        final int version = ++guideGeneration;
        showOverlayInteraction("新手演示 · 打个招呼", "今天想怎样一起玩？这是一条本地示例，不会记录心情或答题统计。", Arrays.asList(
            new PetOverlayView.InteractionChoice("陪我摸会鱼", "relax", true),
            new PetOverlayView.InteractionChoice("一起认真一点", "focus")
        ), choice -> {
            if (version != guideGeneration) return;
            stopGuideDemo(false);
            settings.guide().advance(2, false);
            if (choice != null) sayText("收到，我会一直在这里。以后从互动页还能继续玩。", 4500);
        });
    }

    private void startGuideTheater() throws Exception {
        // Dedicated bundled actors guarantee a stable preview even with a one-GIF external library.
        String actorB = "001-76dec374.gif".equals(currentPet) ? "005-5473df2b.gif" : "001-76dec374.gif";
        Drawable drawable = pets.load(actorB);
        Point bounds = screenBounds();
        int petSize = fittedPetSize(bounds, Math.max(96, Math.min(180, settings.sizeDp())));
        int width = fitWindowWidth(bounds, Math.max(petSize + dp(20), dp(168)));
        int height = fitWindowHeight(bounds, petSize + dp(92));
        visitorOverlay = new PetOverlayView(this, petSize, width, height);
        visitorOverlay.setPet(drawable, settings.opacity() / 100f, settings.mirrored(), -facing);
        visitorParams = new WindowManager.LayoutParams(width, height,
            WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE | WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE,
            PixelFormat.TRANSLUCENT);
        visitorParams.gravity = Gravity.TOP | Gravity.START;
        try {
            placeTheaterPair(bounds, width);
            windowManager.addView(visitorOverlay, visitorParams);
        } catch (Exception failure) {
            removeVisitor(false);
            restoreTheaterPosition();
            throw failure;
        }
        guideDemo = "theater";
        theaterActive = true;
        theaterVisitor = true;
        ++guideGeneration;
        int version = ++theaterVersion;
        playTheaterScene(new TheaterScriptStore.Script("guide", "初次见面", List.of(
            new TheaterScriptStore.Scene("我宣布：今天也要按时休息。", "收到！我负责盯住时钟。"),
            new TheaterScriptStore.Scene("我们需要邀请真人好友吗？", "不用，我们是本地小搭档。"),
            new TheaterScriptStore.Scene("演完啦，下次在互动页见！", "好呀，记得来找我们玩。")
        )), 0, version);
    }

    private void stopGuideDemo(boolean completed) {
        String previous = guideDemo;
        if (previous.isEmpty()) return;
        guideGeneration++;
        guideDemo = "";
        if (overlay != null) { overlay.hideInteraction(); overlay.hideBubble(); }
        collapseInteractionWindow();
        interactionBusy = false;
        if (completed && "interaction".equals(previous)) settings.guide().advance(2, false);
        if ("theater".equals(previous)) {
            theaterActive = false;
            theaterVersion++;
            removeVisitor(false);
            theaterVisitor = false;
            restoreTheaterPosition();
            if (completed) settings.guide().advance(3, false);
        }
        applyPendingPetRefresh();
        restartSchedules();
    }

    private void pauseGuide() {
        stopGuideDemo(false);
        if (settings.guide().holdsAttention()) settings.guide().dismiss();
        resumeAfterGuide();
    }

    private void restoreGuideVisibility() {
        GuideStore guide = settings.guide();
        if (!guide.hasBorrowedVisibility()) return;
        boolean hidden = guide.originalHidden();
        boolean through = guide.originalClickThrough();
        guide.finishBorrowing();
        settings.setPetHidden(hidden);
        settings.setClickThrough(through);
        if (hidden) removeMainOverlay(false);
        else applyTouchMode();
        updateNotification();
    }

    private void resumeAfterGuide() {
        if (!settings.guide().holdsAttention()) {
            restoreGuideVisibility();
            settings.putBoolean(SettingsStore.DEMO_VISIT_SEEN, true);
            restartSchedules();
            showNextVisit();
        }
    }

    private void warmInteractionContent() {
        if (settings.guide().holdsAttention() || !interactionContent.needsRefill() || interactionSyncBusy) return;
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
        overlay.hideQuickMenu();
        collapseMenuWindow();
        Point bounds = screenBounds();
        int width = fitWindowWidth(bounds, Math.max(baseWindowWidth, dp(196)));
        int height = fitWindowHeight(bounds, baseWindowHeight + dp(300));
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
        int height = SettingsStore.clamp(desiredHeight, baseWindowHeight, usableHeight(bounds));
        resizeWindowAnchored(windowParams.width, height);
    }

    private void collapseInteractionWindow() {
        if (!interactionExpanded || overlay == null || windowParams == null) return;
        interactionExpanded = false;
        if (!menuExpanded) resizeWindowAnchored(baseWindowWidth, baseWindowHeight);
    }

    private void expandMenuWindow() {
        if (menuExpanded || overlay == null || windowParams == null || theaterActive) return;
        Rect safe = safeScreenBounds();
        int petSize = Math.min(baseWindowHeight - dp(64), Math.max(dp(48), safe.height() - overlay.preferredMenuHeight() - dp(8)));
        int width = Math.min(safe.width(), Math.max(baseWindowWidth, overlay.preferredMenuWidth()));
        int height = Math.min(safe.height(), overlay.preferredMenuHeight() + dp(8) + petSize);
        int petTop = windowParams.y + windowParams.height - (baseWindowHeight - dp(64));
        int center = windowParams.x + windowParams.width / 2;
        int menuSpace = height - petSize;
        menuAbove = petTop - safe.top >= menuSpace || safe.bottom - petTop - petSize < menuSpace;
        menuExpanded = true;
        overlay.setPetSize(petSize);
        overlay.arrangeMenu(menuAbove);
        windowParams.width = width;
        windowParams.height = height;
        windowParams.x = SettingsStore.clamp(center - width / 2, safe.left, Math.max(safe.left, safe.right - width));
        int desiredTop = menuAbove ? petTop - menuSpace : petTop;
        windowParams.y = SettingsStore.clamp(desiredTop, safe.top, Math.max(safe.top, safe.bottom - height));
        windowManager.updateViewLayout(overlay, windowParams);
    }

    private void collapseMenuWindow() {
        if (!menuExpanded || overlay == null || windowParams == null) return;
        int petSize = ((android.widget.FrameLayout.LayoutParams) overlay.petImageLayout()).height;
        int petTop = windowParams.y + (menuAbove ? windowParams.height - petSize : 0);
        int center = windowParams.x + windowParams.width / 2;
        menuExpanded = false;
        updateBaseGeometry();
        overlay.restorePetLayout(baseWindowWidth);
        overlay.setPetSize(baseWindowHeight - dp(64));
        if (!interactionExpanded) {
            windowParams.width = baseWindowWidth;
            windowParams.height = baseWindowHeight;
            Point bounds = screenBounds();
            windowParams.x = SettingsStore.clamp(center - baseWindowWidth / 2, 0, Math.max(0, bounds.x - baseWindowWidth));
            windowParams.y = SettingsStore.clamp(petTop - dp(64), 0, maxWindowY(bounds, baseWindowHeight));
            windowManager.updateViewLayout(overlay, windowParams);
        }
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

    private void explainRecovery(boolean through) {
        if (settings.recoveryHintSeen()) {
            if (through) enableClickThrough(); else hidePet();
            return;
        }
        pauseGuide();
        interactionBusy = true;
        showOverlayInteraction("随时能找回来", "打开桌搭子首页，点“恢复桌宠”就能找回。也可在“我的 → 系统权限”允许通知，把恢复入口放在通知里。", Arrays.asList(
            new PetOverlayView.InteractionChoice(through ? "知道了，开启穿透" : "知道了，隐藏", "continue", true),
            new PetOverlayView.InteractionChoice("暂不改变", "cancel")
        ), choice -> {
            finishInteraction();
            if (!"continue".equals(choice)) return;
            settings.putBoolean("recovery_hint_seen", true);
            if (through) enableClickThrough(); else hidePet();
        });
    }

    private void hidePet() {
        pauseGuide();
        settings.setPetHidden(true);
        settings.setClickThrough(false);
        removeVisitor(false);
        removeMainOverlay(true);
        updateNotification();
    }

    private void enableClickThrough() {
        pauseGuide();
        if (overlay == null || windowParams == null) return;
        settings.setClickThrough(true);
        interactionGeneration++;
        answerInteraction("已开启穿透，可以恢复桌宠后重新互动");
        if (interactionBusy) finishInteraction();
        if (theaterActive) finishTheater(false);
        overlay.hideQuickMenu();
        collapseMenuWindow();
        removeVisitor(false);
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
        if (theaterActive) return true;
        if (overlay.hitInteractive(event.getX(), event.getY())) return false;
        if (overlay.isInteractionVisible()) {
            if (event.getActionMasked() == MotionEvent.ACTION_UP) overlay.dismissInteraction();
            return true;
        }
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
                    collapseMenuWindow();
                    say("grab", "轻点，我的像素会掉渣。", 2500);
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
                    if (overlay.isQuickMenuVisible()) {
                        overlay.hideQuickMenu();
                        collapseMenuWindow();
                    } else {
                        expandMenuWindow();
                        overlay.post(overlay::showQuickMenu);
                    }
                }
                if (event.getActionMasked() == MotionEvent.ACTION_UP && settings.guide().holdsAttention()) settings.guide().advance(1, false);
                dragging = false;
                applyPendingPetRefresh();
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
        if (settings.guide().holdsAttention() || settings.isQuiet() || !settings.dailySpeechEnabled()) return;
        sayText(licenses.hasPremiumAccess() ? words.reaction(action, fallback) : fallback, duration);
    }

    private void sayText(String message, long duration) {
        if (overlay == null) return;
        long version = ++bubbleVersion;
        overlay.say(message);
        handler.postDelayed(() -> {
            if (overlay != null && version == bubbleVersion) overlay.hideBubble();
        }, duration);
    }

    private void startTheater(boolean manual) {
        if (settings.guide().holdsAttention()) { if (manual) sayText("请在首页继续新手体验，或选择稍后继续。", 4200); return; }
        handler.removeCallbacks(theaterTask);
        if (!manual && settings.isQuiet()) { scheduleTheater(); return; }
        if (!licenses.hasPremiumAccess()) {
            if (manual) sayText("体验或正式激活后可以上演小剧场。", 5200);
            scheduleTheater();
            return;
        }
        if (overlay == null || settings.petHidden() || settings.clickThrough()) {
            if (manual) sayText("先让桌宠显示出来，再上演小剧场。", 4800);
            scheduleTheater();
            return;
        }
        if (theaterActive || interactionBusy || dragging) {
            if (manual && theaterActive) sayText("这一场还没演完。", 3600);
            else if (manual && interactionBusy) sayText("先把眼前这个互动完成吧。", 3600);
            scheduleTheater();
            return;
        }
        List<TheaterScriptStore.Script> pool = theaterScripts.playbackPool();
        if (pool.isEmpty()) {
            if (manual) sayText("还没有可演的剧本。", 4200);
            scheduleTheater();
            return;
        }
        String actorB = pets.randomPet(currentPet);
        if (actorB.isEmpty() || actorB.equals(currentPet)) {
            if (manual) sayText("小剧场还缺一位演员，再准备一个 GIF 吧。", 5200);
            scheduleTheater();
            return;
        }
        TheaterScriptStore.Script script = pool.get(random.nextInt(pool.size()));
        overlay.hideQuickMenu();
        collapseMenuWindow();
        if (overlay.isInteractionVisible()) overlay.dismissInteraction();
        if (visitorOverlay != null) removeVisitor(true);
        cancelMovement();
        loadCurrentPet();
        theaterActive = true;
        theaterVisitor = true;
        int version = ++theaterVersion;
        Point bounds = screenBounds();
        int petSize = fittedPetSize(bounds, Math.max(96, Math.min(200, settings.sizeDp() - 20)));
        int width = fitWindowWidth(bounds, Math.max(petSize + dp(20), dp(168)));
        int height = fitWindowHeight(bounds, petSize + dp(92));
        try {
            Drawable drawable = pets.load(actorB);
            visitorOverlay = new PetOverlayView(this, petSize, width, height);
            visitorOverlay.setPet(drawable, settings.opacity() / 100f, settings.mirrored(), -facing);
            visitorParams = new WindowManager.LayoutParams(width, height,
                WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE
                    | WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE
                    | WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS,
                PixelFormat.TRANSLUCENT);
            visitorParams.gravity = Gravity.TOP | Gravity.START;
            placeTheaterPair(bounds, width);
            windowManager.addView(visitorOverlay, visitorParams);
        } catch (Exception error) {
            theaterActive = false;
            theaterVisitor = false;
            removeVisitor(false);
            restoreTheaterPosition();
            if (manual) sayText("搭档暂时登不了台，稍后再试。", 4800);
            scheduleTheater();
            return;
        }
        playTheaterScene(script, 0, version);
    }

    private void placeTheaterPair(Point ignoredBounds, int ignoredWidth) {
        if (windowParams == null || visitorParams == null || overlay == null || visitorOverlay == null) return;
        if (theaterRestoreWindow == null)
            theaterRestoreWindow = new Rect(windowParams.x, windowParams.y,
                windowParams.x + windowParams.width, windowParams.y + windowParams.height);
        Rect safe = safeScreenBounds();
        int margin = dp(8), gap = dp(12);
        int width = Math.max(dp(60), Math.min(dp(260), (safe.width() - 2 * margin - gap) / 2));
        int height = Math.min(safe.height() - 2 * margin, dp(300));
        int petSize = Math.max(dp(36), Math.min(Math.min(dp(Math.min(112, settings.sizeDp())), width - dp(12)), height / 3));
        overlay.configureTheater(petSize, width, height);
        visitorOverlay.configureTheater(petSize, width, height);
        windowParams.width = visitorParams.width = width;
        windowParams.height = visitorParams.height = height;
        windowParams.x = safe.left + margin;
        visitorParams.x = safe.right - margin - width;
        windowParams.y = visitorParams.y = safe.top + margin;
        windowManager.updateViewLayout(overlay, windowParams);
        if (visitorOverlay.isAttachedToWindow()) windowManager.updateViewLayout(visitorOverlay, visitorParams);
    }

    private void restoreTheaterPosition() {
        if (theaterRestoreWindow == null) return;
        currentTheaterScript = null;
        currentTheaterPartner = false;
        currentTheaterTextOffset = 0;
        Rect original = theaterRestoreWindow;
        theaterRestoreWindow = null;
        if (overlay == null || windowParams == null) return;
        updateBaseGeometry();
        overlay.hideBubble();
        overlay.restorePetLayout(baseWindowWidth);
        overlay.setPetSize(baseWindowHeight - dp(64));
        Point bounds = screenBounds();
        windowParams.width = baseWindowWidth;
        windowParams.height = baseWindowHeight;
        windowParams.x = SettingsStore.clamp(original.left, 0, Math.max(0, bounds.x - baseWindowWidth));
        windowParams.y = SettingsStore.clamp(original.top, 0, maxWindowY(bounds, baseWindowHeight));
        windowManager.updateViewLayout(overlay, windowParams);
    }

    static long theaterReadingMillis(String text) {
        int characters = text.codePointCount(0, text.length());
        return Math.max(6000L, 1200L + characters * 300L);
    }

    private void playTheaterScene(TheaterScriptStore.Script script, int index, int version) {
        if (version != theaterVersion) return;
        if (overlay == null || visitorOverlay == null) { finishTheater(false); return; }
        if (index >= script.scenes.size()) {
            if ("theater".equals(guideDemo)) stopGuideDemo(true); else finishTheater(true);
            return;
        }
        currentTheaterScript = script;
        currentTheaterScene = index;
        playTheaterTurn(script, index, version, false, 0);
    }

    private void playTheaterTurn(TheaterScriptStore.Script script, int index, int version,
                                 boolean partner, int textOffset) {
        if (!theaterActive || version != theaterVersion || overlay == null || visitorOverlay == null) return;
        TheaterScriptStore.Scene scene = script.scenes.get(index);
        String line = partner ? scene.companion : scene.main;
        int offset = Math.max(0, Math.min(textOffset, line.length()));
        PetOverlayView mainActor = overlay, partnerActor = visitorOverlay;
        PetOverlayView speaker = partner ? partnerActor : mainActor;
        PetOverlayView listener = partner ? mainActor : partnerActor;
        String page = speaker.theaterPages(line.substring(offset)).get(0);
        int nextOffset = offset + page.length();
        currentTheaterPartner = partner;
        currentTheaterTextOffset = offset;
        ++bubbleVersion;
        ++visitorBubbleVersion;
        listener.hideBubbleImmediately();
        speaker.say(page);
        handler.postDelayed(() -> {
            if (!theaterActive || version != theaterVersion || overlay != mainActor || visitorOverlay != partnerActor) return;
            if (nextOffset < line.length())
                playTheaterTurn(script, index, version, partner, nextOffset);
            else if (!partner)
                playTheaterTurn(script, index, version, true, 0);
            else
                playTheaterScene(script, index + 1, version);
        }, theaterReadingMillis(page));
    }

    private Rect safeScreenBounds() {
        Point bounds = screenBounds();
        int left = 0, top = 0, right = 0, bottom = dp(24);
        if (Build.VERSION.SDK_INT >= 30) {
            Insets insets = windowManager.getCurrentWindowMetrics().getWindowInsets()
                .getInsetsIgnoringVisibility(WindowInsets.Type.systemBars() | WindowInsets.Type.displayCutout());
            left = insets.left; top = insets.top; right = insets.right; bottom = insets.bottom;
        } else {
            int status = getResources().getIdentifier("status_bar_height", "dimen", "android");
            int navigation = getResources().getIdentifier("navigation_bar_height", "dimen", "android");
            if (status != 0) top = getResources().getDimensionPixelSize(status);
            if (navigation != 0) bottom = getResources().getDimensionPixelSize(navigation);
            WindowInsets insets = overlay == null ? null : overlay.getRootWindowInsets();
            if (insets != null) {
                left = insets.getStableInsetLeft(); right = insets.getStableInsetRight();
                top = Math.max(top, insets.getStableInsetTop()); bottom = Math.max(bottom, insets.getStableInsetBottom());
                if (insets.getDisplayCutout() != null) {
                    left = Math.max(left, insets.getDisplayCutout().getSafeInsetLeft());
                    top = Math.max(top, insets.getDisplayCutout().getSafeInsetTop());
                    right = Math.max(right, insets.getDisplayCutout().getSafeInsetRight());
                    bottom = Math.max(bottom, insets.getDisplayCutout().getSafeInsetBottom());
                }
            }
        }
        return new Rect(left, top, Math.max(left + 1, bounds.x - right), Math.max(top + 1, bounds.y - bottom));
    }

    private void finishTheater(boolean announce) {
        theaterVersion++;
        theaterActive = false;
        if (theaterVisitor) {
            removeVisitor(false);
            theaterVisitor = false;
        }
        restoreTheaterPosition();
        applyPendingPetRefresh();
        if (announce && overlay != null) say("theater_finish", "谢幕。把掌声留给下一次摸鱼。", 4200);
        scheduleTheater();
        scheduleWander();
        showNextVisit();
    }

    private void checkReminders() {
        handler.removeCallbacks(reminderTask);
        if (licenses.hasPremiumAccess()) {
            long now = System.currentTimeMillis();
            List<ReminderStore.Reminder> due = reminderStore.due(now);
            for (ReminderStore.Reminder reminder : due) reminderStore.markPending(reminder.id, reminder.at);
            if (!due.isEmpty() && settings.guide().holdsAttention()) pauseGuide();
            if (!theaterActive && !interactionBusy) {
                for (ReminderStore.Reminder reminder : due) {
                    if (showReminder(reminder)) reminderStore.markDelivered(reminder.id, reminder.at, now);
                }
            }
        }
        scheduleReminders();
    }

    private boolean showReminder(ReminderStore.Reminder reminder) {
        boolean shown = false;
        if (overlay == null && !settings.petHidden() && Settings.canDrawOverlays(this)) {
            try { createOverlay(false); } catch (RuntimeException ignored) { }
        }
        String fallback = reminder.message.isEmpty() ? "休息一下吧" : reminder.message;
        if (overlay != null && overlay.isAttachedToWindow() && !settings.petHidden()) {
            sayText(fallback, 6200);
            shown = true;
            if (!reminder.expressionPetId.isEmpty() && !reminder.expressionPetId.equals(currentPet))
                showReminderExpression(reminder.expressionPetId);
        }
        NotificationManager notifications = getSystemService(NotificationManager.class);
        NotificationChannel channel = notifications.getNotificationChannel(REMINDER_CHANNEL_ID);
        if (notifications.areNotificationsEnabled() && channel != null && channel.getImportance() != NotificationManager.IMPORTANCE_NONE) {
            try { showReminderNotification(fallback); shown = true; } catch (SecurityException ignored) { }
        }
        // Without a visible pet or an allowed notification, keep it due for a later attempt.
        return shown;
    }

    private void showReminderExpression(String petId) {
        if (overlay == null) return;
        String original = currentPet;
        PetOverlayView originalOverlay = overlay;
        try {
            overlay.setPet(pets.load(petId), settings.opacity() / 100f, settings.mirrored(), facing);
            int version = ++reminderExpressionVersion;
            handler.postDelayed(() -> {
                if (overlay != originalOverlay || version != reminderExpressionVersion || !currentPet.equals(original)) return;
                loadCurrentPet();
            }, 6000);
        } catch (Exception ignored) { }
    }

    private void playTrialVisit(String category) {
        if (settings.guide().holdsAttention()) { sayText("请先完成新手体验，或从首页点稍后继续。", 4000); return; }
        if (!settings.trialVisitsEnabled()) {
            sayText("体验来访暂时关掉了。", 4200);
            return;
        }
        if (!licenses.isTrialActive()) {
            sayText("体验结束后，点一下发给对象才需要激活。", 5200);
            return;
        }
        if (trialVisitBusy || visitorOverlay != null) {
            sayText("来访还在演，稍等一下。", 3600);
            return;
        }
        trialVisitBusy = true;
        sayText("正在叫人过来…", 3600);
        networkExecutor.execute(() -> {
            try {
                CompanionService.Visit visit = companions.playTrialVisit(category);
                handler.post(() -> receiveVisit(visit));
            } catch (Exception error) {
                handler.post(() -> sayText(safeMessage(error), 6500));
            } finally {
                trialVisitBusy = false;
            }
        });
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
        refreshVisitOwner();
        try { if (!companions.retainVisit(visit)) return; }
        catch (Exception error) { sayText("来访暂存失败：" + safeMessage(error), 5000); return; }
        if (!settings.guide().holdsAttention() && !settings.isQuiet() && (overlay == null || settings.petHidden() || settings.clickThrough()))
            showVisitorNotification(visit.senderName(), visit.message());
        showNextVisit();
    }

    private void showNextVisit() {
        refreshVisitOwner();
        if (settings.guide().holdsAttention() || settings.isQuiet() || theaterActive || interactionBusy || visitorOverlay != null
            || overlay == null || settings.petHidden() || settings.clickThrough()) return;
        try {
            List<CompanionService.Visit> pending = companions.pendingVisits();
            if (!pending.isEmpty()) showVisitor(pending.get(0));
        } catch (Exception ignored) {
            // Keep the inbox file intact; a subsequent refresh can retry.
        }
    }

    private void refreshVisitOwner() {
        String currentOwner = licenses.visitOwner();
        if (currentOwner.equals(visitOwner)) return;
        visitOwner = currentOwner;
        if (!theaterVisitor) removeVisitor(false);
        getSystemService(NotificationManager.class).cancel(VISITOR_NOTIFICATION_ID);
    }

    private final Runnable quietResumeTask = () -> {
        lastQuiet = settings.isQuiet();
        restartSchedules();
        showNextVisit();
        updateNotification();
    };

    private void scheduleQuietResume() {
        handler.removeCallbacks(quietResumeTask);
        long delay = settings.quietUntilUtc() - System.currentTimeMillis();
        if (delay > 0) handler.postDelayed(quietResumeTask, delay + 100);
    }

    @Override public void onConfigurationChanged(android.content.res.Configuration newConfig) {
        super.onConfigurationChanged(newConfig);
        if (overlay != null) handler.post(() -> {
            refreshOverlayGeometry();
            if (theaterActive && currentTheaterScript != null)
                playTheaterTurn(currentTheaterScript, currentTheaterScene, ++theaterVersion,
                    currentTheaterPartner, currentTheaterTextOffset);
        });
        if (visitorOverlay != null && visitorParams != null) {
            Point bounds = screenBounds();
            visitorParams.x = SettingsStore.clamp(visitorParams.x, 0, Math.max(0, bounds.x - visitorParams.width));
            visitorParams.y = SettingsStore.clamp(visitorParams.y, 0, maxWindowY(bounds, visitorParams.height));
            try { windowManager.updateViewLayout(visitorOverlay, visitorParams); } catch (Exception ignored) { }
        }
    }

    private void showVisitor(CompanionService.Visit visit) {
        if (!companions.isCurrentVisit(visit) || settings.isQuiet() || theaterActive || visitorOverlay != null) return;
        final long generation = ++visitorGeneration;
        try {
            Drawable drawable = ImageDecoder.decodeDrawable(ImageDecoder.createSource(visit.file()));
            if (drawable instanceof AnimatedImageDrawable animated) {
                animated.setRepeatCount(AnimatedImageDrawable.REPEAT_INFINITE);
                animated.start();
            }
            Point bounds = screenBounds();
            int petSize = fittedPetSize(bounds, Math.max(96, Math.min(200, settings.sizeDp() - 20)));
            int width = fitWindowWidth(bounds, Math.max(petSize + dp(20), dp(168)));
            int height = fitWindowHeight(bounds, petSize + dp(92));
            visitorOverlay = new PetOverlayView(this, petSize, width, height);
            visitorFile = visit.file();
            visitorOverlay.setPet(drawable, 1f, false, -facing);
            String reaction = visit.message() == null || visit.message().trim().isEmpty()
                ? visit.senderName() + " 来串门啦！"
                : visit.senderName() + "：" + visit.message().trim();
            visitorOverlay.say(reaction);
            visitorParams = new WindowManager.LayoutParams(width, height,
                WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE
                    | WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE
                    | WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS,
                PixelFormat.TRANSLUCENT);
            visitorParams.gravity = Gravity.TOP | Gravity.START;
            int mainX = windowParams == null ? bounds.x / 2 : windowParams.x;
            visitorParams.x = mainX < bounds.x / 2 ? Math.max(0, bounds.x - width - dp(8)) : dp(8);
            visitorParams.y = windowParams == null ? bounds.y / 2 : windowParams.y;
            windowManager.addView(visitorOverlay, visitorParams);
            getSystemService(NotificationManager.class).cancel(VISITOR_NOTIFICATION_ID);
            handler.postDelayed(() -> {
                if (generation != visitorGeneration) return;
                try { companions.completeVisit(visit); }
                catch (Exception error) { removeVisitor(false); return; }
                removeVisitor(false);
                showNextVisit();
            }, 10_000);
        } catch (Exception error) {
            removeVisitor(false);
            showVisitorNotification(visit.senderName(), visit.message());
        }
    }

    private void removeVisitor(boolean deleteFile) {
        visitorGeneration++;
        visitorBubbleVersion++;
        if (visitorOverlay != null) {
            try { windowManager.removeView(visitorOverlay); } catch (Exception ignored) { }
            visitorOverlay = null;
            visitorParams = null;
        }
        // Real visits remain in the durable inbox when hidden, interrupted or stopped.
        if (deleteFile && theaterVisitor && visitorFile != null) visitorFile.delete();
        visitorFile = null;
    }

    private void restartSchedules() {
        handler.removeCallbacks(wanderTask);
        handler.removeCallbacks(interactionTask);
        handler.removeCallbacks(petSwitchTask);
        handler.removeCallbacks(theaterTask);
        handler.removeCallbacks(reminderTask);
        scheduleWander();
        scheduleInteraction();
        schedulePetSwitch();
        scheduleTheater();
        scheduleReminders();
    }

    private void scheduleWander() {
        handler.removeCallbacks(wanderTask);
        if (settings.guide().holdsAttention() || !settings.movement() || overlay == null || settings.clickThrough() || theaterActive) return;
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
        if (settings.guide().holdsAttention() || settings.isQuiet()) return;
        if (!settings.interactions() || overlay == null || !licenses.hasPremiumAccess() || theaterActive) return;
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
        if (settings.guide().holdsAttention() || !settings.randomPet() || overlay == null || theaterActive) return;
        handler.postDelayed(petSwitchTask, settings.randomPetInterval() * 1000L);
    }

    private void scheduleTheater() {
        handler.removeCallbacks(theaterTask);
        if (settings.guide().holdsAttention() || settings.isQuiet()) return;
        if (!licenses.hasPremiumAccess() || !settings.theaterEnabled() || overlay == null || theaterActive) return;
        handler.postDelayed(theaterTask, settings.theaterInterval() * 1000L);
    }

    private void scheduleReminders() {
        handler.removeCallbacks(reminderTask);
        if (!licenses.hasPremiumAccess()) return;
        handler.postDelayed(reminderTask, 20_000L);
    }

    private void scheduleCompanionPoll() {
        handler.removeCallbacks(companionPollTask);
        if ((licenses.isActivated() || licenses.isTrialActive()) && settings.running()) {
            handler.postDelayed(companionPollTask, 30_000L);
        }
    }

    private void scheduleTrialCheck() {
        handler.removeCallbacks(trialCheckTask);
        if (destroyed || trialCheckInFlight || licenses.isActivated() || !settings.running()) return;
        handler.postDelayed(trialCheckTask, Math.max(0, nextTrialCheckAt - SystemClock.elapsedRealtime()));
    }

    private void refreshTrialInBackground() {
        if (destroyed || trialCheckInFlight || licenses.isActivated() || !settings.running()) return;
        trialCheckInFlight = true;
        networkExecutor.execute(() -> {
            try {
                licenses.checkTrial();
            } catch (Exception ignored) { }
            handler.post(() -> {
                trialCheckInFlight = false;
                if (destroyed || !settings.running()) return;
                long remaining = licenses.trialRemainingSeconds();
                long delayMs = remaining > 0
                    ? Math.min(remaining, 24L * 60L * 60L) * 1000L
                    : 3_600_000L;
                nextTrialCheckAt = SystemClock.elapsedRealtime() + delayMs;
                scheduleTrialCheck();
                refreshVisitOwner();
                if (overlay != null) {
                    overlay.setTrialVisitVisible(licenses.isTrialActive() && settings.trialVisitsEnabled());
                    if (menuExpanded) refreshOverlayGeometry();
                }
                restartSchedules();
            });
        });
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

    private int usableWidth(Point bounds) {
        return Math.max(dp(96), bounds.x - dp(8));
    }

    private int usableHeight(Point bounds) {
        return Math.max(dp(96), bounds.y - dp(BOTTOM_GUARD_DP + 8));
    }

    private int fitWindowWidth(Point bounds, int desired) {
        return Math.min(desired, usableWidth(bounds));
    }

    private int fitWindowHeight(Point bounds, int desired) {
        return Math.min(desired, usableHeight(bounds));
    }

    private int fittedPetSize(Point bounds, int sizeDp) {
        int requested = dp(SettingsStore.clamp(sizeDp, 96, 280));
        int maxWidth = Math.max(dp(72), usableWidth(bounds) - dp(12));
        int maxHeight = Math.max(dp(72), usableHeight(bounds) - dp(64));
        return Math.min(requested, Math.min(maxWidth, maxHeight));
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
        NotificationChannel reminder = new NotificationChannel(
            REMINDER_CHANNEL_ID, "桌搭子提醒", NotificationManager.IMPORTANCE_DEFAULT);
        reminder.setDescription("到点时由桌宠提醒你");
        NotificationManager manager = getSystemService(NotificationManager.class);
        manager.createNotificationChannel(visitor);
        manager.createNotificationChannel(reminder);
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
        Intent open = new Intent(this, FlutterMainActivity.class)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_SINGLE_TOP
                | Intent.FLAG_ACTIVITY_CLEAR_TOP);
        PendingIntent content = PendingIntent.getActivity(this, REQUEST_CONTENT,
            open, PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
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

    private void showReminderNotification(String message) {
        PendingIntent open = PendingIntent.getActivity(this, REQUEST_CONTENT,
            new Intent(this, FlutterMainActivity.class)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_SINGLE_TOP
                    | Intent.FLAG_ACTIVITY_CLEAR_TOP),
            PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        Notification notification = new Notification.Builder(this, REMINDER_CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_pet)
            .setContentTitle("桌搭子提醒")
            .setContentText(message)
            .setContentIntent(open)
            .setAutoCancel(true)
            .build();
        getSystemService(NotificationManager.class).notify(REMINDER_NOTIFICATION_ID, notification);
    }

    private void showVisitorNotification(String sender, String message) {
        PendingIntent show = servicePendingIntent(REQUEST_SHOW, ACTION_SHOW);
        String contentText = message == null || message.trim().isEmpty()
            ? "点一下显示桌宠并查看来访"
            : message.trim();
        Notification notification = new Notification.Builder(this, VISITOR_CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_pet)
            .setContentTitle(sender + " 的桌宠来串门了")
            .setContentText(contentText)
            .setStyle(new Notification.BigTextStyle().bigText(contentText))
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
