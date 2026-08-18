import 'dart:async';

import 'package:flutter/foundation.dart';

import 'host_api.dart';

class AppController extends ChangeNotifier {
  AppController({HostApi? api}) : _api = api ?? HostApi();

  final HostApi _api;
  HostSnapshot snapshot = HostSnapshot.empty();
  Uint8List? petGif;
  Map<String, dynamic>? companion;
  bool loading = true;
  bool busy = false;
  bool companionLoading = false;
  String? companionError;
  DateTime? _trialSyncedAt;
  int _trialSecondsAtSync = 0;
  Timer? _trialTimer;

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
      await _pullSnapshot(checkTrial: true);
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
    await _guard(() async {
      snapshot = await _api.serviceAction(action);
      await Future<void>.delayed(const Duration(milliseconds: 260));
      snapshot = await _api.snapshot();
      _rememberTrial(snapshot);
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
    if (snapshot.activePet != previousPet) await _loadActivePet();
    notifyListeners();
  }

  Future<void> requestOverlayPermission() => _api.requestOverlayPermission();

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
      _rememberTrial(snapshot);
    });
  }

  Future<void> checkTrial() async {
    await _guard(() async {
      snapshot = await _api.checkTrial();
      _rememberTrial(snapshot);
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
    await _loadActivePet();
  }

  Future<void> _loadActivePet() async {
    petGif = await _api.petGif(snapshot.activePet);
  }

  void _rememberTrial(HostSnapshot next) {
    _trialSecondsAtSync = next.trialSeconds;
    _trialSyncedAt = DateTime.now();
    _syncTrialTimer();
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
    if (busy) return;
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
    _trialTimer?.cancel();
    super.dispose();
  }
}

String trialClock(int seconds) {
  final safe = seconds < 0 ? 0 : seconds;
  final minutes = safe ~/ 60;
  final remaining = safe % 60;
  return '${minutes.toString().padLeft(2, '0')}:${remaining.toString().padLeft(2, '0')}';
}
