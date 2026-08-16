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

  Future<void> initialize() async {
    loading = true;
    notifyListeners();
    try {
      snapshot = await _api.snapshot();
      await _loadActivePet();
    } finally {
      loading = false;
      notifyListeners();
    }
  }

  Future<void> refresh() async {
    snapshot = await _api.snapshot();
    await _loadActivePet();
    notifyListeners();
  }

  Future<void> service(String action) async {
    await _guard(() async {
      snapshot = await _api.serviceAction(action);
      await Future<void>.delayed(const Duration(milliseconds: 260));
      snapshot = await _api.snapshot();
    });
  }

  Future<void> react(String reaction) async {
    await _guard(() async {
      snapshot = await _api.react(reaction);
    });
  }

  Future<void> syncInteractions() async {
    await _guard(() async {
      snapshot = await _api.syncInteractions();
    });
  }

  Future<void> setSetting(String key, Object value) async {
    final previousPet = snapshot.activePet;
    snapshot = await _api.setSetting(key, value);
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
      await _loadActivePet();
    });
  }

  Future<void> deleteCustom(String petId) async {
    await _guard(() async {
      snapshot = await _api.deleteCustom(petId);
      await _loadActivePet();
    });
  }

  Future<void> activate(String code) async {
    await _guard(() async {
      snapshot = await _api.activate(code);
    });
  }

  Future<void> checkTrial() async {
    await _guard(() async {
      snapshot = await _api.checkTrial();
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

  Future<void> _loadActivePet() async {
    petGif = await _api.petGif(snapshot.activePet);
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
}
