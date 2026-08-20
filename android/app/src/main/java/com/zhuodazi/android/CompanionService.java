package com.zhuodazi.android;

import android.content.Context;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.File;
import java.io.IOException;
import java.security.MessageDigest;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

final class CompanionService {
    private static final int MAXIMUM_GIF_BYTES = 8 * 1024 * 1024;
    private final Context context;
    private final LicenseService licenses;
    private final PetRepository pets;

    CompanionService(Context context, LicenseService licenses, PetRepository pets) {
        this.context = context.getApplicationContext();
        this.licenses = licenses;
        this.pets = pets;
    }

    Profile refreshProfile() throws Exception {
        return parseProfile(NetworkClient.json(context, "GET", DeskPetApi.COMPANION, null,
            licenses, NetworkClient.Auth.ACTIVATED));
    }

    Profile updateName(String displayName) throws Exception {
        String name = displayName == null ? "" : displayName.trim();
        if (name.length() < 1 || name.length() > 20) throw new IllegalArgumentException("昵称需为 1 至 20 个字符");
        return parseProfile(NetworkClient.json(context, "PATCH", DeskPetApi.COMPANION,
            new JSONObject().put("displayName", name), licenses, NetworkClient.Auth.ACTIVATED));
    }

    Profile pair(String pairingCode) throws Exception {
        String code = pairingCode == null ? "" : pairingCode.replaceAll("[^A-Za-z0-9]", "")
            .toUpperCase(Locale.ROOT);
        if (code.length() != 6 && code.length() != 8) {
            throw new IllegalArgumentException("请输入搭子的 8 位配对码");
        }
        return parseProfile(NetworkClient.json(context, "POST", DeskPetApi.COMPANION_PAIR,
            new JSONObject().put("code", code), licenses, NetworkClient.Auth.ACTIVATED));
    }

    Profile unpair() throws Exception {
        return parseProfile(NetworkClient.json(context, "DELETE", DeskPetApi.COMPANION_PAIR, null,
            licenses, NetworkClient.Auth.ACTIVATED));
    }

    String sendCurrentGif() throws Exception {
        byte[] gif = pets.readGif(pets.selectedPet(), MAXIMUM_GIF_BYTES);
        byte[] response = NetworkClient.request(context, "POST", DeskPetApi.COMPANION_DELIVERIES, gif,
            "image/gif", licenses, NetworkClient.Auth.ACTIVATED, NetworkClient.DEFAULT_MAX_RESPONSE);
        JSONObject json = new JSONObject(new String(response, java.nio.charset.StandardCharsets.UTF_8));
        String recipient = json.optString("recipientName");
        if (recipient.trim().isEmpty()) throw new IOException("搭子服务返回的数据无效");
        return recipient;
    }

    Visit playTrialVisit(String category) throws Exception {
        JSONObject body = new JSONObject().put("category", category == null ? "" : category);
        JSONObject json = NetworkClient.json(context, "POST", DeskPetApi.TRIAL_VISIT_PLAY, body,
            licenses, NetworkClient.Auth.PREMIUM);
        String id = json.optString("id");
        String sender = json.optString("senderName", "桌搭子");
        String hash = json.optString("sha256");
        String downloadPath = json.optString("downloadPath");
        if (!id.matches("[A-Za-z0-9._:-]{1,128}") || !hash.matches("(?i)[0-9a-f]{64}")
            || !downloadPath.matches("/api/trial/visit-stickers/[0-9a-fA-F-]{36}/file")) {
            throw new IOException("来访表情无效");
        }
        byte[] gif = NetworkClient.request(context, "GET", downloadPath, null, null,
            licenses, NetworkClient.Auth.PREMIUM, MAXIMUM_GIF_BYTES);
        validateGif(gif, hash);
        return new Visit(id, sender, pets.saveInboxGif(id, gif));
    }

    List<Visit> receive() throws Exception {
        JSONObject response = NetworkClient.json(context, "GET", DeskPetApi.COMPANION_DELIVERIES, null,
            licenses, NetworkClient.Auth.ACTIVATED);
        JSONArray deliveries = response.optJSONArray("deliveries");
        List<Visit> visits = new ArrayList<>();
        if (deliveries == null) return visits;
        for (int index = 0; index < Math.min(10, deliveries.length()); index++) {
            JSONObject item = deliveries.optJSONObject(index);
            if (item == null) continue;
            String id = item.optString("id");
            String sender = item.optString("senderName", "搭子");
            String hash = item.optString("sha256");
            String downloadPath = item.optString("downloadPath");
            if (!id.matches("[A-Za-z0-9._:-]{1,128}") || !hash.matches("(?i)[0-9a-f]{64}")
                || !downloadPath.startsWith(DeskPetApi.COMPANION_DOWNLOAD_PREFIX)) continue;
            byte[] gif = NetworkClient.request(context, "GET", downloadPath, null, null,
                licenses, NetworkClient.Auth.ACTIVATED, MAXIMUM_GIF_BYTES);
            validateGif(gif, hash);
            File file = pets.saveInboxGif(id, gif);
            NetworkClient.json(context, "POST", DeskPetApi.COMPANION_DELIVERIES + "/" + id + "/acknowledge",
                null, licenses, NetworkClient.Auth.ACTIVATED);
            visits.add(new Visit(id, sender, file));
        }
        return visits;
    }

    private static Profile parseProfile(JSONObject json) throws Exception {
        JSONObject payload = json.optJSONObject("profile");
        if (payload == null) payload = json;
        JSONObject partnerJson = payload.optJSONObject("partner");
        Partner partner = partnerJson == null ? null
            : new Partner(partnerJson.optString("displayName", "搭子"), partnerJson.optString("pairedAt"));
        String displayName = payload.optString("displayName").trim();
        if (displayName.isEmpty()) displayName = "桌搭子";
        String pairingCode = payload.optString("pairingCode").replaceAll("[^A-Za-z0-9]", "")
            .toUpperCase(Locale.ROOT);
        if (pairingCode.length() != 6 && pairingCode.length() != 8) {
            throw new IOException("搭子服务未返回有效配对码，请稍后重试");
        }
        return new Profile(displayName, pairingCode, partner);
    }

    private static void validateGif(byte[] bytes, String expectedHash) throws Exception {
        if (bytes.length < 10 || bytes[0] != 'G' || bytes[1] != 'I' || bytes[2] != 'F'
            || !((bytes[3] == '8' && bytes[4] == '7' && bytes[5] == 'a')
                || (bytes[3] == '8' && bytes[4] == '9' && bytes[5] == 'a'))) {
            throw new IOException("收到的文件不是有效 GIF");
        }
        StringBuilder actual = new StringBuilder(64);
        for (byte value : MessageDigest.getInstance("SHA-256").digest(bytes)) {
            actual.append(String.format("%02x", value & 0xff));
        }
        if (!actual.toString().equalsIgnoreCase(expectedHash)) throw new IOException("收到的 GIF 校验失败");
    }

    record Partner(String displayName, String pairedAt) { }
    record Profile(String displayName, String pairingCode, Partner partner) { }
    record Visit(String id, String senderName, File file) { }
}
