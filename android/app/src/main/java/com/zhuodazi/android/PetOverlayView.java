package com.zhuodazi.android;

import android.content.Context;
import android.graphics.Color;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.Drawable;
import android.view.Gravity;
import android.view.View;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TextView;

final class PetOverlayView extends FrameLayout {
    interface MenuListener { void onAction(String action); }

    static final String MENU_INTERACT = "interact";
    static final String MENU_SEND = "send";
    static final String MENU_NEXT = "next";
    static final String MENU_CLICK_THROUGH = "click_through";
    static final String MENU_HIDE = "hide";

    private final ImageView petImage;
    private final TextView bubble;
    private final LinearLayout quickMenu;
    private final int bubbleHeight;
    private MenuListener menuListener;

    PetOverlayView(Context context, int petSize, int windowWidth, int windowHeight) {
        super(context);
        setClipChildren(false);
        setClipToPadding(false);

        bubbleHeight = dp(88);
        bubble = new TextView(context);
        bubble.setTextColor(Color.rgb(23, 32, 30));
        bubble.setTextSize(14);
        bubble.setGravity(Gravity.CENTER);
        bubble.setMaxLines(3);
        bubble.setPadding(dp(14), dp(8), dp(14), dp(8));
        bubble.setElevation(dp(5));
        GradientDrawable bubbleBackground = new GradientDrawable();
        bubbleBackground.setColor(0xf7ffffff);
        bubbleBackground.setCornerRadius(dp(10));
        bubbleBackground.setStroke(dp(1), 0x22000000);
        bubble.setBackground(bubbleBackground);
        bubble.setVisibility(View.INVISIBLE);
        LayoutParams bubbleParams = new LayoutParams(
            Math.min(windowWidth - dp(8), dp(280)), LayoutParams.WRAP_CONTENT,
            Gravity.TOP | Gravity.CENTER_HORIZONTAL);
        bubbleParams.topMargin = dp(4);
        addView(bubble, bubbleParams);

        quickMenu = new LinearLayout(context);
        quickMenu.setOrientation(LinearLayout.VERTICAL);
        quickMenu.setPadding(dp(5), dp(5), dp(5), dp(5));
        quickMenu.setElevation(dp(6));
        GradientDrawable menuBackground = new GradientDrawable();
        menuBackground.setColor(0xfaffffff);
        menuBackground.setCornerRadius(dp(8));
        menuBackground.setStroke(dp(1), 0x22000000);
        quickMenu.setBackground(menuBackground);
        LinearLayout firstRow = menuRow();
        firstRow.addView(menuAction("互动", MENU_INTERACT), weightedAction());
        firstRow.addView(menuAction("发给搭子", MENU_SEND), weightedAction());
        firstRow.addView(menuAction("换一只", MENU_NEXT), weightedAction());
        quickMenu.addView(firstRow, new LinearLayout.LayoutParams(LayoutParams.MATCH_PARENT, dp(34)));
        LinearLayout secondRow = menuRow();
        secondRow.addView(menuAction("开启穿透", MENU_CLICK_THROUGH), weightedAction());
        secondRow.addView(menuAction("隐藏桌宠", MENU_HIDE), weightedAction());
        quickMenu.addView(secondRow, new LinearLayout.LayoutParams(LayoutParams.MATCH_PARENT, dp(34)));
        quickMenu.setVisibility(View.GONE);
        LayoutParams menuParams = new LayoutParams(Math.min(windowWidth - dp(8), dp(292)), dp(80),
            Gravity.TOP | Gravity.CENTER_HORIZONTAL);
        menuParams.topMargin = dp(4);
        addView(quickMenu, menuParams);

        petImage = new ImageView(context);
        petImage.setScaleType(ImageView.ScaleType.FIT_CENTER);
        LayoutParams imageParams = new LayoutParams(petSize, petSize, Gravity.BOTTOM | Gravity.CENTER_HORIZONTAL);
        addView(petImage, imageParams);
    }

    void setPet(Drawable drawable, float opacity, boolean mirrored, int direction) {
        petImage.setImageDrawable(drawable);
        petImage.setAlpha(opacity);
        face(direction, mirrored);
    }

    void face(int direction, boolean mirrored) {
        int sign = direction == 0 ? 1 : direction;
        petImage.setScaleX(sign * (mirrored ? -1f : 1f));
    }

    void say(String message) {
        quickMenu.setVisibility(View.GONE);
        bubble.setText(message);
        bubble.setVisibility(View.VISIBLE);
        bubble.animate().cancel();
        bubble.setAlpha(0f);
        bubble.setTranslationY(dp(5));
        bubble.animate().alpha(1f).translationY(0f).setDuration(160).start();
    }

    void hideBubble() {
        bubble.animate().alpha(0f).setDuration(150).withEndAction(() -> bubble.setVisibility(View.INVISIBLE)).start();
    }

    void setMenuListener(MenuListener listener) { menuListener = listener; }

    void toggleQuickMenu() {
        boolean show = quickMenu.getVisibility() != View.VISIBLE;
        bubble.setVisibility(View.INVISIBLE);
        quickMenu.setVisibility(show ? View.VISIBLE : View.GONE);
    }

    void hideQuickMenu() { quickMenu.setVisibility(View.GONE); }

    private LinearLayout menuRow() {
        LinearLayout row = new LinearLayout(getContext());
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setGravity(Gravity.CENTER_VERTICAL);
        return row;
    }

    private TextView menuAction(String label, String action) {
        TextView button = new TextView(getContext());
        button.setText(label);
        button.setTextSize(12);
        button.setTextColor(Color.rgb(23, 32, 30));
        button.setGravity(Gravity.CENTER);
        button.setClickable(true);
        button.setFocusable(true);
        GradientDrawable background = new GradientDrawable();
        background.setColor(0xffedf5f2);
        background.setCornerRadius(dp(5));
        background.setStroke(dp(1), 0x18167d6c);
        button.setBackground(background);
        LinearLayout.LayoutParams params = weightedAction();
        params.setMargins(dp(2), dp(2), dp(2), dp(2));
        button.setLayoutParams(params);
        button.setOnClickListener(view -> {
            quickMenu.setVisibility(View.GONE);
            if (menuListener != null) menuListener.onAction(action);
        });
        return button;
    }

    private LinearLayout.LayoutParams weightedAction() {
        return new LinearLayout.LayoutParams(0, LayoutParams.MATCH_PARENT, 1f);
    }

    int bubbleHeight() { return bubbleHeight; }

    private int dp(int value) {
        return Math.round(value * getResources().getDisplayMetrics().density);
    }
}
