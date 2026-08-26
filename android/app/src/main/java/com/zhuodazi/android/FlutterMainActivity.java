package com.zhuodazi.android;

import android.Manifest;
import android.app.Activity;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.ImageDecoder;
import android.graphics.drawable.AnimatedImageDrawable;
import android.graphics.drawable.Drawable;
import android.net.Uri;
import android.os.Build;
import android.provider.Settings;

import androidx.annotation.NonNull;
import androidx.core.content.FileProvider;

import org.json.JSONObject;

import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.nio.file.Files;
import java.nio.file.StandardCopyOption;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

import io.flutter.embedding.android.FlutterActivity;
import io.flutter.embedding.engine.FlutterEngine;
import io.flutter.plugin.common.MethodCall;
import io.flutter.plugin.common.MethodChannel;

public final class FlutterMainActivity extends FlutterActivity {
    private static final String CHANNEL = "com.zhuodazi.android/host";
    private static final int REQUEST_GIF = 4302;
    private static final int REQUEST_NOTIFICATIONS = 4303;
    private static final int REQUEST_THEATER_SCRIPT = 4304;
    private static final int REQUEST_LIBRARY = 4305;
    private static final String AUTHOR_WECHAT = "wcl_lcw627";
    private static final String WEBSITE_URL = "https://desktoppet.online/";

    private final ExecutorService executor = Executors.newSingleThreadExecutor();
    private SettingsStore settings;
    private PetRepository pets;
    private WordRepository words;
    private LicenseService licenses;
    private InteractionContentService interactionContent;
    private CompanionService companions;
    private TheaterScriptStore theaterScripts;
    private ReminderStore reminders;
    private UpdateService updates;
    private MethodChannel channel;
    private MethodChannel.Result pendingImport;
    private MethodChannel.Result pendingTheaterImport;
    private MethodChannel.Result pendingLibraryImport;
    private boolean startAfterOverlayGrant;

    @Override public void configureFlutterEngine(@NonNull FlutterEngine engine) {
        super.configureFlutterEngine(engine);
        settings = new SettingsStore(this);
        pets = new PetRepository(this, settings);
        words = new WordRepository(this, settings);
        licenses = new LicenseService(this);
        interactionContent = new InteractionContentService(this, licenses);
        companions = new CompanionService(this, licenses, pets);
        theaterScripts = new TheaterScriptStore(settings);
        reminders = new ReminderStore(settings);
        updates = new UpdateService(this, settings, licenses);
        channel = new MethodChannel(engine.getDartExecutor().getBinaryMessenger(), CHANNEL);
        channel.setMethodCallHandler(this::handleCall);
    }

