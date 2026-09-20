package com.zhuodazi.android;

import android.content.Context;
import android.graphics.Color;
import android.graphics.Rect;
import java.util.ArrayList;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.Drawable;
import android.text.TextUtils;
import android.text.StaticLayout;
import android.text.Layout;
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
    static final String MENU_THEATER = "theater";
    static final String MENU_SEND = "send";
    static final String MENU_GIRLFRIEND_VISIT = "girlfriend_visit";
    static final String MENU_FRIEND_VISIT = "friend_visit";
    static final String MENU_COMPANION_VISIT = "companion_visit";
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
    private int bubbleAnimationGeneration;
    private int interactionGeneration;

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
            Math.min(Math.max(windowWidth - dp(12), dp(96)), dp(168)), LayoutParams.WRAP_CONTENT,
            Gravity.TOP | Gravity.CENTER_HORIZONTAL);
        bubbleParams.topMargin = dp(4);
        addView(bubble, bubbleParams);

        quickMenu = new LinearLayout(context);
        quickMenu.setOrientation(LinearLayout.VERTICAL);
        quickMenu.setPadding(dp(8), dp(8), dp(8), dp(4));
        quickMenu.setElevation(dp(8));
        quickMenu.setClickable(true);
        GradientDrawable menuBackground = new GradientDrawable();
        menuBackground.setColor(0xfaffffff);
        menuBackground.setCornerRadius(dp(12));
        menuBackground.setStroke(dp(1), 0x22000000);
        quickMenu.setBackground(menuBackground);
        LinearLayout firstRow = menuRow();
        firstRow.addView(menuAction("互动一下", MENU_INTERACT));
        firstRow.addView(menuAction("小剧场", MENU_THEATER));
        quickMenu.addView(firstRow, menuRowParams());
        LinearLayout secondRow = menuRow();
        secondRow.addView(menuAction("换一只", MENU_NEXT));
        secondRow.addView(menuAction("发给搭子", MENU_SEND));
        quickMenu.addView(secondRow, menuRowParams());
        LinearLayout thirdRow = menuRow();
        thirdRow.addView(menuAction("触摸穿透", MENU_CLICK_THROUGH));
        thirdRow.addView(menuAction("隐藏桌宠", MENU_HIDE));
        quickMenu.addView(thirdRow, menuRowParams());
        quickMenu.setVisibility(View.GONE);
        LayoutParams menuParams = new LayoutParams(
            LayoutParams.MATCH_PARENT, LayoutParams.WRAP_CONTENT, Gravity.TOP);
        menuParams.leftMargin = dp(8);
        menuParams.rightMargin = dp(8);
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
        close.setMinWidth(dp(44));
        close.setMinHeight(dp(44));
        close.setOnClickListener(view -> completeInteraction(null));
        titleRow.addView(close, new LinearLayout.LayoutParams(dp(44), dp(44)));
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
        setAppearance(opacity, mirrored, direction);
    }

    void setAppearance(float opacity, boolean mirrored, int direction) {
        petImage.setAlpha(opacity);
        face(direction, mirrored);
    }

    void setPetSize(int size) {
        LayoutParams params = (LayoutParams) petImage.getLayoutParams();
        if (params.width == size && params.height == size) return;
        params.width = size;
        params.height = size;
        petImage.setLayoutParams(params);
    }

    void face(int direction, boolean mirrored) {
        int sign = direction == 0 ? 1 : direction;
        petImage.setScaleX(sign * (mirrored ? -1f : 1f));
    }

    void say(String message) {
        cancelBubbleAnimation();
        quickMenu.setVisibility(View.GONE);
        bubble.bringToFront();
        bubble.setText(message);
        bubble.setVisibility(View.VISIBLE);
        bubble.setAlpha(0f);
        bubble.setTranslationY(dp(5));
        bubble.animate().alpha(1f).translationY(0f).setDuration(160).start();
    }

    void hideBubble() {
        cancelBubbleAnimation();
        int generation = bubbleAnimationGeneration;
        bubble.animate().alpha(0f).setDuration(150).withEndAction(() -> {
            if (generation == bubbleAnimationGeneration) bubble.setVisibility(View.INVISIBLE);
        }).start();
    }

    void hideBubbleImmediately() {
        cancelBubbleAnimation();
        bubble.setAlpha(0f);
        bubble.setVisibility(View.INVISIBLE);
    }

    private void cancelBubbleAnimation() {
        bubbleAnimationGeneration++;
        bubble.animate().withEndAction(null);
        bubble.animate().cancel();
    }

    void setMenuListener(MenuListener listener) { menuListener = listener; }

    void showInteraction(String title, String message, List<InteractionChoice> choices,
                         InteractionListener listener) {
        cancelBubbleAnimation();
        quickMenu.setVisibility(View.GONE);
        bubble.setVisibility(View.INVISIBLE);
        int generation = ++interactionGeneration;
        interactionListener = listener;
        interactionTitle.setText(title);
        interactionMessage.setText(message);
        interactionChoices.removeAllViews();
        for (InteractionChoice choice : choices) {
            TextView button = new TextView(getContext());
            button.setText(choice.label);
            button.setTextSize(13);
            button.setGravity(Gravity.CENTER);
            button.setClickable(true);
            button.setFocusable(true);
            button.setMinHeight(dp(44));
            button.setTextColor(choice.primary ? Color.WHITE : Color.rgb(23, 32, 30));
            GradientDrawable background = new GradientDrawable();
            background.setColor(choice.primary ? 0xff147d6b : 0xffedf5f2);
            background.setCornerRadius(dp(6));
            background.setStroke(dp(1), choice.primary ? 0xff147d6b : 0x28167d6c);
            button.setBackground(background);
            LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(
                LayoutParams.MATCH_PARENT, dp(44));
            params.bottomMargin = dp(6);
            button.setLayoutParams(params);
            button.setOnClickListener(view -> {
                if (generation == interactionGeneration) completeInteraction(choice.value);
            });
            interactionChoices.addView(button);
        }
        interactionCard.bringToFront();
        interactionCard.setVisibility(View.VISIBLE);
    }

    void hideInteraction() {
        interactionGeneration++;
        interactionListener = null;
        interactionChoices.removeAllViews();
        interactionCard.setVisibility(View.GONE);
    }

    boolean isInteractionVisible() { return interactionCard.getVisibility() == View.VISIBLE; }

    int interactionCardHeight() { return interactionCard.getMeasuredHeight(); }

    boolean toggleQuickMenu() {
        if (isQuickMenuVisible()) {
            hideQuickMenu();
            return false;
        }
        showQuickMenu();
        return true;
    }

    void showQuickMenu() {
        if (isInteractionVisible()) return;
        cancelBubbleAnimation();
        bubble.setVisibility(View.INVISIBLE);
        quickMenu.bringToFront();
        quickMenu.setVisibility(View.VISIBLE);
    }

    void hideQuickMenu() { quickMenu.setVisibility(View.GONE); }

    boolean isQuickMenuVisible() { return quickMenu.getVisibility() == View.VISIBLE; }

    boolean hitInteractive(float x, float y) {
        return hitVisible(quickMenu, x, y) || hitVisible(interactionCard, x, y);
    }

    void dismissInteraction() { completeInteraction(null); }

    // Demo visits are available on the app's companion page, keeping this menu compact.
    void setTrialVisitVisible(boolean visible) { }

    int preferredMenuWidth() { return dp(196); }
    int preferredMenuHeight() { return dp(162); }

    android.view.ViewGroup.LayoutParams petImageLayout() { return petImage.getLayoutParams(); }

    Rect petRect() {
        LayoutParams params = (LayoutParams) petImage.getLayoutParams();
        int left = (getWidth() - params.width) / 2;
        int top = (params.gravity & Gravity.VERTICAL_GRAVITY_MASK) == Gravity.TOP
            ? params.topMargin : getHeight() - params.height - params.bottomMargin;
        return new Rect(left, top, left + params.width, top + params.height);
    }

    void arrangeMenu(boolean above) {
        LayoutParams pet = (LayoutParams) petImage.getLayoutParams();
        pet.gravity = (above ? Gravity.BOTTOM : Gravity.TOP) | Gravity.CENTER_HORIZONTAL;
        pet.topMargin = pet.bottomMargin = 0;
        petImage.setLayoutParams(pet);
        LayoutParams menu = (LayoutParams) quickMenu.getLayoutParams();
        menu.gravity = (above ? Gravity.TOP : Gravity.BOTTOM) | Gravity.CENTER_HORIZONTAL;
        quickMenu.setLayoutParams(menu);
    }

    void configureTheater(int petSize, int width, int height) {
        setPetSize(petSize);
        LayoutParams pet = (LayoutParams) petImage.getLayoutParams();
        pet.gravity = Gravity.TOP | Gravity.CENTER_HORIZONTAL;
        pet.topMargin = 0;
        petImage.setLayoutParams(pet);
        LayoutParams text = (LayoutParams) bubble.getLayoutParams();
        text.width = Math.max(dp(40), width - dp(12));
        text.topMargin = petSize + dp(8);
        bubble.setLayoutParams(text);
        bubble.setTextSize(13);
        int available = Math.max(dp(32), height - text.topMargin - dp(8));
        int lines = Math.max(1, (available - bubble.getPaddingTop() - bubble.getPaddingBottom()) / Math.max(bubble.getLineHeight(), (int) Math.ceil(bubble.getTextSize() * 1.3f)));
        bubble.setMaxLines(lines);
        bubble.setEllipsize(null);
        bubble.setBreakStrategy(Layout.BREAK_STRATEGY_SIMPLE);
        bubble.setHyphenationFrequency(Layout.HYPHENATION_FREQUENCY_NONE);
    }

    List<String> theaterPages(String message) {
        LayoutParams params = (LayoutParams) bubble.getLayoutParams();
        int width = Math.max(1, params.width - bubble.getPaddingLeft() - bubble.getPaddingRight());
        StaticLayout layout = StaticLayout.Builder.obtain(message, 0, message.length(), bubble.getPaint(), width)
            .setAlignment(Layout.Alignment.ALIGN_NORMAL).setIncludePad(bubble.getIncludeFontPadding())
            .setLineSpacing(bubble.getLineSpacingExtra(), bubble.getLineSpacingMultiplier())
            .setBreakStrategy(bubble.getBreakStrategy()).setHyphenationFrequency(bubble.getHyphenationFrequency()).build();
        List<String> pages = new ArrayList<>();
        int linesPerPage = Math.max(1, bubble.getMaxLines());
        for (int line = 0; line < layout.getLineCount(); line += linesPerPage) {
            int endLine = Math.min(layout.getLineCount(), line + linesPerPage) - 1;
            pages.add(message.substring(layout.getLineStart(line), layout.getLineEnd(endLine)));
        }
        return pages.isEmpty() ? List.of("") : pages;
    }

    void restorePetLayout(int width) {
        arrangeMenu(true);
        bubble.setMaxLines(4);
        bubble.setEllipsize(TextUtils.TruncateAt.END);
        LayoutParams text = (LayoutParams) bubble.getLayoutParams();
        text.width = Math.min(Math.max(width - dp(12), dp(96)), dp(168));
        text.topMargin = dp(4);
        bubble.setLayoutParams(text);
    }

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

    private LinearLayout.LayoutParams menuRowParams() {
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(
            LayoutParams.MATCH_PARENT, dp(44));
        params.bottomMargin = dp(6);
        return params;
    }

    private TextView menuAction(String label, String action) {
        TextView button = new TextView(getContext());
        button.setText(label);
        button.setTextSize(13);
        button.setTypeface(button.getTypeface(), android.graphics.Typeface.BOLD);
        button.setTextColor(Color.rgb(23, 32, 30));
        button.setGravity(Gravity.CENTER);
        button.setClickable(true);
        button.setFocusable(true);
        button.setMinHeight(dp(44));
        GradientDrawable background = new GradientDrawable();
        background.setColor(0xffedf5f2);
        background.setCornerRadius(dp(8));
        background.setStroke(dp(1), 0x18167d6c);
        button.setBackground(background);
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(
            0, LayoutParams.MATCH_PARENT, 1f);
        params.setMargins(dp(3), 0, dp(3), 0);
        button.setLayoutParams(params);
        button.setOnClickListener(view -> {
            quickMenu.setVisibility(View.GONE);
            if (menuListener != null) menuListener.onAction(action);
        });
        return button;
    }

    int bubbleHeight() { return bubbleHeight; }

    private boolean hitVisible(View view, float x, float y) {
        if (view.getVisibility() != View.VISIBLE) return false;
        int slop = dp(6);
        return x >= view.getLeft() - slop && x <= view.getRight() + slop
            && y >= view.getTop() - slop && y <= view.getBottom() + slop;
    }

    private int dp(int value) {
        return Math.round(value * getResources().getDisplayMetrics().density);
    }
}
