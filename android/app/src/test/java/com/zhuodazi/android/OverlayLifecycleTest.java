package com.zhuodazi.android;

import android.content.Context;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.Rect;
import android.graphics.drawable.ColorDrawable;
import android.os.Handler;
import android.os.Looper;
import android.view.WindowManager;
import android.view.View;
import android.view.MotionEvent;
import android.widget.TextView;
import android.widget.LinearLayout;
import android.content.res.Configuration;
import java.lang.reflect.Field;
import java.lang.reflect.Method;
import java.time.Duration;
import java.util.List;
import java.util.ArrayList;
import java.util.concurrent.AbstractExecutorService;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.ExecutorService;
import org.junit.After;
import org.junit.Before;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.robolectric.Robolectric;
import org.robolectric.RobolectricTestRunner;
import org.robolectric.RuntimeEnvironment;
import org.robolectric.annotation.Config;
import org.robolectric.annotation.LooperMode;
import static org.junit.Assert.*;
import static org.mockito.ArgumentMatchers.*;
import static org.mockito.Mockito.*;
import static org.robolectric.Shadows.shadowOf;

/** Runs the real Android overlay service/view lifecycle with all network collaborators mocked. */
@RunWith(RobolectricTestRunner.class)
@Config(sdk = 28, application = android.app.Application.class)
@LooperMode(LooperMode.Mode.PAUSED)
public class OverlayLifecycleTest {
    private PetOverlayService service;
    private SettingsStore settings;
    private LicenseService licenses;
    private PetRepository pets;
    private QueuedExecutor network;

    @Before public void setUp() throws Exception {
        Context app = RuntimeEnvironment.getApplication();
        app.getSharedPreferences(SettingsStore.PREFS, 0).edit().clear().commit();
        settings = new SettingsStore(app);
        settings.putBoolean(SettingsStore.MOVEMENT, false);
        settings.putBoolean(SettingsStore.RANDOM_PET, false);
        settings.putBoolean(SettingsStore.INTERACTIONS, false);
        settings.setRunning(true);
        licenses = mock(LicenseService.class);
        when(licenses.visitOwner()).thenReturn("trial:test");
        when(licenses.hasPremiumAccess()).thenReturn(true);
        when(licenses.isTrialActive()).thenReturn(true);
        when(licenses.trialRemainingSeconds()).thenReturn(3600L);
        pets = mock(PetRepository.class);
        when(pets.selectedPet()).thenReturn("001-76dec374.gif");
        when(pets.randomPet(anyString())).thenReturn("005-5473df2b.gif");
        when(pets.load(anyString())).thenAnswer(call -> new ColorDrawable(Color.GREEN));
        service = Robolectric.buildService(PetOverlayService.class).get();
        ((ExecutorService) field("networkExecutor")).shutdownNow();
        network = new QueuedExecutor();
        field("networkExecutor", network);
        field("liveService", service);
        field("settings", settings);
        field("licenses", licenses);
        field("pets", pets);
        field("words", mock(WordRepository.class));
        field("interactionContent", mock(InteractionContentService.class));
        CompanionService companions = mock(CompanionService.class);
        when(companions.pendingVisits()).thenReturn(List.of());
        field("companions", companions);
        field("theaterScripts", new TheaterScriptStore(settings));
        field("reminderStore", new ReminderStore(settings));
        field("windowManager", app.getSystemService(Context.WINDOW_SERVICE));
        field("visitOwner", "trial:test");
        invoke("createOverlay", new Class<?>[]{boolean.class}, false);
    }

    @After public void tearDown() throws Exception {
        field("liveService", null);
        ((Handler) field("handler")).removeCallbacksAndMessages(null);
        invoke("removeVisitor", new Class<?>[]{boolean.class}, false);
        invoke("removeMainOverlay", new Class<?>[]{boolean.class}, false);
        ((ExecutorService) field("networkExecutor")).shutdownNow();
        ((Handler) field("handler")).removeCallbacksAndMessages(null);
    }

