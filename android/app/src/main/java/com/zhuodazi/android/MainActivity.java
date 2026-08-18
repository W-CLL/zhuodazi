package com.zhuodazi.android;

import android.app.Activity;
import android.content.Intent;
import android.os.Bundle;

/** Leftover entry used by older notifications; forwards to the Flutter settings UI. */
public final class MainActivity extends Activity {
    @Override protected void onCreate(Bundle state) {
        super.onCreate(state);
        startActivity(new Intent(this, FlutterMainActivity.class)
            .addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP | Intent.FLAG_ACTIVITY_SINGLE_TOP));
        finish();
    }
}
