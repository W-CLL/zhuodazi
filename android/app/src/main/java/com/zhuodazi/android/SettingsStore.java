package com.zhuodazi.android;

import android.content.Context;
import android.content.SharedPreferences;

final class SettingsStore {
    static final String PREFS = "zhuodazi_settings";
    static final String SIZE = "size_dp";
    static final String OPACITY = "opacity";
    static final String MIRRORED = "mirrored";
    static final String MOVEMENT = "random_movement";
    static final String INTERACTIONS = "random_interactions";
    static final String INTERACTION_MODE = "interaction_mode";
    static final String PERSONALITY = "personality";
    static final String RANDOM_PET = "random_pet";
    static final String RANDOM_PET_INTERVAL = "random_pet_interval";
    static final String ACTIVE_PET = "active_pet";
    static final String WORD_PACK = "word_pack";
    static final String START_ON_BOOT = "start_on_boot";
    static final String RUNNING = "service_running";
    static final String PET_HIDDEN = "pet_hidden";
    static final String CLICK_THROUGH = "click_through";
    static final String POSITION_X = "position_x";
    static final String POSITION_Y = "position_y";
    static final String TRIAL_EXPIRES_AT = "trial_expires_at";
    static final String SELECTED_TAB = "selected_tab";
    static final String THEATER_ENABLED = "theater_enabled";
    static final String THEATER_INTERVAL = "theater_interval";
    static final String THEATER_SCRIPTS = "theater_scripts";
    static final String REMINDERS = "reminders";
    static final String XIANYU_URL = "xianyu_url";
    static final String AUTO_CHECK_UPDATES = "auto_check_updates";
    static final String IGNORED_UPDATE_VERSION = "ignored_update_version";
    static final String PET_LIBRARIES = "pet_libraries";
    static final String ACTIVE_LIBRARY = "active_library";
    static final String DEMO_VISIT_SEEN = "demo_visit_seen";
    static final String COMPANION_HALL_DEFAULT_APPLIED = "companion_hall_default_applied";
    static final String REMOTE_CONFIG = "remote_config";
    static final String REMOTE_DEFAULTS_APPLIED = "remote_defaults_applied";
    static final String WECHAT_ID = "wechat_id";
    static final String ANNOUNCEMENT = "announcement";
    static final String FEATURE_TRIAL_VISITS = "feature_trial_visits";
    static final String FEATURE_COMPANION_HALL = "feature_companion_hall";
    static final String FEATURE_FISH_MODE = "feature_fish_mode";
    static final String FEATURE_AUTO_UPDATES = "feature_auto_updates";

    private final SharedPreferences values;

    SettingsStore(Context context) {
        values = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
    }

    int sizeDp() { return clamp(values.getInt(SIZE, 96), 96, 280); }
    int opacity() { return clamp(values.getInt(OPACITY, 100), 20, 100); }
    boolean mirrored() { return values.getBoolean(MIRRORED, false); }
    boolean movement() { return values.getBoolean(MOVEMENT, true); }
    boolean interactions() { return values.getBoolean(INTERACTIONS, true); }
    boolean randomPet() { return values.getBoolean(RANDOM_PET, true); }
    boolean startOnBoot() { return values.getBoolean(START_ON_BOOT, false); }
    boolean running() { return values.getBoolean(RUNNING, false); }
    boolean petHidden() { return values.getBoolean(PET_HIDDEN, false); }
    boolean clickThrough() { return values.getBoolean(CLICK_THROUGH, false); }
    boolean trialActive() { return trialExpiresAt() > System.currentTimeMillis(); }
    String personality() { return values.getString(PERSONALITY, "lively"); }
    String interactionMode() { return values.getString(INTERACTION_MODE, "standard"); }
    String activePet() { return values.getString(ACTIVE_PET, ""); }
    String wordPack() { return values.getString(WORD_PACK, "元气夸夸.json"); }
    int randomPetInterval() { return clamp(values.getInt(RANDOM_PET_INTERVAL, 300), 30, 3600); }
    int positionX(int fallback) { return values.getInt(POSITION_X, fallback); }
    int positionY(int fallback) { return values.getInt(POSITION_Y, fallback); }
    int selectedTab() { return clamp(values.getInt(SELECTED_TAB, 0), 0, 4); }
    long trialExpiresAt() { return values.getLong(TRIAL_EXPIRES_AT, 0L); }
    boolean theaterEnabled() { return values.getBoolean(THEATER_ENABLED, false); }
    int theaterInterval() {
        int value = values.getInt(THEATER_INTERVAL, 300);
        return value == 60 || value == 180 || value == 300 || value == 600 || value == 1800 ? value : 300;
    }
    String theaterScriptsJson() { return values.getString(THEATER_SCRIPTS, "[]"); }
    String remindersJson() { return values.getString(REMINDERS, "[]"); }
    String xianyuUrl() { return values.getString(XIANYU_URL, ""); }
    boolean autoCheckUpdates() { return values.getBoolean(AUTO_CHECK_UPDATES, true); }
    String ignoredUpdateVersion() { return values.getString(IGNORED_UPDATE_VERSION, ""); }
    String petLibrariesJson() { return values.getString(PET_LIBRARIES, "[]"); }
    String activeLibrary() { return values.getString(ACTIVE_LIBRARY, ""); }
    boolean demoVisitSeen() { return values.getBoolean(DEMO_VISIT_SEEN, false); }
    boolean companionHallDefaultApplied() { return values.getBoolean(COMPANION_HALL_DEFAULT_APPLIED, false); }
    boolean remoteDefaultsApplied() { return values.getBoolean(REMOTE_DEFAULTS_APPLIED, false); }
    String wechatId() { return values.getString(WECHAT_ID, "wcl_lcw627"); }
    String announcement() { return values.getString(ANNOUNCEMENT, ""); }
    boolean trialVisitsEnabled() { return values.getBoolean(FEATURE_TRIAL_VISITS, true); }
    boolean companionHallEnabled() { return values.getBoolean(FEATURE_COMPANION_HALL, true); }
    boolean fishModeEnabled() { return values.getBoolean(FEATURE_FISH_MODE, true); }
    boolean autoUpdatesEnabled() { return values.getBoolean(FEATURE_AUTO_UPDATES, true); }
    String remoteConfigJson() { return values.getString(REMOTE_CONFIG, ""); }

