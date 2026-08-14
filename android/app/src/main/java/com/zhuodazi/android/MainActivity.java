package com.zhuodazi.android;

import android.Manifest;
import android.app.Activity;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.graphics.ImageDecoder;
import android.graphics.Typeface;
import android.graphics.drawable.AnimatedImageDrawable;
import android.graphics.drawable.Drawable;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.provider.Settings;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.AdapterView;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.SeekBar;
import android.widget.Spinner;
import android.widget.Switch;
import android.widget.TextView;
import android.widget.Toast;

import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.nio.file.Files;
import java.nio.file.StandardCopyOption;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

public final class MainActivity extends Activity {
    private static final int REQUEST_OVERLAY = 301;
    private static final int REQUEST_GIF = 302;
    private static final int REQUEST_NOTIFICATIONS = 303;
    private static final long MAX_GIF_BYTES = 20L * 1024 * 1024;

    private SettingsStore settingsStore;
    private PetRepository petRepository;
    private WordRepository wordRepository;
    private TextView statusText;
    private Button permissionButton;
    private Button startButton;
    private Button stopButton;
    private Spinner petSpinner;
    private boolean binding;
    private boolean startAfterPermission;

    @Override protected void onCreate(Bundle state) {
        super.onCreate(state);
        settingsStore = new SettingsStore(this);
        petRepository = new PetRepository(this, settingsStore);
        wordRepository = new WordRepository(this, settingsStore);
        getWindow().setStatusBarColor(Color.rgb(244, 247, 246));
        getWindow().setNavigationBarColor(Color.rgb(244, 247, 246));
        getWindow().getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR);
        setContentView(buildContent());
    }

    @Override protected void onResume() {
        super.onResume();
        refreshStatus();
        refreshPetSpinner();
        if (startAfterPermission && Settings.canDrawOverlays(this)) {
            startAfterPermission = false;
            startPet();
        }
    }

    private View buildContent() {
        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        scroll.setBackgroundColor(Color.rgb(244, 247, 246));
        LinearLayout content = vertical();
        content.setPadding(dp(20), dp(18), dp(20), dp(40));
        scroll.addView(content, matchWrap());

        TextView title = text("桌搭子", 28, true);
        title.setTextColor(Color.rgb(23, 32, 30));
        content.addView(title);
        TextView subtitle = text("Android 悬浮桌宠", 14, false);
        subtitle.setTextColor(Color.rgb(91, 105, 101));
        content.addView(subtitle, margins(0, 2, 0, 16));

        statusText = text("", 15, true);
        content.addView(statusText, margins(0, 0, 0, 8));
        permissionButton = button("授予悬浮窗权限", false);
        permissionButton.setOnClickListener(v -> requestOverlayPermission(true));
        content.addView(permissionButton, matchMargins(0, 0, 0, 18));

        section(content, "桌宠控制");
        LinearLayout actions = horizontal();
        startButton = button("启动桌宠", true);
        startButton.setOnClickListener(v -> startPet());
        stopButton = button("收起桌宠", false);
        stopButton.setOnClickListener(v -> sendService(PetOverlayService.ACTION_STOP));
        Button nextButton = button("换一只", false);
        nextButton.setOnClickListener(v -> sendService(PetOverlayService.ACTION_NEXT));
        actions.addView(startButton, weighted());
        actions.addView(stopButton, weightedMargins(8));
        actions.addView(nextButton, weightedMargins(8));
        content.addView(actions, matchMargins(0, 0, 0, 10));
        Button speakButton = button("让桌搭子说句话", false);
        speakButton.setOnClickListener(v -> sendService(PetOverlayService.ACTION_SAY));
        content.addView(speakButton, matchMargins(0, 0, 0, 18));

        section(content, "外观与资源");
        label(content, "当前桌宠");
        petSpinner = spinner();
        content.addView(petSpinner, matchMargins(0, 4, 0, 8));
        petSpinner.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {
            @Override public void onItemSelected(AdapterView<?> parent, View view, int position, long id) {
                if (binding) return;
                List<String> pets = petRepository.pets();
                if (position >= 0 && position < pets.size()) {
                    String selected = pets.get(position);
                    if (selected.equals(settingsStore.activePet())) return;
                    settingsStore.putString(SettingsStore.ACTIVE_PET, selected);
                    settingsStore.putBoolean(SettingsStore.RANDOM_PET, false);
                    sendService(PetOverlayService.ACTION_REFRESH);
                }
            }
            @Override public void onNothingSelected(AdapterView<?> parent) { }
        });
        Button importButton = button("导入自己的 GIF", false);
        importButton.setOnClickListener(v -> chooseGif());
        content.addView(importButton, matchMargins(0, 0, 0, 12));

        seekRow(content, "桌宠大小", 96, 280, settingsStore.sizeDp(), "dp", value -> {
            settingsStore.putInt(SettingsStore.SIZE, value);
            sendService(PetOverlayService.ACTION_REFRESH);
        });
        seekRow(content, "透明度", 20, 100, settingsStore.opacity(), "%", value -> {
            settingsStore.putInt(SettingsStore.OPACITY, value);
            sendService(PetOverlayService.ACTION_REFRESH);
        });
        addSwitch(content, "镜像显示", "反转桌宠朝向", settingsStore.mirrored(), checked -> {
            settingsStore.putBoolean(SettingsStore.MIRRORED, checked);
            sendService(PetOverlayService.ACTION_REFRESH);
        });

        label(content, "互动词包");
        List<String> wordPacks = wordRepository.packs();
        List<String> wordPackNames = new ArrayList<>();
        for (String pack : wordPacks) wordPackNames.add(wordRepository.displayName(pack));
        Spinner wordSpinner = spinner();
        wordSpinner.setAdapter(adapter(wordPackNames));
        wordSpinner.setSelection(Math.max(0, wordPacks.indexOf(settingsStore.wordPack())), false);
        wordSpinner.setOnItemSelectedListener(selection(position -> {
            if (position < wordPacks.size()) {
                settingsStore.putString(SettingsStore.WORD_PACK, wordPacks.get(position));
                sendService(PetOverlayService.ACTION_REFRESH);
            }
        }));
        content.addView(wordSpinner, matchMargins(0, 4, 0, 18));

        section(content, "行为与互动");
        addSwitch(content, "随机走动", "桌宠会在屏幕范围内自己散步", settingsStore.movement(), checked -> {
            settingsStore.putBoolean(SettingsStore.MOVEMENT, checked);
            sendService(PetOverlayService.ACTION_REFRESH);
        });
        addSwitch(content, "随机互动", "空闲时显示互动气泡", settingsStore.interactions(), checked -> {
            settingsStore.putBoolean(SettingsStore.INTERACTIONS, checked);
            sendService(PetOverlayService.ACTION_REFRESH);
        });
        addSwitch(content, "随机换宠", "从内置月薪喵资源中轮换", settingsStore.randomPet(), checked -> {
            settingsStore.putBoolean(SettingsStore.RANDOM_PET, checked);
            sendService(PetOverlayService.ACTION_REFRESH);
        });

        addChoice(content, "性格", new String[]{"活泼", "害羞", "黏人", "混乱"},
            new String[]{"lively", "shy", "clingy", "chaotic"}, settingsStore.personality(), value -> {
                settingsStore.putString(SettingsStore.PERSONALITY, value);
                sendService(PetOverlayService.ACTION_REFRESH);
            });
        addChoice(content, "互动频率", new String[]{"安静", "标准", "活跃"},
            new String[]{"quiet", "standard", "lively"}, settingsStore.interactionMode(), value -> {
                settingsStore.putString(SettingsStore.INTERACTION_MODE, value);
                sendService(PetOverlayService.ACTION_REFRESH);
            });
        addChoice(content, "随机换宠间隔", new String[]{"30 秒", "1 分钟", "5 分钟", "10 分钟", "30 分钟"},
            new String[]{"30", "60", "300", "600", "1800"}, String.valueOf(settingsStore.randomPetInterval()), value -> {
                settingsStore.putInt(SettingsStore.RANDOM_PET_INTERVAL, Integer.parseInt(value));
                sendService(PetOverlayService.ACTION_REFRESH);
            });

        section(content, "系统");
        addSwitch(content, "开机后自动恢复", "仅在已授予悬浮窗权限时生效", settingsStore.startOnBoot(), checked ->
            settingsStore.putBoolean(SettingsStore.START_ON_BOOT, checked));

        TextView footer = text("单击桌宠显示互动，双击打开设置。拖动后快速松手可以投掷。", 13, false);
        footer.setTextColor(Color.rgb(91, 105, 101));
        footer.setGravity(Gravity.CENTER_HORIZONTAL);
        content.addView(footer, margins(0, 24, 0, 0));
        refreshPetSpinner();
        return scroll;
    }

    private void refreshStatus() {
        boolean permission = Settings.canDrawOverlays(this);
        boolean running = settingsStore.running();
        statusText.setText(!permission ? "需要悬浮窗权限" : running ? "桌宠正在运行" : "桌宠已收起");
        statusText.setTextColor(!permission ? Color.rgb(174, 74, 44) : running ? Color.rgb(22, 125, 108) : Color.rgb(91, 105, 101));
        permissionButton.setVisibility(permission ? View.GONE : View.VISIBLE);
        startButton.setEnabled(permission && !running);
        stopButton.setEnabled(running);
    }

    private void startPet() {
        if (!Settings.canDrawOverlays(this)) {
            requestOverlayPermission(true);
            return;
        }
        requestNotificationPermission();
        sendService(PetOverlayService.ACTION_START);
        settingsStore.setRunning(true);
        refreshStatus();
    }

    private void requestOverlayPermission(boolean startAfter) {
        startAfterPermission = startAfter;
        Intent intent = new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION,
            Uri.parse("package:" + getPackageName()));
        startActivityForResult(intent, REQUEST_OVERLAY);
    }

    private void requestNotificationPermission() {
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, REQUEST_NOTIFICATIONS);
        }
    }

    private void sendService(String action) {
        Intent intent = new Intent(this, PetOverlayService.class).setAction(action);
        if (PetOverlayService.ACTION_START.equals(action)) startForegroundService(intent);
        else if (settingsStore.running()) startService(intent);
        getWindow().getDecorView().postDelayed(this::refreshStatus, 180);
    }

    private void chooseGif() {
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        intent.addCategory(Intent.CATEGORY_OPENABLE);
        intent.setType("image/gif");
        startActivityForResult(intent, REQUEST_GIF);
    }

    @Override protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode == REQUEST_OVERLAY) {
            refreshStatus();
            return;
        }
        if (requestCode != REQUEST_GIF || resultCode != RESULT_OK || data == null || data.getData() == null) return;
        File pending = new File(getFilesDir(), "pets/imported.pending.gif");
        try {
            File destination = petRepository.importedFile();
            File parent = destination.getParentFile();
            if (parent != null && !parent.exists() && !parent.mkdirs()) throw new IllegalStateException("无法创建桌宠目录");
            long copied = 0;
            try (InputStream input = getContentResolver().openInputStream(data.getData());
                 FileOutputStream output = new FileOutputStream(pending)) {
                if (input == null) throw new IllegalStateException("无法读取所选文件");
                byte[] buffer = new byte[8192];
                int count;
                while ((count = input.read(buffer)) >= 0) {
                    copied += count;
                    if (copied > MAX_GIF_BYTES) throw new IllegalArgumentException("GIF 不能超过 20 MB");
                    output.write(buffer, 0, count);
                }
            }
            Drawable decoded = ImageDecoder.decodeDrawable(ImageDecoder.createSource(pending));
            if (decoded instanceof AnimatedImageDrawable animated) animated.stop();
            Files.move(pending.toPath(), destination.toPath(), StandardCopyOption.REPLACE_EXISTING);
            settingsStore.putString(SettingsStore.ACTIVE_PET, PetRepository.IMPORTED_ID);
            settingsStore.putBoolean(SettingsStore.RANDOM_PET, false);
            refreshPetSpinner();
            sendService(PetOverlayService.ACTION_REFRESH);
            Toast.makeText(this, "GIF 已导入", Toast.LENGTH_SHORT).show();
        } catch (Exception error) {
            pending.delete();
            Toast.makeText(this, "导入失败：" + error.getMessage(), Toast.LENGTH_LONG).show();
        }
    }

    private void refreshPetSpinner() {
        if (petSpinner == null) return;
        binding = true;
        List<String> pets = petRepository.pets();
        List<String> names = new ArrayList<>();
        for (String pet : pets) names.add(petRepository.displayName(pet));
        petSpinner.setAdapter(adapter(names));
        petSpinner.setSelection(Math.max(0, pets.indexOf(petRepository.selectedPet())), false);
        binding = false;
    }

    private void section(LinearLayout parent, String title) {
        View line = new View(this);
        line.setBackgroundColor(Color.rgb(216, 224, 221));
        parent.addView(line, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(1)));
        TextView heading = text(title, 18, true);
        heading.setTextColor(Color.rgb(23, 32, 30));
        parent.addView(heading, margins(0, 16, 0, 12));
    }

    private void label(LinearLayout parent, String value) {
        TextView label = text(value, 14, true);
        label.setTextColor(Color.rgb(55, 68, 64));
        parent.addView(label);
    }

    private void seekRow(LinearLayout parent, String title, int min, int max, int current, String suffix, IntConsumer consumer) {
        LinearLayout heading = horizontal();
        TextView name = text(title, 15, true);
        TextView value = text(current + suffix, 14, false);
        value.setTextColor(Color.rgb(22, 125, 108));
        value.setGravity(Gravity.END);
        heading.addView(name, weighted());
        heading.addView(value, weighted());
        parent.addView(heading, margins(0, 8, 0, 0));
        SeekBar seek = new SeekBar(this);
        seek.setMax(max - min);
        seek.setProgress(current - min);
        seek.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener() {
            @Override public void onProgressChanged(SeekBar bar, int progress, boolean fromUser) {
                value.setText((min + progress) + suffix);
            }
            @Override public void onStartTrackingTouch(SeekBar bar) { }
            @Override public void onStopTrackingTouch(SeekBar bar) { consumer.accept(min + bar.getProgress()); }
        });
        parent.addView(seek, matchMargins(0, 0, 0, 8));
    }

    private void addSwitch(LinearLayout parent, String title, String subtitle, boolean checked, BooleanConsumer consumer) {
        Switch control = new Switch(this);
        control.setText(title + "\n" + subtitle);
        control.setTextSize(15);
        control.setTextColor(Color.rgb(23, 32, 30));
        control.setGravity(Gravity.CENTER_VERTICAL);
        control.setPadding(0, dp(4), 0, dp(4));
        control.setChecked(checked);
        control.setOnCheckedChangeListener((button, value) -> consumer.accept(value));
        parent.addView(control, matchMargins(0, 0, 0, 4));
    }

    private void addChoice(LinearLayout parent, String title, String[] labels, String[] values, String current, StringConsumer consumer) {
        label(parent, title);
        Spinner spinner = spinner();
        spinner.setAdapter(adapter(Arrays.asList(labels)));
        int selected = 0;
        for (int index = 0; index < values.length; index++) if (values[index].equals(current)) selected = index;
        spinner.setSelection(selected, false);
        spinner.setOnItemSelectedListener(selection(position -> consumer.accept(values[position])));
        parent.addView(spinner, matchMargins(0, 4, 0, 12));
    }

    private AdapterView.OnItemSelectedListener selection(IntConsumer consumer) {
        return new AdapterView.OnItemSelectedListener() {
            private boolean first = true;
            @Override public void onItemSelected(AdapterView<?> parent, View view, int position, long id) {
                if (first) { first = false; return; }
                consumer.accept(position);
            }
            @Override public void onNothingSelected(AdapterView<?> parent) { }
        };
    }

    private ArrayAdapter<String> adapter(List<String> values) {
        ArrayAdapter<String> adapter = new ArrayAdapter<>(this, android.R.layout.simple_spinner_item, values);
        adapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        return adapter;
    }

    private LinearLayout vertical() {
        LinearLayout layout = new LinearLayout(this);
        layout.setOrientation(LinearLayout.VERTICAL);
        return layout;
    }

    private LinearLayout horizontal() {
        LinearLayout layout = new LinearLayout(this);
        layout.setOrientation(LinearLayout.HORIZONTAL);
        layout.setGravity(Gravity.CENTER_VERTICAL);
        return layout;
    }

    private TextView text(String value, int sp, boolean bold) {
        TextView text = new TextView(this);
        text.setText(value);
        text.setTextSize(sp);
        if (bold) text.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        return text;
    }

    private Button button(String text, boolean primary) {
        Button button = new Button(this);
        button.setText(text);
        button.setTextSize(14);
        button.setAllCaps(false);
        button.setMinHeight(dp(48));
        GradientDrawable background = new GradientDrawable();
        background.setCornerRadius(dp(6));
        background.setColor(primary ? Color.rgb(22, 125, 108) : Color.WHITE);
        background.setStroke(dp(1), primary ? Color.rgb(22, 125, 108) : Color.rgb(195, 207, 203));
        button.setTextColor(primary ? Color.WHITE : Color.rgb(23, 32, 30));
        button.setBackground(background);
        return button;
    }

    private Spinner spinner() {
        Spinner spinner = new Spinner(this, Spinner.MODE_DROPDOWN);
        spinner.setMinimumHeight(dp(48));
        return spinner;
    }

    private LinearLayout.LayoutParams matchWrap() {
        return new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
    }

    private LinearLayout.LayoutParams weighted() {
        return new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1);
    }

    private LinearLayout.LayoutParams weightedMargins(int left) {
        LinearLayout.LayoutParams params = weighted();
        params.leftMargin = dp(left);
        return params;
    }

    private LinearLayout.LayoutParams margins(int left, int top, int right, int bottom) {
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        params.setMargins(dp(left), dp(top), dp(right), dp(bottom));
        return params;
    }

    private LinearLayout.LayoutParams matchMargins(int left, int top, int right, int bottom) {
        LinearLayout.LayoutParams params = matchWrap();
        params.setMargins(dp(left), dp(top), dp(right), dp(bottom));
        return params;
    }

    private int dp(int value) {
        return Math.round(value * getResources().getDisplayMetrics().density);
    }

    private interface IntConsumer { void accept(int value); }
    private interface BooleanConsumer { void accept(boolean value); }
    private interface StringConsumer { void accept(String value); }
}
