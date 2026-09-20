package com.zhuodazi.android;

import java.io.*;
import java.nio.file.*;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.*;

/** Durable inbox. An entry is retained until its full visit has been displayed. */
final class PendingVisitQueue {
    private static final Object LOCK = new Object();
    private static final int FORMAT = 1;
    private final File file;

    record Entry(String id, String senderName, String message, String path) { }
    private record State(List<Entry> pending, List<String> completed) { }

    PendingVisitQueue(File file) { this.file = file; }

    static String ownerKey(String owner) {
        try {
            byte[] bytes = MessageDigest.getInstance("SHA-256").digest(owner.getBytes(StandardCharsets.UTF_8));
            StringBuilder result = new StringBuilder(64);
            for (byte value : bytes) result.append(String.format("%02x", value & 0xff));
            return result.toString();
        } catch (Exception error) { throw new IllegalStateException(error); }
    }

    static PendingVisitQueue forOwner(File filesDirectory, String owner) {
        if (owner == null || owner.isEmpty()) throw new IllegalArgumentException("缺少来访身份");
        return new PendingVisitQueue(new File(filesDirectory, "visit-inboxes/" + ownerKey(owner) + "/pending.bin"));
    }

    static void migrateForActivation(File filesDirectory, String previousOwner, String nextOwner) throws IOException {
        // Only this installation's first trial activation transfers its pending and receipt history.
        if (!previousOwner.startsWith("trial:") || !nextOwner.startsWith("license:")) return;
        synchronized (LOCK) {
            PendingVisitQueue source = forOwner(filesDirectory, previousOwner);
            if (!source.file.isFile()) return;
            PendingVisitQueue destination = forOwner(filesDirectory, nextOwner);
            State old = source.read(), target = destination.read();
            for (String id : old.completed) {
                if (!target.completed.contains(id)) target.completed.add(id);
            }
            target.pending.removeIf(item -> target.completed.contains(item.id));
            for (Entry entry : old.pending) {
                if (!target.completed.contains(entry.id) && target.pending.stream().noneMatch(item -> item.id.equals(entry.id)))
                    target.pending.add(entry);
            }
            if (target.pending.size() > 100) throw new IOException("待看来访已满，请先查看已有来访");
            while (target.completed.size() > 256) target.completed.remove(0);
            destination.write(target);
        }
    }

    static void finishTrialMigration(File filesDirectory, String previousOwner) throws IOException {
        if (!previousOwner.startsWith("trial:")) return;
        synchronized (LOCK) {
            PendingVisitQueue source = forOwner(filesDirectory, previousOwner);
            // After credentials are persisted, discard only the source index, never its GIFs.
            if (source.file.isFile()) source.write(new State(new ArrayList<>(), new ArrayList<>()));
        }
    }

    boolean contains(String id) throws IOException {
        synchronized (LOCK) {
            State state = read();
            return state.completed.contains(id) || state.pending.stream().anyMatch(item -> item.id.equals(id));
        }
    }

    void enqueue(Entry entry) throws IOException {
        synchronized (LOCK) {
            State state = read();
            if (state.completed.contains(entry.id) || state.pending.stream().anyMatch(item -> item.id.equals(entry.id))) return;
            if (state.pending.size() >= 100) throw new IOException("待看来访已满，请先查看已有来访");
            state.pending.add(entry);
            write(state);
        }
    }

    List<Entry> pending() throws IOException {
        synchronized (LOCK) { return new ArrayList<>(read().pending); }
    }

    void complete(String id) throws IOException {
        synchronized (LOCK) {
            State state = read();
            List<Entry> removed = new ArrayList<>();
            state.pending.removeIf(item -> { if (!item.id.equals(id)) return false; removed.add(item); return true; });
            if (!state.completed.contains(id)) state.completed.add(id);
            while (state.completed.size() > 256) state.completed.remove(0);
            write(state);
            for (Entry entry : removed) new File(entry.path).delete();
        }
    }

    private State read() throws IOException {
        if (!file.exists()) return new State(new ArrayList<>(), new ArrayList<>());
        try (DataInputStream input = new DataInputStream(new BufferedInputStream(new FileInputStream(file)))) {
            if (input.readInt() != FORMAT) throw new IOException("来访队列版本无效");
            int count = input.readInt();
            if (count < 0 || count > 100) throw new IOException("来访队列无效");
            List<Entry> pending = new ArrayList<>();
            for (int index = 0; index < count; index++) pending.add(new Entry(input.readUTF(), input.readUTF(), input.readUTF(), input.readUTF()));
            int completedCount = input.readInt();
            if (completedCount < 0 || completedCount > 256) throw new IOException("来访记录无效");
            List<String> completed = new ArrayList<>();
            for (int index = 0; index < completedCount; index++) completed.add(input.readUTF());
            return new State(pending, completed);
        }
    }

    private void write(State state) throws IOException {
        File parent = file.getParentFile();
        if (parent != null && !parent.isDirectory() && !parent.mkdirs()) throw new IOException("无法保存待看来访");
        File temporary = new File(file.getPath() + ".tmp");
        try (FileOutputStream bytes = new FileOutputStream(temporary);
             DataOutputStream output = new DataOutputStream(new BufferedOutputStream(bytes))) {
            output.writeInt(FORMAT);
            output.writeInt(state.pending.size());
            for (Entry entry : state.pending) {
                output.writeUTF(entry.id); output.writeUTF(entry.senderName);
                output.writeUTF(entry.message); output.writeUTF(entry.path);
            }
            output.writeInt(state.completed.size());
            for (String id : state.completed) output.writeUTF(id);
            output.flush();
            bytes.getFD().sync();
        }
        try {
            Files.move(temporary.toPath(), file.toPath(), StandardCopyOption.REPLACE_EXISTING, StandardCopyOption.ATOMIC_MOVE);
        } catch (AtomicMoveNotSupportedException unsupported) {
            Files.move(temporary.toPath(), file.toPath(), StandardCopyOption.REPLACE_EXISTING);
        }
    }
}
