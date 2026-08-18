package com.zhuodazi.android;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.UUID;

final class TheaterScriptStore {
    static final int MAX_SCRIPTS = 10;
    static final int MIN_SCENES = 3;
    static final int MAX_SCENES = 5;
    static final int MAX_FILE_BYTES = 256 * 1024;

    static final class Scene {
        final String main;
        final String companion;

        Scene(String main, String companion) {
            this.main = main;
            this.companion = companion;
        }

        JSONObject toJson() {
            JSONObject value = new JSONObject();
            try {
                value.put("main", main);
                value.put("companion", companion);
            } catch (Exception ignored) { }
            return value;
        }
    }

    static final class Script {
        final String id;
        final String name;
        final List<Scene> scenes;

        Script(String id, String name, List<Scene> scenes) {
            this.id = id;
            this.name = name;
            this.scenes = scenes;
        }

        JSONObject toJson() {
            JSONArray items = new JSONArray();
            for (Scene scene : scenes) items.put(scene.toJson());
            JSONObject value = new JSONObject();
            try {
                value.put("id", id);
                value.put("name", name);
                value.put("scenes", items);
            } catch (Exception ignored) { }
            return value;
        }

        java.util.Map<String, Object> toMap() {
            List<java.util.Map<String, String>> sceneMaps = new ArrayList<>();
            for (Scene scene : scenes) {
                java.util.Map<String, String> item = new java.util.LinkedHashMap<>();
                item.put("main", scene.main);
                item.put("companion", scene.companion);
                sceneMaps.add(item);
            }
            java.util.Map<String, Object> value = new java.util.LinkedHashMap<>();
            value.put("id", id);
            value.put("name", name);
            value.put("scenes", sceneMaps);
            return value;
        }
    }

    private final SettingsStore settings;

    TheaterScriptStore(SettingsStore settings) {
        this.settings = settings;
    }

    List<Script> scripts() {
        List<Script> result = new ArrayList<>();
        try {
            JSONArray items = new JSONArray(settings.theaterScriptsJson());
            for (int index = 0; index < items.length() && result.size() < MAX_SCRIPTS; index++) {
                Script script = fromJson(items.optJSONObject(index), null);
                if (script != null) result.add(script);
            }
        } catch (Exception ignored) { }
        return result;
    }

    List<Script> playbackPool() {
        List<Script> imported = scripts();
        return imported.isEmpty() ? builtInScripts() : imported;
    }

    Script importFile(File file) throws Exception {
        if (file == null || !file.isFile()) throw new IllegalArgumentException("请选择有效的小剧场 JSON 剧本。");
        String name = file.getName().toLowerCase(Locale.ROOT);
        if (!name.endsWith(".json")) throw new IllegalArgumentException("请选择有效的小剧场 JSON 剧本。");
        if (file.length() > MAX_FILE_BYTES) throw new IllegalArgumentException("小剧场剧本不能超过 256 KB。");
        try (FileInputStream input = new FileInputStream(file)) {
            return importJson(readLimited(input, MAX_FILE_BYTES).getBytes(StandardCharsets.UTF_8), file.getName());
        }
    }

    Script importJson(byte[] bytes, String fallbackName) throws Exception {
        if (bytes == null || bytes.length == 0) throw new IllegalArgumentException("请选择有效的小剧场 JSON 剧本。");
        if (bytes.length > MAX_FILE_BYTES) throw new IllegalArgumentException("小剧场剧本不能超过 256 KB。");
        JSONObject root = new JSONObject(new String(bytes, StandardCharsets.UTF_8));
        Script parsed = fromJson(root, fallbackName);
        if (parsed == null) throw new IllegalArgumentException("剧本至少需要 3 组有效的 main/companion 对白。");
        List<Script> current = scripts();
        if (current.size() >= MAX_SCRIPTS) throw new IllegalStateException("最多只能保存 10 个小剧场剧本。");
        String uniqueName = uniqueName(parsed.name, current);
        Script stored = new Script("theater-" + UUID.randomUUID().toString().replace("-", ""), uniqueName, parsed.scenes);
        current.add(stored);
        persist(current);
        return stored;
    }

    void delete(String id) {
        if (id == null || id.trim().isEmpty()) return;
        List<Script> current = scripts();
        current.removeIf(item -> id.equals(item.id));
        persist(current);
    }

    private void persist(List<Script> scripts) {
        JSONArray items = new JSONArray();
        for (Script script : scripts) items.put(script.toJson());
        settings.putString(SettingsStore.THEATER_SCRIPTS, items.toString());
    }

    private static String uniqueName(String name, List<Script> existing) {
        String base = (name == null || name.trim().isEmpty()) ? "小剧场剧本" : name;
        String candidate = base;
        int suffix = 2;
        while (containsName(existing, candidate)) {
            candidate = base + " " + suffix;
            suffix++;
        }
        return candidate;
    }

