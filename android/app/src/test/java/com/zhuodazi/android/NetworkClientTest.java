package com.zhuodazi.android;

import org.junit.Test;
import java.nio.charset.StandardCharsets;
import static org.junit.Assert.*;

public class NetworkClientTest {
    @Test public void trialHallDenialKeepsStatusAndReasonForFlutter() {
        NetworkClient.HttpFailure failure = NetworkClient.httpFailure(new NetworkClient.HttpResult(403,
            "{\"error\":\"激活完整版本后可以使用搭子联机\",\"code\":\"COMPANION_ACTIVATION_REQUIRED\"}"
                .getBytes(StandardCharsets.UTF_8)));
        assertEquals(403, failure.status);
        assertEquals("COMPANION_ACTIVATION_REQUIRED", failure.serverCode);
        assertEquals("激活完整版本后可以使用搭子联机", failure.getMessage());
    }

    @Test public void nonJsonOutageStillKeepsHttpStatus() {
        NetworkClient.HttpFailure failure = NetworkClient.httpFailure(new NetworkClient.HttpResult(503,
            "<html>Service Unavailable</html>".getBytes(StandardCharsets.UTF_8)));
        assertEquals(503, failure.status);
        assertEquals("", failure.serverCode);
        assertEquals("服务请求失败（503）", failure.getMessage());
    }

    @Test public void expiredTrialIsDistinctFromActivationRequirement() {
        NetworkClient.HttpFailure failure = NetworkClient.httpFailure(new NetworkClient.HttpResult(401,
            "{\"error\":\"体验已结束\",\"code\":\"TRIAL_EXPIRED\"}".getBytes(StandardCharsets.UTF_8)));
        assertEquals(401, failure.status);
        assertEquals("TRIAL_EXPIRED", failure.serverCode);
    }
}
