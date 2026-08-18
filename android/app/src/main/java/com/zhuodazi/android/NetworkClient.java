package com.zhuodazi.android;

import android.content.Context;
import android.os.Build;

import org.json.JSONObject;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;

final class NetworkClient {
    static final String SERVICE_BASE = DeskPetApi.BASE_URL;
    static final int DEFAULT_MAX_RESPONSE = 256 * 1024;

    enum Auth { NONE, PREMIUM, ACTIVATED }

    private NetworkClient() { }

    static JSONObject json(Context context, String method, String path, JSONObject body,
                           LicenseService licenses, Auth auth) throws Exception {
        return json(context, method, path, body, licenses, auth, DEFAULT_MAX_RESPONSE);
    }

    static JSONObject json(Context context, String method, String path, JSONObject body,
                           LicenseService licenses, Auth auth, int maximumResponseBytes) throws Exception {
        byte[] payload = body == null ? null : body.toString().getBytes(StandardCharsets.UTF_8);
        HttpResult result = execute(context, method, path, payload,
            payload == null ? null : "application/json; charset=utf-8", licenses, auth, maximumResponseBytes);
        if (result.status < 200 || result.status >= 300) {
            throw new IOException(errorMessage(result.body, result.status));
        }
        try { return new JSONObject(new String(result.body, StandardCharsets.UTF_8)); }
        catch (Exception error) { throw new IOException("服务返回的数据无效", error); }
    }

    static HttpResult execute(Context context, String method, String path, byte[] body, String contentType,
                              LicenseService licenses, Auth auth, int maximumResponseBytes) throws Exception {
        String address = path.startsWith("https://") ? path : SERVICE_BASE + path;
        HttpURLConnection connection = open(context, method, address, licenses, auth);
        connection.setConnectTimeout(20_000);
        connection.setReadTimeout(35_000);
        if (body != null) {
            connection.setDoOutput(true);
            connection.setFixedLengthStreamingMode(body.length);
            if (contentType != null) connection.setRequestProperty("Content-Type", contentType);
            try (OutputStream output = connection.getOutputStream()) { output.write(body); }
        }
        try {
            int status = connection.getResponseCode();
            long contentLength = connection.getContentLengthLong();
            if (contentLength > maximumResponseBytes) throw new IOException("服务响应过大");
            InputStream stream = status >= 200 && status < 300
                ? connection.getInputStream() : connection.getErrorStream();
            byte[] response = readLimited(stream == null ? new ByteArrayInputStream(new byte[0]) : stream,
                maximumResponseBytes);
            return new HttpResult(status, response);
        } finally {
            connection.disconnect();
        }
    }

    static byte[] request(Context context, String method, String path, byte[] body, String contentType,
                          LicenseService licenses, Auth auth, int maximumResponseBytes) throws Exception {
        HttpResult result = execute(context, method, path, body, contentType, licenses, auth, maximumResponseBytes);
        if (result.status < 200 || result.status >= 300) throw new IOException(errorMessage(result.body, result.status));
        return result.body;
    }

    static HttpURLConnection open(Context context, String method, String address,
                                  LicenseService licenses, Auth auth) throws Exception {
        HttpURLConnection connection = (HttpURLConnection) new URL(address).openConnection();
        connection.setRequestMethod(method);
        connection.setUseCaches(false);
        connection.setInstanceFollowRedirects(false);
        connection.setRequestProperty("Accept", "application/json");
        connection.setRequestProperty("User-Agent", "ZhuoDazi-Android/" + appVersion(context));
        connection.setRequestProperty("X-DeskPet-Platform", "android");
        connection.setRequestProperty("X-DeskPet-Architecture", architecture());
        connection.setRequestProperty("X-DeskPet-Version", appVersion(context));
        if (auth != Auth.NONE) licenses.authorize(connection, auth == Auth.ACTIVATED);
        return connection;
    }

    static String architecture() {
        return Build.SUPPORTED_ABIS.length == 0 ? "arm64-v8a" : Build.SUPPORTED_ABIS[0];
    }

    static byte[] readLimited(InputStream input, int maximumBytes) throws IOException {
        try (InputStream source = input; ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[8192];
            int count;
            while ((count = source.read(buffer)) >= 0) {
                if (output.size() + count > maximumBytes) throw new IOException("服务响应过大");
                output.write(buffer, 0, count);
            }
            return output.toByteArray();
        }
    }

    static String appVersion(Context context) {
        try {
            String version = context.getPackageManager().getPackageInfo(context.getPackageName(), 0).versionName;
            return version == null || version.trim().isEmpty() ? "1.1.0" : version;
        }
        catch (Exception ignored) { return "1.1.0"; }
    }

    static String errorMessage(byte[] bytes, int status) {
        try {
            JSONObject json = new JSONObject(new String(bytes, StandardCharsets.UTF_8));
            String message = json.optString("error", json.optString("message", ""));
            if (!message.trim().isEmpty()) return message;
        } catch (Exception ignored) { }
        return status == 401 || status == 403 ? "设备激活已失效，请重新激活" : "服务请求失败（" + status + "）";
    }

    static final class HttpResult {
        final int status;
        final byte[] body;

        HttpResult(int status, byte[] body) {
            this.status = status;
            this.body = body;
        }
    }
}