    private void handleCall(MethodCall call, MethodChannel.Result result) {
        try {
            switch (call.method) {
                case "snapshot" -> result.success(snapshot());
                case "petGif" -> result.success(pets.readGif((String) call.argument("petId"), 8 * 1024 * 1024));
                case "setSetting" -> setSetting(call, result);
                case "serviceAction" -> serviceAction((String) call.argument("action"), result);
                case "react" -> react((String) call.argument("reaction"), result);
                case "syncInteractions" -> syncInteractions(result);
                case "requestOverlayPermission" -> requestOverlayPermission(result);
                case "requestNotificationPermission" -> requestNotificationPermission(result);
                case "openAppSettings" -> openAppSettings(result);
                case "importGif" -> importGif(result);
                case "importLibrary" -> importLibrary(result);
                case "selectLibrary" -> selectLibrary((String) call.argument("id"), result);
                case "deleteLibrary" -> deleteLibrary((String) call.argument("id"), result);
                case "deleteCustom" -> deleteCustom(call, result);
                case "activate" -> activate((String) call.argument("code"), result);
                case "checkTrial" -> checkTrial(result);
                case "siteLinks" -> siteLinks(result);
                case "openUrl" -> openUrl((String) call.argument("url"), result);
                case "copyText" -> copyText((String) call.argument("text"), result);
                case "importTheaterScript" -> importTheaterScript(result);
                case "deleteTheaterScript" -> deleteTheaterScript((String) call.argument("id"), result);
                case "saveReminder" -> saveReminder(call, result);
                case "deleteReminder" -> deleteReminder((String) call.argument("id"), result);
                case "checkUpdate" -> checkUpdate(!Boolean.FALSE.equals(call.argument("manual")), result);
                case "downloadUpdate" -> downloadUpdate(result);
                case "installUpdate" -> installUpdate(result);
                case "ignoreUpdate" -> ignoreUpdate(result);
                case "companionRefresh" -> runAsync(result, () -> profileMap(companions.refreshProfile()));
                case "companionUpdateName" -> runAsync(result,
                    () -> profileMap(companions.updateName((String) call.argument("name"))));
                case "companionPair" -> runAsync(result,
                    () -> profileMap(companions.pair((String) call.argument("code"))));
                case "companionUnpair" -> runAsync(result, () -> profileMap(companions.unpair()));
                case "companionSend" -> runAsync(result, companions::sendCurrentGif);
                case "companionHallRefresh" -> runAsync(result, () -> hallMap(companions.refreshHall()));
                case "companionHallSet" -> runAsync(result,
                    () -> profileMap(companions.setHallEnabled(Boolean.TRUE.equals(call.argument("enabled")))));
                case "companionHallSend" -> runAsync(result,
                    () -> companions.sendToHall((String) call.argument("recipientId"), (String) call.argument("message")));
                default -> result.notImplemented();
            }
        } catch (Exception error) {
            result.error("HOST_ERROR", safeMessage(error), null);
        }
    }

    private Map<String, Object> snapshot() {
        Map<String, Object> value = new LinkedHashMap<>();
        value.put("overlayAllowed", Settings.canDrawOverlays(this));
        value.put("notificationAllowed", Build.VERSION.SDK_INT < 33
            || checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED);
        value.put("running", settings.running());
        value.put("hidden", settings.petHidden());
        value.put("clickThrough", settings.clickThrough());
        value.put("sizeDp", settings.sizeDp());
        value.put("opacity", settings.opacity());
        value.put("mirrored", settings.mirrored());
        value.put("movement", settings.movement());
        value.put("interactions", settings.interactions());
        value.put("randomPet", settings.randomPet());
        value.put("startOnBoot", settings.startOnBoot());
        value.put("personality", settings.personality());
        value.put("interactionMode", settings.interactionMode());
        value.put("interactionCachedCount", interactionContent.cachedCount());
        value.put("interactionOnlineCount", interactionContent.onlineCount());
        value.put("interactionCatalogVersion", interactionContent.catalogVersion());
        value.put("interactionSyncError", interactionContent.lastSyncError());
        value.put("interactionLastSyncAt", interactionContent.lastSyncAt());
        value.put("randomPetInterval", settings.randomPetInterval());
        value.put("activePet", pets.selectedPet());
        value.put("pets", petMaps());
        value.put("libraries", pets.libraryMaps());
        value.put("activeLibrary", settings.activeLibrary());
        value.put("libraryGifCount", pets.libraryGifCount());
        value.put("wordPacks", words.packs());
        value.put("wordPack", settings.wordPack());
        value.put("activated", licenses.isActivated());
        value.put("premium", licenses.hasPremiumAccess());
        value.put("deviceCount", licenses.deviceCount());
        value.put("trialSeconds", licenses.trialRemainingSeconds());
        value.put("installationSuffix", lastEight(licenses.installationId()));
        value.put("version", NetworkClient.appVersion(this));
        value.put("theaterEnabled", settings.theaterEnabled());
        value.put("theaterInterval", settings.theaterInterval());
        value.put("theaterScripts", theaterScriptMaps());
        value.put("reminders", reminderMaps());
        value.put("xianyuUrl", settings.xianyuUrl());
        value.put("wechatId", AUTHOR_WECHAT);
        value.put("websiteUrl", WEBSITE_URL);
        value.put("autoCheckUpdates", settings.autoCheckUpdates());
        value.put("ignoredUpdateVersion", settings.ignoredUpdateVersion());
        value.put("canInstallPackages", canInstallPackages());
        value.put("update", updates.state().toMap());
        return value;
    }