    void putInt(String key, int value) { values.edit().putInt(key, value).apply(); }
    void putLong(String key, long value) { values.edit().putLong(key, value).apply(); }
    void putBoolean(String key, boolean value) { values.edit().putBoolean(key, value).apply(); }
    void putString(String key, String value) { values.edit().putString(key, value).apply(); }
    void setRunning(boolean running) { putBoolean(RUNNING, running); }
    void setPetHidden(boolean hidden) { putBoolean(PET_HIDDEN, hidden); }
    void setClickThrough(boolean clickThrough) { putBoolean(CLICK_THROUGH, clickThrough); }
    void setTrialRemaining(int seconds) {
        long expiresAt = seconds <= 0 ? 0L : System.currentTimeMillis() + seconds * 1000L;
        putLong(TRIAL_EXPIRES_AT, expiresAt);
    }
    void setTrialExpiresAt(long expiresAtMillis) {
        putLong(TRIAL_EXPIRES_AT, Math.max(0L, expiresAtMillis));
    }
    void clearTrial() {
        putLong(TRIAL_EXPIRES_AT, 0L);
    }

    void applyRemoteConfig(org.json.JSONObject payload) {
        org.json.JSONObject features = payload.optJSONObject("features");
        org.json.JSONObject defaults = payload.optJSONObject("defaults");
        String wechat = payload.optString("wechatId", "wcl_lcw627").trim();
        if (wechat.isEmpty()) wechat = "wcl_lcw627";
        String announcement = payload.optString("announcement", "").replace('\r', ' ').replace('\n', ' ').trim();
        String url = payload.optString("xianyuUrl", "").trim();
        android.content.SharedPreferences.Editor editor = values.edit();
        editor.putString(REMOTE_CONFIG, payload.toString());
        editor.putString(WECHAT_ID, wechat);
        editor.putString(ANNOUNCEMENT, announcement);
        if (url.startsWith("https://")) editor.putString(XIANYU_URL, url);
        editor.putBoolean(FEATURE_TRIAL_VISITS, features == null || features.optBoolean("trialVisits", true));
        editor.putBoolean(FEATURE_COMPANION_HALL, features == null || features.optBoolean("companionHall", true));
        editor.putBoolean(FEATURE_FISH_MODE, features == null || features.optBoolean("fishMode", true));
        editor.putBoolean(FEATURE_AUTO_UPDATES, features == null || features.optBoolean("autoUpdates", true));
        boolean existingUser = false;
        for (String key : values.getAll().keySet()) {
            if (REMOTE_CONFIG.equals(key) || WECHAT_ID.equals(key) || ANNOUNCEMENT.equals(key)
                || XIANYU_URL.equals(key) || FEATURE_TRIAL_VISITS.equals(key)
                || FEATURE_COMPANION_HALL.equals(key) || FEATURE_FISH_MODE.equals(key)
                || FEATURE_AUTO_UPDATES.equals(key) || REMOTE_DEFAULTS_APPLIED.equals(key)) {
                continue;
            }
            existingUser = true;
            break;
        }
        if (existingUser || remoteDefaultsApplied()) {
            editor.putBoolean(REMOTE_DEFAULTS_APPLIED, true);
        } else if (defaults != null) {
            String personality = defaults.optString("personality", "lively");
            String mode = defaults.optString("interactionMode", "standard");
            int interval = defaults.optInt("theaterIntervalSeconds", 300);
            if (!personality.equals("lively") && !personality.equals("shy")
                && !personality.equals("clingy") && !personality.equals("chaotic")) {
                personality = "lively";
            }
            if (!mode.equals("quiet") && !mode.equals("standard") && !mode.equals("lively")) {
                mode = "standard";
            }
            if (interval != 60 && interval != 180 && interval != 300 && interval != 600 && interval != 1800) {
                interval = 300;
            }
            editor.putString(PERSONALITY, personality);
            editor.putString(INTERACTION_MODE, mode);
            editor.putInt(THEATER_INTERVAL, interval);
            editor.putBoolean(REMOTE_DEFAULTS_APPLIED, true);
        }
        editor.apply();
    }

    void savePosition(int x, int y) {
        values.edit().putInt(POSITION_X, x).putInt(POSITION_Y, y).apply();
    }

    static int clamp(int value, int minimum, int maximum) {
        return Math.max(minimum, Math.min(maximum, value));
    }
}
