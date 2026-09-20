package com.zhuodazi.android;

import android.content.Context;

import org.json.JSONObject;

import java.net.HttpURLConnection;
import java.util.Locale;
import java.util.UUID;

final class LicenseService {
    static final Object IDENTITY_LOCK = new Object();
    private final Context context;
    private final SettingsStore settings;
    private final SecureLicenseStore secureStore;
    private int lastDeviceCount = 1;
    private SecureLicenseStore.LicenseRecord frozenRecord;
    private boolean frozenTrialActive;

    LicenseService(Context context) {
        this.context = context.getApplicationContext();
        settings = new SettingsStore(this.context);
        secureStore = new SecureLicenseStore(this.context);
    }

    private LicenseService(LicenseService source) {
        context = source.context;
        settings = source.settings;
        secureStore = source.secureStore;
        frozenRecord = source.record();
        frozenTrialActive = source.isTrialActive();
    }

    private SecureLicenseStore.LicenseRecord record() {
        return frozenRecord == null ? secureStore.record() : frozenRecord.copy();
    }

    LicenseService snapshot() {
        synchronized (IDENTITY_LOCK) { return new LicenseService(this); }
    }

    String visitOwner() {
        SecureLicenseStore.LicenseRecord value = record();
        return value.isActivated() ? "license:" + value.licenseId : "trial:" + value.installationId;
    }

    boolean isActivated() { return record().isActivated(); }
    boolean isTrialActive() { return !isActivated() && (frozenRecord == null ? settings.trialActive() : frozenTrialActive); }
    boolean hasPremiumAccess() { return isActivated() || isTrialActive(); }
    String installationId() { return record().installationId; }
    String licenseSuffix() {
        String id = record().licenseId;
        return id.length() < 8 ? "" : id.substring(id.length() - 8);
    }
    int deviceCount() { return lastDeviceCount; }
    long trialRemainingSeconds() {
        return Math.max(0L, (settings.trialExpiresAt() - System.currentTimeMillis() + 999L) / 1000L);
    }

    void activate(String activationCode) throws Exception {
        String code = activationCode == null ? "" : activationCode.replaceAll("[^A-Za-z0-9]", "")
            .toUpperCase(Locale.ROOT);
        if (code.length() != 6) throw new IllegalArgumentException("请输入有效的 6 位激活码");
        SecureLicenseStore.LicenseRecord record = secureStore.record();
        JSONObject body = new JSONObject().put("code", code)
            .put("installationId", record.installationId).put("credential", record.credential)
            .put("appVersion", NetworkClient.appVersion(context));
        JSONObject response = NetworkClient.json(context, "POST", DeskPetApi.ACTIVATE, body,
            this, NetworkClient.Auth.NONE);
        String licenseId = response.optString("licenseId");
        try { UUID.fromString(licenseId); }
        catch (Exception error) { throw new IllegalStateException("激活服务返回的授权无效"); }
        int deviceCount = response.optInt("deviceCount", 1);
        lastDeviceCount = deviceCount >= 1 && deviceCount <= 2 ? deviceCount : 1;
        synchronized (IDENTITY_LOCK) {
            if (!record().licenseId.equals(record.licenseId)) throw new IllegalStateException("账号已切换，请重新激活");
            PendingVisitQueue.migrateForActivation(context.getFilesDir(),
                record.isActivated() ? "license:" + record.licenseId : "trial:" + record.installationId,
                "license:" + licenseId);
            secureStore.activate(licenseId, response.optString("activatedAt"));
            try {
                PendingVisitQueue.finishTrialMigration(context.getFilesDir(),
                    record.isActivated() ? "license:" + record.licenseId : "trial:" + record.installationId);
            } catch (java.io.IOException ignored) {
                // The destination is already durable; an inaccessible source index must not undo activation.
            }
            settings.clearTrial();
        }
    }

    TrialStatus checkTrial() throws Exception {
        if (isActivated()) {
            settings.clearTrial();
            return new TrialStatus(false, 0);
        }
        SecureLicenseStore.LicenseRecord record = record();
        JSONObject body = new JSONObject().put("installationId", record.installationId)
            .put("credential", record.credential).put("appVersion", NetworkClient.appVersion(context));
        JSONObject response = NetworkClient.json(context, "POST", DeskPetApi.TRIAL, body,
            this, NetworkClient.Auth.NONE);
        int seconds = response.optInt("remainingSeconds", -1);
        String expiresAtText = response.optString("expiresAt", "");
        boolean hasExpiresAt = !expiresAtText.isEmpty() && !response.isNull("expiresAt");
        if (!hasExpiresAt && (seconds < 0 || seconds > 24 * 60 * 60)) {
            throw new IllegalStateException("体验服务返回的数据无效");
        }
        if (!response.optBoolean("allowed")) {
            settings.clearTrial();
            return new TrialStatus(false, 0);
        }
        long expiresAt = parseExpiresAtMillis(response);
        if (expiresAt <= System.currentTimeMillis()) {
            settings.clearTrial();
            return new TrialStatus(false, 0);
        }
        settings.setTrialExpiresAt(expiresAt);
        return new TrialStatus(true, (int) Math.min(Integer.MAX_VALUE, trialRemainingSeconds()));
    }

    private static long parseExpiresAtMillis(JSONObject response) {
        String expiresAtText = response.optString("expiresAt", "");
        if (!expiresAtText.isEmpty() && !response.isNull("expiresAt")) {
            try {
                long expiresAt = java.time.Instant.parse(expiresAtText).toEpochMilli();
                String serverTimeText = response.optString("serverTime", "");
                if (!serverTimeText.isEmpty() && !response.isNull("serverTime")) {
                    long serverTime = java.time.Instant.parse(serverTimeText).toEpochMilli();
                    long remaining = expiresAt - serverTime;
                    return remaining > 0 ? System.currentTimeMillis() + remaining : System.currentTimeMillis();
                }
                return expiresAt;
            } catch (Exception ignored) { }
        }
        int seconds = Math.max(0, response.optInt("remainingSeconds", 0));
        return seconds <= 0 ? 0L : System.currentTimeMillis() + seconds * 1000L;
    }

    void authorize(HttpURLConnection connection, boolean activatedOnly) {
        SecureLicenseStore.LicenseRecord record = record();
        if (record.isActivated()) {
            connection.setRequestProperty("Authorization", "Bearer " + record.licenseId + "." + record.credential);
        } else if (!activatedOnly && isTrialActive()) {
            connection.setRequestProperty("Authorization", "Trial " + record.installationId + "." + record.credential);
        } else {
            throw new IllegalStateException(activatedOnly ? "搭子联机需要正式激活" : "此功能需要激活或有效体验");
        }
    }

    record TrialStatus(boolean allowed, int remainingSeconds) { }
}
