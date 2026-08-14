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
    static final String SERVICE_BASE = "https://in.desktoppet.online";
    static final int DEFAULT_MAX_RESPONSE = 256 * 1024;

    enum Auth { NONE, PREMIUM, ACTIVATED }

    private NetworkClient() { }

    static JSONObject json(Context context, String method, String path, JSONObject body,
                           LicenseService licenses, Auth auth) throws Exception {
        byte[] payload = body == null ? null : body.toString().getBytes(StandardCharsets.UTF_8);
        byte[] response = request(context, method, path, payload,
            payload == null ? null : "application/json; charset=utf-8", licenses, auth, DEFAULT_MAX_RESPONSE);
        try { return new JSONObject(new String(response, StandardCharsets.UTF_8)); }
        catch (Exception error) { throw new IOException("服务返回的数据无效", error); }
    }

    static byte[] request(Context context, String method, String path, byte[] body, String contentType,
                          LicenseService licenses, Auth auth, int maximumResponseBytes) throws Exception {
        String address = path.startsWith("https://") ? path : SERVICE_BASE + path;
        HttpURLConnection connection = (HttpURLConnection) new URL(address).openConnection();
        connection.setRequestMethod(method);
        connection.setConnectTimeout(20_000);
        connection.setReadTimeout(35_000);
        connection.setUseCaches(false);
        connection.setInstanceFollowRedirects(false);
        connection.setRequestProperty("Accept", "application/json");
        connection.setRequestProperty("User-Agent", "ZhuoDazi-Android/" + appVersion(context));
        connection.setRequestProperty("X-DeskPet-Platform", "android");
        connection.setRequestProperty("X-DeskPet-Architecture",
            Build.SUPPORTED_ABIS.length == 0 ? "unknown" : Build.SUPPORTED_ABIS[0]);
        connection.setRequestProperty("X-DeskPet-Version", appVersion(context));
        if (auth != Auth.NONE) licenses.authorize(connection, auth == Auth.ACTIVATED);
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
            if (status < 200 || status >= 300) throw new IOException(errorMessage(response, status));
            return response;
        } finally {
            connection.disconnect();
        }
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

    private static String errorMessage(byte[] bytes, int status) {
        try {
            JSONObject json = new JSONObject(new String(bytes, StandardCharsets.UTF_8));
            String message = json.optString("error", json.optString("message", ""));
            if (!message.trim().isEmpty()) return message;
        } catch (Exception ignored) { }
        return status == 401 || status == 403 ? "设备激活已失效，请重新激活" : "服务请求失败（" + status + "）";
    }
}
