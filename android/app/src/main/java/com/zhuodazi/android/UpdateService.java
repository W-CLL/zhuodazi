package com.zhuodazi.android;

import android.content.Context;
import android.net.Uri;
import android.util.Base64;

import com.google.crypto.tink.subtle.Ed25519Verify;

import org.json.JSONObject;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.Arrays;
import java.util.LinkedHashMap;
import java.util.Locale;
import java.util.Map;
import java.util.regex.Pattern;

final class UpdateService {
    static final String PHASE_IDLE = "idle";
    static final String PHASE_CHECKING = "checking";
    static final String PHASE_CURRENT = "current";
    static final String PHASE_AVAILABLE = "available";
    static final String PHASE_DOWNLOADING = "downloading";
    static final String PHASE_DOWNLOADED = "downloaded";
    static final String PHASE_ERROR = "error";

    private static final int MAX_MANIFEST_BYTES = 512 * 1024;
    private static final long MAX_DOWNLOAD_BYTES = 300L * 1024 * 1024;
    private static final Pattern VERSION_PATTERN =
        Pattern.compile("^\\d+\\.\\d+\\.\\d+(?:-[0-9A-Za-z.-]+)?$");
    private static final Pattern SHA256_PATTERN = Pattern.compile("^[0-9a-fA-F]{64}$");

    private final Context context;
    private final SettingsStore settings;
    private final LicenseService licenses;
    private State state = State.idle();
    private File downloadedApk;

    UpdateService(Context context, SettingsStore settings, LicenseService licenses) {
        this.context = context.getApplicationContext();
        this.settings = settings;
        this.licenses = licenses;
    }

    State state() {
        synchronized (this) { return state; }
    }

    State check(boolean manual) throws Exception {
        setState(new State(PHASE_CHECKING, "正在检查更新…", null, 0, null));
        try {
            String path = DeskPetApi.UPDATE_LATEST
                + "?platform=android&architecture=" + Uri.encode(NetworkClient.architecture());
            NetworkClient.Auth auth = licenses.hasPremiumAccess()
                ? NetworkClient.Auth.PREMIUM : NetworkClient.Auth.NONE;
            NetworkClient.HttpResult result = NetworkClient.execute(
                context, "GET", path, null, null, licenses, auth, MAX_MANIFEST_BYTES);
            if (result.status == 404 && isNoRelease(result.body)) {
                return setState(State.current("当前已经是最新版本"));
            }
            if (result.status == 401 || result.status == 403) {
                throw new IOException(authMessage(result.body, result.status));
            }
            if (result.status < 200 || result.status >= 300) {
                throw new IOException(NetworkClient.errorMessage(result.body, result.status));
            }
            Manifest manifest = Manifest.parse(new JSONObject(new String(result.body, StandardCharsets.UTF_8)));
            validate(manifest);
            if (compareVersions(manifest.version, NetworkClient.appVersion(context)) <= 0) {
                synchronized (this) { downloadedApk = null; }
                return setState(State.current("当前已经是最新版本"));
            }
            String ignored = settings.ignoredUpdateVersion();
            if (!manual && !ignored.isEmpty() && ignored.equalsIgnoreCase(manifest.version)) {
                return setState(new State(PHASE_AVAILABLE, "已忽略 v" + manifest.version, manifest, 0, null));
            }
            return setState(new State(PHASE_AVAILABLE, "发现新版本 v" + manifest.version, manifest, 0, null));
        } catch (Exception error) {
            State failed = new State(PHASE_ERROR, friendly(error, "暂时无法检查更新，请稍后重试"),
                state().manifest, 0, null);
            setState(failed);
            if (manual) throw new IOException(failed.message, error);
            return failed;
        }
    }

