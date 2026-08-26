package com.zhuodazi.android;

final class DeskPetApi {
    static final String HOST = "in.desktoppet.online";
    static final String BASE_URL = "https://" + HOST;
    static final String SIGNING_PUBLIC_KEY_SPKI =
        "MCowBQYDK2VwAyEANjBEMMQ5TY+0ECNoRqQy9780eoVOzkKpzFDq2TwLytU=";

    static final String ACTIVATE = "/api/activate";
    static final String TRIAL = "/api/trial";
    static final String FEEDBACK = "/api/feedback";
    static final String INTERACTION_PROFILE = "/api/interactions/profile";
    static final String INTERACTION_EVENTS = "/api/interactions/events";
    static final String CONTENT_BATCH = "/api/content/batch";
    static final String CONTENT_OFFLINE_PACK = "/api/content/offline-pack";
    static final String COMPANION = "/api/companion";
    static final String COMPANION_PAIR = "/api/companion/pair";
    static final String COMPANION_DELIVERIES = "/api/companion/deliveries";
    static final String COMPANION_HALL = "/api/companion/hall";
    static final String COMPANION_HALL_DELIVERIES = "/api/companion/hall/deliveries";
    static final String TRIAL_VISIT_PLAY = "/api/trial/visit-stickers/play";
    static final String ANALYTICS_EVENTS = "/api/analytics/events";
    static final String COMPANION_DOWNLOAD_PREFIX = "/api/companion/";
    static final String SITE_SETTINGS = "/api/public/site-settings";
    static final String UPDATE_LATEST = "/api/update/latest";
    static final String DOWNLOAD_PATH_PREFIX = "/downloads/";

    private DeskPetApi() { }
}
