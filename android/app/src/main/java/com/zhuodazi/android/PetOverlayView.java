package com.zhuodazi.android;

import android.content.Context;
import android.graphics.Color;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.Drawable;
import android.text.TextUtils;
import android.view.Gravity;
import android.view.View;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TextView;

import java.util.List;

final class PetOverlayView extends FrameLayout {
    interface MenuListener { void onAction(String action); }
    interface InteractionListener { void onChoice(String value); }

    static final class InteractionChoice {
        final String label;
        final String value;
        final boolean primary;

        InteractionChoice(String label, String value) { this(label, value, false); }

        InteractionChoice(String label, String value, boolean primary) {
            this.label = label;
            this.value = value;
            this.primary = primary;
        }
    }

    static final String MENU_INTERACT = "interact";
    static final String MENU_SEND = "send";
    static final String MENU_NEXT = "next";
    static final String MENU_CLICK_THROUGH = "click_through";
    static final String MENU_HIDE = "hide";

    private final ImageView petImage;
    private final TextView bubble;
    private final LinearLayout quickMenu;
    private final LinearLayout interactionCard;
    private final TextView interactionTitle;
    private final TextView interactionMessage;
    private final LinearLayout interactionChoices;
    private final int bubbleHeight;
    private MenuListener menuListener;
    private InteractionListener interactionListener;

