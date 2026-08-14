package com.zhuodazi.android;

import android.content.Context;
import android.graphics.ImageDecoder;
import android.graphics.drawable.AnimatedImageDrawable;
import android.graphics.drawable.Drawable;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.List;

final class PetRepository {
    static final int MAX_CUSTOM_PETS = 3;
    private static final String CUSTOM_PREFIX = "@custom:";
    private final Context context;
    private final SettingsStore settings;

    PetRepository(Context context, SettingsStore settings) {
        this.context = context.getApplicationContext();
        this.settings = settings;
        migrateLegacyImport();
    }

    List<String> pets() {
        List<String> result = new ArrayList<>();
        for (int slot = 1; slot <= MAX_CUSTOM_PETS; slot++) {
            if (customFile(slot).isFile()) result.add(customId(slot));
        }
        try {
            String[] names = context.getAssets().list("");
            if (names != null) {
                Arrays.sort(names);
                for (String name : names) {
                    if (name.matches("\\d{3}-[0-9a-f]{8}\\.gif")) result.add(name);
                }
            }
        } catch (IOException ignored) { }
        return result;
    }

    String selectedPet() {
        List<String> available = pets();
        if (available.isEmpty()) return "";
        String selected = settings.activePet();
        return available.contains(selected) ? selected : available.get(0);
    }

    String nextPet(String current) {
        List<String> available = pets();
        if (available.isEmpty()) return "";
        int currentIndex = available.indexOf(current);
        return available.get((currentIndex + 1 + available.size()) % available.size());
    }

    String randomPet(String current) {
        List<String> available = pets();
        if (available.size() < 2) return available.isEmpty() ? "" : available.get(0);
        Collections.shuffle(available);
        return available.get(0).equals(current) ? available.get(1) : available.get(0);
    }

    String displayName(String petId) {
        if (isCustom(petId)) return "我的 GIF " + customSlot(petId);
        int dash = petId.indexOf('-');
        String number = dash > 0 ? petId.substring(0, dash) : petId.replace(".gif", "");
        return "月薪喵 " + number;
    }

    Drawable load(String petId) throws IOException {
        ImageDecoder.Source source = isCustom(petId)
            ? ImageDecoder.createSource(customFile(customSlot(petId)))
            : ImageDecoder.createSource(context.getAssets(), petId);
        Drawable drawable = ImageDecoder.decodeDrawable(source);
        if (drawable instanceof AnimatedImageDrawable animated) {
            animated.setRepeatCount(AnimatedImageDrawable.REPEAT_INFINITE);
            animated.start();
        }
        return drawable;
    }

    File nextImportFile() {
        for (int slot = 1; slot <= MAX_CUSTOM_PETS; slot++) {
            File file = customFile(slot);
            if (!file.exists()) return file;
        }
        return null;
    }

    String idForFile(File file) {
        for (int slot = 1; slot <= MAX_CUSTOM_PETS; slot++) {
            if (customFile(slot).equals(file)) return customId(slot);
        }
        throw new IllegalArgumentException("未知的自定义桌宠文件");
    }

    boolean deleteCustom(String petId) {
        return isCustom(petId) && customFile(customSlot(petId)).delete();
    }

    int customPetCount() {
        int count = 0;
        for (int slot = 1; slot <= MAX_CUSTOM_PETS; slot++) if (customFile(slot).isFile()) count++;
        return count;
    }

    boolean isCustom(String petId) {
        return petId != null && petId.matches("@custom:[1-3]");
    }

    byte[] readGif(String petId, int maximumBytes) throws IOException {
        try (InputStream input = isCustom(petId)
                ? new FileInputStream(customFile(customSlot(petId)))
                : context.getAssets().open(petId);
             ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[8192];
            int count;
            int total = 0;
            while ((count = input.read(buffer)) >= 0) {
                total += count;
                if (total > maximumBytes) throw new IOException("GIF 不能超过 8 MB");
                output.write(buffer, 0, count);
            }
            byte[] bytes = output.toByteArray();
            if (bytes.length < 10 || bytes[0] != 'G' || bytes[1] != 'I' || bytes[2] != 'F') {
                throw new IOException("当前文件不是有效的 GIF");
            }
            return bytes;
        }
    }

    File saveInboxGif(String id, byte[] bytes) throws IOException {
        File directory = new File(context.getFilesDir(), "companion");
        if (!directory.exists() && !directory.mkdirs()) throw new IOException("无法创建搭子收件目录");
        File pending = new File(directory, id + ".pending");
        File target = new File(directory, id + ".gif");
        try (FileOutputStream output = new FileOutputStream(pending)) { output.write(bytes); }
        if (target.exists() && !target.delete()) throw new IOException("无法替换搭子 GIF");
        if (!pending.renameTo(target)) throw new IOException("无法保存搭子 GIF");
        return target;
    }

    private File customFile(int slot) {
        return new File(context.getFilesDir(), "pets/custom_" + slot + ".gif");
    }

    private void migrateLegacyImport() {
        File legacy = new File(context.getFilesDir(), "pets/imported.gif");
        File firstSlot = customFile(1);
        if (legacy.isFile() && !firstSlot.exists() && legacy.renameTo(firstSlot)
            && "@imported".equals(settings.activePet())) {
            settings.putString(SettingsStore.ACTIVE_PET, customId(1));
        }
    }

    private static String customId(int slot) { return CUSTOM_PREFIX + slot; }

    private static int customSlot(String petId) {
        try { return Integer.parseInt(petId.substring(CUSTOM_PREFIX.length())); }
        catch (Exception ignored) { return 1; }
    }
}