    private List<Map<String, Object>> theaterScriptMaps() {
        List<Map<String, Object>> result = new ArrayList<>();
        for (TheaterScriptStore.Script script : theaterScripts.scripts()) result.add(script.toMap());
        return result;
    }

    private List<Map<String, Object>> reminderMaps() {
        List<Map<String, Object>> result = new ArrayList<>();
        for (ReminderStore.Reminder reminder : reminders.reminders()) result.add(reminder.toMap());
        return result;
    }

    private List<Map<String, String>> petMaps() {
        List<Map<String, String>> result = new ArrayList<>();
        for (String pet : pets.pets()) {
            Map<String, String> item = new LinkedHashMap<>();
            item.put("id", pet);
            item.put("name", pets.displayName(pet));
            result.add(item);
        }
        return result;
    }

    private void setSetting(MethodCall call, MethodChannel.Result result) {
        String key = (String) call.argument("key");
        Object value = call.argument("value");
        if (SettingsStore.ACTIVE_PET.equals(key)) {
            settings.putString(key, String.valueOf(value));
            settings.putBoolean(SettingsStore.RANDOM_PET, false);
        } else if (SettingsStore.SIZE.equals(key) || SettingsStore.OPACITY.equals(key)
            || SettingsStore.RANDOM_PET_INTERVAL.equals(key)
            || SettingsStore.THEATER_INTERVAL.equals(key)) {
            int number = ((Number) value).intValue();
            if (SettingsStore.THEATER_INTERVAL.equals(key)
                && number != 60 && number != 180 && number != 300 && number != 600 && number != 1800) {
                number = 300;
            }
            settings.putInt(key, number);
        } else if (SettingsStore.PERSONALITY.equals(key) || SettingsStore.INTERACTION_MODE.equals(key)
            || SettingsStore.WORD_PACK.equals(key)
            || SettingsStore.IGNORED_UPDATE_VERSION.equals(key)) {
            settings.putString(key, String.valueOf(value));
        } else {
            settings.putBoolean(key, Boolean.TRUE.equals(value));
        }
        if (!SettingsStore.START_ON_BOOT.equals(key)) sendService(PetOverlayService.ACTION_REFRESH);
        result.success(snapshot());
    }

    private void serviceAction(String action, MethodChannel.Result result) {
        String nativeAction = switch (action == null ? "" : action) {
            case "start" -> PetOverlayService.ACTION_START;
            case "stop" -> PetOverlayService.ACTION_STOP;
            case "next" -> PetOverlayService.ACTION_NEXT;
            case "interact" -> PetOverlayService.ACTION_INTERACT;
            case "send" -> PetOverlayService.ACTION_SEND_COMPANION;
            case "trialVisitGirlfriend" -> PetOverlayService.ACTION_TRIAL_VISIT;
            case "trialVisitFriend" -> PetOverlayService.ACTION_TRIAL_VISIT;
            case "trialVisitCompanion" -> PetOverlayService.ACTION_TRIAL_VISIT;
            case "theater" -> PetOverlayService.ACTION_THEATER;
            case "show" -> PetOverlayService.ACTION_SHOW;
            case "hide" -> PetOverlayService.ACTION_HIDE;
            case "clickThrough" -> PetOverlayService.ACTION_CLICK_THROUGH;
            default -> PetOverlayService.ACTION_REFRESH;
        };
        if ("start".equals(action) && !Settings.canDrawOverlays(this)) {
            result.error("OVERLAY_PERMISSION", "请先授予悬浮窗权限", null);
            return;
        }
        if (PetOverlayService.ACTION_TRIAL_VISIT.equals(nativeAction)) {
            String category = switch (action) {
                case "trialVisitFriend" -> "friend";
                case "trialVisitCompanion" -> "companion";
                default -> "girlfriend";
            };
            sendService(nativeAction, category);
        } else {
            sendService(nativeAction);
        }
        result.success(snapshot());
    }

    private void sendService(String action) {
        sendService(action, null);
    }

