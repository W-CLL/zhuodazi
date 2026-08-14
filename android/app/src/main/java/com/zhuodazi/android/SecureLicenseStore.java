package com.zhuodazi.android;

import android.content.Context;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;

import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import java.security.SecureRandom;
import java.util.UUID;

import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

final class SecureLicenseStore {
    private static final String KEY_ALIAS = "zhuodazi_android_license_v1";
    private static final byte FILE_VERSION = 1;
    private final File recordFile;
    private LicenseRecord record;

    SecureLicenseStore(Context context) {
        recordFile = new File(context.getFilesDir(), "license.secure");
        record = load();
        if (record == null || !record.isValid()) {
            record = LicenseRecord.create();
            save();
        }
    }

    synchronized LicenseRecord record() { return record.copy(); }

    synchronized void activate(String licenseId, String activatedAt) {
        record.licenseId = licenseId;
        record.activatedAt = activatedAt == null ? "" : activatedAt;
        save();
    }

    private LicenseRecord load() {
        if (!recordFile.isFile()) return null;
        try (FileInputStream input = new FileInputStream(recordFile);
             ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[4096];
            int count;
            while ((count = input.read(buffer)) >= 0) {
                if (output.size() + count > 16 * 1024) return null;
                output.write(buffer, 0, count);
            }
            byte[] file = output.toByteArray();
            if (file.length < 16 || file[0] != FILE_VERSION) return null;
            int ivLength = file[1] & 0xff;
            if (ivLength != 12 || file.length <= 2 + ivLength) return null;
            byte[] iv = new byte[ivLength];
            byte[] ciphertext = new byte[file.length - 2 - ivLength];
            System.arraycopy(file, 2, iv, 0, ivLength);
            System.arraycopy(file, 2 + ivLength, ciphertext, 0, ciphertext.length);
            Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
            cipher.init(Cipher.DECRYPT_MODE, key(), new GCMParameterSpec(128, iv));
            JSONObject json = new JSONObject(new String(cipher.doFinal(ciphertext), StandardCharsets.UTF_8));
            return LicenseRecord.fromJson(json);
        } catch (Exception ignored) {
            return null;
        }
    }

    private void save() {
        try {
            byte[] plaintext = record.toJson().toString().getBytes(StandardCharsets.UTF_8);
            Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
            cipher.init(Cipher.ENCRYPT_MODE, key());
            byte[] ciphertext = cipher.doFinal(plaintext);
            byte[] iv = cipher.getIV();
            File pending = new File(recordFile.getParentFile(), recordFile.getName() + ".pending");
            try (FileOutputStream output = new FileOutputStream(pending)) {
                output.write(FILE_VERSION);
                output.write(iv.length);
                output.write(iv);
                output.write(ciphertext);
                output.getFD().sync();
            }
            if (recordFile.exists() && !recordFile.delete()) throw new IllegalStateException("无法更新激活凭据");
            if (!pending.renameTo(recordFile)) throw new IllegalStateException("无法保存激活凭据");
        } catch (Exception error) {
            throw new IllegalStateException("无法安全保存设备凭据", error);
        }
    }

    private SecretKey key() throws Exception {
        KeyStore keyStore = KeyStore.getInstance("AndroidKeyStore");
        keyStore.load(null);
        if (keyStore.containsAlias(KEY_ALIAS)) {
            return ((KeyStore.SecretKeyEntry) keyStore.getEntry(KEY_ALIAS, null)).getSecretKey();
        }
        KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore");
        generator.init(new KeyGenParameterSpec.Builder(
            KEY_ALIAS, KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
            .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
            .setRandomizedEncryptionRequired(true)
            .build());
        return generator.generateKey();
    }

    static final class LicenseRecord {
        final int version;
        final String installationId;
        final String credential;
        String licenseId;
        String activatedAt;

        private LicenseRecord(int version, String installationId, String credential,
                              String licenseId, String activatedAt) {
            this.version = version;
            this.installationId = installationId;
            this.credential = credential;
            this.licenseId = licenseId == null ? "" : licenseId;
            this.activatedAt = activatedAt == null ? "" : activatedAt;
        }

        static LicenseRecord create() {
            SecureRandom random = new SecureRandom();
            byte[] installation = new byte[16];
            byte[] credential = new byte[32];
            random.nextBytes(installation);
            random.nextBytes(credential);
            return new LicenseRecord(1, hex(installation),
                Base64.encodeToString(credential, Base64.URL_SAFE | Base64.NO_WRAP | Base64.NO_PADDING), "", "");
        }

        static LicenseRecord fromJson(JSONObject json) {
            return new LicenseRecord(json.optInt("version"), json.optString("installationId"),
                json.optString("credential"), json.optString("licenseId"), json.optString("activatedAt"));
        }

        JSONObject toJson() {
            try {
                return new JSONObject().put("version", version).put("installationId", installationId)
                    .put("credential", credential).put("licenseId", licenseId).put("activatedAt", activatedAt);
            } catch (Exception error) { throw new IllegalStateException(error); }
        }

        LicenseRecord copy() { return new LicenseRecord(version, installationId, credential, licenseId, activatedAt); }

        boolean isActivated() {
            try { return !licenseId.isEmpty() && UUID.fromString(licenseId).toString().equalsIgnoreCase(licenseId); }
            catch (Exception ignored) { return false; }
        }

        boolean isValid() {
            return version == 1 && installationId.matches("[0-9a-f]{32}")
                && credential.matches("[A-Za-z0-9_-]{43}") && (licenseId.isEmpty() || isActivated());
        }

        private static String hex(byte[] bytes) {
            StringBuilder result = new StringBuilder(bytes.length * 2);
            for (byte value : bytes) result.append(String.format("%02x", value & 0xff));
            return result.toString();
        }
    }
}
