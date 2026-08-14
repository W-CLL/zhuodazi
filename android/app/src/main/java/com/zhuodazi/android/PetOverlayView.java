package com.zhuodazi.android;

import android.content.Context;
import android.graphics.Color;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.Drawable;
import android.view.Gravity;
import android.view.View;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.TextView;

final class PetOverlayView extends FrameLayout {
    private final ImageView petImage;
    private final TextView bubble;
    private final int bubbleHeight;

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

    int bubbleHeight() { return bubbleHeight; }

    private int dp(int value) {
        return Math.round(value * getResources().getDisplayMetrics().density);
    }
}
