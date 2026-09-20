import 'dart:async';

import 'package:flutter/foundation.dart';

import 'host_api.dart';

class AppController extends ChangeNotifier {
  AppController({HostApi? api}) : _api = api ?? HostApi() {
    _api.listen(_onHostEvent);
  }

  final HostApi _api;
  HostSnapshot snapshot = HostSnapshot.empty();
  UpdateState update = UpdateState.idle();
  Uint8List? petGif;
  Map<String, dynamic>? companion;
  Map<String, dynamic>? companionHall;
  bool loading = true;
  bool busy = false;
  bool companionLoading = false;
  String? companionError;
  HallFailure? hallError;
  bool hallLoading = false;
  DateTime? hallNextSendAt;
  DateTime? _trialSyncedAt;
  int _trialSecondsAtSync = 0;
  Timer? _trialTimer;
  Timer? _quietTimer;
  bool _autoChecked = false;
  bool _awaitingOverlayGrant = false;
  String? _pendingPetAction;
  String operationStatus = '';
  Timer? _runtimeTimer;
  bool _disposed = false;
  bool _runtimePolling = false;

  int get liveTrialSeconds {
    if (snapshot.activated) return 0;
    if (_trialSyncedAt == null) return snapshot.trialSeconds;
    final elapsed = DateTime.now().difference(_trialSyncedAt!).inSeconds;
    final remaining = _trialSecondsAtSync - elapsed;
    if (remaining < 0) return 0;
    return remaining;
  }

  String get licenseLabel {
    if (snapshot.activated) return '已激活';
    if (liveTrialSeconds > 0) return '体验中 ${trialClock(liveTrialSeconds)}';
    return '基础版';
  }

  Future<void> initialize() async {
    loading = true;
    notifyListeners();
    try {
      await _pullSnapshot(checkTrial: false);
      try {
        snapshot = await _api.siteLinks();
        _rememberTrial(snapshot);
        _rememberUpdate(snapshot);
      } catch (_) {}
      await _pullSnapshot(checkTrial: true);
      unawaited(_maybeAutoCheckUpdates());
    } finally {
      loading = false;
      notifyListeners();
    }
  }

  Future<void> refresh({bool checkTrial = false}) async {
    await _pullSnapshot(checkTrial: checkTrial);
    notifyListeners();
  }

  Future<void> service(String action) async {
    if (action == 'stop') {
      _pendingPetAction = null;
      _awaitingOverlayGrant = false;
    }
    const needsVisible = {'next', 'interact', 'theater', 'show', 'start'};
    if (needsVisible.contains(action) && !snapshot.overlayAllowed) {
      _pendingPetAction = action;
      await requestOverlayPermission();
      if (snapshot.overlayAllowed) return;
      throw const HostFailure('允许悬浮窗后回到这里，将继续刚才的操作');
    }
    operationStatus = switch (action) {
      'interact' => '准备中…',
      'theater' => '正在准备小剧场',
      _ => '正在准备桌宠',
    };
    try {
      await _guard(() async {
        snapshot = await _api.serviceAction(action);
        _rememberTrial(snapshot);
        _rememberUpdate(snapshot);
        await _loadActivePet();
      });
    } finally {
      operationStatus = '';
      if (!_disposed) notifyListeners();
    }
  }

  Future<void> guide(String action) async {
    if ({'dismiss', 'dismissNotice'}.contains(action) &&
        (_pendingPetAction?.startsWith('guide:') ?? false)) {
      _pendingPetAction = null;
      _awaitingOverlayGrant = false;
    }
    if (!{'dismiss', 'dismissNotice'}.contains(action) &&
        !snapshot.overlayAllowed) {
      _pendingPetAction = 'guide:$action';
      await requestOverlayPermission();
      if (snapshot.overlayAllowed) return;
      throw const HostFailure('允许悬浮窗后返回，即可继续新手体验');
    }
    await _guard(() async {
      snapshot = await _api.guideAction(action);
      _rememberTrial(snapshot);
    });
  }