    private static boolean containsName(List<Script> existing, String name) {
        for (Script script : existing) {
            if (script.name.equalsIgnoreCase(name)) return true;
        }
        return false;
    }

    private static Script fromJson(JSONObject root, String fallbackName) {
        if (root == null) return null;
        JSONArray rawScenes = root.optJSONArray("scenes");
        if (rawScenes == null) return null;
        List<Scene> scenes = new ArrayList<>();
        for (int index = 0; index < rawScenes.length() && scenes.size() < MAX_SCENES; index++) {
            JSONObject item = rawScenes.optJSONObject(index);
            if (item == null) continue;
            String main = cleanLine(firstString(item, "main", "actorA"), 60);
            String companion = cleanLine(firstString(item, "companion", "actorB"), 60);
            if (main.isEmpty() || companion.isEmpty()) continue;
            scenes.add(new Scene(main, companion));
        }
        if (scenes.size() < MIN_SCENES) return null;
        String id = cleanLine(root.optString("id"), 64);
        if (id.isEmpty()) id = "theater-" + UUID.randomUUID().toString().replace("-", "");
        String name = cleanLine(root.optString("name"), 40);
        if (name.isEmpty()) name = cleanLine(stripExtension(fallbackName), 40);
        if (name.isEmpty()) name = "小剧场剧本";
        return new Script(id, name, scenes);
    }

    private static String firstString(JSONObject item, String primary, String alias) {
        String value = item.optString(primary, "").trim();
        return value.isEmpty() ? item.optString(alias, "").trim() : value;
    }

    private static String cleanLine(String value, int limit) {
        if (value == null) return "";
        String cleaned = value.replace('\u00a0', ' ').trim().replaceAll("\\s+", " ");
        return cleaned.length() <= limit ? cleaned : cleaned.substring(0, limit);
    }

    private static String stripExtension(String name) {
        if (name == null) return "";
        int slash = Math.max(name.lastIndexOf('/'), name.lastIndexOf('\\'));
        String file = slash >= 0 ? name.substring(slash + 1) : name;
        int dot = file.lastIndexOf('.');
        return dot > 0 ? file.substring(0, dot) : file;
    }

    static List<Script> builtInScripts() {
        List<Script> scripts = new ArrayList<>();
        scripts.add(script("准时下班行动",
            scene("我宣布，今天最重要的任务是准时下班。", "收到，我已经把时钟放在最显眼的位置。"),
            scene("可是待办列表看起来还很长。", "先分清必须完成和可以明天继续的事情。"),
            scene("要是突然又来一个紧急需求呢？", "先问截止时间和优先级，别让所有事情都变成最高级。"),
            scene("有道理，我现在专心完成手上这一项。", "我负责提醒你保存文件，也提醒你起来喝水。"),
            scene("计划通过，收尾之后一起撤退！", "行动代号：关电脑之前再检查一次提交。")));
        scripts.add(script("零食失踪案",
            scene("报告，我放在桌边的小饼干不见了。", "先别慌，请描述它最后一次出现的位置。"),
            scene("就在键盘旁边，包装还是完整的。", "现场只有你、我，还有一杯看起来很可疑的咖啡。"),
            scene("咖啡没有手，应该拿不走饼干吧？", "也可能有人边想问题，边无意识地把它吃掉了。"),
            scene("等等，我口袋里为什么有一张包装纸。", "证据已经出现，案件正在变得简单。"),
            scene("我承认，是过去的我给现在的我留了个谜题。", "结案。下一包零食请登记后再吃。")));
        scripts.add(script("灵感紧急会议",
            scene("我盯着空白页面十分钟了，灵感还是没来。", "那就先写一个绝对不会采用的版本。"),
            scene("故意写差，真的会有用吗？", "空白最难修改，有了第一句就能知道哪里不满意。"),
            scene("好，我先写：这是一个非常普通的开头。", "很好，现在问问自己，它怎样才会不普通。"),
            scene("也许让主角一出门就遇见会说话的桌宠。", "这个方向不错，而且演员已经在现场了。"),
            scene("原来灵感不是等来的，是聊着聊着长出来的。", "会议结束，趁它还热赶快写下来。")));
        return scripts;
    }

    private static Script script(String name, Scene... scenes) {
        return new Script("builtin-" + name, name, java.util.Arrays.asList(scenes));
    }

    private static Scene scene(String main, String companion) {
        return new Scene(main, companion);
    }

    static String readLimited(InputStream input, int maximumBytes) throws Exception {
        try (InputStream source = input; ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[8192];
            int count;
            while ((count = source.read(buffer)) >= 0) {
                if (output.size() + count > maximumBytes) throw new IllegalArgumentException("小剧场剧本不能超过 256 KB。");
                output.write(buffer, 0, count);
            }
            return output.toString(StandardCharsets.UTF_8);
        }
    }
}