    State download(ProgressListener progress) throws Exception {
        Manifest manifest;
        synchronized (this) { manifest = state.manifest; }
        if (manifest == null) throw new IllegalStateException("请先检查更新");
        File directory = updatesDirectory();
        if (!directory.exists() && !directory.mkdirs()) throw new IOException("无法创建更新目录");
        File finalFile = new File(directory, apkFileName(manifest.version));
        File temporary = new File(directory, finalFile.getName() + ".part");
        setState(new State(PHASE_DOWNLOADING, "正在下载新版本…", manifest, 0, null));
        NetworkClient.Auth auth = licenses.hasPremiumAccess()
            ? NetworkClient.Auth.PREMIUM : NetworkClient.Auth.NONE;
        HttpURLConnection connection = NetworkClient.open(context, "GET", manifest.url, licenses, auth);
        connection.setConnectTimeout(20_000);
        connection.setReadTimeout(10 * 60 * 1000);
        connection.setRequestProperty("Accept", "application/vnd.android.package-archive,application/octet-stream,*/*");
        try {
            int status = connection.getResponseCode();
            if (status == 401 || status == 403) {
                throw new IOException(authMessage(readErrorBody(connection), status));
            }
            if (status < 200 || status >= 300) {
                throw new IOException(NetworkClient.errorMessage(readErrorBody(connection), status));
            }
            long total = connection.getContentLengthLong();
            if (total > MAX_DOWNLOAD_BYTES) throw new IOException("更新文件超过 300 MB");
            MessageDigest digest = MessageDigest.getInstance("SHA-256");
            long received = 0;
            int lastProgress = -1;
            long startedAt = System.nanoTime();
            try (InputStream input = connection.getInputStream();
                 FileOutputStream output = new FileOutputStream(temporary)) {
                byte[] buffer = new byte[64 * 1024];
                int count;
                while ((count = input.read(buffer)) >= 0) {
                    received += count;
                    if (received > MAX_DOWNLOAD_BYTES) throw new IOException("更新文件超过 300 MB");
                    digest.update(buffer, 0, count);
                    output.write(buffer, 0, count);
                    if (total > 0) {
                        int percent = (int) Math.min(99, received * 100 / total);
                        if (percent >= lastProgress + 2) {
                            lastProgress = percent;
                            double seconds = Math.max((System.nanoTime() - startedAt) / 1_000_000_000d, 0.1);
                            double megabytesPerSecond = received / 1024d / 1024d / seconds;
                            String message = String.format(Locale.ROOT,
                                "正在下载 %d%% · %.1f MB/s", percent, megabytesPerSecond);
                            State next = new State(PHASE_DOWNLOADING, message, manifest, percent, null);
                            setState(next);
                            if (progress != null) progress.onProgress(next);
                        }
                    }
                }
                output.flush();
            }
            String actual = hex(digest.digest());
            if (!actual.equalsIgnoreCase(manifest.sha256)) {
                throw new IOException("更新文件校验失败，已取消更新");
            }
            if (finalFile.exists() && !finalFile.delete()) {
                throw new IOException("无法覆盖已下载的更新包");
            }
            if (!temporary.renameTo(finalFile)) {
                throw new IOException("无法保存更新包");
            }
            synchronized (this) { downloadedApk = finalFile; }
            return setState(new State(PHASE_DOWNLOADED, "更新已下载并通过校验", manifest, 100, finalFile.getAbsolutePath()));
        } catch (Exception error) {
            if (temporary.exists()) temporary.delete();
            synchronized (this) { downloadedApk = null; }
            State failed = new State(PHASE_ERROR, friendly(error, "暂时无法下载更新，请稍后重试"),
                manifest, 0, null);
            setState(failed);
            throw new IOException(failed.message, error);
        } finally {
            connection.disconnect();
        }
    }

    File downloadedFile() {
        synchronized (this) {
            if (downloadedApk != null && downloadedApk.isFile()) return downloadedApk;
            return null;
        }
    }

    State ignoreAvailable() {
        Manifest manifest;
        synchronized (this) { manifest = state.manifest; }
        if (manifest == null) return state();
        settings.putString(SettingsStore.IGNORED_UPDATE_VERSION, manifest.version);
        return setState(new State(PHASE_AVAILABLE, "已忽略 v" + manifest.version, manifest, 0, null));
    }

    void clearIgnored() {
        settings.putString(SettingsStore.IGNORED_UPDATE_VERSION, "");
    }