  void _syncRuntimeTimer() {
    if (!snapshot.running) {
      _runtimeTimer?.cancel();
      _runtimeTimer = null;
      return;
    }
    _runtimeTimer ??= Timer.periodic(const Duration(milliseconds: 800), (
      _,
    ) async {
      if (_disposed || busy || _runtimePolling) return;
      _runtimePolling = true;
      final previous = snapshot;
      try {
        final next = await _api.snapshot();
        if (_disposed || busy || !identical(snapshot, previous)) return;
        snapshot = next;
        _rememberTrial(next);
        notifyListeners();
        if (next.activePet != previous.activePet) {
          await _loadActivePet();
          if (!_disposed) notifyListeners();
        }
      } catch (_) {
        // Keep durable progress and let the user retry; polling never advances a step.
      } finally {
        _runtimePolling = false;
      }
    });
  }

  Future<void> react(String reaction) async {
    await _guard(() async {
      snapshot = await _api.react(reaction);
      _rememberTrial(snapshot);
    });
  }

  Future<void> syncInteractions() async {
    await _guard(() async {
      snapshot = await _api.syncInteractions();
      _rememberTrial(snapshot);
    });
  }

  Future<void> setSetting(String key, Object value) async {
    final previousPet = snapshot.activePet;
    snapshot = await _api.setSetting(key, value);
    _rememberTrial(snapshot);
    _rememberUpdate(snapshot);
    if (snapshot.activePet != previousPet) await _loadActivePet();
    notifyListeners();
  }

  Future<void> requestOverlayPermission() async {
    _awaitingOverlayGrant = true;
    await _api.requestOverlayPermission();
    await refresh();
    await startPetAfterOverlayGrant();
  }

  Future<void> onAppResumed() async {
    try {
      await refresh();
      await startPetAfterOverlayGrant();
    } catch (error) {
      operationStatus = readableHostError(error);
      if (!_disposed) notifyListeners();
    }
  }

  Future<void> startPetAfterOverlayGrant() async {
    if (!_awaitingOverlayGrant) return;
    if (!snapshot.overlayAllowed) return;
    _awaitingOverlayGrant = false;
    final pending = _pendingPetAction;
    _pendingPetAction = null;
    if (pending != null) {
      if (pending.startsWith('guide:')) {
        await guide(pending.substring(6));
      } else {
        await service(pending);
      }
    } else if (!snapshot.running) {
      await service('start');
    }
  }

  Future<void> requestNotificationPermission() =>
      _api.requestNotificationPermission();

  Future<void> openAppSettings() => _api.openAppSettings();

  Future<void> importGif() async {
    await _guard(() async {
      snapshot = await _api.importGif();
      _rememberTrial(snapshot);
      await _loadActivePet();
    });
  }

  Future<void> importLibrary() async {
    await _guard(() async {
      snapshot = await _api.importLibrary();
      _rememberTrial(snapshot);
      await _loadActivePet();
    });
  }

  Future<void> selectLibrary(String id) async {
    await _guard(() async {
      snapshot = await _api.selectLibrary(id);
      _rememberTrial(snapshot);
      await _loadActivePet();
    });
  }

  Future<void> deleteLibrary(String id) async {
    await _guard(() async {
      snapshot = await _api.deleteLibrary(id);
      _rememberTrial(snapshot);
      await _loadActivePet();
    });
  }

  Future<void> deleteCustom(String petId) async {
    await _guard(() async {
      snapshot = await _api.deleteCustom(petId);
      _rememberTrial(snapshot);
      await _loadActivePet();
    });
  }

  Future<void> activate(String code) async {
    await _guard(() async {
      snapshot = await _api.activate(code);
      companion = null;
      companionHall = null;
      hallError = null;
      _rememberTrial(snapshot);
    });
  }

  Future<void> checkTrial() async {
    await _guard(() async {
      snapshot = await _api.checkTrial();
      _rememberTrial(snapshot);
    });
  }

  Future<void> refreshSiteLinks() async {
    await _guard(() async {
      snapshot = await _api.siteLinks();
      _rememberTrial(snapshot);
    });
  }

  Future<void> openUrl(String url) => _api.openUrl(url);

  Future<void> copyText(String text) => _api.copyText(text);

  Future<void> importTheaterScript() async {
    await _guard(() async {
      snapshot = await _api.importTheaterScript();
      _rememberTrial(snapshot);
    });
  }