    PetOverlayView(Context context, int petSize, int windowWidth, int windowHeight) {
        super(context);
        setClipChildren(false);
        setClipToPadding(false);

        bubbleHeight = dp(76);
        bubble = new TextView(context);
        bubble.setTextColor(Color.rgb(23, 32, 30));
        bubble.setTextSize(13);
        bubble.setGravity(Gravity.START | Gravity.CENTER_VERTICAL);
        bubble.setMaxLines(4);
        bubble.setEllipsize(TextUtils.TruncateAt.END);
        bubble.setPadding(dp(12), dp(8), dp(12), dp(8));
        bubble.setElevation(dp(5));
        GradientDrawable bubbleBackground = new GradientDrawable();
        bubbleBackground.setColor(0xf7ffffff);
        bubbleBackground.setCornerRadius(dp(10));
        bubbleBackground.setStroke(dp(1), 0x22000000);
        bubble.setBackground(bubbleBackground);
        bubble.setVisibility(View.INVISIBLE);
        LayoutParams bubbleParams = new LayoutParams(
            Math.min(windowWidth - dp(10), dp(144)), LayoutParams.WRAP_CONTENT,
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
        firstRow.addView(menuAction("互动一下", MENU_INTERACT));
        firstRow.addView(menuAction("换一只", MENU_NEXT));
        quickMenu.addView(firstRow, new LinearLayout.LayoutParams(LayoutParams.MATCH_PARENT, dp(34)));
        LinearLayout secondRow = menuRow();
        secondRow.addView(menuAction("发给搭子", MENU_SEND));
        secondRow.addView(menuAction("触摸穿透", MENU_CLICK_THROUGH));
        quickMenu.addView(secondRow, new LinearLayout.LayoutParams(LayoutParams.MATCH_PARENT, dp(34)));
        LinearLayout thirdRow = menuRow();
        thirdRow.addView(menuAction("隐藏桌宠", MENU_HIDE));
        quickMenu.addView(thirdRow, new LinearLayout.LayoutParams(LayoutParams.MATCH_PARENT, dp(34)));
        quickMenu.setVisibility(View.GONE);
        LayoutParams menuParams = new LayoutParams(Math.min(windowWidth - dp(8), dp(144)), dp(112),
            Gravity.CENTER);
        addView(quickMenu, menuParams);

        interactionCard = new LinearLayout(context);
        interactionCard.setOrientation(LinearLayout.VERTICAL);
        interactionCard.setPadding(dp(12), dp(10), dp(12), dp(10));
        interactionCard.setElevation(dp(7));
        interactionCard.setClickable(true);
        GradientDrawable interactionBackground = new GradientDrawable();
        interactionBackground.setColor(0xfcffffff);
        interactionBackground.setCornerRadius(dp(8));
        interactionBackground.setStroke(dp(1), 0x24000000);
        interactionCard.setBackground(interactionBackground);

        LinearLayout titleRow = new LinearLayout(context);
        titleRow.setOrientation(LinearLayout.HORIZONTAL);
        titleRow.setGravity(Gravity.CENTER_VERTICAL);
        interactionTitle = new TextView(context);
        interactionTitle.setTextColor(Color.rgb(12, 95, 82));
        interactionTitle.setTextSize(13);
        interactionTitle.setTypeface(interactionTitle.getTypeface(), android.graphics.Typeface.BOLD);
        titleRow.addView(interactionTitle, new LinearLayout.LayoutParams(0, dp(30), 1f));
        TextView close = new TextView(context);
        close.setText("×");
        close.setContentDescription("关闭互动");
        close.setTextColor(Color.rgb(93, 107, 103));
        close.setTextSize(22);
        close.setGravity(Gravity.CENTER);
        close.setClickable(true);
        close.setOnClickListener(view -> completeInteraction(null));
        titleRow.addView(close, new LinearLayout.LayoutParams(dp(34), dp(30)));
        interactionCard.addView(titleRow, new LinearLayout.LayoutParams(
            LayoutParams.MATCH_PARENT, LayoutParams.WRAP_CONTENT));

        interactionMessage = new TextView(context);
        interactionMessage.setTextColor(Color.rgb(23, 32, 30));
        interactionMessage.setTextSize(13);
        interactionMessage.setGravity(Gravity.START);
        interactionMessage.setLineSpacing(0, 1.12f);
        interactionMessage.setMaxLines(6);
        interactionMessage.setEllipsize(TextUtils.TruncateAt.END);
        interactionMessage.setPadding(0, dp(4), 0, dp(8));
        interactionCard.addView(interactionMessage, new LinearLayout.LayoutParams(
            LayoutParams.MATCH_PARENT, LayoutParams.WRAP_CONTENT));

        interactionChoices = new LinearLayout(context);
        interactionChoices.setOrientation(LinearLayout.VERTICAL);
        interactionCard.addView(interactionChoices, new LinearLayout.LayoutParams(
            LayoutParams.MATCH_PARENT, LayoutParams.WRAP_CONTENT));
        interactionCard.setVisibility(View.GONE);
        LayoutParams interactionParams = new LayoutParams(
            LayoutParams.MATCH_PARENT, LayoutParams.WRAP_CONTENT, Gravity.TOP | Gravity.CENTER_HORIZONTAL);
        interactionParams.setMargins(dp(6), dp(4), dp(6), 0);
        addView(interactionCard, interactionParams);

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
        bubble.bringToFront();
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

    void showInteraction(String title, String message, List<InteractionChoice> choices,
                         InteractionListener listener) {
        quickMenu.setVisibility(View.GONE);
        bubble.setVisibility(View.INVISIBLE);
        interactionListener = listener;
        interactionTitle.setText(title);
        interactionMessage.setText(message);
        interactionChoices.removeAllViews();
        for (InteractionChoice choice : choices) {
            TextView button = new TextView(getContext());
            button.setText(choice.label);
            button.setTextSize(12);
            button.setGravity(Gravity.CENTER);
            button.setClickable(true);
            button.setFocusable(true);
            button.setTextColor(choice.primary ? Color.WHITE : Color.rgb(23, 32, 30));
            GradientDrawable background = new GradientDrawable();
            background.setColor(choice.primary ? 0xff147d6b : 0xffedf5f2);
            background.setCornerRadius(dp(6));
            background.setStroke(dp(1), choice.primary ? 0xff147d6b : 0x28167d6c);
            button.setBackground(background);
            LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(
                LayoutParams.MATCH_PARENT, dp(38));
            params.bottomMargin = dp(6);
            button.setLayoutParams(params);
            button.setOnClickListener(view -> completeInteraction(choice.value));
            interactionChoices.addView(button);
        }
        interactionCard.bringToFront();
        interactionCard.setVisibility(View.VISIBLE);
    }

    void hideInteraction() {
        interactionListener = null;
        interactionChoices.removeAllViews();
        interactionCard.setVisibility(View.GONE);
    }

    boolean isInteractionVisible() { return interactionCard.getVisibility() == View.VISIBLE; }

    int interactionCardHeight() { return interactionCard.getMeasuredHeight(); }

    void toggleQuickMenu() {
        if (isInteractionVisible()) return;
        boolean show = quickMenu.getVisibility() != View.VISIBLE;
        bubble.setVisibility(View.INVISIBLE);
        if (show) quickMenu.bringToFront();
        quickMenu.setVisibility(show ? View.VISIBLE : View.GONE);
    }

    void hideQuickMenu() { quickMenu.setVisibility(View.GONE); }

    private void completeInteraction(String value) {
        InteractionListener listener = interactionListener;
        hideInteraction();
        if (listener != null) listener.onChoice(value);
    }

    private LinearLayout menuRow() {
        LinearLayout row = new LinearLayout(getContext());
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setGravity(Gravity.CENTER_VERTICAL);
        return row;
    }

    private TextView menuAction(String label, String action) {
        TextView button = new TextView(getContext());
        button.setText(label);
        button.setTextSize(11);
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
