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
        return parseProfile(NetworkClient.json(context, "GET", "/api/companion", null,
            licenses, NetworkClient.Auth.ACTIVATED));
    }

    Profile updateName(String displayName) throws Exception {
        String name = displayName == null ? "" : displayName.trim();
        if (name.length() < 1 || name.length() > 20) throw new IllegalArgumentException("昵称需为 1 至 20 个字符");
        return parseProfile(NetworkClient.json(context, "PATCH", "/api/companion",
            new JSONObject().put("displayName", name), licenses, NetworkClient.Auth.ACTIVATED));
    }

    Profile pair(String pairingCode) throws Exception {
        String code = pairingCode == null ? "" : pairingCode.replaceAll("[^A-Za-z0-9]", "")
            .toUpperCase(Locale.ROOT);
        if (code.length() != 6) throw new IllegalArgumentException("请输入搭子的 6 位配对码");
        return parseProfile(NetworkClient.json(context, "POST", "/api/companion/pair",
            new JSONObject().put("code", code), licenses, NetworkClient.Auth.ACTIVATED));
    }

    Profile unpair() throws Exception {
        return parseProfile(NetworkClient.json(context, "DELETE", "/api/companion/pair", null,
            licenses, NetworkClient.Auth.ACTIVATED));
    }

    String sendCurrentGif() throws Exception {
        byte[] gif = pets.readGif(pets.selectedPet(), MAXIMUM_GIF_BYTES);
        byte[] response = NetworkClient.request(context, "POST", "/api/companion/deliveries", gif,
            "image/gif", licenses, NetworkClient.Auth.ACTIVATED, NetworkClient.DEFAULT_MAX_RESPONSE);
        JSONObject json = new JSONObject(new String(response, java.nio.charset.StandardCharsets.UTF_8));
        String recipient = json.optString("recipientName");
        if (recipient.trim().isEmpty()) throw new IOException("搭子服务返回的数据无效");
        return recipient;
    }

    List<Visit> receive() throws Exception {
        JSONObject response = NetworkClient.json(context, "GET", "/api/companion/deliveries", null,
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
                || !downloadPath.startsWith("/api/companion/")) continue;
            byte[] gif = NetworkClient.request(context, "GET", downloadPath, null, null,
                licenses, NetworkClient.Auth.ACTIVATED, MAXIMUM_GIF_BYTES);
            validateGif(gif, hash);
            File file = pets.saveInboxGif(id, gif);
            NetworkClient.json(context, "POST", "/api/companion/deliveries/" + id + "/acknowledge",
                null, licenses, NetworkClient.Auth.ACTIVATED);
            visits.add(new Visit(id, sender, file));
        }
        return visits;
    }

    private static Profile parseProfile(JSONObject json) throws Exception {
        JSONObject partnerJson = json.optJSONObject("partner");
        Partner partner = partnerJson == null ? null
            : new Partner(partnerJson.optString("displayName", "搭子"), partnerJson.optString("pairedAt"));
        String displayName = json.optString("displayName");
        String pairingCode = json.optString("pairingCode");
        if (displayName.trim().isEmpty() || pairingCode.length() != 6) throw new IOException("搭子服务返回的数据无效");
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
