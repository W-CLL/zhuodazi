package com.zhuodazi.android;

import org.junit.Test;
import java.util.HashMap;
import java.util.Map;
import static org.junit.Assert.*;

public class ReminderDeliveryTest {
    private SettingsStore store(Map<String,Object> data, boolean repeat) {
        SettingsStore settings = new SettingsStore(SettingsStoreTest.preferences(data));
        settings.putString(SettingsStore.REMINDERS, "[{\"id\":\"r1\",\"enabled\":true,\"at\":1000,\"message\":\"喝水\",\"repeatDaily\":" + repeat + "}]");
        return settings;
    }

    @Test public void checkingDueNeverConsumesAnUnseenReminder() {
        ReminderStore reminders = new ReminderStore(store(new HashMap<>(), false));
        assertEquals(1, reminders.due(2000).size());
        assertEquals(1, reminders.due(3000).size());
        assertTrue(reminders.reminders().get(0).enabled);
        reminders.markDelivered("r1", 1000, 3000);
        assertTrue(reminders.due(3000).isEmpty());
    }

    @Test public void blockedNotificationAndInterruptedGuideKeepTheOccurrenceAcrossRestart() {
        Map<String,Object> data = new HashMap<>();
        SettingsStore settings = store(data, false);
        ReminderStore reminders = new ReminderStore(settings);
        reminders.markPending("r1", 1000);
        ReminderStore restarted = new ReminderStore(new SettingsStore(SettingsStoreTest.preferences(data)));
        restarted.normalizePast(5000);
        assertEquals(1, restarted.due(5000).size());
        restarted.markDelivered("r1", 1000, 5000);
        assertEquals("{}", settings.pendingReminderOccurrences());
        assertTrue(restarted.due(5000).isEmpty());
    }

    @Test public void repeatDateAdvancesOnlyAfterDeliveryAndStaleReceiptsCannotConsumeAnotherOccurrence() {
        ReminderStore reminders = new ReminderStore(store(new HashMap<>(), true));
        reminders.markDelivered("r1", 2000, 3000);
        assertEquals(1000, reminders.due(3000).get(0).at);
        reminders.markDelivered("r1", 1000, 3000);
        assertEquals(86_401_000L, reminders.reminders().get(0).at);
        reminders.markDelivered("r1", 1000, 86_402_000L);
        assertEquals(1, reminders.due(86_402_000L).size());
    }
}
