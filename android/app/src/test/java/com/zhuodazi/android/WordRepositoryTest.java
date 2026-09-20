package com.zhuodazi.android;

import android.content.Context;
import android.content.res.AssetManager;

import org.junit.Before;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.robolectric.RobolectricTestRunner;
import org.robolectric.RuntimeEnvironment;
import org.robolectric.annotation.Config;

import java.io.ByteArrayInputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileNotFoundException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;
import java.util.HashMap;
import java.util.Map;

import static org.junit.Assert.*;
import static org.mockito.ArgumentMatchers.anyString;
import static org.mockito.Mockito.*;

@RunWith(RobolectricTestRunner.class)
@Config(sdk = 28, application = android.app.Application.class)
public class WordRepositoryTest {
    private static final String DEFAULT_PACK = "互联网嘴替.json";
    private static final String WORDS = "{\"reactions\":{\"idle\":[\"你好呀\"],\"happy\":[\"真开心\"]}}";
    private SettingsStore settings;
    private Context context;
    private AssetManager assets;
    private final Map<String, String> files = new HashMap<>();

    @Before public void setUp() throws Exception {
        settings = new SettingsStore(SettingsStoreTest.preferences(new HashMap<>()));
        context = mock(Context.class);
        assets = mock(AssetManager.class);
        when(context.getAssets()).thenReturn(assets);
        when(assets.open(anyString())).thenAnswer(call -> {
            String path = call.getArgument(0);
            String contents = files.get(path);
            if (contents == null) throw new FileNotFoundException(path);
            return new ByteArrayInputStream(contents.getBytes(StandardCharsets.UTF_8));
        });
    }

    private void catalog(String... names) throws Exception {
        when(assets.list("word-packs")).thenReturn(names);
    }

    @Test public void onlyDedicatedDirectoryCanSupplyPacksEvenWhenRootJsonLooksLikeDialogue() throws Exception {
        when(assets.list("")).thenReturn(new String[]{
            "freeform_resolutions.json", "res_monitor_config.json", "res_process_config.json", "extra.json"
        });
        files.put("extra.json", WORDS);
        files.put("res_monitor_config.json", WORDS);
        files.put("word-packs/" + DEFAULT_PACK, WORDS);
        catalog(DEFAULT_PACK);

        WordRepository repository = new WordRepository(context, settings);

        assertEquals(Arrays.asList(DEFAULT_PACK), repository.packs());
        assertEquals("备用台词", repository.reaction("res_monitor_config.json", "idle", "备用台词"));
        verify(assets, never()).list("");
        verify(assets, never()).open("res_monitor_config.json");
    }

    @Test public void invalidDocumentsAndUnreadableEntriesAreExcludedWithoutHidingValidPacks() throws Exception {
        catalog("config.json", "invalid.json", "empty.json", "missing-idle.json", "mixed.json",
            "blank.json", "not-an-array.json", "missing.json", "../escape.json", "nested\\escape.json",
            "notes.txt", DEFAULT_PACK);
        files.put("word-packs/config.json", "{\"resolution\":[1920,1080]}");
        files.put("word-packs/invalid.json", "invalid json");
        files.put("word-packs/empty.json", "{\"reactions\":{\"idle\":[]}}");
        files.put("word-packs/missing-idle.json", "{\"reactions\":{\"happy\":[\"好呀\"]}}");
        files.put("word-packs/mixed.json", "{\"reactions\":{\"idle\":[\"好呀\",7]}}");
        files.put("word-packs/blank.json", "{\"reactions\":{\"idle\":[\"  \"]}}");
        files.put("word-packs/not-an-array.json", "{\"reactions\":{\"idle\":\"好呀\"}}");
        files.put("word-packs/" + DEFAULT_PACK, WORDS);

        WordRepository repository = new WordRepository(context, settings);

        assertEquals(Arrays.asList(DEFAULT_PACK), repository.packs());
        assertEquals("你好呀", repository.reaction("idle", "备用台词"));
        verify(assets, never()).open("word-packs/../escape.json");
        verify(assets, never()).open("word-packs/nested\\escape.json");
    }

