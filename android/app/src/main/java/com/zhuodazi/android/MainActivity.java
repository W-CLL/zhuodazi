package com.zhuodazi.android;

import android.Manifest;
import android.app.Activity;
import android.app.AlertDialog;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.content.res.ColorStateList;
import android.graphics.Color;
import android.graphics.ImageDecoder;
import android.graphics.Typeface;
import android.graphics.drawable.AnimatedImageDrawable;
import android.graphics.drawable.Drawable;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.provider.Settings;
import android.text.InputFilter;
import android.text.InputType;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.AdapterView;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.EditText;
import android.widget.FrameLayout;
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
import java.util.Locale;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public final class MainActivity extends Activity {
    private static final int REQUEST_OVERLAY = 301;
    private static final int REQUEST_GIF = 302;
    private static final int REQUEST_NOTIFICATIONS = 303;
    private static final int PAGE = 0xfff4f7f6;
    private static final int SURFACE = 0xffffffff;
    private static final int TEXT = 0xff17201e;
    private static final int MUTED = 0xff5b6965;
    private static final int LINE = 0xffd8e0dd;
    private static final int BRAND = 0xff167d6c;
    private static final int BRAND_DARK = 0xff0d5f52;
    private static final int ACCENT = 0xffd7843e;
    private static final int DANGER = 0xffa74736;
    private static final String[] TAB_TITLES = {"首页", "桌宠", "互动", "搭子", "我的"};

    private final Handler handler = new Handler(Looper.getMainLooper());
    private final ExecutorService networkExecutor = Executors.newSingleThreadExecutor();
    private SettingsStore settingsStore;
    private PetRepository petRepository;
    private WordRepository wordRepository;
    private LicenseService licenseService;
    private CompanionService companionService;
    private FrameLayout pageHost;
    private TextView screenTitle;
    private TextView licenseBadge;
    private final List<TextView> navLabels = new ArrayList<>();
    private final List<View> navIndicators = new ArrayList<>();
    private int currentTab;
    private boolean startAfterPermission;
    private boolean companionLoading;
    private boolean companionLoadAttempted;
    private CompanionService.Profile companionProfile;
    private TextView trialCountdown;
    private long lastPremiumState = -1;

    private final Runnable countdownTask = new Runnable() {
        @Override public void run() {
            updateLicenseHeader();
            if (trialCountdown != null) trialCountdown.setText(trialDescription());
            long premiumState = licenseService.hasPremiumAccess() ? 1 : 0;
            if (lastPremiumState >= 0 && premiumState != lastPremiumState) {
                sendService(PetOverlayService.ACTION_REFRESH);
                renderCurrentPage();
            }
            lastPremiumState = premiumState;
            handler.postDelayed(this, 1000L);
        }
    };

    @Override protected void onCreate(Bundle state) {
        super.onCreate(state);
        settingsStore = new SettingsStore(this);
        petRepository = new PetRepository(this, settingsStore);
        wordRepository = new WordRepository(this, settingsStore);
        licenseService = new LicenseService(this);
        companionService = new CompanionService(this, licenseService, petRepository);
        currentTab = settingsStore.selectedTab();
        configureSystemBars();
        setContentView(buildShell());
        renderCurrentPage();
        handler.post(countdownTask);
        if (!licenseService.isActivated()) checkTrial(false);
    }

    @Override protected void onResume() {
        super.onResume();
        if (pageHost != null) renderCurrentPage();
        if (startAfterPermission && Settings.canDrawOverlays(this)) {
            startAfterPermission = false;
            startPet();
        }
    }

    @Override protected void onDestroy() {
        handler.removeCallbacksAndMessages(null);
        networkExecutor.shutdownNow();
        super.onDestroy();
    }

    private void configureSystemBars() {
        getWindow().setStatusBarColor(PAGE);
        getWindow().setNavigationBarColor(SURFACE);
        if (Build.VERSION.SDK_INT >= 28) getWindow().setNavigationBarDividerColor(LINE);
        getWindow().getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR);
    }

    private View buildShell() {
        LinearLayout shell = vertical();
        shell.setBackgroundColor(PAGE);

        LinearLayout appBar = horizontal();
        appBar.setPadding(dp(18), dp(8), dp(14), dp(8));
        appBar.setGravity(Gravity.CENTER_VERTICAL);
        appBar.setBackgroundColor(PAGE);
        LinearLayout titles = vertical();
        TextView brand = text("桌搭子", 21, true);
        brand.setTextColor(TEXT);
        screenTitle = text(TAB_TITLES[currentTab], 12, false);
        screenTitle.setTextColor(MUTED);
        titles.addView(brand);
        titles.addView(screenTitle);
        appBar.addView(titles, new LinearLayout.LayoutParams(0, dp(58), 1f));
        licenseBadge = text("", 12, true);
        licenseBadge.setGravity(Gravity.CENTER);
        licenseBadge.setPadding(dp(10), dp(5), dp(10), dp(5));
        appBar.addView(licenseBadge, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(34)));
        shell.addView(appBar, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(66)));

        pageHost = new FrameLayout(this);
        shell.addView(pageHost, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));

        LinearLayout nav = horizontal();
        nav.setBackgroundColor(SURFACE);
        nav.setPadding(dp(4), 0, dp(4), 0);
        for (int index = 0; index < TAB_TITLES.length; index++) {
            int tab = index;
            LinearLayout item = vertical();
            item.setGravity(Gravity.CENTER_HORIZONTAL);
            item.setClickable(true);
            item.setFocusable(true);
            View indicator = new View(this);
            item.addView(indicator, new LinearLayout.LayoutParams(dp(28), dp(3)));
            TextView label = text(TAB_TITLES[index], 13, false);
            label.setGravity(Gravity.CENTER);
            item.addView(label, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
            item.setOnClickListener(view -> selectTab(tab));
            navLabels.add(label);
            navIndicators.add(indicator);
            nav.addView(item, new LinearLayout.LayoutParams(0, dp(64), 1f));
        }
        shell.addView(nav, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(64)));
        updateNavigation();
        updateLicenseHeader();
        return shell;
    }

    private void selectTab(int tab) {
        if (tab < 0 || tab >= TAB_TITLES.length) return;
        currentTab = tab;
        settingsStore.putInt(SettingsStore.SELECTED_TAB, tab);
        renderCurrentPage();
    }

    private void renderCurrentPage() {
        if (pageHost == null) return;
        trialCountdown = null;
        screenTitle.setText(TAB_TITLES[currentTab]);
        updateNavigation();
        pageHost.removeAllViews();
        View page = switch (currentTab) {
            case 1 -> buildPetPage();
            case 2 -> buildInteractionPage();
            case 3 -> buildCompanionPage();
            case 4 -> buildAccountPage();
            default -> buildHomePage();
        };
        pageHost.addView(page, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        updateLicenseHeader();
    }

    private View buildHomePage() {
        LinearLayout content = pageContent();
        content.addView(pageHeading("现在的状态", "桌宠显示、后台服务和权限集中控制"));

        LinearLayout status = panel();
        TextView state = text(petStatusTitle(), 20, true);
        state.setTextColor(Settings.canDrawOverlays(this) && settingsStore.running() ? BRAND : TEXT);
        status.addView(state);
        TextView detail = text(petStatusDetail(), 13, false);
        detail.setTextColor(MUTED);
        status.addView(detail, margins(0, 5, 0, 14));

        if (!Settings.canDrawOverlays(this)) {
            Button permission = button("授予悬浮窗权限", true);
            permission.setOnClickListener(view -> requestOverlayPermission(true));
            status.addView(permission, matchMargins(0, 0, 0, 0));
        } else if (!settingsStore.running()) {
            Button start = button("启动桌宠", true);
            start.setOnClickListener(view -> startPet());
            status.addView(start, matchMargins(0, 0, 0, 0));
        } else if (settingsStore.petHidden() || settingsStore.clickThrough()) {
            Button show = button(settingsStore.clickThrough() ? "恢复触摸" : "显示桌宠", true);
            show.setOnClickListener(view -> sendService(PetOverlayService.ACTION_SHOW));
            status.addView(show, matchMargins(0, 0, 0, 0));
        }
        content.addView(status, matchMargins(0, 0, 0, 18));

        if (settingsStore.running()) {
            section(content, "快捷控制");
            LinearLayout first = horizontal();
            Button interact = button("互动", false);
            interact.setOnClickListener(view -> sendService(PetOverlayService.ACTION_INTERACT));
            Button next = button("换一只", false);
            next.setOnClickListener(view -> sendService(PetOverlayService.ACTION_NEXT));
            Button hide = button("隐藏", false);
            hide.setOnClickListener(view -> sendService(PetOverlayService.ACTION_HIDE));
            first.addView(interact, weighted());
            first.addView(next, weightedMargins(8));
            first.addView(hide, weightedMargins(8));
            content.addView(first, matchMargins(0, 0, 0, 9));
            Button clickThrough = button(settingsStore.clickThrough() ? "恢复桌宠触摸" : "开启触摸穿透", false);
            clickThrough.setOnClickListener(view -> sendService(settingsStore.clickThrough()
                ? PetOverlayService.ACTION_SHOW : PetOverlayService.ACTION_CLICK_THROUGH));
            content.addView(clickThrough, matchMargins(0, 0, 0, 9));
            Button stop = button("完全退出后台服务", false);
            styleDangerOutline(stop);
            stop.setOnClickListener(view -> sendService(PetOverlayService.ACTION_STOP));
            content.addView(stop, matchMargins(0, 0, 0, 18));
        }

        section(content, "使用权益");
        LinearLayout licensePanel = panel();
        TextView licenseTitle = text(licenseTitle(), 17, true);
        licenseTitle.setTextColor(licenseService.isActivated() ? BRAND : ACCENT);
        licensePanel.addView(licenseTitle);
        trialCountdown = text(trialDescription(), 13, false);
        trialCountdown.setTextColor(MUTED);
        licensePanel.addView(trialCountdown, margins(0, 5, 0, 12));
        Button manage = button(licenseService.isActivated() ? "查看设备信息" : "激活与体验", false);
        manage.setOnClickListener(view -> selectTab(4));
        licensePanel.addView(manage);
        content.addView(licensePanel, matchMargins(0, 0, 0, 22));
        return scroll(content);
    }

    private View buildPetPage() {
        LinearLayout content = pageContent();
        content.addView(pageHeading("我的桌宠", "选择形象并调整悬浮表现"));
        section(content, "桌宠形象");
        List<String> pets = petRepository.pets();
        List<String> names = new ArrayList<>();
        for (String pet : pets) names.add(petRepository.displayName(pet));
        Spinner petSpinner = spinner();
        petSpinner.setAdapter(adapter(names));
        String selected = petRepository.selectedPet();
        petSpinner.setSelection(Math.max(0, pets.indexOf(selected)), false);
        petSpinner.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {
            @Override public void onItemSelected(AdapterView<?> parent, View view, int position, long id) {
                if (position < 0 || position >= pets.size()) return;
                String pet = pets.get(position);
                if (pet.equals(settingsStore.activePet())) return;
                settingsStore.putString(SettingsStore.ACTIVE_PET, pet);
                settingsStore.putBoolean(SettingsStore.RANDOM_PET, false);
                sendService(PetOverlayService.ACTION_REFRESH);
                renderCurrentPage();
            }
            @Override public void onNothingSelected(AdapterView<?> parent) { }
        });
        content.addView(petSpinner, matchMargins(0, 0, 0, 9));

        LinearLayout resourceActions = horizontal();
        Button importButton = button(licenseService.isActivated()
            ? "导入 GIF（" + petRepository.customPetCount() + "/3）"
            : "激活后可导入 GIF", false);
        importButton.setEnabled(licenseService.isActivated()
            && petRepository.customPetCount() < PetRepository.MAX_CUSTOM_PETS);
        importButton.setOnClickListener(view -> chooseGif());
        resourceActions.addView(importButton, weighted());
        if (petRepository.isCustom(selected)) {
            Button delete = button("删除", false);
            styleDangerOutline(delete);
            delete.setOnClickListener(view -> confirmDeleteCustom(selected));
            resourceActions.addView(delete, weightedMargins(8));
        }
        content.addView(resourceActions, matchMargins(0, 0, 0, 18));

        section(content, "外观");
        seekRow(content, "桌宠大小", 96, 280, settingsStore.sizeDp(), "dp", value -> {
            settingsStore.putInt(SettingsStore.SIZE, value);
            sendService(PetOverlayService.ACTION_REFRESH);
        });
        seekRow(content, "透明度", 20, 100, settingsStore.opacity(), "%", value -> {
            settingsStore.putInt(SettingsStore.OPACITY, value);
            sendService(PetOverlayService.ACTION_REFRESH);
        });
        addSwitch(content, "镜像显示", "反转桌宠朝向", settingsStore.mirrored(), true, checked -> {
            settingsStore.putBoolean(SettingsStore.MIRRORED, checked);
            sendService(PetOverlayService.ACTION_REFRESH);
        });

        section(content, "行为");
        addSwitch(content, "随机走动", "在屏幕范围内自动散步", settingsStore.movement(), true, checked -> {
            settingsStore.putBoolean(SettingsStore.MOVEMENT, checked);
            sendService(PetOverlayService.ACTION_REFRESH);
        });
        addSwitch(content, "随机换宠", "按设置间隔轮换桌宠", settingsStore.randomPet(), true, checked -> {
            settingsStore.putBoolean(SettingsStore.RANDOM_PET, checked);
            sendService(PetOverlayService.ACTION_REFRESH);
        });
        addChoice(content, "性格", new String[]{"活泼", "害羞", "黏人", "混乱"},
            new String[]{"lively", "shy", "clingy", "chaotic"}, settingsStore.personality(), true, value -> {
                settingsStore.putString(SettingsStore.PERSONALITY, value);
                sendService(PetOverlayService.ACTION_REFRESH);
            });
        addChoice(content, "随机换宠间隔", new String[]{"30 秒", "1 分钟", "5 分钟", "10 分钟", "30 分钟"},
            new String[]{"30", "60", "300", "600", "1800"}, String.valueOf(settingsStore.randomPetInterval()), true, value -> {
                settingsStore.putInt(SettingsStore.RANDOM_PET_INTERVAL, Integer.parseInt(value));
                sendService(PetOverlayService.ACTION_REFRESH);
            });
        return scroll(content);
    }

    private View buildInteractionPage() {
        LinearLayout content = pageContent();
        content.addView(pageHeading("互动", "气泡内容与主动陪伴设置"));
        boolean premium = licenseService.hasPremiumAccess();
        LinearLayout access = panel();
        TextView title = text(premium ? "高级互动已开启" : "基础互动模式", 17, true);
        title.setTextColor(premium ? BRAND : ACCENT);
        access.addView(title);
        TextView detail = text(premium ? trialDescription() : "正式激活或体验期间可使用词包与随机互动", 13, false);
        detail.setTextColor(MUTED);
        access.addView(detail, margins(0, 5, 0, premium ? 0 : 12));
        if (!premium) {
            Button activate = button("前往激活", false);
            activate.setOnClickListener(view -> selectTab(4));
            access.addView(activate);
        }
        content.addView(access, matchMargins(0, 0, 0, 18));

        Button interact = button("让桌宠互动一下", true);
        interact.setOnClickListener(view -> sendService(PetOverlayService.ACTION_INTERACT));
        content.addView(interact, matchMargins(0, 0, 0, 18));

        section(content, "主动互动");
        addSwitch(content, "随机互动", "按频率主动显示互动气泡", settingsStore.interactions(), premium, checked -> {
            settingsStore.putBoolean(SettingsStore.INTERACTIONS, checked);
            sendService(PetOverlayService.ACTION_REFRESH);
        });
        addChoice(content, "互动频率", new String[]{"安静 · 60–120 分钟", "标准 · 30–60 分钟", "活跃 · 10–30 分钟"},
            new String[]{"quiet", "standard", "lively"}, settingsStore.interactionMode(), premium, value -> {
                settingsStore.putString(SettingsStore.INTERACTION_MODE, value);
                sendService(PetOverlayService.ACTION_REFRESH);
            });

        section(content, "互动词包");
        List<String> packs = wordRepository.packs();
        List<String> packNames = new ArrayList<>();
        for (String pack : packs) packNames.add(wordRepository.displayName(pack));
        Spinner wordSpinner = spinner();
        wordSpinner.setAdapter(adapter(packNames));
        wordSpinner.setSelection(Math.max(0, packs.indexOf(settingsStore.wordPack())), false);
        wordSpinner.setEnabled(premium);
        wordSpinner.setAlpha(premium ? 1f : 0.45f);
        wordSpinner.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {
            @Override public void onItemSelected(AdapterView<?> parent, View view, int position, long id) {
                if (!premium || position < 0 || position >= packs.size()) return;
                String pack = packs.get(position);
                if (pack.equals(settingsStore.wordPack())) return;
                settingsStore.putString(SettingsStore.WORD_PACK, pack);
                sendService(PetOverlayService.ACTION_REFRESH);
            }
            @Override public void onNothingSelected(AdapterView<?> parent) { }
        });
        content.addView(wordSpinner, matchMargins(0, 0, 0, 22));
        return scroll(content);
    }

    private View buildCompanionPage() {
        LinearLayout content = pageContent();
        content.addView(pageHeading("搭子联机", "配对后互相发送当前桌宠"));
        if (!licenseService.isActivated()) {
            LinearLayout gate = panel();
            TextView title = text("正式激活后可用", 18, true);
            title.setTextColor(ACCENT);
            gate.addView(title);
            TextView detail = text("安卓设备独立计为一个激活设备", 13, false);
            detail.setTextColor(MUTED);
            gate.addView(detail, margins(0, 5, 0, 13));
            Button activate = button("前往激活", true);
            activate.setOnClickListener(view -> selectTab(4));
            gate.addView(activate);
            content.addView(gate);
            return scroll(content);
        }

        if (companionProfile == null) {
            LinearLayout loading = panel();
            TextView state = text(companionLoading ? "正在连接搭子服务…" : "尚未读取搭子资料", 16, true);
            state.setTextColor(TEXT);
            loading.addView(state);
            Button refresh = button("刷新资料", false);
            refresh.setEnabled(!companionLoading);
            refresh.setOnClickListener(view -> refreshCompanion(true));
            loading.addView(refresh, matchMargins(0, 12, 0, 0));
            content.addView(loading);
            if (!companionLoadAttempted && !companionLoading) pageHost.post(() -> refreshCompanion(false));
            return scroll(content);
        }

        section(content, "我的资料");
        EditText name = input("搭子昵称", companionProfile.displayName());
        name.setFilters(new InputFilter[]{new InputFilter.LengthFilter(20)});
        content.addView(name, matchMargins(0, 0, 0, 8));
        LinearLayout nameActions = horizontal();
        Button saveName = button("保存昵称", false);
        saveName.setOnClickListener(view -> runCompanionAction(() -> companionService.updateName(name.getText().toString()),
            "昵称已更新"));
        Button refresh = button("刷新", false);
        refresh.setOnClickListener(view -> refreshCompanion(true));
        nameActions.addView(saveName, weighted());
        nameActions.addView(refresh, weightedMargins(8));
        content.addView(nameActions, matchMargins(0, 0, 0, 16));

        LinearLayout codePanel = panel();
        TextView codeLabel = text("我的配对码", 13, true);
        codeLabel.setTextColor(MUTED);
        codePanel.addView(codeLabel);
        TextView code = text(companionProfile.pairingCode(), 27, true);
        code.setTextColor(BRAND_DARK);
        codePanel.addView(code, margins(0, 3, 0, 9));
        Button copy = button("复制配对码", false);
        copy.setOnClickListener(view -> copyText("配对码", companionProfile.pairingCode()));
        codePanel.addView(copy);
        content.addView(codePanel, matchMargins(0, 0, 0, 18));

        section(content, "当前搭子");
        if (companionProfile.partner() == null) {
            EditText pairCode = input("输入搭子的 8 位配对码", "");
            pairCode.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_FLAG_CAP_CHARACTERS);
            pairCode.setFilters(new InputFilter[]{new InputFilter.LengthFilter(8)});
            content.addView(pairCode, matchMargins(0, 0, 0, 9));
            Button pair = button("立即配对", true);
            pair.setOnClickListener(view -> runCompanionAction(() -> companionService.pair(pairCode.getText().toString()),
                "配对成功"));
            content.addView(pair, matchMargins(0, 0, 0, 18));
        } else {
            LinearLayout partner = panel();
            TextView partnerName = text(companionProfile.partner().displayName(), 18, true);
            partnerName.setTextColor(TEXT);
            partner.addView(partnerName);
            TextView paired = text("已连接", 13, false);
            paired.setTextColor(BRAND);
            partner.addView(paired, margins(0, 3, 0, 13));
            Button send = button("发送当前桌宠", true);
            send.setOnClickListener(view -> runSendFromApp());
            partner.addView(send, matchMargins(0, 0, 0, 8));
            Button unpair = button("解除配对", false);
            styleDangerOutline(unpair);
            unpair.setOnClickListener(view -> confirmUnpair());
            partner.addView(unpair);
            content.addView(partner, matchMargins(0, 0, 0, 18));
        }
        return scroll(content);
    }

    private View buildAccountPage() {
        LinearLayout content = pageContent();
        content.addView(pageHeading("我的", "激活、权限与应用设置"));
        section(content, "激活与体验");
        LinearLayout license = panel();
        TextView title = text(licenseTitle(), 18, true);
        title.setTextColor(licenseService.isActivated() ? BRAND : ACCENT);
        license.addView(title);
        trialCountdown = text(trialDescription(), 13, false);
        trialCountdown.setTextColor(MUTED);
        license.addView(trialCountdown, margins(0, 5, 0, 12));
        TextView identity = text("安卓设备 ID · " + lastEight(licenseService.installationId()), 12, false);
        identity.setTextColor(MUTED);
        license.addView(identity);
        content.addView(license, matchMargins(0, 0, 0, 12));

        if (!licenseService.isActivated()) {
            EditText activationCode = input("输入 6 位激活码", "");
            activationCode.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_FLAG_CAP_CHARACTERS);
            activationCode.setFilters(new InputFilter[]{new InputFilter.LengthFilter(8)});
            content.addView(activationCode, matchMargins(0, 0, 0, 8));
            Button activate = button("激活此安卓设备", true);
            activate.setOnClickListener(view -> activate(activationCode.getText().toString()));
            content.addView(activate, matchMargins(0, 0, 0, 8));
            Button trial = button("检查体验时间", false);
            trial.setOnClickListener(view -> checkTrial(true));
            content.addView(trial, matchMargins(0, 0, 0, 18));
        }

        section(content, "系统权限");
        addActionRow(content, "悬浮窗权限", Settings.canDrawOverlays(this) ? "已允许" : "需要授权", view ->
            requestOverlayPermission(false));
        if (Build.VERSION.SDK_INT >= 33) {
            boolean notifications = checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED;
            addActionRow(content, "通知权限", notifications ? "已允许" : "需要授权", view -> requestNotificationPermission());
        }
        addSwitch(content, "开机后自动恢复", "启动后台服务并显示桌宠", settingsStore.startOnBoot(), true,
            checked -> settingsStore.putBoolean(SettingsStore.START_ON_BOOT, checked));

        section(content, "应用");
        addActionRow(content, "当前版本", "Android v" + NetworkClient.appVersion(this), null);
        addActionRow(content, "应用系统设置", "通知、电池与权限", view -> openAppSettings());
        TextView footer = text("桌搭子 Android · 不含小剧场", 12, false);
        footer.setTextColor(MUTED);
        footer.setGravity(Gravity.CENTER);
        content.addView(footer, margins(0, 24, 0, 10));
        return scroll(content);
    }

    private void startPet() {
        if (!Settings.canDrawOverlays(this)) {
            requestOverlayPermission(true);
            return;
        }
        requestNotificationPermission();
        sendService(PetOverlayService.ACTION_START);
    }

    private void sendService(String action) {
        Intent intent = new Intent(this, PetOverlayService.class).setAction(action);
        try {
            if (PetOverlayService.ACTION_START.equals(action)) startForegroundService(intent);
            else if (settingsStore.running()) startService(intent);
        } catch (RuntimeException error) {
            Toast.makeText(this, "操作失败：" + safeMessage(error), Toast.LENGTH_LONG).show();
        }
        handler.postDelayed(this::renderCurrentPage, 220L);
    }

    private void requestOverlayPermission(boolean startAfter) {
        startAfterPermission = startAfter;
        Intent intent = new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION,
            Uri.parse("package:" + getPackageName()));
        startActivityForResult(intent, REQUEST_OVERLAY);
    }

    private void requestNotificationPermission() {
        if (Build.VERSION.SDK_INT >= 33
            && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, REQUEST_NOTIFICATIONS);
        }
    }

    private void openAppSettings() {
        Intent intent = new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS,
            Uri.parse("package:" + getPackageName()));
        startActivity(intent);
    }

    private void chooseGif() {
        if (!licenseService.isActivated()) {
            Toast.makeText(this, "正式激活后才能导入自己的桌宠", Toast.LENGTH_LONG).show();
            return;
        }
        if (petRepository.nextImportFile() == null) {
            Toast.makeText(this, "最多可以导入 3 个自定义 GIF", Toast.LENGTH_LONG).show();
            return;
        }
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        intent.addCategory(Intent.CATEGORY_OPENABLE);
        intent.setType("image/gif");
        startActivityForResult(intent, REQUEST_GIF);
    }

    @Override protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode == REQUEST_OVERLAY) {
            renderCurrentPage();
            return;
        }
        if (requestCode != REQUEST_GIF || resultCode != RESULT_OK || data == null || data.getData() == null) return;
        File pending = new File(getCacheDir(), "imported-pet.pending.gif");
        try {
            File destination = petRepository.nextImportFile();
            if (destination == null) throw new IllegalStateException("最多可以导入 3 个自定义 GIF");
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
                    if (copied > PetRepository.MAX_GIF_BYTES) {
                        throw new IllegalArgumentException("GIF 不能超过 8 MB");
                    }
                    output.write(buffer, 0, count);
                }
            }
            PetRepository.validateGifFile(pending);
            Drawable decoded = ImageDecoder.decodeDrawable(ImageDecoder.createSource(pending));
            if (decoded instanceof AnimatedImageDrawable animated) animated.stop();
            Files.move(pending.toPath(), destination.toPath(), StandardCopyOption.REPLACE_EXISTING);
            settingsStore.putString(SettingsStore.ACTIVE_PET, petRepository.idForFile(destination));
            settingsStore.putBoolean(SettingsStore.RANDOM_PET, false);
            sendService(PetOverlayService.ACTION_REFRESH);
            renderCurrentPage();
            Toast.makeText(this, "GIF 已导入", Toast.LENGTH_SHORT).show();
        } catch (Exception error) {
            pending.delete();
            Toast.makeText(this, "导入失败：" + safeMessage(error), Toast.LENGTH_LONG).show();
        }
    }

    private void confirmDeleteCustom(String petId) {
        new AlertDialog.Builder(this).setTitle("删除自定义桌宠")
            .setMessage("将从这台安卓设备删除该 GIF。")
            .setNegativeButton("取消", null)
            .setPositiveButton("删除", (dialog, which) -> {
                if (petRepository.deleteCustom(petId)) {
                    settingsStore.putString(SettingsStore.ACTIVE_PET, "");
                    sendService(PetOverlayService.ACTION_REFRESH);
                    renderCurrentPage();
                }
            }).show();
    }

    private void activate(String code) {
        runNetwork(() -> {
            licenseService.activate(code);
            return true;
        }, ignored -> {
            companionProfile = null;
            companionLoadAttempted = false;
            sendService(PetOverlayService.ACTION_REFRESH);
            renderCurrentPage();
            Toast.makeText(this, "此安卓设备已激活", Toast.LENGTH_LONG).show();
        });
    }

    private void checkTrial(boolean announce) {
        runNetwork(licenseService::checkTrial, status -> {
            sendService(PetOverlayService.ACTION_REFRESH);
            renderCurrentPage();
            if (announce) {
                String message = status.allowed() ? "体验剩余 " + status.remainingSeconds() + " 秒" : "体验已结束，基础陪伴仍可使用";
                Toast.makeText(this, message, Toast.LENGTH_LONG).show();
            }
        }, error -> {
            if (announce) Toast.makeText(this, safeMessage(error), Toast.LENGTH_LONG).show();
        });
    }

    private void refreshCompanion(boolean announce) {
        if (companionLoading) return;
        companionLoading = true;
        companionLoadAttempted = true;
        renderCurrentPage();
        runNetwork(companionService::refreshProfile, profile -> {
            companionLoading = false;
            companionProfile = profile;
            renderCurrentPage();
            if (announce) Toast.makeText(this, "搭子资料已刷新", Toast.LENGTH_SHORT).show();
        }, error -> {
            companionLoading = false;
            renderCurrentPage();
            Toast.makeText(this, "连接失败：" + safeMessage(error), Toast.LENGTH_LONG).show();
        });
    }

    private void runCompanionAction(ThrowingSupplier<CompanionService.Profile> operation, String successMessage) {
        runNetwork(operation, profile -> {
            companionProfile = profile;
            renderCurrentPage();
            Toast.makeText(this, successMessage, Toast.LENGTH_SHORT).show();
        });
    }

    private void runSendFromApp() {
        runNetwork(companionService::sendCurrentGif, recipient ->
            Toast.makeText(this, "已发送给 " + recipient, Toast.LENGTH_LONG).show());
    }

    private void confirmUnpair() {
        new AlertDialog.Builder(this).setTitle("解除搭子配对")
            .setMessage("解除后双方需要重新输入配对码才能互发桌宠。")
            .setNegativeButton("取消", null)
            .setPositiveButton("解除", (dialog, which) -> runCompanionAction(companionService::unpair, "已解除配对"))
            .show();
    }

    private <T> void runNetwork(ThrowingSupplier<T> operation, Success<T> success) {
        runNetwork(operation, success, error ->
            Toast.makeText(this, safeMessage(error), Toast.LENGTH_LONG).show());
    }

    private <T> void runNetwork(ThrowingSupplier<T> operation, Success<T> success, Failure failure) {
        networkExecutor.execute(() -> {
            try {
                T result = operation.get();
                runOnUiThread(() -> success.accept(result));
            } catch (Exception error) {
                runOnUiThread(() -> failure.accept(error));
            }
        });
    }

    private void copyText(String label, String value) {
        ClipboardManager clipboard = (ClipboardManager) getSystemService(CLIPBOARD_SERVICE);
        clipboard.setPrimaryClip(ClipData.newPlainText(label, value));
        Toast.makeText(this, label + "已复制", Toast.LENGTH_SHORT).show();
    }

    private String petStatusTitle() {
        if (!Settings.canDrawOverlays(this)) return "等待悬浮窗权限";
        if (!settingsStore.running()) return "桌宠已停止";
        if (settingsStore.petHidden()) return "后台接收中";
        if (settingsStore.clickThrough()) return "桌宠正在穿透";
        return "桌宠正在陪伴你";
    }

    private String petStatusDetail() {
        if (!Settings.canDrawOverlays(this)) return "授权后才能显示在其他应用上层";
        if (!settingsStore.running()) return "启动后可从通知栏随时控制";
        if (settingsStore.petHidden()) return "悬浮形象已隐藏，搭子消息仍会通知";
        if (settingsStore.clickThrough()) return "触摸操作已传递给下层应用";
        return "点桌宠打开互动、发送、换宠、穿透和隐藏菜单";
    }

    private String licenseTitle() {
        if (licenseService.isActivated()) return "安卓设备已激活 · " + licenseService.licenseSuffix();
        if (licenseService.hasPremiumAccess()) return "完整功能体验中";
        return "免费基础模式";
    }

    private String trialDescription() {
        if (licenseService.isActivated()) return "高级互动与搭子联机均已开放";
        long seconds = licenseService.trialRemainingSeconds();
        if (seconds > 0) return String.format(Locale.CHINA, "完整功能体验剩余 %d:%02d · 搭子联机需正式激活", seconds / 60, seconds % 60);
        return "基础桌宠、拖动、走动和最多 3 个自定义 GIF 可继续免费使用";
    }

    private void updateLicenseHeader() {
        if (licenseBadge == null || licenseService == null) return;
        String text;
        int color;
        int background;
        if (licenseService.isActivated()) {
            text = "已激活";
            color = BRAND_DARK;
            background = 0xffdff1eb;
        } else if (licenseService.hasPremiumAccess()) {
            text = "体验中";
            color = 0xff7c4b1f;
            background = 0xffffead7;
        } else {
            text = "免费版";
            color = MUTED;
            background = 0xffe9eeec;
        }
        licenseBadge.setText(text);
        licenseBadge.setTextColor(color);
        licenseBadge.setBackground(rounded(background, 8, 0, 0));
    }

    private void updateNavigation() {
        for (int index = 0; index < navLabels.size(); index++) {
            boolean selected = index == currentTab;
            navLabels.get(index).setTextColor(selected ? BRAND_DARK : MUTED);
            navLabels.get(index).setTypeface(Typeface.DEFAULT, selected ? Typeface.BOLD : Typeface.NORMAL);
            navIndicators.get(index).setBackgroundColor(selected ? BRAND : Color.TRANSPARENT);
        }
    }

    private LinearLayout pageContent() {
        LinearLayout content = vertical();
        content.setPadding(dp(18), dp(12), dp(18), dp(32));
        return content;
    }

    private ScrollView scroll(LinearLayout content) {
        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        scroll.setBackgroundColor(PAGE);
        scroll.addView(content, new ScrollView.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        return scroll;
    }

    private View pageHeading(String title, String subtitle) {
        LinearLayout block = vertical();
        TextView heading = text(title, 25, true);
        heading.setTextColor(TEXT);
        block.addView(heading);
        TextView detail = text(subtitle, 13, false);
        detail.setTextColor(MUTED);
        block.addView(detail, margins(0, 3, 0, 0));
        block.setLayoutParams(margins(0, 0, 0, 20));
        return block;
    }

    private LinearLayout panel() {
        LinearLayout panel = vertical();
        panel.setPadding(dp(16), dp(15), dp(16), dp(15));
        panel.setBackground(rounded(SURFACE, 8, LINE, 1));
        return panel;
    }

    private void section(LinearLayout parent, String title) {
        LinearLayout heading = horizontal();
        TextView label = text(title, 16, true);
        label.setTextColor(TEXT);
        heading.addView(label, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(35)));
        View line = new View(this);
        line.setBackgroundColor(LINE);
        heading.addView(line, new LinearLayout.LayoutParams(0, dp(1), 1f));
        parent.addView(heading, matchMargins(0, 2, 0, 9));
    }

    private void addActionRow(LinearLayout parent, String title, String value, View.OnClickListener listener) {
        LinearLayout row = horizontal();
        row.setPadding(dp(2), dp(5), dp(2), dp(5));
        LinearLayout labels = vertical();
        TextView heading = text(title, 15, true);
        heading.setTextColor(TEXT);
        TextView detail = text(value, 13, false);
        detail.setTextColor(MUTED);
        labels.addView(heading);
        labels.addView(detail);
        row.addView(labels, new LinearLayout.LayoutParams(0, dp(54), 1f));
        if (listener != null) {
            TextView arrow = text("›", 25, false);
            arrow.setTextColor(MUTED);
            arrow.setGravity(Gravity.CENTER);
            row.addView(arrow, new LinearLayout.LayoutParams(dp(30), dp(54)));
            row.setClickable(true);
            row.setFocusable(true);
            row.setOnClickListener(listener);
        }
        parent.addView(row, matchMargins(0, 0, 0, 4));
    }

    private void seekRow(LinearLayout parent, String title, int minimum, int maximum, int current,
                         String suffix, IntConsumer consumer) {
        LinearLayout heading = horizontal();
        TextView name = text(title, 15, true);
        name.setTextColor(TEXT);
        TextView value = text(current + suffix, 14, false);
        value.setTextColor(BRAND);
        value.setGravity(Gravity.END | Gravity.CENTER_VERTICAL);
        heading.addView(name, weighted());
        heading.addView(value, weighted());
        parent.addView(heading, matchMargins(0, 4, 0, 0));
        SeekBar seek = new SeekBar(this);
        seek.setMax(maximum - minimum);
        seek.setProgress(current - minimum);
        seek.setProgressTintList(ColorStateList.valueOf(BRAND));
        seek.setThumbTintList(ColorStateList.valueOf(BRAND));
        seek.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener() {
            @Override public void onProgressChanged(SeekBar bar, int progress, boolean fromUser) {
                value.setText((minimum + progress) + suffix);
            }
            @Override public void onStartTrackingTouch(SeekBar bar) { }
            @Override public void onStopTrackingTouch(SeekBar bar) { consumer.accept(minimum + bar.getProgress()); }
        });
        parent.addView(seek, matchMargins(0, 0, 0, 8));
    }

    private void addSwitch(LinearLayout parent, String title, String subtitle, boolean checked, boolean enabled,
                           BooleanConsumer consumer) {
        LinearLayout row = horizontal();
        row.setPadding(dp(2), dp(5), dp(2), dp(5));
        LinearLayout labels = vertical();
        TextView heading = text(title, 15, true);
        heading.setTextColor(enabled ? TEXT : 0xff929c99);
        TextView detail = text(subtitle, 13, false);
        detail.setTextColor(enabled ? MUTED : 0xffa7afad);
        labels.addView(heading);
        labels.addView(detail);
        row.addView(labels, new LinearLayout.LayoutParams(0, dp(58), 1f));
        Switch control = new Switch(this);
        control.setChecked(checked);
        control.setEnabled(enabled);
        control.setThumbTintList(ColorStateList.valueOf(enabled ? BRAND : 0xffa7afad));
        control.setTrackTintList(ColorStateList.valueOf(enabled ? 0xffacd5ca : 0xffd7dddb));
        control.setOnCheckedChangeListener((button, value) -> consumer.accept(value));
        row.addView(control, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(58)));
        parent.addView(row, matchMargins(0, 0, 0, 4));
    }

    private void addChoice(LinearLayout parent, String title, String[] labels, String[] values,
                           String current, boolean enabled, StringConsumer consumer) {
        TextView label = text(title, 14, true);
        label.setTextColor(enabled ? TEXT : 0xff929c99);
        parent.addView(label);
        Spinner spinner = spinner();
        spinner.setAdapter(adapter(Arrays.asList(labels)));
        int selected = 0;
        for (int index = 0; index < values.length; index++) if (values[index].equals(current)) selected = index;
        spinner.setSelection(selected, false);
        spinner.setEnabled(enabled);
        spinner.setAlpha(enabled ? 1f : 0.45f);
        spinner.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {
            @Override public void onItemSelected(AdapterView<?> parent, View view, int position, long id) {
                if (enabled && !values[position].equals(current)) consumer.accept(values[position]);
            }
            @Override public void onNothingSelected(AdapterView<?> parent) { }
        });
        parent.addView(spinner, matchMargins(0, 3, 0, 12));
    }

    private Button button(String label, boolean primary) {
        Button button = new Button(this);
        button.setText(label);
        button.setTextSize(14);
        button.setAllCaps(false);
        button.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        button.setTextColor(primary ? Color.WHITE : BRAND_DARK);
        button.setMinHeight(0);
        button.setMinimumHeight(0);
        button.setPadding(dp(12), 0, dp(12), 0);
        button.setBackground(rounded(primary ? BRAND : 0xffe7f1ee, 7, primary ? 0 : 0xffb8d4cc, primary ? 0 : 1));
        button.setStateListAnimator(null);
        button.setLayoutParams(new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(46)));
        return button;
    }

    private void styleDangerOutline(Button button) {
        button.setTextColor(DANGER);
        button.setBackground(rounded(0xfffff6f3, 7, 0xffdfb6ad, 1));
    }

    private Spinner spinner() {
        Spinner spinner = new Spinner(this);
        spinner.setBackgroundTintList(ColorStateList.valueOf(BRAND));
        spinner.setMinimumHeight(dp(48));
        return spinner;
    }

    private EditText input(String hint, String value) {
        EditText input = new EditText(this);
        input.setHint(hint);
        input.setText(value);
        input.setTextSize(15);
        input.setTextColor(TEXT);
        input.setHintTextColor(0xff8d9995);
        input.setSingleLine(true);
        input.setPadding(dp(12), 0, dp(12), 0);
        input.setBackground(rounded(SURFACE, 7, LINE, 1));
        return input;
    }

    private ArrayAdapter<String> adapter(List<String> values) {
        ArrayAdapter<String> adapter = new ArrayAdapter<>(this, android.R.layout.simple_spinner_item, values);
        adapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        return adapter;
    }

    private GradientDrawable rounded(int color, int radiusDp, int strokeColor, int strokeDp) {
        GradientDrawable background = new GradientDrawable();
        background.setColor(color);
        background.setCornerRadius(dp(radiusDp));
        if (strokeDp > 0) background.setStroke(dp(strokeDp), strokeColor);
        return background;
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
        text.setLetterSpacing(0f);
        text.setTypeface(Typeface.DEFAULT, bold ? Typeface.BOLD : Typeface.NORMAL);
        return text;
    }

    private LinearLayout.LayoutParams weighted() {
        return new LinearLayout.LayoutParams(0, dp(46), 1f);
    }

    private LinearLayout.LayoutParams weightedMargins(int leftDp) {
        LinearLayout.LayoutParams params = weighted();
        params.leftMargin = dp(leftDp);
        return params;
    }

    private LinearLayout.LayoutParams matchMargins(int left, int top, int right, int bottom) {
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        params.setMargins(dp(left), dp(top), dp(right), dp(bottom));
        return params;
    }

    private LinearLayout.LayoutParams margins(int left, int top, int right, int bottom) {
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        params.setMargins(dp(left), dp(top), dp(right), dp(bottom));
        return params;
    }

    private int dp(int value) { return Math.round(value * getResources().getDisplayMetrics().density); }

    private static String lastEight(String value) {
        return value == null || value.length() <= 8 ? value : value.substring(value.length() - 8);
    }

    private static String safeMessage(Exception error) {
        String message = error.getMessage();
        return message == null || message.trim().isEmpty() ? "网络服务暂时不可用" : message;
    }

    private interface IntConsumer { void accept(int value); }
    private interface BooleanConsumer { void accept(boolean value); }
    private interface StringConsumer { void accept(String value); }
    private interface ThrowingSupplier<T> { T get() throws Exception; }
    private interface Success<T> { void accept(T value); }
    private interface Failure { void accept(Exception error); }
}
