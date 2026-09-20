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
import java.net.URLEncoder;
import java.nio.charset.StandardCharsets;

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
            licenses, NetworkClient.Auth.PREMIUM));
    }

    Profile updateName(String displayName) throws Exception {
        String name = displayName == null ? "" : displayName.trim();
        if (name.isEmpty() || name.codePointCount(0, name.length()) > 12) throw new IllegalArgumentException("昵称需为 1 至 12 个字符");
        return parseProfile(NetworkClient.json(context, "PATCH", DeskPetApi.COMPANION,
            new JSONObject().put("displayName", name), licenses, NetworkClient.Auth.PREMIUM));
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

    Hall refreshHall() throws Exception {
        JSONObject json = NetworkClient.json(context, "GET", DeskPetApi.COMPANION_HALL, null,
            licenses, NetworkClient.Auth.PREMIUM);
        JSONArray peopleJson = json.optJSONArray("people");
        List<HallPerson> people = new ArrayList<>();
        if (peopleJson != null) {
            for (int index = 0; index < peopleJson.length(); index++) {
                JSONObject item = peopleJson.optJSONObject(index);
                if (item == null) continue;
                String id = item.optString("id").trim();
                String name = item.optString("displayName", "桌搭子").trim();
                if (!id.isEmpty() && !name.isEmpty()) people.add(new HallPerson(id, name, item.optBoolean("online", true)));
            }
        }
        return new Hall(json.optBoolean("enabled", false), people);
    }

    Profile setHallEnabled(boolean enabled) throws Exception {
        return parseProfile(NetworkClient.json(context, "PATCH", DeskPetApi.COMPANION_HALL,
            new JSONObject().put("enabled", enabled), licenses, NetworkClient.Auth.PREMIUM));
    }

    String sendToHall(String recipientId, String message, String petId) throws Exception {
        String target = recipientId == null ? "" : recipientId.trim();
        if (target.isEmpty() || target.length() > 128) throw new IllegalArgumentException("请选择一位在线用户");
        if (message != null && message.codePointCount(0, message.length()) > 120) throw new IllegalArgumentException("留言最多 120 个字符");
        byte[] gif = pets.readGif(petId == null || petId.isEmpty() ? pets.selectedPet() : petId, MAXIMUM_GIF_BYTES);
        String path = DeskPetApi.COMPANION_HALL_DELIVERIES + "/"
            + URLEncoder.encode(target, "UTF-8").replace("+", "%20")
            + "?message=" + URLEncoder.encode(message == null ? "" : message.trim(), "UTF-8").replace("+", "%20");
        byte[] response = NetworkClient.request(context, "POST", path, gif, "image/gif", licenses,
            NetworkClient.Auth.PREMIUM, NetworkClient.DEFAULT_MAX_RESPONSE);
        JSONObject json = new JSONObject(new String(response, StandardCharsets.UTF_8));
        String recipient = json.optString("recipientName");
        if (recipient.trim().isEmpty()) throw new IOException("搭子服务返回的数据无效");
        return recipient;
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
        LicenseService session = licenses.snapshot();
        String owner = session.visitOwner();
        JSONObject body = new JSONObject().put("category", category == null ? "" : category);
        JSONObject json = NetworkClient.json(context, "POST", DeskPetApi.TRIAL_VISIT_PLAY, body,
            session, NetworkClient.Auth.PREMIUM);
        String id = json.optString("id");
        String sender = json.optString("senderName", "桌搭子");
        String hash = json.optString("sha256");
        String downloadPath = json.optString("downloadPath");
        if (!id.matches("[A-Za-z0-9._:-]{1,128}") || !hash.matches("(?i)[0-9a-f]{64}")
            || !downloadPath.matches("/api/trial/visit-stickers/[0-9a-fA-F-]{36}/file")) {
            throw new IOException("来访表情无效");
        }
        byte[] gif = NetworkClient.request(context, "GET", downloadPath, null, null,
            session, NetworkClient.Auth.PREMIUM, MAXIMUM_GIF_BYTES);
        validateGif(gif, hash);
        synchronized (LicenseService.IDENTITY_LOCK) {
            requireOwner(owner);
            Visit visit = new Visit(owner, id, sender, "", pets.saveInboxGif(PendingVisitQueue.ownerKey(owner) + "-" + id, gif));
            retainVisit(visit);
            return visit;
        }
    }

    List<Visit> receive() throws Exception {
        LicenseService session = licenses.snapshot();
        String owner = session.visitOwner();
        PendingVisitQueue inbox = inbox(owner);
        JSONObject response = NetworkClient.json(context, "GET", DeskPetApi.COMPANION_DELIVERIES, null,
            session, NetworkClient.Auth.PREMIUM);
        JSONArray deliveries = response.optJSONArray("deliveries");
        List<Visit> visits = new ArrayList<>();
        if (deliveries == null) return visits;
        for (int index = 0; index < Math.min(10, deliveries.length()); index++) {
            requireOwner(owner);
            JSONObject item = deliveries.optJSONObject(index);
            if (item == null) continue;
            String id = item.optString("id");
            String sender = item.optString("senderName", "搭子");
            String message = item.optString("message", "");
            String hash = item.optString("sha256");
            String downloadPath = item.optString("downloadPath");
            if (!id.matches("[A-Za-z0-9._:-]{1,128}") || !hash.matches("(?i)[0-9a-f]{64}")
                || !downloadPath.startsWith(DeskPetApi.COMPANION_DOWNLOAD_PREFIX)) continue;
            if (!inbox.contains(id)) {
                byte[] gif = NetworkClient.request(context, "GET", downloadPath, null, null,
                    session, NetworkClient.Auth.PREMIUM, MAXIMUM_GIF_BYTES);
                validateGif(gif, hash);
                synchronized (LicenseService.IDENTITY_LOCK) {
                    requireOwner(owner);
                    File file = pets.saveInboxGif(PendingVisitQueue.ownerKey(owner) + "-" + id, gif);
                    // Persist before acknowledging device receipt, including when presentation is paused.
                    inbox.enqueue(new PendingVisitQueue.Entry(id, sender, message, file.getAbsolutePath()));
                }
            }
            NetworkClient.json(context, "POST", DeskPetApi.COMPANION_DELIVERIES + "/" + id + "/acknowledge",
                null, session, NetworkClient.Auth.PREMIUM);
        }
        return pendingVisits(owner);
    }

    List<Visit> pendingVisits() throws IOException {
        return pendingVisits(licenses.visitOwner());
    }

    private List<Visit> pendingVisits(String owner) throws IOException {
        List<Visit> result = new ArrayList<>();
        for (PendingVisitQueue.Entry item : inbox(owner).pending()) {
            result.add(new Visit(owner, item.id(), item.senderName(), item.message(), new File(item.path())));
        }
        return result;
    }

    boolean retainVisit(Visit visit) throws IOException {
        synchronized (LicenseService.IDENTITY_LOCK) {
            if (!isCurrentVisit(visit)) return false;
            inbox(visit.owner()).enqueue(new PendingVisitQueue.Entry(visit.id(), visit.senderName(), visit.message(), visit.file().getAbsolutePath()));
            return true;
        }
    }

    void completeVisit(Visit visit) throws IOException {
        synchronized (LicenseService.IDENTITY_LOCK) {
            requireOwner(visit.owner());
            inbox(visit.owner()).complete(visit.id());
        }
    }

    boolean isCurrentVisit(Visit visit) { return visit.owner().equals(licenses.visitOwner()); }

    private void requireOwner(String owner) throws IOException {
        if (!owner.equals(licenses.visitOwner())) throw new IOException("账号已切换，请重试");
    }

    private PendingVisitQueue inbox(String owner) { return PendingVisitQueue.forOwner(context.getFilesDir(), owner); }

    private Profile parseProfile(JSONObject json) throws Exception {
        JSONObject payload = json.optJSONObject("profile");
        if (payload == null) payload = json;
        JSONObject partnerJson = payload.optJSONObject("partner");
        Partner partner = partnerJson == null ? null
            : new Partner(partnerJson.optString("displayName", "搭子"), partnerJson.optString("pairedAt"));
        String displayName = payload.optString("displayName").trim();
        if (displayName.isEmpty()) displayName = "桌搭子";
        String pairingCode = payload.optString("pairingCode").replaceAll("[^A-Za-z0-9]", "")
            .toUpperCase(Locale.ROOT);
        if ((licenses.isActivated() || !pairingCode.isEmpty()) && pairingCode.length() != 6 && pairingCode.length() != 8) {
            throw new IOException("搭子服务未返回有效配对码，请稍后重试");
        }
        return new Profile(displayName, pairingCode, partner,
            payload.optBoolean("hallEnabled", false), payload.optBoolean("online", false));
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
    record Profile(String displayName, String pairingCode, Partner partner, boolean hallEnabled, boolean online) { }
    record Hall(boolean enabled, List<HallPerson> people) { }
    record HallPerson(String id, String displayName, boolean online) { }
    record Visit(String owner, String id, String senderName, String message, File file) { }
}