    private void sendService(String action, String visitCategory) {
        Intent intent = new Intent(this, PetOverlayService.class).setAction(action);
        if (visitCategory != null) intent.putExtra(PetOverlayService.EXTRA_VISIT_CATEGORY, visitCategory);
        try {
            if (PetOverlayService.ACTION_START.equals(action)) startForegroundService(intent);
            else if (PetOverlayService.ACTION_STOP.equals(action) || settings.running()) startService(intent);
        } catch (RuntimeException ignored) { }
    }

    private void react(String reaction, MethodChannel.Result result) {
        if (!settings.running()) {
            result.error("PET_NOT_RUNNING", "请先启动桌宠，再进行互动", null);
            return;
        }
        Intent intent = new Intent(this, PetOverlayService.class)
            .setAction(PetOverlayService.ACTION_REACT)
            .putExtra(PetOverlayService.EXTRA_REACTION, reaction == null ? "happy" : reaction);
        try {
            startService(intent);
            result.success(snapshot());
        } catch (RuntimeException error) {
            result.error("HOST_ERROR", safeMessage(error), null);
        }
    }

    private void syncInteractions(MethodChannel.Result result) {
        runAsync(result, () -> {
            interactionContent.refillOnline();
            sendService(PetOverlayService.ACTION_REFRESH);
            return snapshot();
        });
    }

