package com.zhuodazi.android;

import org.junit.Test;
import static org.junit.Assert.*;

public class UpdateServiceTest {
    @Test public void acceptsOnlySignedOssDownloadRedirects() {
        assertTrue(UpdateService.isTrustedDownloadRedirect(
            "https://oss-download.desktoppet.online/releases/android/arm64-v8a/1.4.1/app.apk?Expires=1&Signature=x"));
        assertFalse(UpdateService.isTrustedDownloadRedirect(
            "https://evil.example/releases/android/arm64-v8a/1.4.1/app.apk?Expires=1&Signature=x"));
        assertFalse(UpdateService.isTrustedDownloadRedirect(
            "http://oss-download.desktoppet.online/releases/android/arm64-v8a/1.4.1/app.apk?Expires=1"));
        assertFalse(UpdateService.isTrustedDownloadRedirect(
            "https://oss-download.desktoppet.online/not-releases/app.apk?Expires=1"));
        assertFalse(UpdateService.isTrustedDownloadRedirect(
            "https://oss-download.desktoppet.online/releases/app.apk"));
    }

    @Test public void recognizesAllHttpRedirectStatuses() {
        assertTrue(UpdateService.isRedirect(301));
        assertTrue(UpdateService.isRedirect(302));
        assertTrue(UpdateService.isRedirect(303));
        assertTrue(UpdateService.isRedirect(307));
        assertTrue(UpdateService.isRedirect(308));
        assertFalse(UpdateService.isRedirect(200));
    }
}
