package com.zhuodazi.android;

import org.junit.Test;
import java.util.HashMap;
import java.util.Map;
import static org.junit.Assert.*;

public class GuideStoreTest {
    @Test public void freshInstallWaitsAtWelcomeUntilExplicitBegin() {
        Map<String,Object> data = new HashMap<>();
        GuideStore guide = new GuideStore(SettingsStoreTest.preferences(data));
        assertEquals(0, guide.step());
        assertTrue(guide.holdsAttention());
        assertEquals(false, guide.snapshot().get("guideUpgradeNotice"));
        guide.begin(false);
        assertEquals(1, guide.step());
    }

    @Test public void upgradeShowsOneNoticeWithoutInterruptingOrChangingPreferences() {
        Map<String,Object> data = new HashMap<>();
        data.put(SettingsStore.RANDOM_PET, false);
        data.put(SettingsStore.THEATER_ENABLED, true);
        GuideStore guide = new GuideStore(SettingsStoreTest.preferences(data));
        assertTrue(guide.dismissed());
        assertFalse(guide.holdsAttention());
        assertEquals(true, guide.snapshot().get("guideUpgradeNotice"));
        guide.dismissNotice();
        guide = new GuideStore(SettingsStoreTest.preferences(data));
        assertEquals(false, guide.snapshot().get("guideUpgradeNotice"));
        assertEquals(false, data.get(SettingsStore.RANDOM_PET));
        assertEquals(true, data.get(SettingsStore.THEATER_ENABLED));
        assertEquals(0, guide.snapshot().get("guideCompletedSteps"));
    }

    @Test public void dismissRestartAndContinueKeepTheCurrentUnfinishedStep() {
        Map<String,Object> data = new HashMap<>();
        GuideStore guide = new GuideStore(SettingsStoreTest.preferences(data));
        guide.begin(false);
        guide.advance(1, false);
        guide.dismiss();
        guide.advance(2, false); // Stale UI callback after closing must not complete an unseen step.
        guide = new GuideStore(SettingsStoreTest.preferences(data));
        assertEquals(2, guide.step());
        assertTrue(guide.dismissed());
        guide.begin(false);
        assertEquals(2, guide.step());
        assertEquals(2, guide.snapshot().get("guideCompletedSteps"));
    }

    @Test public void skippedStepIsNotReportedAsActualExperienceAndStaleCallbacksAreIgnored() {
        GuideStore guide = new GuideStore(SettingsStoreTest.preferences(new HashMap<>()));
        guide.begin(false);
        guide.advance(1, true);
        guide.advance(1, false);
        assertEquals(2, guide.step());
        assertEquals(0, guide.snapshot().get("guideCompletedSteps"));
        assertEquals(2, guide.snapshot().get("guideSkippedSteps"));
        guide.advance(2, false);
        assertEquals(3, guide.step());
        assertEquals(4, guide.snapshot().get("guideCompletedSteps"));
    }

    @Test public void completionAndReplayDoNotRewriteCompanionshipChoices() {
        Map<String,Object> data = new HashMap<>();
        GuideStore guide = new GuideStore(SettingsStoreTest.preferences(data));
        data.put(SettingsStore.QUIET_UNTIL, 123456789L);
        data.put(SettingsStore.THEATER_ENABLED, false);
        guide.begin(false);
        for (int step = 1; step <= 4; step++) guide.advance(step, false);
        assertEquals(5, guide.step());
        assertFalse(guide.holdsAttention());
        assertEquals(30, guide.snapshot().get("guideCompletedSteps"));
        guide.begin(true);
        assertEquals(1, guide.step());
        assertEquals(0, guide.snapshot().get("guideCompletedSteps"));
        assertEquals(123456789L, data.get(SettingsStore.QUIET_UNTIL));
        assertEquals(false, data.get(SettingsStore.THEATER_ENABLED));
    }
    @Test public void borrowedVisibilitySurvivesRestartAndNeverReplacesTheOriginalChoice() {
        Map<String,Object> data = new HashMap<>();
        GuideStore guide = new GuideStore(SettingsStoreTest.preferences(data));
        guide.borrowVisibility(true, true);
        guide.borrowVisibility(false, false);
        guide = new GuideStore(SettingsStoreTest.preferences(data));
        assertTrue(guide.hasBorrowedVisibility());
        assertTrue(guide.originalHidden());
        assertTrue(guide.originalClickThrough());
        guide.finishBorrowing();
        assertFalse(guide.hasBorrowedVisibility());
        assertEquals(0, guide.snapshot().get("guideCompletedSteps"));
    }

}
