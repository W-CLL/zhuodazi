package com.zhuodazi.android;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.provider.Settings;

public final class BootReceiver extends BroadcastReceiver {
    @Override public void onReceive(Context context, Intent intent) {
        SettingsStore settings = new SettingsStore(context);
        if (settings.startOnBoot() && Settings.canDrawOverlays(context)) {
            Intent service = new Intent(context, PetOverlayService.class)
                .setAction(PetOverlayService.ACTION_START);
            try {
                context.startForegroundService(service);
            } catch (RuntimeException ignored) {
                settings.setRunning(false);
            }
        }
    }
}