    private void requestOverlayPermission(MethodChannel.Result result) {
        if (Settings.canDrawOverlays(this)) {
            startPetIfNeeded();
            result.success(true);
            return;
        }
        startAfterOverlayGrant = true;
        startActivity(new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION,
            Uri.parse("package:" + getPackageName())));
        result.success(false);
    }

    @Override
    protected void onResume() {
        super.onResume();
        if (startAfterOverlayGrant && Settings.canDrawOverlays(this)) {
            startAfterOverlayGrant = false;
            startPetIfNeeded();
        }
    }

    private void startPetIfNeeded() {
        if (!Settings.canDrawOverlays(this) || settings.running()) return;
        sendService(PetOverlayService.ACTION_START);
    }

    private void requestNotificationPermission(MethodChannel.Result result) {
        if (Build.VERSION.SDK_INT < 33
            || checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED) {
            result.success(true);
            return;
        }
        requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, REQUEST_NOTIFICATIONS);
        result.success(false);
    }

    private void openAppSettings(MethodChannel.Result result) {
        startActivity(new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS,
            Uri.parse("package:" + getPackageName())));
        result.success(true);
    }

    private void importGif(MethodChannel.Result result) {
        if (!licenses.isActivated()) {
            result.error("ACTIVATION_REQUIRED", "正式激活后才能导入自己的桌宠", null);
            return;
        }
        if (pets.nextImportFile() == null) {
            result.error("LIMIT", "最多可以导入 3 个自定义 GIF", null);
            return;
        }
        pendingImport = result;
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT)
            .addCategory(Intent.CATEGORY_OPENABLE).setType("image/gif");
        startActivityForResult(intent, REQUEST_GIF);
    }

    private void importLibrary(MethodChannel.Result result) {
        if (!licenses.hasPremiumAccess()) {
            result.error("PREMIUM_REQUIRED", "体验或正式激活后才能绑定图鉴目录", null);
            return;
        }
        if (pets.libraries().libraries().size() >= PetLibraryStore.MAX_LIBRARIES) {
            result.error("LIMIT", "最多只能绑定 3 个图鉴目录", null);
            return;
        }
        pendingLibraryImport = result;
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT_TREE)
            .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION
                | Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION);
        startActivityForResult(intent, REQUEST_LIBRARY);
    }

    private void selectLibrary(String id, MethodChannel.Result result) {
        if (id != null && !id.isEmpty() && !PetLibraryStore.BUILTIN_ID.equals(id)
            && !licenses.hasPremiumAccess()) {
            result.error("PREMIUM_REQUIRED", "体验或正式激活后才能使用外部图鉴", null);
            return;
        }
        runAsync(result, () -> {
            pets.libraries().select(id);
            pets.refreshLibraryCache();
            sendService(PetOverlayService.ACTION_REFRESH);
            return snapshot();
        });
    }

    private void deleteLibrary(String id, MethodChannel.Result result) {
        runAsync(result, () -> {
            pets.libraries().delete(id);
            pets.refreshLibraryCache();
            sendService(PetOverlayService.ACTION_REFRESH);
            return snapshot();
        });
    }

    private void deleteCustom(MethodCall call, MethodChannel.Result result) {
        String petId = (String) call.argument("petId");
        boolean deleted = pets.deleteCustom(petId);
        if (deleted && petId.equals(settings.activePet())) settings.putString(SettingsStore.ACTIVE_PET, "");
        sendService(PetOverlayService.ACTION_REFRESH);
        result.success(snapshot());
    }

    private void activate(String code, MethodChannel.Result result) {
        runAsync(result, () -> {
            licenses.activate(code);
            return snapshot();
        });
    }

    private void checkTrial(MethodChannel.Result result) {
        runAsync(result, () -> {
            licenses.checkTrial();
            return snapshot();
        });
    }

    private void siteLinks(MethodChannel.Result result) {
        runAsync(result, () -> {
            try {
                JSONObject response = NetworkClient.json(this, "GET", DeskPetApi.SITE_SETTINGS,
                    null, licenses, NetworkClient.Auth.NONE);
                String url = response.optString("xianyuUrl", "").trim();
                if (url.startsWith("https://")) settings.putString(SettingsStore.XIANYU_URL, url);
            } catch (Exception ignored) { }
            return snapshot();
        });
    }

    private void openUrl(String url, MethodChannel.Result result) {
        String target = url == null ? "" : url.trim();
        if (target.isEmpty()) target = WEBSITE_URL;
        if (!target.startsWith("https://") && !target.startsWith("http://")) {
            result.error("INVALID_URL", "链接无效", null);
            return;
        }
        startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(target))
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        result.success(true);
    }

    private void copyText(String text, MethodChannel.Result result) {
        String value = text == null ? "" : text;
        ClipboardManager clipboard = (ClipboardManager) getSystemService(CLIPBOARD_SERVICE);
        clipboard.setPrimaryClip(ClipData.newPlainText("桌搭子", value));
        result.success(true);
    }

    private void importTheaterScript(MethodChannel.Result result) {
        if (!licenses.hasPremiumAccess()) {
            result.error("PREMIUM_REQUIRED", "体验或正式激活后才能导入小剧场剧本", null);
            return;
        }
        if (theaterScripts.scripts().size() >= TheaterScriptStore.MAX_SCRIPTS) {
            result.error("LIMIT", "最多只能保存 10 个小剧场剧本", null);
            return;
        }
        pendingTheaterImport = result;
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT)
            .addCategory(Intent.CATEGORY_OPENABLE)
            .setType("application/json");
        intent.putExtra(Intent.EXTRA_MIME_TYPES, new String[]{"application/json", "text/plain", "*/*"});
        startActivityForResult(intent, REQUEST_THEATER_SCRIPT);
    }

    private void deleteTheaterScript(String id, MethodChannel.Result result) {
        theaterScripts.delete(id);
        sendService(PetOverlayService.ACTION_REFRESH);
        result.success(snapshot());
    }

    private void saveReminder(MethodCall call, MethodChannel.Result result) {
        if (!licenses.hasPremiumAccess()) {
            result.error("PREMIUM_REQUIRED", "体验或正式激活后才能使用提醒", null);
            return;
        }
        Number at = call.argument("at");
        reminders.save(
            (String) call.argument("id"),
            Boolean.TRUE.equals(call.argument("enabled")),
            at == null ? 0L : at.longValue(),
            (String) call.argument("message"),
            (String) call.argument("emotion"),
            (String) call.argument("expressionPetId"),
            Boolean.TRUE.equals(call.argument("repeatDaily")));
        sendService(PetOverlayService.ACTION_REFRESH);
        result.success(snapshot());
    }

    private void deleteReminder(String id, MethodChannel.Result result) {
        reminders.delete(id);
        sendService(PetOverlayService.ACTION_REFRESH);
        result.success(snapshot());
    }

    private void checkUpdate(boolean manual, MethodChannel.Result result) {
        if (manual) updates.clearIgnored();
        runAsync(result, () -> {
            updates.check(manual);
            return snapshot();
        });
    }

    private void downloadUpdate(MethodChannel.Result result) {
        runAsync(result, () -> {
            updates.download(state -> runOnUiThread(() -> {
                if (channel != null) channel.invokeMethod("updateProgress", state.toMap());
            }));
            return snapshot();
        });
    }

    private void installUpdate(MethodChannel.Result result) {
        File apk = updates.downloadedFile();
        if (apk == null) {
            result.error("UPDATE_MISSING", "已下载的更新文件不存在", null);
            return;
        }
        if (!canInstallPackages()) {
            startActivity(new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,
                Uri.parse("package:" + getPackageName())).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
            result.error("INSTALL_PERMISSION", "请先允许桌搭子安装应用，再回来点安装更新", null);
            return;
        }
        Uri uri = FileProvider.getUriForFile(this, getPackageName() + ".update", apk);
        Intent intent = new Intent(Intent.ACTION_VIEW);
        intent.setDataAndType(uri, "application/vnd.android.package-archive");
        intent.setClipData(ClipData.newRawUri("update", uri));
        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_ACTIVITY_NEW_TASK);
        for (var resolve : getPackageManager().queryIntentActivities(intent, PackageManager.MATCH_DEFAULT_ONLY)) {
            grantUriPermission(resolve.activityInfo.packageName, uri, Intent.FLAG_GRANT_READ_URI_PERMISSION);
        }
        startActivity(intent);
        result.success(snapshot());
    }

    private void ignoreUpdate(MethodChannel.Result result) {
        updates.ignoreAvailable();
        result.success(snapshot());
    }

    private boolean canInstallPackages() {
        return getPackageManager().canRequestPackageInstalls();
    }

    private void runAsync(MethodChannel.Result result, ThrowingSupplier<Object> operation) {
        executor.execute(() -> {
            try {
                Object value = operation.get();
                runOnUiThread(() -> result.success(value));
            } catch (Exception error) {
                runOnUiThread(() -> result.error("NETWORK_ERROR", safeMessage(error), null));
            }
        });
    }

    private Map<String, Object> profileMap(CompanionService.Profile profile) {
        Map<String, Object> result = new LinkedHashMap<>();
        result.put("displayName", profile.displayName());
        result.put("pairingCode", profile.pairingCode());
        result.put("hallEnabled", profile.hallEnabled());
        result.put("online", profile.online());
        if (profile.partner() == null) result.put("partner", null);
        else {
            Map<String, String> partner = new LinkedHashMap<>();
            partner.put("displayName", profile.partner().displayName());
            partner.put("pairedAt", profile.partner().pairedAt());
            result.put("partner", partner);
        }
        return result;
    }

    private Map<String, Object> hallMap(CompanionService.Hall hall) {
        Map<String, Object> result = new LinkedHashMap<>();
        result.put("enabled", hall.enabled());
        List<Map<String, Object>> people = new ArrayList<>();
        for (CompanionService.HallPerson person : hall.people()) {
            Map<String, Object> item = new LinkedHashMap<>();
            item.put("id", person.id());
            item.put("displayName", person.displayName());
            item.put("online", person.online());
            people.add(item);
        }
        result.put("people", people);
        return result;
    }

    @Override protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode == REQUEST_THEATER_SCRIPT) {
            handleTheaterImportResult(resultCode, data);
            return;
        }
        if (requestCode == REQUEST_LIBRARY) {
            handleLibraryImportResult(resultCode, data);
            return;
        }
        if (requestCode != REQUEST_GIF || pendingImport == null) return;
        MethodChannel.Result result = pendingImport;
        pendingImport = null;
        if (resultCode != Activity.RESULT_OK || data == null || data.getData() == null) {
            result.success(snapshot());
            return;
        }
        executor.execute(() -> {
            File pending = new File(getCacheDir(), "imported-pet.pending.gif");
            try {
                File destination = pets.nextImportFile();
                if (destination == null) throw new IllegalStateException("最多可以导入 3 个自定义 GIF");
                long copied = 0;
                try (InputStream input = getContentResolver().openInputStream(data.getData());
                     FileOutputStream output = new FileOutputStream(pending)) {
                    if (input == null) throw new IllegalStateException("无法读取所选文件");
                    byte[] buffer = new byte[8192];
                    int count;
                    while ((count = input.read(buffer)) >= 0) {
                        copied += count;
                        if (copied > PetRepository.MAX_GIF_BYTES) {
                            throw new IllegalArgumentException("GIF 不能超过 8 MB");
                        }
                        output.write(buffer, 0, count);
                    }
                }
                PetRepository.validateGifFile(pending);
                Drawable decoded = ImageDecoder.decodeDrawable(ImageDecoder.createSource(pending));
                if (decoded instanceof AnimatedImageDrawable animated) animated.stop();
                File parent = destination.getParentFile();
                if (parent != null && !parent.exists() && !parent.mkdirs()) {
                    throw new IllegalStateException("无法创建桌宠目录");
                }
                Files.move(pending.toPath(), destination.toPath(), StandardCopyOption.REPLACE_EXISTING);
                settings.putString(SettingsStore.ACTIVE_PET, pets.idForFile(destination));
                settings.putBoolean(SettingsStore.RANDOM_PET, false);
                sendService(PetOverlayService.ACTION_REFRESH);
                runOnUiThread(() -> result.success(snapshot()));
            } catch (Exception error) {
                pending.delete();
                runOnUiThread(() -> result.error("IMPORT_ERROR", safeMessage(error), null));
            }
        });
    }

    private void handleLibraryImportResult(int resultCode, Intent data) {
        if (pendingLibraryImport == null) return;
        MethodChannel.Result result = pendingLibraryImport;
        pendingLibraryImport = null;
        if (resultCode != Activity.RESULT_OK || data == null || data.getData() == null) {
            result.success(snapshot());
            return;
        }
        Uri tree = data.getData();
        executor.execute(() -> {
            try {
                int flags = data.getFlags();
                pets.libraries().add(tree, flags);
                pets.refreshLibraryCache();
                sendService(PetOverlayService.ACTION_REFRESH);
                runOnUiThread(() -> result.success(snapshot()));
            } catch (Exception error) {
                runOnUiThread(() -> result.error("IMPORT_ERROR", safeMessage(error), null));
            }
        });
    }

    private void handleTheaterImportResult(int resultCode, Intent data) {
        if (pendingTheaterImport == null) return;
        MethodChannel.Result result = pendingTheaterImport;
        pendingTheaterImport = null;
        if (resultCode != Activity.RESULT_OK || data == null || data.getData() == null) {
            result.success(snapshot());
            return;
        }
        executor.execute(() -> {
            try (InputStream input = getContentResolver().openInputStream(data.getData())) {
                if (input == null) throw new IllegalStateException("无法读取所选文件");
                byte[] bytes = TheaterScriptStore.readLimited(input, TheaterScriptStore.MAX_FILE_BYTES)
                    .getBytes(java.nio.charset.StandardCharsets.UTF_8);
                String name = data.getData().getLastPathSegment();
                theaterScripts.importJson(bytes, name);
                sendService(PetOverlayService.ACTION_REFRESH);
                runOnUiThread(() -> result.success(snapshot()));
            } catch (Exception error) {
                runOnUiThread(() -> result.error("IMPORT_ERROR", safeMessage(error), null));
            }
        });
    }

    @Override protected void onDestroy() {
        executor.shutdownNow();
        super.onDestroy();
    }

    private static String lastEight(String value) {
        if (value == null || value.length() <= 8) return value == null ? "" : value;
        return value.substring(value.length() - 8);
    }

    private static String safeMessage(Exception error) {
        String message = error.getMessage();
        return message == null || message.trim().isEmpty() ? "操作失败，请稍后再试" : message;
    }

    @FunctionalInterface
    private interface ThrowingSupplier<T> { T get() throws Exception; }
}
