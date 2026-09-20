package com.zhuodazi.android;

import android.content.SharedPreferences;
import org.json.JSONObject;
import org.junit.Test;
import java.lang.reflect.Proxy;
import java.util.*;
import static org.junit.Assert.*;

public class SettingsStoreTest {
    static SharedPreferences preferences(Map<String, Object> values) {
        Object editor = Proxy.newProxyInstance(SharedPreferences.Editor.class.getClassLoader(), new Class<?>[]{SharedPreferences.Editor.class},
            (proxy, method, args) -> {
                if (method.getName().startsWith("put")) { values.put((String) args[0], args[1]); return proxy; }
                if (method.getName().equals("remove")) { values.remove(args[0]); return proxy; }
                return method.getReturnType() == boolean.class ? true : null;
            });
        return (SharedPreferences) Proxy.newProxyInstance(SharedPreferences.class.getClassLoader(), new Class<?>[]{SharedPreferences.class},
            (proxy, method, args) -> switch (method.getName()) {
                case "edit" -> editor;
                case "getAll" -> new HashMap<>(values);
                case "contains" -> values.containsKey(args[0]);
                default -> method.getName().startsWith("get") ? values.getOrDefault(args[0], args[1]) : null;
            });
    }

    private JSONObject remote() throws Exception {
        return new JSONObject("{\"defaults\":{\"personality\":\"chaotic\",\"interactionMode\":\"lively\",\"theaterIntervalSeconds\":600},\"xianyuUrl\":\"\"}");
    }

    @Test public void trialAndRuntimeMetadataDoNotBlockFirstRemoteDefaults() throws Exception {
        Map<String,Object> values = new HashMap<>();
        values.put(SettingsStore.TRIAL_EXPIRES_AT, 12345L);
        values.put(SettingsStore.RUNNING, true);
        values.put(SettingsStore.ACTIVE_PET, "001.gif");
        values.put(SettingsStore.REMOTE_DEFAULTS_APPLIED, true); // Legacy bug marked applied without seeding any field.
        SettingsStore settings = new SettingsStore(preferences(values));
        settings.applyRemoteConfig(remote());
        assertEquals("chaotic", settings.personality());
        assertEquals("lively", settings.interactionMode());
        assertEquals(600, settings.theaterInterval());
    }

    @Test public void remoteDefaultsPreserveExistingUserChoiceFieldByField() throws Exception {
        Map<String,Object> values = new HashMap<>();
        values.put(SettingsStore.PERSONALITY, "shy");
        SettingsStore settings = new SettingsStore(preferences(values));
        settings.markUserEdited(SettingsStore.PERSONALITY);
        settings.applyRemoteConfig(remote());
        assertEquals("shy", settings.personality());
        assertEquals("lively", settings.interactionMode());
        settings.putString(SettingsStore.INTERACTION_MODE, "quiet");
        settings.applyRemoteConfig(remote());
        assertEquals("quiet", settings.interactionMode());
    }

    @Test public void clearedPurchaseLinkRemovesPreviouslyCachedDestination() throws Exception {
        Map<String,Object> values = new HashMap<>();
        values.put(SettingsStore.XIANYU_URL, "https://old.example/item");
        SettingsStore settings = new SettingsStore(preferences(values));
        settings.applyRemoteConfig(remote());
        assertEquals("", settings.xianyuUrl());
    }
}
