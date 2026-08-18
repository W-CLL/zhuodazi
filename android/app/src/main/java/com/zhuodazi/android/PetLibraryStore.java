package com.zhuodazi.android;

import android.content.ContentResolver;
import android.content.Context;
import android.content.Intent;
import android.database.Cursor;
import android.net.Uri;
import android.provider.DocumentsContract;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.UUID;

final class PetLibraryStore {
    static final int MAX_LIBRARIES = 3;
    static final int MAX_LIBRARY_GIFS = 500;
    static final String BUILTIN_ID = "builtin";

    static final class Library {
        final String id;
        final String name;
        final String uri;

        Library(String id, String name, String uri) {
            this.id = id;
            this.name = name;
            this.uri = uri;
        }

        JSONObject toJson() {
            JSONObject value = new JSONObject();
            try {
                value.put("id", id);
                value.put("name", name);
                value.put("uri", uri);
            } catch (Exception ignored) { }
            return value;
        }

        Map<String, Object> toMap(int gifCount) {
            Map<String, Object> value = new LinkedHashMap<>();
            value.put("id", id);
            value.put("name", name);
            value.put("uri", uri);
            value.put("gifCount", gifCount);
            return value;
        }
    }

    static final class LibraryGif {
        final String id;
        final String name;
        final Uri uri;

        LibraryGif(String id, String name, Uri uri) {
            this.id = id;
            this.name = name;
            this.uri = uri;
        }
    }

    private final Context context;
    private final SettingsStore settings;

    PetLibraryStore(Context context, SettingsStore settings) {
        this.context = context.getApplicationContext();
        this.settings = settings;
    }

    List<Library> libraries() {
        List<Library> result = new ArrayList<>();
        try {
            JSONArray items = new JSONArray(settings.petLibrariesJson());
            for (int index = 0; index < items.length() && result.size() < MAX_LIBRARIES; index++) {
                JSONObject item = items.optJSONObject(index);
                if (item == null) continue;
                String id = item.optString("id", "").trim();
                String uri = item.optString("uri", "").trim();
                String name = item.optString("name", "").trim();
                if (id.isEmpty() || uri.isEmpty()) continue;
                if (result.stream().anyMatch(current -> current.uri.equals(uri))) continue;
                result.add(new Library(id, name.isEmpty() ? "图鉴目录" : name, uri));
            }
        } catch (Exception ignored) { }
        return result;
    }

    Library activeLibrary() {
        String selected = settings.activeLibrary();
        for (Library library : libraries()) {
            if (library.id.equals(selected)) return library;
        }
        return null;
    }

    Library add(Uri treeUri) {
        return add(treeUri, Intent.FLAG_GRANT_READ_URI_PERMISSION);
    }

    Library add(Uri treeUri, int flags) {
        if (treeUri == null) throw new IllegalArgumentException("请选择一个 GIF 目录");
        String persisted = persistTree(treeUri, flags);
        List<Library> current = libraries();
        for (Library library : current) {
            if (library.uri.equals(persisted)) {
                settings.putString(SettingsStore.ACTIVE_LIBRARY, library.id);
                return library;
            }
        }
        if (current.size() >= MAX_LIBRARIES) throw new IllegalStateException("最多只能绑定 3 个图鉴目录");
        String name = directoryName(treeUri);
        Library library = new Library("library-" + UUID.randomUUID().toString().replace("-", ""),
            name.isEmpty() ? "图鉴目录" : name, persisted);
        current.add(library);
        persist(current);
        settings.putString(SettingsStore.ACTIVE_LIBRARY, library.id);
        return library;
    }

    void select(String id) {
        if (id == null || id.isEmpty() || BUILTIN_ID.equals(id)) {
            settings.putString(SettingsStore.ACTIVE_LIBRARY, "");
            return;
        }
        for (Library library : libraries()) {
            if (library.id.equals(id)) {
                settings.putString(SettingsStore.ACTIVE_LIBRARY, library.id);
                return;
            }
        }
        throw new IllegalArgumentException("所选图鉴目录不存在");
    }

    void delete(String id) {
        if (id == null || id.isEmpty()) return;
        List<Library> current = libraries();
        Library removed = null;
        for (Library library : current) {
            if (library.id.equals(id)) { removed = library; break; }
        }
        if (removed == null) return;
        current.remove(removed);
        persist(current);
        if (removed.id.equals(settings.activeLibrary())) settings.putString(SettingsStore.ACTIVE_LIBRARY, "");
        try {
            context.getContentResolver().releasePersistableUriPermission(Uri.parse(removed.uri),
                Intent.FLAG_GRANT_READ_URI_PERMISSION);
        } catch (Exception ignored) { }
    }

