package com.zhuodazi.android;

import android.content.Context;

import org.json.JSONObject;

import java.net.HttpURLConnection;
import java.util.Locale;
import java.util.UUID;

final class LicenseService {
    private final Context context;
    private final SettingsStore settings;
    private final SecureLicenseStore secureStore;

    LicenseService(Context context) {
        this.context = context.getApplicationContext();
        settings = new SettingsStore(this.context);
        secureStore = new SecureLicenseStore(this.context);
    }

    boolean isActivated() { return secureStore.record().isActivated(); }
    boolean hasPremiumAccess() { return isActivated() || settings.trialActive(); }
    String installationId() { return secureStore.record().installationId; }
    String licenseSuffix() {
        String id = secureStore.record().licenseId;
        return id.length() < 8 ? "" : id.substring(id.length() - 8);
    }
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
        secureStore.activate(licenseId, response.optString("activatedAt"));
        settings.setTrialRemaining(0);
    }

    TrialStatus checkTrial() throws Exception {
        if (isActivated()) {
            settings.setTrialRemaining(0);
            return new TrialStatus(false, 0);
        }
        SecureLicenseStore.LicenseRecord record = secureStore.record();
        JSONObject body = new JSONObject().put("installationId", record.installationId)
            .put("credential", record.credential).put("appVersion", NetworkClient.appVersion(context));
        JSONObject response = NetworkClient.json(context, "POST", DeskPetApi.TRIAL, body,
            this, NetworkClient.Auth.NONE);
        int seconds = response.optInt("remainingSeconds", -1);
        if (seconds < 0 || seconds > 300) throw new IllegalStateException("体验服务返回的数据无效");
        boolean allowed = response.optBoolean("allowed") && seconds > 0;
        settings.setTrialRemaining(allowed ? seconds : 0);
        return new TrialStatus(allowed, seconds);
    }

    void authorize(HttpURLConnection connection, boolean activatedOnly) {
        SecureLicenseStore.LicenseRecord record = secureStore.record();
        if (record.isActivated()) {
            connection.setRequestProperty("Authorization", "Bearer " + record.licenseId + "." + record.credential);
        } else if (!activatedOnly && settings.trialActive()) {
            connection.setRequestProperty("Authorization", "Trial " + record.installationId + "." + record.credential);
        } else {
            throw new IllegalStateException(activatedOnly ? "搭子联机需要正式激活" : "此功能需要激活或有效体验");
        }
    }

    record TrialStatus(boolean allowed, int remainingSeconds) { }
}
