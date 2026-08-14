package com.zhuodazi.android;

import android.content.Context;
import android.graphics.ImageDecoder;
import android.graphics.drawable.AnimatedImageDrawable;
import android.graphics.drawable.Drawable;

import java.io.File;
import java.io.IOException;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.List;

final class PetRepository {
    static final String IMPORTED_ID = "@imported";
    private final Context context;
    private final SettingsStore settings;

    PetRepository(Context context, SettingsStore settings) {
        this.context = context;
        this.settings = settings;
    }

    List<String> pets() {
        List<String> result = new ArrayList<>();
        try {
            String[] names = context.getAssets().list("");
            if (names != null) {
                Arrays.sort(names);
                for (String name : names) {
                    if (name.matches("\\d{3}-[0-9a-f]{8}\\.gif")) result.add(name);
                }
            }
        } catch (IOException ignored) { }
        if (importedFile().isFile()) result.add(0, IMPORTED_ID);
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
        if (IMPORTED_ID.equals(petId)) return "我的 GIF";
        int dash = petId.indexOf('-');
        String number = dash > 0 ? petId.substring(0, dash) : petId.replace(".gif", "");
        return "月薪喵 " + number;
    }

    Drawable load(String petId) throws IOException {
        ImageDecoder.Source source = IMPORTED_ID.equals(petId)
            ? ImageDecoder.createSource(importedFile())
            : ImageDecoder.createSource(context.getAssets(), petId);
        Drawable drawable = ImageDecoder.decodeDrawable(source);
        if (drawable instanceof AnimatedImageDrawable animated) {
            animated.setRepeatCount(AnimatedImageDrawable.REPEAT_INFINITE);
            animated.start();
        }
        return drawable;
    }

    File importedFile() {
        return new File(context.getFilesDir(), "pets/imported.gif");
    }
}