    List<LibraryGif> scan(Library library) {
        if (library == null) return List.of();
        Uri tree = Uri.parse(library.uri);
        String documentId;
        try { documentId = DocumentsContract.getTreeDocumentId(tree); }
        catch (Exception error) { return List.of(); }
        Uri children = DocumentsContract.buildChildDocumentsUriUsingTree(tree, documentId);
        List<LibraryGif> result = new ArrayList<>();
        scanChildren(tree, children, result);
        return result;
    }

    private void scanChildren(Uri tree, Uri children, List<LibraryGif> result) {
        if (result.size() >= MAX_LIBRARY_GIFS) return;
        String[] columns = {
            DocumentsContract.Document.COLUMN_DOCUMENT_ID,
            DocumentsContract.Document.COLUMN_DISPLAY_NAME,
            DocumentsContract.Document.COLUMN_MIME_TYPE
        };
        try (Cursor cursor = context.getContentResolver().query(children, columns, null, null, null)) {
            if (cursor == null) return;
            int idIndex = cursor.getColumnIndex(DocumentsContract.Document.COLUMN_DOCUMENT_ID);
            int nameIndex = cursor.getColumnIndex(DocumentsContract.Document.COLUMN_DISPLAY_NAME);
            int mimeIndex = cursor.getColumnIndex(DocumentsContract.Document.COLUMN_MIME_TYPE);
            if (idIndex < 0 || nameIndex < 0 || mimeIndex < 0) return;
            while (cursor.moveToNext() && result.size() < MAX_LIBRARY_GIFS) {
                String documentId = cursor.getString(idIndex);
                String name = cursor.getString(nameIndex);
                String mime = cursor.getString(mimeIndex);
                if (documentId == null) continue;
                Uri child = DocumentsContract.buildDocumentUriUsingTree(tree, documentId);
                if (DocumentsContract.Document.MIME_TYPE_DIR.equals(mime)) {
                    scanChildren(tree, DocumentsContract.buildChildDocumentsUriUsingTree(tree, documentId), result);
                    continue;
                }
                if (!isGif(name, mime)) continue;
                result.add(new LibraryGif(
                    "library:" + encode(child.toString()),
                    displayName(name),
                    child));
            }
        } catch (Exception ignored) { }
    }

    private String persistTree(Uri treeUri, int flags) {
        ContentResolver resolver = context.getContentResolver();
        int persistable = flags & (Intent.FLAG_GRANT_READ_URI_PERMISSION
            | Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
        if (persistable == 0) persistable = Intent.FLAG_GRANT_READ_URI_PERMISSION;
        try { resolver.takePersistableUriPermission(treeUri, persistable); }
        catch (SecurityException error) {
            throw new IllegalStateException("无法长期读取该目录，请重新选择", error);
        }
        return treeUri.toString();
    }

    private void persist(List<Library> libraries) {
        JSONArray items = new JSONArray();
        for (Library library : libraries) items.put(library.toJson());
        settings.putString(SettingsStore.PET_LIBRARIES, items.toString());
    }

    private String directoryName(Uri treeUri) {
        String last = treeUri.getLastPathSegment();
        if (last == null || last.isEmpty()) return "图鉴目录";
        int colon = last.lastIndexOf(':');
        String name = colon >= 0 ? last.substring(colon + 1) : last;
        if (name.contains("/")) name = name.substring(name.lastIndexOf('/') + 1);
        return name.isEmpty() ? "图鉴目录" : Uri.decode(name);
    }

    private static boolean isGif(String name, String mime) {
        String fileName = name == null ? "" : name.toLowerCase(Locale.ROOT);
        String type = mime == null ? "" : mime.toLowerCase(Locale.ROOT);
        return fileName.endsWith(".gif") || "image/gif".equals(type);
    }

    private static String displayName(String name) {
        if (name == null || name.trim().isEmpty()) return "目录 GIF";
        int dot = name.lastIndexOf('.');
        String stem = dot > 0 ? name.substring(0, dot) : name;
        return stem.trim().isEmpty() ? "目录 GIF" : stem;
    }

    static String encode(String value) {
        return Uri.encode(value);
    }

    static String decodeId(String petId) {
        if (petId == null || !petId.startsWith("library:")) return "";
        return Uri.decode(petId.substring("library:".length()));
    }
}