    @Test public void metadataRefreshKeepsTutorialInteractionUntilTheUserResponds() throws Exception {
        invoke("handleGuideAction", new Class<?>[]{String.class}, "begin");
        settings.guide().advance(1, false);
        invoke("handleGuideAction", new Class<?>[]{String.class}, "interaction");
        PetOverlayView before = (PetOverlayView) field("overlay");
        assertTrue(before.isInteractionVisible());
        invoke("refreshOverlay");
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofSeconds(2));
        assertSame("metadata refresh must not tear down the active scene", before, field("overlay"));
        assertTrue(before.isInteractionVisible());
        assertEquals("interaction", field("guideDemo"));
        assertEquals(2, settings.guide().step());
    }

    @Test public void metadataRefreshKeepsTutorialTheaterAliveForItsFullDuration() throws Exception {
        invoke("handleGuideAction", new Class<?>[]{String.class}, "begin");
        settings.guide().advance(1, true);
        settings.guide().advance(2, true);
        invoke("handleGuideAction", new Class<?>[]{String.class}, "theater");
        Object actor = field("visitorOverlay");
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofSeconds(1));
        invoke("refreshOverlay");
        assertSame(actor, field("visitorOverlay"));
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofSeconds(34));
        assertEquals(3, settings.guide().step());
        assertEquals("theater", field("guideDemo"));
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(1100));
        assertEquals("scene=" + field("currentTheaterScene") + " bubble=" + bubble((PetOverlayView) field("overlay")).getText(), 4, settings.guide().step());
    }

    @Test public void metadataRefreshAlsoKeepsOrdinaryInteractionAndTheater() throws Exception {
        settings.guide().dismiss();
        field("interactionBusy", true);
        invoke("showMoodInteraction");
        PetOverlayView before = (PetOverlayView) field("overlay");
        invoke("refreshOverlay");
        assertSame(before, field("overlay"));
        assertTrue(before.isInteractionVisible());
        invoke("finishInteraction");
        invoke("startTheater", new Class<?>[]{boolean.class}, true);
        Object actor = field("visitorOverlay");
        assertNotNull(actor);
        invoke("refreshOverlay");
        assertEquals(true, field("theaterActive"));
        assertSame(actor, field("visitorOverlay"));
    }

    @Test public void trialCallbackAndRepeatedCommandsDoNotInterruptTutorialOrRepeatChecks() throws Exception {
        invoke("handleGuideAction", new Class<?>[]{String.class}, "begin");
        settings.guide().advance(1, true);
        invoke("handleGuideAction", new Class<?>[]{String.class}, "interaction");
        PetOverlayView before = (PetOverlayView) field("overlay");
        invoke("scheduleTrialCheck");
        shadowOf(Looper.getMainLooper()).idle();
        invoke("scheduleTrialCheck");
        invoke("scheduleTrialCheck");
        shadowOf(Looper.getMainLooper()).idle();
        assertEquals("one request while in flight", 1, network.tasks.size());
        network.runNext();
        shadowOf(Looper.getMainLooper()).idle();
        assertSame(before, field("overlay"));
        assertTrue(before.isInteractionVisible());
        invoke("scheduleTrialCheck");
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofSeconds(5));
        assertEquals("fresh trial data stays cached until next due time", 0, network.tasks.size());
        verify(licenses, times(1)).checkTrial();
        before.dismissInteraction();
        assertEquals("the original response callback still works", 3, settings.guide().step());
    }

    @Test public void failedTrialCallbackDoesNotInterruptOrdinaryTheater() throws Exception {
        settings.guide().dismiss();
        invoke("startTheater", new Class<?>[]{boolean.class}, true);
        Object actor = field("visitorOverlay");
        Object main = field("overlay");
        doThrow(new IllegalStateException("offline")).when(licenses).checkTrial();
        invoke("refreshTrialInBackground");
        network.runNext();
        shadowOf(Looper.getMainLooper()).idle();
        assertSame(main, field("overlay"));
        assertSame(actor, field("visitorOverlay"));
        assertEquals(true, field("theaterActive"));
    }

    @Test public void activatedAccountKeepsOrdinaryInteractionAndTheaterAcrossRefresh() throws Exception {
        when(licenses.isActivated()).thenReturn(true);
        when(licenses.isTrialActive()).thenReturn(false);
        metadataRefreshAlsoKeepsOrdinaryInteractionAndTheater();
        invoke("scheduleTrialCheck");
        shadowOf(Looper.getMainLooper()).idle();
        verify(licenses, never()).checkTrial();
    }

    @Test public void guideBorrowedClickThroughRestoresAfterRefreshAndDismiss() throws Exception {
        settings.setClickThrough(true);
        invoke("applyTouchMode");
        invoke("handleGuideAction", new Class<?>[]{String.class}, "begin");
        settings.guide().advance(1, true);
        invoke("handleGuideAction", new Class<?>[]{String.class}, "interaction");
        invoke("refreshOverlay");
        assertFalse(settings.clickThrough());
        assertEquals("interaction", field("guideDemo"));
        invoke("handleGuideAction", new Class<?>[]{String.class}, "dismiss");
        assertTrue(settings.clickThrough());
        WindowManager.LayoutParams params = (WindowManager.LayoutParams) field("windowParams");
        assertNotEquals(0, params.flags & WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE);
        assertFalse(settings.guide().hasBorrowedVisibility());
    }

    @Test public void guideBorrowedHiddenPetRestoresAfterRefreshAndDismiss() throws Exception {
        invoke("hidePet");
        invoke("handleGuideAction", new Class<?>[]{String.class}, "begin");
        settings.guide().advance(1, true);
        invoke("handleGuideAction", new Class<?>[]{String.class}, "interaction");
        invoke("refreshOverlay");
        assertNotNull(field("overlay"));
        invoke("handleGuideAction", new Class<?>[]{String.class}, "dismiss");
        assertTrue(settings.petHidden());
        assertNull(field("overlay"));
        assertFalse(settings.guide().hasBorrowedVisibility());
    }

    @Test public void explicitClickThroughStillEndsGuideAndMakesOverlayUntouchable() throws Exception {
        invoke("handleGuideAction", new Class<?>[]{String.class}, "begin");
        settings.guide().advance(1, true);
        invoke("handleGuideAction", new Class<?>[]{String.class}, "interaction");
        invoke("enableClickThrough");
        assertEquals("", field("guideDemo"));
        assertFalse(settings.guide().holdsAttention());
        assertTrue(settings.clickThrough());
        assertFalse(((PetOverlayView) field("overlay")).isInteractionVisible());
    }

    @Test public void trialExpiryRefreshesAccessWithoutCuttingOffCurrentResponse() throws Exception {
        settings.guide().dismiss();
        field("interactionBusy", true);
        invoke("showMoodInteraction");
        PetOverlayView before = (PetOverlayView) field("overlay");
        invoke("refreshTrialInBackground");
        when(licenses.isTrialActive()).thenReturn(false);
        when(licenses.hasPremiumAccess()).thenReturn(false);
        when(licenses.trialRemainingSeconds()).thenReturn(0L);
        network.runNext();
        shadowOf(Looper.getMainLooper()).idle();
        assertSame(before, field("overlay"));
        assertTrue(before.isInteractionVisible());
        before.dismissInteraction();
        invoke("startTheater", new Class<?>[]{boolean.class}, true);
        assertEquals("future premium scenes remain blocked after expiry", false, field("theaterActive"));
        assertNull(field("visitorOverlay"));
    }

    @Test public void changedAppearanceAndRotationPreserveActiveInteractionAndApplyPetAfterward() throws Exception {
        invoke("handleGuideAction", new Class<?>[]{String.class}, "begin");
        settings.guide().advance(1, true);
        invoke("handleGuideAction", new Class<?>[]{String.class}, "interaction");
        PetOverlayView before = (PetOverlayView) field("overlay");
        settings.putInt(SettingsStore.SIZE, 180);
        settings.putInt(SettingsStore.OPACITY, 70);
        when(pets.selectedPet()).thenReturn("005-5473df2b.gif");
        invoke("refreshOverlay");
        service.onConfigurationChanged(new Configuration());
        shadowOf(Looper.getMainLooper()).idle();
        assertSame(before, field("overlay"));
        assertTrue(before.isInteractionVisible());
        assertEquals("001-76dec374.gif", field("currentPet"));
        before.dismissInteraction();
        assertEquals(3, settings.guide().step());
        assertEquals("005-5473df2b.gif", field("currentPet"));
    }

    @Test public void quietModeAlreadyEnabledAllowsManualGuideButNewQuietRequestStopsIt() throws Exception {
        RuntimeEnvironment.getApplication().getSharedPreferences(SettingsStore.PREFS, 0)
            .edit().putLong(SettingsStore.QUIET_UNTIL, System.currentTimeMillis() + 60_000).commit();
        invoke("refreshOverlay");
        invoke("handleGuideAction", new Class<?>[]{String.class}, "begin");
        settings.guide().advance(1, true);
        invoke("handleGuideAction", new Class<?>[]{String.class}, "interaction");
        invoke("refreshOverlay");
        assertEquals("interaction", field("guideDemo"));
        RuntimeEnvironment.getApplication().getSharedPreferences(SettingsStore.PREFS, 0)
            .edit().putLong(SettingsStore.QUIET_UNTIL, 0).commit();
        invoke("refreshOverlay");
        RuntimeEnvironment.getApplication().getSharedPreferences(SettingsStore.PREFS, 0)
            .edit().putLong(SettingsStore.QUIET_UNTIL, System.currentTimeMillis() + 60_000).commit();
        invoke("refreshOverlay");
        assertEquals("", field("guideDemo"));
        assertFalse(settings.guide().holdsAttention());
        assertFalse(((PetOverlayView) field("overlay")).isInteractionVisible());
    }

    @Test public void explicitHideStillEndsGuideAndDetachesBothActors() throws Exception {
        invoke("handleGuideAction", new Class<?>[]{String.class}, "begin");
        settings.guide().advance(1, true);
        settings.guide().advance(2, true);
        invoke("handleGuideAction", new Class<?>[]{String.class}, "theater");
        invoke("hidePet");
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofSeconds(15));
        assertNull(field("overlay"));
        assertNull(field("visitorOverlay"));
        assertEquals("", field("guideDemo"));
        assertEquals(3, settings.guide().step());
    }

    @Test public void oldBubbleHideCannotHideNewlyShownMessage() throws Exception {
        PetOverlayView view = (PetOverlayView) field("overlay");
        Field bubbleField = PetOverlayView.class.getDeclaredField("bubble");
        bubbleField.setAccessible(true);
        TextView bubble = (TextView) bubbleField.get(view);
        view.say("first");
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(200));
        view.hideBubble();
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(50));
        view.say("second");
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(250));
        assertEquals("second", bubble.getText().toString());
        assertEquals(View.VISIBLE, bubble.getVisibility());
        assertEquals(1f, bubble.getAlpha(), 0.01f);
    }

    @Test public void detachedOldChoiceCannotAnswerOrCloseTheNextInteraction() throws Exception {
        PetOverlayView view = (PetOverlayView) field("overlay");
        int[] responses = {0};
        List<PetOverlayView.InteractionChoice> choices = List.of(new PetOverlayView.InteractionChoice("好", "yes"));
        view.showInteraction("first", "first", choices, value -> responses[0]++);
        Field choicesField = PetOverlayView.class.getDeclaredField("interactionChoices");
        choicesField.setAccessible(true);
        LinearLayout buttons = (LinearLayout) choicesField.get(view);
        View oldButton = buttons.getChildAt(0);
        view.showInteraction("second", "second", choices, value -> responses[0] += 10);
        oldButton.performClick();
        assertEquals(0, responses[0]);
        assertTrue(view.isInteractionVisible());
        buttons.getChildAt(0).performClick();
        assertEquals(10, responses[0]);
        assertFalse(view.isInteractionVisible());
    }

    @Test public void releasingOrCancelingDragAppliesPetSelectedDuringDrag() throws Exception {
        for (int endAction : new int[]{MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL}) {
            touch(MotionEvent.ACTION_DOWN, 10, 10, 0);
            touch(MotionEvent.ACTION_MOVE, 100, 100, 500);
            assertEquals(true, field("dragging"));
            String selected = "selected-" + endAction + ".gif";
            when(pets.selectedPet()).thenReturn(selected);
            invoke("refreshOverlay");
            assertNotEquals(selected, field("currentPet"));
            touch(endAction, 100, 100, 1000);
            assertEquals(selected, field("currentPet"));
        }
    }

    @Test public void explicitClickThroughEndsOrdinaryInteractionAndTheater() throws Exception {
        settings.guide().dismiss();
        field("interactionBusy", true);
        invoke("showMoodInteraction");
        invoke("enableClickThrough");
        assertEquals(false, field("interactionBusy"));
        assertFalse(((PetOverlayView) field("overlay")).isInteractionVisible());
        settings.setClickThrough(false);
        invoke("applyTouchMode");
        invoke("startTheater", new Class<?>[]{boolean.class}, true);
        invoke("enableClickThrough");
        assertEquals(false, field("theaterActive"));
        assertNull(field("visitorOverlay"));
    }

    @Test public void secondTheaterLineSurvivesTheFirstLinesOldHideTimer() throws Exception {
        settings.guide().dismiss();
        useKnownTheaterScript();
        invoke("startTheater", new Class<?>[]{boolean.class}, true);
        PetOverlayView actor = (PetOverlayView) field("visitorOverlay");
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(6000));
        TextView bubble = bubble(actor);
        assertEquals("partner one", bubble.getText().toString());
        assertEquals(View.INVISIBLE, bubble((PetOverlayView) field("overlay")).getVisibility());
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(700));
        assertEquals(View.VISIBLE, bubble.getVisibility());
        assertEquals(1f, bubble.getAlpha(), 0.01f);
    }

    @Test public void stoppingAndImmediatelyRestartingTheaterIgnoresOldTurnCompletion() throws Exception {
        settings.guide().dismiss();
        useKnownTheaterScript();
        invoke("startTheater", new Class<?>[]{boolean.class}, true);
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(6300));
        Object oldActor = field("visitorOverlay");
        invoke("finishTheater", new Class<?>[]{boolean.class}, false);
        invoke("startTheater", new Class<?>[]{boolean.class}, true);
        Object newActor = field("visitorOverlay");
        assertNotSame(oldActor, newActor);
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(900));
        assertEquals(true, field("theaterActive"));
        assertSame(newActor, field("visitorOverlay"));
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(1900));
        assertEquals(View.INVISIBLE, bubble((PetOverlayView) newActor).getVisibility());
        assertEquals("main one", bubble((PetOverlayView) field("overlay")).getText().toString());
    }

    @Test public void theaterShowsOneSpeakerAtATimeInOrderWithSixSecondsForEachShortLine() throws Exception {
        settings.guide().dismiss();
        useKnownTheaterScript();
        invoke("startTheater", new Class<?>[]{boolean.class}, true);
        TextView main = bubble((PetOverlayView) field("overlay"));
        TextView partner = bubble((PetOverlayView) field("visitorOverlay"));
        assertEquals("main one", main.getText().toString());
        assertEquals(View.VISIBLE, main.getVisibility());
        assertEquals(View.INVISIBLE, partner.getVisibility());
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(5900));
        assertEquals("main one", main.getText().toString());
        assertEquals(View.INVISIBLE, partner.getVisibility());
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(200));
        assertEquals(View.INVISIBLE, main.getVisibility());
        assertEquals(View.VISIBLE, partner.getVisibility());
        assertEquals("partner one", partner.getText().toString());
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(5800));
        assertEquals("partner one", partner.getText().toString());
        assertEquals(View.INVISIBLE, main.getVisibility());
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(200));
        assertEquals("main two", main.getText().toString());
        assertEquals(View.VISIBLE, main.getVisibility());
        assertEquals(View.INVISIBLE, partner.getVisibility());
    }

    @Test public void allMainPagesFinishBeforePartnerPagesAndThenTheNextScene() throws Exception {
        settings.guide().dismiss();
        String mainLine = "这是主角说的一页对白。\n".repeat(24);
        String partnerLine = "这是搭档的回应。\n".repeat(18);
        useTheaterScript(mainLine, partnerLine);
        invoke("startTheater", new Class<?>[]{boolean.class}, true);
        PetOverlayView main = (PetOverlayView) field("overlay");
        PetOverlayView partner = (PetOverlayView) field("visitorOverlay");
        assertTrue(main.theaterPages(mainLine).size() > 1);
        assertTrue(partner.theaterPages(partnerLine).size() > 1);
        readAllCurrentSpeakerPages(main, partner, mainLine, false);
        assertEquals(0, field("currentTheaterScene"));
        readAllCurrentSpeakerPages(partner, main, partnerLine, true);
        assertEquals(1, field("currentTheaterScene"));
        assertEquals("main two", bubble(main).getText().toString());
        assertEquals(View.INVISIBLE, bubble(partner).getVisibility());
    }

    @Test public void rotatingDuringPartnerPageKeepsSpeakerAndOffsetAndInvalidatesOldDeadline() throws Exception {
        settings.guide().dismiss();
        String partnerLine = "搭档的这一段需要分页。\n".repeat(24);
        useTheaterScript("main one", partnerLine);
        invoke("startTheater", new Class<?>[]{boolean.class}, true);
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(6100));
        PetOverlayView main = (PetOverlayView) field("overlay");
        PetOverlayView partner = (PetOverlayView) field("visitorOverlay");
        long firstPageTime = PetOverlayService.theaterReadingMillis(bubble(partner).getText().toString());
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(firstPageTime));
        int offset = (int) field("currentTheaterTextOffset");
        assertTrue(offset > 0);
        String page = bubble(partner).getText().toString();
        long pageTime = PetOverlayService.theaterReadingMillis(page);
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofSeconds(2));
        int oldVersion = (int) field("theaterVersion");
        service.onConfigurationChanged(new Configuration());
        shadowOf(Looper.getMainLooper()).idle();
        assertTrue((int) field("theaterVersion") > oldVersion);
        assertEquals(true, field("currentTheaterPartner"));
        assertEquals(offset, field("currentTheaterTextOffset"));
        assertEquals(page, bubble(partner).getText().toString());
        assertEquals(View.INVISIBLE, bubble(main).getVisibility());
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(pageTime - 1500));
        assertEquals("old deadline cannot advance the resumed page", offset, field("currentTheaterTextOffset"));
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(1600));
        assertTrue((int) field("currentTheaterTextOffset") > offset);
    }

    @Test public void switchingSpeakersCancelsAnOldBubbleFadeWithoutLeavingBothVisible() throws Exception {
        settings.guide().dismiss();
        useKnownTheaterScript();
        invoke("startTheater", new Class<?>[]{boolean.class}, true);
        PetOverlayView main = (PetOverlayView) field("overlay");
        PetOverlayView partner = (PetOverlayView) field("visitorOverlay");
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(5950));
        main.hideBubble();
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(100));
        assertEquals(View.INVISIBLE, bubble(main).getVisibility());
        assertEquals(View.VISIBLE, bubble(partner).getVisibility());
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(200));
        assertEquals(View.INVISIBLE, bubble(main).getVisibility());
        assertEquals(View.VISIBLE, bubble(partner).getVisibility());
    }

    private void readAllCurrentSpeakerPages(PetOverlayView speaker, PetOverlayView listener,
                                            String expected, boolean partner) throws Exception {
        StringBuilder seen = new StringBuilder();
        while (seen.length() < expected.length()) {
            String page = bubble(speaker).getText().toString();
            assertFalse(page.isEmpty());
            assertEquals(partner, field("currentTheaterPartner"));
            assertEquals(seen.length(), field("currentTheaterTextOffset"));
            assertEquals(View.VISIBLE, bubble(speaker).getVisibility());
            assertEquals(View.INVISIBLE, bubble(listener).getVisibility());
            seen.append(page);
            assertTrue(expected.startsWith(seen.toString()));
            shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(PetOverlayService.theaterReadingMillis(page) + 1));
        }
        assertEquals(expected, seen.toString());
    }

    @Test public void oldTheaterSceneCallbackCannotFinishNewerPerformance() throws Exception {
        settings.guide().dismiss();
        useKnownTheaterScript();
        invoke("startTheater", new Class<?>[]{boolean.class}, true);
        int oldVersion = (int) field("theaterVersion");
        TheaterScriptStore.Script oldScript = ((TheaterScriptStore) field("theaterScripts")).playbackPool().get(0);
        invoke("finishTheater", new Class<?>[]{boolean.class}, false);
        invoke("startTheater", new Class<?>[]{boolean.class}, true);
        Object actor = field("visitorOverlay");
        invoke("playTheaterScene", new Class<?>[]{TheaterScriptStore.Script.class, int.class, int.class}, oldScript, 1, oldVersion);
        assertEquals(true, field("theaterActive"));
        assertSame(actor, field("visitorOverlay"));
    }

    @Test public void theaterActorsStayAtTopAndRestoreOriginalPositionAfterEnding() throws Exception {
        settings.guide().dismiss();
        useKnownTheaterScript();
        WindowManager.LayoutParams main = (WindowManager.LayoutParams) field("windowParams");
        int originalX = main.x, originalY = main.y;
        invoke("startTheater", new Class<?>[]{boolean.class}, true);
        WindowManager.LayoutParams partner = (WindowManager.LayoutParams) field("visitorParams");
        Rect safe = (Rect) invoke("safeScreenBounds");
        assertTrue(main.x >= safe.left && main.x + main.width < partner.x);
        assertTrue(partner.x + partner.width <= safe.right);
        assertEquals(main.y, partner.y);
        assertTrue(main.y >= safe.top && main.y < safe.top + 24);
        int left = main.x, right = partner.x, top = main.y;
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofSeconds(13));
        touch(MotionEvent.ACTION_DOWN, 10, 10, 0);
        touch(MotionEvent.ACTION_MOVE, 180, 180, 100);
        touch(MotionEvent.ACTION_UP, 180, 180, 200);
        assertEquals(left, main.x);
        assertEquals(right, partner.x);
        assertEquals(top, main.y);
        invoke("finishTheater", new Class<?>[]{boolean.class}, false);
        assertEquals(originalX, main.x);
        assertEquals(originalY, main.y);
    }

    @Test public void realInteractionCloseClearsRuntimeSnapshotAndAllowsAnotherInteraction() throws Exception {
        settings.guide().dismiss();
        field("interactionBusy", true);
        invoke("showMoodInteraction");
        assertEquals(true, PetOverlayService.runtimeSnapshot().get("interactionBusy"));
        ((PetOverlayView) field("overlay")).dismissInteraction();
        assertEquals(false, PetOverlayService.runtimeSnapshot().get("interactionBusy"));
        invoke("startTheater", new Class<?>[]{boolean.class}, true);
        assertEquals(true, PetOverlayService.runtimeSnapshot().get("theaterActive"));
        invoke("finishTheater", new Class<?>[]{boolean.class}, false);
        assertEquals(false, PetOverlayService.runtimeSnapshot().get("theaterActive"));
    }

    @Test public void compactMenuNeverCoversPetAtTopOrBottomAndEveryButtonIsTouchable() throws Exception {
        settings.guide().dismiss();
        PetOverlayView view = (PetOverlayView) field("overlay");
        WindowManager.LayoutParams params = (WindowManager.LayoutParams) field("windowParams");
        Rect safe = (Rect) invoke("safeScreenBounds");
        for (int y : new int[]{safe.top, safe.bottom - params.height}) {
            params.y = y;
            invoke("expandMenuWindow");
            view.showQuickMenu();
            view.measure(View.MeasureSpec.makeMeasureSpec(params.width, View.MeasureSpec.EXACTLY),
                View.MeasureSpec.makeMeasureSpec(params.height, View.MeasureSpec.EXACTLY));
            view.layout(0, 0, params.width, params.height);
            Field menuField = PetOverlayView.class.getDeclaredField("quickMenu");
            menuField.setAccessible(true);
            LinearLayout menu = (LinearLayout) menuField.get(view);
            Rect menuRect = new Rect(menu.getLeft(), menu.getTop(), menu.getRight(), menu.getBottom());
            assertFalse(Rect.intersects(menuRect, view.petRect()));
            assertEquals(3, menu.getChildCount());
            assertTrue(params.y >= safe.top && params.y + params.height <= safe.bottom);
            for (int row = 0; row < 3; row++) {
                LinearLayout buttons = (LinearLayout) menu.getChildAt(row);
                assertEquals(2, buttons.getChildCount());
                for (int child = 0; child < 2; child++) assertTrue(buttons.getChildAt(child).isClickable());
            }
            view.hideQuickMenu();
            invoke("collapseMenuWindow");
        }
    }

    @Test public void longTheaterTextIsPagedWithoutLosingCharactersAndGetsReadingTime() throws Exception {
        PetOverlayView view = (PetOverlayView) field("overlay");
        view.configureTheater(72, 140, 210);
        String line = "这是需要完整读完的长对白。\n".repeat(18);
        List<String> pages = view.theaterPages(line);
        assertTrue(pages.size() > 1);
        assertEquals(line, String.join("", pages));
        assertEquals(6000L, PetOverlayService.theaterReadingMillis("你好"));
        assertTrue(PetOverlayService.theaterReadingMillis(line) > 6000L);
    }

    @Test public void eachMixedLanguagePageFitsTheActualTextViewLayout() throws Exception {
        PetOverlayView view = (PetOverlayView) field("overlay");
        view.configureTheater(72, 140, 210);
        String text = "extraordinarilyLongWord 中文和 emoji 🐈🐕 with several words\n".repeat(20);
        List<String> pages = view.theaterPages(text);
        assertTrue(pages.size() > 1);
        assertEquals(text, String.join("", pages));
        TextView bubble = bubble(view);
        for (String page : pages) {
            view.say(page);
            bubble.measure(View.MeasureSpec.makeMeasureSpec(bubble.getLayoutParams().width, View.MeasureSpec.EXACTLY),
                View.MeasureSpec.makeMeasureSpec(210 - 72 - 16, View.MeasureSpec.AT_MOST));
            bubble.layout(0, 0, bubble.getMeasuredWidth(), bubble.getMeasuredHeight());
            assertTrue("page should not need hidden lines", bubble.getLayout().getLineCount() <= bubble.getMaxLines());
            assertEquals(page.length(), bubble.getLayout().getLineEnd(bubble.getLayout().getLineCount() - 1));
        }
    }

    @Test public void sizeChangedDuringMenuOrTheaterIsAppliedWhenClosed() throws Exception {
        settings.guide().dismiss();
        invoke("expandMenuWindow");
        settings.putInt(SettingsStore.SIZE, 160);
        invoke("refreshOverlay");
        invoke("collapseMenuWindow");
        assertEquals((int) field("baseWindowHeight") - (int) invoke("dp", new Class<?>[]{int.class}, 64),
            ((PetOverlayView) field("overlay")).petImageLayout().height);
        assertEquals((int) invoke("dp", new Class<?>[]{int.class}, 160),
            ((PetOverlayView) field("overlay")).petImageLayout().height);
        invoke("startTheater", new Class<?>[]{boolean.class}, true);
        settings.putInt(SettingsStore.SIZE, 120);
        invoke("refreshOverlay");
        invoke("finishTheater", new Class<?>[]{boolean.class}, false);
        assertEquals((int) invoke("dp", new Class<?>[]{int.class}, 120),
            ((PetOverlayView) field("overlay")).petImageLayout().height);
    }

    @Test public void failedPartnerAttachRestoresMainPetInGuideAndOrdinaryTheater() throws Exception {
        settings.guide().dismiss();
        WindowManager manager = spy((WindowManager) field("windowManager"));
        doThrow(new IllegalStateException("window unavailable")).when(manager).addView(any(View.class), any());
        field("windowManager", manager);
        WindowManager.LayoutParams params = (WindowManager.LayoutParams) field("windowParams");
        int x = params.x, y = params.y, width = params.width, height = params.height;
        invoke("startTheater", new Class<?>[]{boolean.class}, true);
        assertNull(field("theaterRestoreWindow"));
        assertEquals(x, params.x); assertEquals(y, params.y);
        assertEquals(width, params.width); assertEquals(height, params.height);
        invoke("handleGuideAction", new Class<?>[]{String.class}, "begin");
        settings.guide().advance(1, true);
        settings.guide().advance(2, true);
        try { invoke("handleGuideAction", new Class<?>[]{String.class}, "theater"); fail("expected failed attach"); }
        catch (IllegalStateException expected) { assertEquals("window unavailable", expected.getMessage()); }
        assertNull(field("theaterRestoreWindow"));
        assertEquals(x, params.x); assertEquals(y, params.y);
        assertEquals(width, params.width); assertEquals(height, params.height);
    }

    @Test @Config(qualifiers = "w568dp-h320dp-land")
    public void landscapeMenuAndTheaterStayInsideSafeBounds() throws Exception {
        compactMenuNeverCoversPetAtTopOrBottomAndEveryButtonIsTouchable();
        theaterActorsStayAtTopAndRestoreOriginalPositionAfterEnding();
    }

    @Test @Config(sdk = 35, qualifiers = "w320dp-h568dp-port")
    public void cutoutAndSystemBarsAreExcludedFromTheaterStage() throws Exception {
        WindowManager manager = spy((WindowManager) field("windowManager"));
        android.view.WindowInsets insets = new android.view.WindowInsets.Builder()
            .setInsetsIgnoringVisibility(android.view.WindowInsets.Type.systemBars() | android.view.WindowInsets.Type.displayCutout(),
                android.graphics.Insets.of(18, 54, 12, 28)).build();
        doReturn(new android.view.WindowMetrics(new Rect(0, 0, 320, 568), insets))
            .when(manager).getCurrentWindowMetrics();
        field("windowManager", manager);
        settings.guide().dismiss();
        invoke("startTheater", new Class<?>[]{boolean.class}, true);
        WindowManager.LayoutParams main = (WindowManager.LayoutParams) field("windowParams");
        WindowManager.LayoutParams partner = (WindowManager.LayoutParams) field("visitorParams");
        assertTrue(main.x >= 18 && main.y >= 54);
        assertTrue(partner.x + partner.width <= 308);
        assertTrue(main.y + main.height <= 540);
        assertTrue(main.x + main.width < partner.x);
    }

    @Test public void oldReminderExpressionCannotRestoreOverNewSelection() throws Exception {
        settings.guide().dismiss();
        invoke("showReminderExpression", new Class<?>[]{String.class}, "reminder-expression.gif");
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofSeconds(3));
        invoke("selectPet", new Class<?>[]{String.class, boolean.class}, "new-selection.gif", false);
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(3500));
        assertEquals("new-selection.gif", field("currentPet"));
    }

    private void useKnownTheaterScript() throws Exception {
        useTheaterScript("main one", "partner one");
    }

    private void useTheaterScript(String main, String partner) throws Exception {
        TheaterScriptStore scripts = mock(TheaterScriptStore.class);
        when(scripts.playbackPool()).thenReturn(List.of(new TheaterScriptStore.Script("test", "test", List.of(
            new TheaterScriptStore.Scene(main, partner),
            new TheaterScriptStore.Scene("main two", "partner two"),
            new TheaterScriptStore.Scene("main three", "partner three")))));
        field("theaterScripts", scripts);
    }

    private TextView bubble(PetOverlayView view) throws Exception {
        Field field = PetOverlayView.class.getDeclaredField("bubble");
        field.setAccessible(true);
        return (TextView) field.get(view);
    }

    private void touch(int action, float x, float y, long elapsed) throws Exception {
        MotionEvent event = MotionEvent.obtain(0, elapsed, action, x, y, 0);
        try { invoke("handleTouch", new Class<?>[]{MotionEvent.class}, event); }
        finally { event.recycle(); }
    }

    private static final class QueuedExecutor extends AbstractExecutorService {
        final List<Runnable> tasks = new ArrayList<>();
        boolean stopped;
        void runNext() { tasks.remove(0).run(); }
        @Override public void execute(Runnable task) { tasks.add(task); }
        @Override public void shutdown() { stopped = true; }
        @Override public List<Runnable> shutdownNow() { stopped = true; tasks.clear(); return List.of(); }
        @Override public boolean isShutdown() { return stopped; }
        @Override public boolean isTerminated() { return stopped; }
        @Override public boolean awaitTermination(long timeout, TimeUnit unit) { return stopped; }
    }

    private Object field(String name) throws Exception {
        Field field = PetOverlayService.class.getDeclaredField(name);
        field.setAccessible(true);
        return field.get(service);
    }
    private void field(String name, Object value) throws Exception {
        Field field = PetOverlayService.class.getDeclaredField(name);
        field.setAccessible(true);
        field.set(service, value);
    }
    private Object invoke(String name) throws Exception { return invoke(name, new Class<?>[]{}); }
    private Object invoke(String name, Class<?>[] types, Object... values) throws Exception {
        Method method = PetOverlayService.class.getDeclaredMethod(name, types);
        method.setAccessible(true);
        try { return method.invoke(service, values); }
        catch (java.lang.reflect.InvocationTargetException failure) {
            if (failure.getCause() instanceof Exception cause) throw cause;
            throw failure;
        }
    }
}
