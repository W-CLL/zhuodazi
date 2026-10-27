package com.zhuodazi.android;

import android.content.Context;
import android.util.Base64;
import org.json.JSONArray;
import org.json.JSONObject;
import org.junit.After;
import org.junit.Before;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.mockito.MockedStatic;
import org.robolectric.RobolectricTestRunner;
import org.robolectric.annotation.Config;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.security.KeyPair;
import java.security.KeyPairGenerator;
import java.security.MessageDigest;
import java.security.Signature;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import static org.junit.Assert.*;
import static org.mockito.ArgumentMatchers.*;
import static org.mockito.Mockito.*;

@RunWith(RobolectricTestRunner.class)
@Config(sdk = 28, application = android.app.Application.class)
public class InteractionContentServiceTest {
    private Context context;
    private LicenseService licenses;
    private InteractionContentService content;
    private Map<String, Object> stored;
    private final List<JSONObject> requests = new ArrayList<>();
    private final List<JSONObject> responses = new ArrayList<>();
    private Exception networkFailure;
    private KeyPair signingKey;
    private MockedStatic<Base64> encoding;
    private MockedStatic<NetworkClient> network;

    @Before public void setUp() throws Exception {
        context = mock(Context.class);
        when(context.getApplicationContext()).thenReturn(context);
        Map<String, Map<String, Object>> files = new HashMap<>();
        stored = files.computeIfAbsent("zhuodazi_interaction_content", key -> new HashMap<>());
        when(context.getSharedPreferences(anyString(), anyInt())).thenAnswer(call ->
            SettingsStoreTest.preferences(files.computeIfAbsent(call.getArgument(0), key -> new HashMap<>())));
        licenses = mock(LicenseService.class);
        when(licenses.hasPremiumAccess()).thenReturn(true);
        content = new InteractionContentService(context, licenses);
        signingKey = KeyPairGenerator.getInstance("Ed25519").generateKeyPair();
        // Exercise the real signature/hash verifier with a test key, never a production private key.
        encoding = mockStatic(Base64.class, CALLS_REAL_METHODS);
        encoding.when(() -> Base64.decode(DeskPetApi.SIGNING_PUBLIC_KEY_SPKI, Base64.DEFAULT))
            .thenReturn(signingKey.getPublic().getEncoded());
        network = mockStatic(NetworkClient.class);
        network.when(() -> NetworkClient.json(eq(context), eq("POST"), eq(DeskPetApi.CONTENT_BATCH),
            any(JSONObject.class), eq(licenses), eq(NetworkClient.Auth.PREMIUM))).thenAnswer(call -> {
                requests.add(new JSONObject(((JSONObject) call.getArgument(3)).toString()));
                if (networkFailure != null) throw networkFailure;
                return responses.remove(0);
            });
    }

    @After public void tearDown() {
        if (network != null) network.close();
        if (encoding != null) encoding.close();
    }

    @Test public void eachTriggerFetchesExactlyOneItemAndFetchAloneDoesNotMarkItShown() throws Exception {
        responses.add(envelope(item("fresh1", "joke")));
        responses.add(envelope(item("fresh2", "tip")));
        assertEquals("fresh1", content.fetchNextOnline(false).id);
        assertEquals("fresh2", content.fetchNextOnline(false).id);
        assertEquals(2, requests.size());
        for (JSONObject request : requests) {
            assertEquals(1, request.getInt("limit"));
            assertEquals(Set.of("joke", "math", "trivia", "riddle", "tip", "care"), strings(request.getJSONArray("types")));
        }
        assertFalse(stored.containsKey("items"));
        assertEquals("[]", stored.get("shown"));
        assertEquals("[]", stored.get("events"));
        network.verify(() -> NetworkClient.json(eq(context), eq("POST"), eq(DeskPetApi.CONTENT_BATCH),
            any(JSONObject.class), eq(licenses), eq(NetworkClient.Auth.PREMIUM)), times(2));
        network.verifyNoMoreInteractions();
    }

    @Test public void quizRequestsAllThreeQuizTypesAndRejectsOtherTypesWithoutRetrying() throws Exception {
        stored.put("last_type", "math");
        responses.add(envelope(item("wrong-category", "joke")));
        assertNull(content.fetchNextOnline(true));
        assertEquals(1, requests.size());
        JSONArray types = requests.get(0).getJSONArray("types");
        assertEquals(Set.of("math", "trivia", "riddle"), strings(types));
        assertEquals("math", types.getString(types.length() - 1));
        assertEquals(1, requests.get(0).getInt("limit"));
        assertEquals("[]", stored.get("events"));
    }