    static int compareVersions(String left, String right) {
        int[] a = parseVersion(left);
        int[] b = parseVersion(right);
        for (int i = 0; i < 4; i++) {
            int comparison = Integer.compare(a[i], b[i]);
            if (comparison != 0) return comparison;
        }
        return 0;
    }

    static void validate(Manifest manifest) throws Exception {
        if (manifest.version.isEmpty() || !VERSION_PATTERN.matcher(manifest.version).matches()
            || compareVersions(manifest.version, "0.0.0") <= 0) {
            throw new IOException("更新清单版本号无效");
        }
        if (!"android".equals(manifest.platform)) {
            throw new IOException("更新包的系统不匹配");
        }
        String architecture = NetworkClient.architecture();
        if (!manifest.architecture.isEmpty() && !manifest.architecture.equals(architecture)) {
            throw new IOException("更新包的架构不匹配");
        }
        Uri uri = Uri.parse(manifest.url);
        if (uri == null || !"https".equalsIgnoreCase(uri.getScheme())
            || !DeskPetApi.HOST.equalsIgnoreCase(uri.getHost())
            || uri.getPath() == null
            || !uri.getPath().startsWith(DeskPetApi.DOWNLOAD_PATH_PREFIX)) {
            throw new IOException("更新下载地址无效");
        }
        if (!SHA256_PATTERN.matcher(manifest.sha256).matches()) {
            throw new IOException("更新清单缺少有效的 SHA-256");
        }
        if (!"ed25519".equalsIgnoreCase(manifest.signatureAlgorithm)) {
            throw new IOException("更新清单签名算法无效");
        }
        byte[] signature;
        try { signature = Base64.decode(manifest.signature, Base64.DEFAULT); }
        catch (Exception error) { throw new IOException("更新清单签名格式无效"); }
        if (signature.length != 64) throw new IOException("更新清单签名格式无效");
        byte[] publicKeyBytes = Base64.decode(DeskPetApi.SIGNING_PUBLIC_KEY_SPKI, Base64.DEFAULT);
        if (publicKeyBytes.length != 44) throw new IOException("内置更新公钥无效");
        byte[] rawPublicKey = Arrays.copyOfRange(publicKeyBytes, 12, 44);
        try {
            new Ed25519Verify(rawPublicKey).verify(signature, signedPayload(manifest));
        } catch (Exception error) {
            throw new IOException("更新清单签名验证失败", error);
        }
    }

    static byte[] signedPayload(Manifest manifest) {
        // Keep this byte-for-byte compatible with Node's JSON.stringify payload.
        String json = "{\"version\":" + jsonString(manifest.version)
            + ",\"url\":" + jsonString(manifest.url)
            + ",\"sha256\":" + jsonString(manifest.sha256.toLowerCase(Locale.ROOT))
            + ",\"notes\":" + jsonString(manifest.notes) + "}";
        return json.getBytes(StandardCharsets.UTF_8);
    }

    private File updatesDirectory() {
        return new File(context.getFilesDir(), "updates");
    }

    private String apkFileName(String version) {
        String architecture = NetworkClient.architecture();
        String label = "armeabi-v7a".equals(architecture) ? "armv7" : "arm64";
        return "ZhuoDazi-Android-" + version + "-" + label + ".apk";
    }

    private State setState(State next) {
        synchronized (this) { state = next; }
        return next;
    }

    private static int[] parseVersion(String value) {
        String core = value == null ? "0" : value.split("-", 2)[0];
        String[] parts = core.split("\\.");
        int[] numbers = new int[4];
        for (int i = 0; i < 4; i++) {
            if (i < parts.length) {
                try { numbers[i] = Integer.parseInt(parts[i]); }
                catch (NumberFormatException ignored) { numbers[i] = 0; }
            }
        }
        return numbers;
    }

    private static boolean isNoRelease(byte[] body) {
        try {
            return "NO_RELEASE".equals(new JSONObject(new String(body, StandardCharsets.UTF_8)).optString("code"));
        } catch (Exception ignored) {
            return false;
        }
    }

    private static String authMessage(byte[] body, int status) {
        String message = NetworkClient.errorMessage(body, status);
        if (message == null || message.isBlank()) return "请先激活桌搭子后再检查更新";
        if (status == 401 && message.contains("请先激活")) return message;
        return "设备绑定已失效，暂时无法更新";
    }

