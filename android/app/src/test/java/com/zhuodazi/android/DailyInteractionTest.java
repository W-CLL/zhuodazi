package com.zhuodazi.android;

import android.content.Context;
import android.content.SharedPreferences;
import org.junit.Test;
import java.util.*;
import static org.junit.Assert.*;
import static org.mockito.Mockito.*;
import static org.mockito.ArgumentMatchers.*;

public class DailyInteractionTest {
    @Test public void automaticMoodPromptsAreCappedAtTwoAndNegativeAnswersGiveThirtyMinutesOfSpace() {
        Context context = mock(Context.class); when(context.getApplicationContext()).thenReturn(context);
        Map<String, Map<String,Object>> files = new HashMap<>();
        when(context.getSharedPreferences(anyString(), anyInt())).thenAnswer(call ->
            SettingsStoreTest.preferences(files.computeIfAbsent(call.getArgument(0), key -> new HashMap<>())));
        InteractionContentService content = new InteractionContentService(context, mock(LicenseService.class));
        Map<String,Object> cache = files.get("zhuodazi_interaction_content");
        assertTrue(content.isMoodPromptDue());
        content.markMoodPrompted(true); assertFalse(content.isMoodPromptDue());
        cache.put("next_mood_at", 0L); assertTrue(content.isMoodPromptDue());
        content.markMoodPrompted(true); cache.put("next_mood_at", 0L); assertFalse(content.isMoodPromptDue());
        content.recordMood("cry");
        SettingsStore settings = new SettingsStore(context);
        assertTrue(settings.isGentleTime());
        assertTrue(settings.dailyGentleUntil() <= System.currentTimeMillis() + 30 * 60_000L);
        DailyJournalStore journal = new DailyJournalStore(context);
        assertEquals("cry", journal.entries().get(0).mood());
        assertEquals(1, journal.entries().size());
        assertFalse("unknown legacy mood values are not inferred for analytics", cache.get("events").toString().contains("low"));
    }

    @Test public void legacyOfflineItemsAreIgnoredAndOnlyExplicitAcknowledgmentsAreJournaled() throws Exception {
        Context context = mock(Context.class); when(context.getApplicationContext()).thenReturn(context);
        Map<String, Map<String,Object>> files = new HashMap<>();
        files.put("zhuodazi_interaction_content", new HashMap<>());
        files.get("zhuodazi_interaction_content").put("items", "[{\"id\":\"local:old\",\"type\":\"math\",\"revision\":1,\"prompt\":\"1+1\",\"answer\":\"2\",\"explanation\":\"\",\"choices\":[]}]");
        when(context.getSharedPreferences(anyString(), anyInt())).thenAnswer(call ->
            SettingsStoreTest.preferences(files.computeIfAbsent(call.getArgument(0), key -> new HashMap<>())));
        InteractionContentService content = new InteractionContentService(context, mock(LicenseService.class));
        assertNull(content.takeNextContent());
        InteractionContentService.Item joke = new InteractionContentService.Item("joke1", "joke", 1, "hello?", "world", "", List.of());
        DailyJournalStore journal = new DailyJournalStore(context);
        assertEquals(0, journal.entries().size());
        content.recordJoke(joke, "one"); content.recordJoke(joke, "one");
        assertEquals(1, journal.entries().size());
        InteractionContentService.Item tip = new InteractionContentService.Item("tip1", "tip", 1, "hello?", "world", "", List.of());
        content.recordAcknowledgment(tip, "two"); content.recordAcknowledgment(tip, "two");
        assertEquals(2, journal.entries().size());
    }
}
