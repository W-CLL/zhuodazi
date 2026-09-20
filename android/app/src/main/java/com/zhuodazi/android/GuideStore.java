package com.zhuodazi.android;

import android.content.SharedPreferences;
import java.util.LinkedHashMap;
import java.util.Map;

/** Local tutorial progress; learning never changes companionship preferences or account data. */
final class GuideStore {
    static final int VERSION = 1;
    private final SharedPreferences values;

    GuideStore(SharedPreferences values) {
        this.values = values;
        synchronized (GuideStore.class) {
            if (!values.contains("guideVersion")) {
                boolean upgraded = !values.getAll().isEmpty();
                values.edit().putInt("guideVersion", VERSION).putInt("guideStep", 0)
                    .putInt("guideCompletedSteps", 0).putInt("guideSkippedSteps", 0)
                    .putBoolean("guideDismissed", upgraded)
                    .putBoolean("guideUpgradeNotice", upgraded).commit();
            }
        }
    }

    int step() { return Math.max(0, Math.min(5, values.getInt("guideStep", 0))); }
    boolean dismissed() { return values.getBoolean("guideDismissed", false); }
    boolean holdsAttention() { return !dismissed() && step() < 5; }
    void begin(boolean replay) {
        SharedPreferences.Editor edit = values.edit().putBoolean("guideDismissed", false)
            .putBoolean("guideUpgradeNotice", false);
        if (replay || step() == 5) edit.putInt("guideStep", 1)
            .putInt("guideCompletedSteps", 0).putInt("guideSkippedSteps", 0);
        else if (step() == 0) edit.putInt("guideStep", 1);
        edit.commit();
    }
    void dismiss() { values.edit().putBoolean("guideDismissed", true)
        .putBoolean("guideUpgradeNotice", false).commit(); }
    void dismissNotice() { values.edit().putBoolean("guideUpgradeNotice", false).commit(); }
    void advance(int expectedStep, boolean skipped) {
        if (dismissed() || step() != expectedStep || expectedStep < 1 || expectedStep > 4) return;
        String key = skipped ? "guideSkippedSteps" : "guideCompletedSteps";
        values.edit().putInt(key, values.getInt(key, 0) | (1 << expectedStep))
            .putInt("guideStep", expectedStep + 1).commit();
    }
    void borrowVisibility(boolean hidden, boolean clickThrough) {
        if (values.getBoolean("guideVisibilityBorrowed", false)) return;
        values.edit().putBoolean("guideOriginalHidden", hidden).putBoolean("guideOriginalClickThrough", clickThrough)
            .putBoolean("guideVisibilityBorrowed", true).commit();
    }
    boolean hasBorrowedVisibility() { return values.getBoolean("guideVisibilityBorrowed", false); }
    boolean originalHidden() { return values.getBoolean("guideOriginalHidden", false); }
    boolean originalClickThrough() { return values.getBoolean("guideOriginalClickThrough", false); }
    void finishBorrowing() { values.edit().remove("guideVisibilityBorrowed").remove("guideOriginalHidden")
        .remove("guideOriginalClickThrough").commit(); }
    Map<String, Object> snapshot() {
        Map<String, Object> result = new LinkedHashMap<>();
        result.put("guideVersion", values.getInt("guideVersion", VERSION));
        result.put("guideStep", step());
        result.put("guideDismissed", dismissed());
        result.put("guideCompletedSteps", values.getInt("guideCompletedSteps", 0));
        result.put("guideSkippedSteps", values.getInt("guideSkippedSteps", 0));
        result.put("guideUpgradeNotice", values.getBoolean("guideUpgradeNotice", false));
        return result;
    }
}