    private static byte[] readErrorBody(HttpURLConnection connection) {
        try (InputStream stream = connection.getErrorStream()) {
            if (stream == null) return new byte[0];
            return NetworkClient.readLimited(stream, 64 * 1024);
        } catch (Exception ignored) {
            return new byte[0];
        }
    }

    private static String friendly(Exception error, String fallback) {
        String message = error.getMessage();
        if (message == null || message.isBlank()) return fallback;
        String lower = message.toLowerCase(Locale.ROOT);
        if (lower.contains("timeout") || message.contains("超时")) return fallback.contains("下载")
            ? "下载更新超时，请稍后重试" : "检查更新超时，请稍后重试";
        if (lower.contains("unable to resolve") || lower.contains("failed to connect")
            || lower.contains("network") || message.contains("无法连接")) {
            return "暂时无法连接服务，请检查网络后重试";
        }
        return message;
    }

    private static String hex(byte[] data) {
        StringBuilder result = new StringBuilder(data.length * 2);
        for (byte value : data) result.append(String.format(Locale.ROOT, "%02x", value & 0xff));
        return result.toString();
    }

    private static String jsonString(String value) {
        String text = value == null ? "" : value;
        StringBuilder output = new StringBuilder("\"");
        for (int i = 0; i < text.length(); i++) {
            char ch = text.charAt(i);
            switch (ch) {
                case '\b' -> output.append("\\b");
                case '\t' -> output.append("\\t");
                case '\n' -> output.append("\\n");
                case '\f' -> output.append("\\f");
                case '\r' -> output.append("\\r");
                case '"' -> output.append("\\\"");
                case '\\' -> output.append("\\\\");
                default -> {
                    if (ch < 0x20) output.append(String.format(Locale.ROOT, "\\u%04x", (int) ch));
                    else output.append(ch);
                }
            }
        }
        return output.append('"').toString();
    }

    @FunctionalInterface
    interface ProgressListener { void onProgress(State state); }

    static final class Manifest {
        final String platform;
        final String architecture;
        final String version;
        final String url;
        final String sha256;
        final String notes;
        final String signatureAlgorithm;
        final String signature;

        Manifest(String platform, String architecture, String version, String url, String sha256,
                 String notes, String signatureAlgorithm, String signature) {
            this.platform = platform;
            this.architecture = architecture;
            this.version = version;
            this.url = url;
            this.sha256 = sha256;
            this.notes = notes;
            this.signatureAlgorithm = signatureAlgorithm;
            this.signature = signature;
        }

        static Manifest parse(JSONObject json) {
            return new Manifest(
                json.optString("platform", ""),
                json.optString("architecture", ""),
                json.optString("version", "").trim(),
                json.optString("url", "").trim(),
                json.optString("sha256", "").trim().toLowerCase(Locale.ROOT),
                json.optString("notes", ""),
                json.optString("signatureAlgorithm", ""),
                json.optString("signature", ""));
        }

        Map<String, Object> toMap() {
            Map<String, Object> value = new LinkedHashMap<>();
            value.put("platform", platform);
            value.put("architecture", architecture);
            value.put("version", version);
            value.put("url", url);
            value.put("sha256", sha256);
            value.put("notes", notes);
            return value;
        }
    }

    static final class State {
        final String phase;
        final String message;
        final Manifest manifest;
        final int progress;
        final String localPath;

        State(String phase, String message, Manifest manifest, int progress, String localPath) {
            this.phase = phase;
            this.message = message;
            this.manifest = manifest;
            this.progress = progress;
            this.localPath = localPath;
        }

        static State idle() { return new State(PHASE_IDLE, "可以检查更新", null, 0, null); }
        static State current(String message) { return new State(PHASE_CURRENT, message, null, 0, null); }

        Map<String, Object> toMap() {
            Map<String, Object> value = new LinkedHashMap<>();
            value.put("phase", phase);
            value.put("message", message);
            value.put("progress", progress);
            if (manifest != null) value.put("manifest", manifest.toMap());
            if (localPath != null) value.put("localPath", localPath);
            return value;
        }
    }
}