    @Test public void shownHistoryIsRecordedOnlyAfterDisplayAndIsExcludedNextTime() throws Exception {
        responses.add(envelope(item("shown-once", "math")));
        responses.add(envelope(item("next-question", "trivia")));
        InteractionContentService.Item first = content.fetchNextOnline(true);
        assertEquals(0, new JSONArray((String) stored.get("events")).length());
        content.markContentShown(first);
        content.markContentShown(first);
        assertEquals(1, new JSONArray((String) stored.get("events")).length());
        assertEquals("content_shown", new JSONArray((String) stored.get("events")).getJSONObject(0).getString("type"));
        assertEquals("next-question", content.fetchNextOnline(true).id);
        assertEquals(Set.of("shown-once"), strings(requests.get(1).getJSONArray("excludeIds")));
        assertEquals("math", requests.get(1).getJSONArray("types").getString(2));
    }

    @Test public void upgradeDiscardsOldOnlineCacheButPreservesShownEventsAndJournal() throws Exception {
        stored.put("items", new JSONArray().put(item("cached-online", "math")).toString());
        stored.put("shown", "[\"previously-shown\"]");
        stored.put("events", "[{\"eventId\":\"pending\",\"type\":\"content_shown\"}]");
        DailyJournalStore journal = new DailyJournalStore(context);
        journal.recordMood("happy");
        content = new InteractionContentService(context, licenses);
        assertFalse(stored.containsKey("items"));
        assertEquals("[\"previously-shown\"]", stored.get("shown"));
        assertEquals(1, new JSONArray((String) stored.get("events")).length());
        assertEquals(1, new DailyJournalStore(context).entries().size());
        networkFailure = new IOException("offline");
        assertThrows(IOException.class, () -> content.fetchNextOnline(true));
        assertEquals(1, requests.size());
        assertEquals(Set.of("previously-shown"), strings(requests.get(0).getJSONArray("excludeIds")));
        assertEquals(1, new JSONArray((String) stored.get("events")).length());
        assertFalse(stored.containsKey("items"));
    }

    @Test public void emptyResponseHasNoFallbackEvenAfterAnEarlierSuccessfulFetch() throws Exception {
        responses.add(envelope(item("unused", "trivia")));
        responses.add(envelope());
        assertNotNull(content.fetchNextOnline(true));
        assertNull(content.fetchNextOnline(true));
        assertEquals(2, requests.size());
        assertEquals("[]", stored.get("shown"));
        assertEquals("[]", stored.get("events"));
    }

    @Test public void invalidSignatureNeverReturnsAnItemOrMarksAnythingShown() throws Exception {
        JSONObject invalid = envelope(item("invalid-signature", "math"));
        invalid.put("signature", java.util.Base64.getEncoder().encodeToString(new byte[64]));
        responses.add(invalid);
        Exception error = assertThrows(Exception.class, () -> content.fetchNextOnline(true));
        assertTrue(error.getMessage().contains("签名验证失败"));
        assertEquals(1, requests.size());
        assertFalse(stored.containsKey("shown"));
        assertFalse(stored.containsKey("events"));
    }

    @Test public void unsupportedTypeFailureDoesNotRetryWithLegacyTypes() throws Exception {
        networkFailure = new IOException("内容类型无效");
        assertThrows(IOException.class, () -> content.fetchNextOnline(false));
        assertEquals(1, requests.size());
        assertFalse(stored.containsKey("items"));
    }

    private JSONObject item(String id, String type) throws Exception {
        return new JSONObject().put("id", id).put("type", type).put("revision", 1)
            .put("prompt", "新题目？").put("answer", "答案").put("explanation", "说明").put("choices", new JSONArray());
    }

    private JSONObject envelope(JSONObject... items) throws Exception {
        JSONArray values = new JSONArray();
        for (JSONObject item : items) values.put(item);
        byte[] payload = new JSONObject().put("schemaVersion", 1).put("kind", "batch")
            .put("catalogVersion", 1).put("items", values).toString().getBytes(StandardCharsets.UTF_8);
        Signature signer = Signature.getInstance("Ed25519");
        signer.initSign(signingKey.getPrivate());
        signer.update(payload);
        StringBuilder hash = new StringBuilder();
        for (byte value : MessageDigest.getInstance("SHA-256").digest(payload)) hash.append(String.format("%02x", value & 0xff));
        return new JSONObject().put("signatureAlgorithm", "ed25519").put("sha256", hash.toString())
            .put("signedPayload", java.util.Base64.getEncoder().encodeToString(payload))
            .put("signature", java.util.Base64.getEncoder().encodeToString(signer.sign()));
    }

    private Set<String> strings(JSONArray array) throws Exception {
        Set<String> values = new HashSet<>();
        for (int i = 0; i < array.length(); i++) values.add(array.getString(i));
        return values;
    }
}