    @Test public void previouslySelectedVendorConfigIsRestoredToDefault() throws Exception {
        settings.putString(SettingsStore.WORD_PACK, "freeform_resolutions.json");
        files.put("word-packs/" + DEFAULT_PACK, WORDS);
        catalog(DEFAULT_PACK);

        WordRepository repository = new WordRepository(context, settings);

        assertEquals(DEFAULT_PACK, settings.wordPack());
        assertEquals("你好呀", repository.reaction("idle", "备用台词"));
    }

    @Test public void movingAssetsPreservesExistingValidSelection() throws Exception {
        String selected = "精神状态.jpg.json";
        settings.putString(SettingsStore.WORD_PACK, selected);
        files.put("word-packs/" + selected, WORDS);
        files.put("word-packs/" + DEFAULT_PACK, WORDS);
        catalog(selected, DEFAULT_PACK);

        WordRepository repository = new WordRepository(context, settings);

        assertEquals(selected, settings.wordPack());
        assertEquals("精神状态.jpg", repository.displayName(selected));
    }

    @Test public void staleInvalidSelectionAfterConstructionIsRepairedBeforeSnapshotOrSpeech() throws Exception {
        files.put("word-packs/" + DEFAULT_PACK, WORDS);
        catalog(DEFAULT_PACK);
        WordRepository repository = new WordRepository(context, settings);

        settings.putString(SettingsStore.WORD_PACK, "res_process_config.json");
        repository.packs();
        assertEquals(DEFAULT_PACK, settings.wordPack());

        settings.putString(SettingsStore.WORD_PACK, "res_monitor_config.json");
        assertEquals("真开心", repository.reaction("happy", "备用台词"));
        assertEquals(DEFAULT_PACK, settings.wordPack());
    }

    @Test public void unavailableDefaultSelectsFirstValidPackAndEmptyCatalogKeepsSafeFallback() throws Exception {
        catalog("晨间问候.json");
        files.put("word-packs/晨间问候.json", WORDS);
        new WordRepository(context, settings);
        assertEquals("晨间问候.json", settings.wordPack());

        catalog();
        WordRepository empty = new WordRepository(context, settings);
        assertTrue(empty.packs().isEmpty());
        assertEquals("", settings.wordPack());
        assertEquals("备用台词", empty.reaction("idle", "备用台词"));
    }

    @Test public void missingActionUsesIdleAndReadOnlyCatalogIsLoadedOnce() throws Exception {
        catalog(DEFAULT_PACK);
        files.put("word-packs/" + DEFAULT_PACK, WORDS);
        WordRepository repository = new WordRepository(context, settings);

        assertEquals("你好呀", repository.reaction("unknown", "备用台词"));
        repository.packs().clear();
        assertEquals(Arrays.asList(DEFAULT_PACK), repository.packs());
        verify(assets, times(1)).open("word-packs/" + DEFAULT_PACK);
    }

    @Test public void allShippedPacksAreDiscoverableAndInteractionFallbackRemainsAtRoot() throws Exception {
        Context app = RuntimeEnvironment.getApplication();
        String[] packagedNames = app.getAssets().list("word-packs");
        assertEquals(Arrays.asList("互联网嘴替.json", "抽象大师.json", "精神状态.jpg.json", "纯爱战士（已黑化）.json"),
            Arrays.asList(packagedNames));
        catalog(packagedNames);
        // Robolectric on Windows can list UTF-8 ZIP entries but cannot open
        // these Chinese filenames. Read the same generated packaging inputs.
        doAnswer(call ->
            new FileInputStream(new File("build/generated/bundledWordAssets", (String) call.getArgument(0))))
            .when(assets).open(anyString());
        WordRepository repository = new WordRepository(context, settings);

        assertEquals(Arrays.asList("互联网嘴替.json", "抽象大师.json", "精神状态.jpg.json", "纯爱战士（已黑化）.json"),
            repository.packs());
        for (String pack : repository.packs()) {
            assertNotEquals("备用台词", repository.reaction(pack, "idle", "备用台词"));
        }
        try (InputStream fallback = app.getAssets().open("interaction_fallback.json")) {
            assertTrue(fallback.read() >= 0);
        }
    }
}
