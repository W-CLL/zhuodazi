package com.zhuodazi.android;

import android.content.Context;
import android.content.ContextWrapper;
import android.content.res.AssetManager;
import org.junit.Before;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.robolectric.RobolectricTestRunner;
import org.robolectric.RuntimeEnvironment;
import org.robolectric.annotation.Config;
import java.io.File;
import java.io.FileOutputStream;
import java.nio.charset.StandardCharsets;
import java.util.HashMap;
import java.util.List;
import static org.junit.Assert.*;
import static org.mockito.Mockito.*;

@RunWith(RobolectricTestRunner.class)
@Config(sdk = 28, application = android.app.Application.class)
public class PetRepositoryTest {
    private Context app;
    private SettingsStore settings;

    @Before public void setUp() {
        app = RuntimeEnvironment.getApplication();
        settings = new SettingsStore(SettingsStoreTest.preferences(new HashMap<>()));
    }

    @Test public void packagedDefaultCatalogContainsAllTwentyNewPets() throws Exception {
        PetRepository repository = new PetRepository(app, settings);
        List<String> pets = repository.bundledPets();
        assertEquals(20, pets.size());
        assertTrue(pets.contains("default-pets/Kitty1.gif"));
        assertTrue(pets.contains("default-pets/一二布布5.gif"));
        assertTrue(pets.contains("default-pets/线条小狗4.gif"));
        assertTrue(pets.contains("default-pets/维尼2.gif"));
        assertTrue(pets.contains("default-pets/罗小黑3.gif"));
        assertTrue(pets.contains("default-pets/蜜桃喵3.gif"));
        assertTrue(pets.stream().allMatch(name -> name.startsWith("default-pets/")));
        assertTrue(repository.readGif("default-pets/Kitty1.gif", (int) PetRepository.MAX_GIF_BYTES).length > 10);
    }

    @Test public void legacyBuiltInSelectionMigratesWhileRandomPreferenceIsPreserved() {
        settings.putString(SettingsStore.ACTIVE_PET, "001-76dec374.gif");
        settings.putBoolean(SettingsStore.RANDOM_PET, false);
        PetRepository repository = new PetRepository(app, settings);
        assertEquals("default-pets/Kitty1.gif", repository.selectedPet());
        assertEquals(repository.selectedPet(), settings.activePet());
        assertFalse(settings.randomPet());
    }

    @Test public void currentDefaultSelectionAndOriginalDisplayNamesArePreserved() {
        settings.putString(SettingsStore.ACTIVE_PET, "default-pets/罗小黑2.gif");
        PetRepository repository = new PetRepository(app, settings);
        assertEquals("default-pets/罗小黑2.gif", repository.selectedPet());
        assertEquals("罗小黑2", repository.displayName(repository.selectedPet()));
        assertEquals("Kitty1", repository.displayName("default-pets/Kitty1.gif"));
    }

    @Test public void importedCustomPetIsNotReplacedWhenDefaultsChange() throws Exception {
        File custom = new File(app.getFilesDir(), "pets/custom_1.gif");
        assertTrue(custom.getParentFile().isDirectory() || custom.getParentFile().mkdirs());
        try {
            try (FileOutputStream output = new FileOutputStream(custom)) {
                output.write("GIF89a123456".getBytes(StandardCharsets.US_ASCII));
            }
            settings.putString(SettingsStore.ACTIVE_PET, "@custom:1");
            PetRepository repository = new PetRepository(app, settings);
            assertEquals("@custom:1", repository.selectedPet());
            assertTrue(repository.pets().contains("@custom:1"));
            assertEquals("我的 GIF 1", repository.displayName(repository.selectedPet()));
        } finally {
            assertTrue(custom.delete());
        }
    }

    @Test public void unrelatedRootAssetsCannotEnterDefaultCatalog() throws Exception {
        AssetManager assets = mock(AssetManager.class);
        when(assets.list("")).thenReturn(new String[]{"001-76dec374.gif", "other.gif"});
        when(assets.list("default-pets")).thenReturn(new String[]{"Kitty1.gif", "manifest.json", "../other.gif"});
        Context wrapped = new ContextWrapper(app) {
            @Override public Context getApplicationContext() { return this; }
            @Override public AssetManager getAssets() { return assets; }
        };
        PetRepository repository = new PetRepository(wrapped, settings);
        assertEquals(List.of("default-pets/Kitty1.gif"), repository.pets());
        verify(assets, never()).list("");
    }
}
