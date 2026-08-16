package com.zhuodazi.android;

import android.Manifest;
import android.app.Activity;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.ImageDecoder;
import android.graphics.drawable.AnimatedImageDrawable;
import android.graphics.drawable.Drawable;
import android.net.Uri;
import android.os.Build;
import android.provider.Settings;

import androidx.annotation.NonNull;

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

    private final ExecutorService executor = Executors.newSingleThreadExecutor();
    private SettingsStore settings;
    private PetRepository pets;
    private WordRepository words;
    private LicenseService licenses;
    private InteractionContentService interactionContent;
    private CompanionService companions;
    private MethodChannel.Result pendingImport;

    @Override public void configureFlutterEngine(@NonNull FlutterEngine engine) {
        super.configureFlutterEngine(engine);
        settings = new SettingsStore(this);
        pets = new PetRepository(this, settings);
        words = new WordRepository(this, settings);
        licenses = new LicenseService(this);
        interactionContent = new InteractionContentService(this, licenses);
        companions = new CompanionService(this, licenses, pets);
        new MethodChannel(engine.getDartExecutor().getBinaryMessenger(), CHANNEL)
            .setMethodCallHandler(this::handleCall);
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
                case "deleteCustom" -> deleteCustom(call, result);
                case "activate" -> activate((String) call.argument("code"), result);
                case "checkTrial" -> checkTrial(result);
                case "companionRefresh" -> runAsync(result, () -> profileMap(companions.refreshProfile()));
                case "companionUpdateName" -> runAsync(result,
                    () -> profileMap(companions.updateName((String) call.argument("name"))));
                case "companionPair" -> runAsync(result,
                    () -> profileMap(companions.pair((String) call.argument("code"))));
                case "companionUnpair" -> runAsync(result, () -> profileMap(companions.unpair()));
                case "companionSend" -> runAsync(result, companions::sendCurrentGif);
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
        value.put("wordPacks", words.packs());
        value.put("wordPack", settings.wordPack());
        value.put("activated", licenses.isActivated());
        value.put("premium", licenses.hasPremiumAccess());
        value.put("trialSeconds", licenses.trialRemainingSeconds());
        value.put("installationSuffix", lastEight(licenses.installationId()));
        value.put("version", NetworkClient.appVersion(this));
        return value;
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
            || SettingsStore.RANDOM_PET_INTERVAL.equals(key)) {
            settings.putInt(key, ((Number) value).intValue());
        } else if (SettingsStore.PERSONALITY.equals(key) || SettingsStore.INTERACTION_MODE.equals(key)
            || SettingsStore.WORD_PACK.equals(key)) {
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
            case "show" -> PetOverlayService.ACTION_SHOW;
            case "hide" -> PetOverlayService.ACTION_HIDE;
            case "clickThrough" -> PetOverlayService.ACTION_CLICK_THROUGH;
            default -> PetOverlayService.ACTION_REFRESH;
        };
        if ("start".equals(action) && !Settings.canDrawOverlays(this)) {
            result.error("OVERLAY_PERMISSION", "请先授予悬浮窗权限", null);
            return;
        }
        sendService(nativeAction);
        result.success(snapshot());
    }

    private void sendService(String action) {
        Intent intent = new Intent(this, PetOverlayService.class).setAction(action);
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
            result.success(true);
            return;
        }
        startActivity(new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION,
            Uri.parse("package:" + getPackageName())));
        result.success(false);
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
        if (profile.partner() == null) result.put("partner", null);
        else {
            Map<String, String> partner = new LinkedHashMap<>();
            partner.put("displayName", profile.partner().displayName());
            partner.put("pairedAt", profile.partner().pairedAt());
            result.put("partner", partner);
        }
        return result;
    }

    @Override protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
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