  Future<void> deleteTheaterScript(String id) async {
    await _guard(() async {
      snapshot = await _api.deleteTheaterScript(id);
      _rememberTrial(snapshot);
    });
  }

  Future<void> saveReminder({
    String? id,
    required bool enabled,
    required int at,
    required String message,
    required String emotion,
    required String expressionPetId,
    required bool repeatDaily,
  }) async {
    await _guard(() async {
      snapshot = await _api.saveReminder(
        id: id,
        enabled: enabled,
        at: at,
        message: message,
        emotion: emotion,
        expressionPetId: expressionPetId,
        repeatDaily: repeatDaily,
      );
      _rememberTrial(snapshot);
    });
  }

  Future<void> deleteReminder(String id) async {
    await _guard(() async {
      snapshot = await _api.deleteReminder(id);
      _rememberTrial(snapshot);
    });
  }

  Future<void> checkUpdate({bool manual = true}) async {
    await _guard(() async {
      snapshot = await _api.checkUpdate(manual: manual);
      _rememberTrial(snapshot);
      _rememberUpdate(snapshot);
    });
  }

  Future<void> downloadUpdate() async {
    await _guard(() async {
      snapshot = await _api.downloadUpdate();
      _rememberTrial(snapshot);
      _rememberUpdate(snapshot);
    });
  }

  Future<void> installUpdate() async {
    await _guard(() async {
      snapshot = await _api.installUpdate();
      _rememberTrial(snapshot);
      _rememberUpdate(snapshot);
    });
  }

  Future<void> ignoreUpdate() async {
    await _guard(() async {
      snapshot = await _api.ignoreUpdate();
      _rememberTrial(snapshot);
      _rememberUpdate(snapshot);
    });
  }

  Future<void> refreshCompanion() async {
    if (busy || companionLoading) return;
    companionLoading = true;
    companionError = null;
    notifyListeners();
    try {
      await _guard(() async {
        companion = await _api.companionRefresh();
      });
    } catch (error) {
      companionError = readableHostError(error);
      rethrow;
    } finally {
      companionLoading = false;
      notifyListeners();
    }
  }

  Future<void> updateCompanionName(String name) async {
    await _guard(() async {
      companion = await _api.companionUpdateName(name);
    });
  }

  Future<void> pairCompanion(String code) async {
    await _guard(() async {
      companion = await _api.companionPair(code);
    });
  }

  Future<void> unpairCompanion() async {
    await _guard(() async {
      companion = await _api.companionUnpair();
    });
  }

  Future<String> sendCompanion() async {
    var recipient = '';
    await _guard(() async {
      recipient = await _api.companionSend();
    });
    return recipient;
  }

  Future<void> setCompanionHall(bool enabled) async {
    if (!snapshot.companionHallEnabled) return;
    await _guard(() async {
      companion = await _api.companionHallSet(enabled);
      companionHall = {'enabled': enabled, 'people': <Object>[]};
      companionHall = await _api.companionHallRefresh();
    });
  }

  Future<void> refreshCompanionHall() async {
    if (!snapshot.companionHallEnabled ||
        hallLoading ||
        (!snapshot.activated && liveTrialSeconds <= 0)) {
      return;
    }
    final revalidateAccess = hallError?.revalidateAccess ?? false;
    hallLoading = true;
    hallError = null;
    notifyListeners();
    try {
      if (revalidateAccess) {
        await _pullSnapshot(checkTrial: true);
        if (!snapshot.activated && liveTrialSeconds <= 0) {
          companion = null;
          companionHall = null;
          return;
        }
      }
      companion = await _api.companionRefresh();
      companionHall = await _api.companionHallRefresh();
    } catch (error) {
      hallError = HallFailure.from(
        error,
        trialActive: !snapshot.activated && liveTrialSeconds > 0,
      );
    } finally {
      hallLoading = false;
      notifyListeners();
    }
  }

  int get hallCooldownSeconds {
    final remaining =
        hallNextSendAt?.difference(DateTime.now()).inMilliseconds ?? 0;
    return remaining > 0 ? (remaining / 1000).ceil() : 0;
  }

  Future<String> sendCompanionHall(
    String recipientId,
    String message, {
    required String petId,
  }) async {
    if (hallCooldownSeconds > 0) {
      throw HostFailure('稍等 $hallCooldownSeconds 秒再发一只');
    }
    var recipient = '';
    await _guard(() async {
      recipient = await _api.companionHallSend(
        recipientId,
        message,
        petId: petId,
      );
      hallNextSendAt = DateTime.now().add(const Duration(seconds: 30));
    });
    return recipient;
  }

  Future<Uint8List?> gifFor(String petId) => _api.petGif(petId);

  Future<void> _pullSnapshot({required bool checkTrial}) async {
    snapshot = await _api.snapshot();
    if (checkTrial && !snapshot.activated) {
      try {
        snapshot = await _api.checkTrial();
      } catch (_) {
        // Keep the last local snapshot if the trial clock cannot be refreshed.
      }
    }
    _rememberTrial(snapshot);
    _rememberUpdate(snapshot);
    await _loadActivePet();
  }

  void _rememberUpdate(HostSnapshot next) {
    update = next.update;
  }

  void _onHostEvent(String method, Object? arguments) {
    if (method == 'snapshotChanged' && arguments is Map) {
      snapshot = HostSnapshot(Map<String, dynamic>.from(arguments));
      _rememberTrial(snapshot);
      notifyListeners();
      return;
    }
    if (method != 'updateProgress' || arguments is! Map) return;
    update = UpdateState.from(Map<String, dynamic>.from(arguments));
    notifyListeners();
  }

  Future<void> _maybeAutoCheckUpdates() async {
    if (_autoChecked ||
        !snapshot.autoCheckUpdates ||
        !snapshot.autoUpdatesEnabled) {
      return;
    }
    _autoChecked = true;
    try {
      final next = await _api.checkUpdate(manual: false);
      snapshot = next;
      _rememberTrial(next);
      _rememberUpdate(next);
      notifyListeners();
    } catch (_) {
      // Keep the last known update state if the silent check fails.
    }
  }

  Future<void> _loadActivePet() async {
    final petId = snapshot.activePet;
    final image = await _api.petGif(petId);
    if (!_disposed && snapshot.activePet == petId) petGif = image;
  }

  void _rememberTrial(HostSnapshot next) {
    _syncRuntimeTimer();
    _trialSecondsAtSync = next.trialSeconds;
    _trialSyncedAt = DateTime.now();
    _syncTrialTimer();
    _quietTimer?.cancel();
    final quietDelay =
        next.quietUntilUtc - DateTime.now().millisecondsSinceEpoch;
    if (quietDelay > 0) {
      _quietTimer = Timer(
        Duration(milliseconds: quietDelay + 100),
        notifyListeners,
      );
    }
  }

  void _syncTrialTimer() {
    _trialTimer?.cancel();
    if (snapshot.activated || liveTrialSeconds <= 0) return;
    _trialTimer = Timer.periodic(const Duration(seconds: 1), (_) {
      if (liveTrialSeconds <= 0) _trialTimer?.cancel();
      notifyListeners();
    });
  }

  Future<void> _guard(Future<void> Function() action) async {
    if (busy) throw const HostFailure('正在处理上一个操作，请稍等');
    busy = true;
    notifyListeners();
    try {
      await action();
    } finally {
      busy = false;
      notifyListeners();
    }
  }

  @override
  void dispose() {
    _disposed = true;
    _runtimeTimer?.cancel();
    _trialTimer?.cancel();
    _quietTimer?.cancel();
    _api.listen((_, _) {});
    super.dispose();
  }
}

String trialClock(int seconds) {
  final safe = seconds < 0 ? 0 : seconds;
  final days = safe ~/ 86400;
  final hours = (safe % 86400) ~/ 3600;
  final minutes = (safe % 3600) ~/ 60;
  final remaining = safe % 60;
  if (days > 0) {
    return '$days天 $hours:${minutes.toString().padLeft(2, '0')}:${remaining.toString().padLeft(2, '0')}';
  }
  if (hours > 0) {
    return '$hours:${minutes.toString().padLeft(2, '0')}:${remaining.toString().padLeft(2, '0')}';
  }
  return '${minutes.toString().padLeft(2, '0')}:${remaining.toString().padLeft(2, '0')}';
}
