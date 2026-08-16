import 'package:flutter/services.dart';

const settingSize = 'size_dp';
const settingOpacity = 'opacity';
const settingMirrored = 'mirrored';
const settingMovement = 'random_movement';
const settingInteractions = 'random_interactions';
const settingInteractionMode = 'interaction_mode';
const settingPersonality = 'personality';
const settingRandomPet = 'random_pet';
const settingRandomPetInterval = 'random_pet_interval';
const settingActivePet = 'active_pet';
const settingWordPack = 'word_pack';
const settingStartOnBoot = 'start_on_boot';

class PetItem {
  const PetItem({required this.id, required this.name});

  final String id;
  final String name;

  bool get isCustom => id.startsWith('@custom:');
}

class HostSnapshot {
  HostSnapshot(this._data);

  factory HostSnapshot.empty() => HostSnapshot(const <String, dynamic>{});

  final Map<String, dynamic> _data;

  bool get overlayAllowed => _bool('overlayAllowed');
  bool get notificationAllowed => _bool('notificationAllowed');
  bool get running => _bool('running');
  bool get hidden => _bool('hidden');
  bool get clickThrough => _bool('clickThrough');
  bool get mirrored => _bool('mirrored');
  bool get movement => _bool('movement');
  bool get interactions => _bool('interactions');
  bool get randomPet => _bool('randomPet');
  bool get startOnBoot => _bool('startOnBoot');
  bool get activated => _bool('activated');
  bool get premium => _bool('premium');
  int get sizeDp => _int('sizeDp', 96);
  int get opacity => _int('opacity', 100);
  int get randomPetInterval => _int('randomPetInterval', 300);
  int get interactionCachedCount => _int('interactionCachedCount', 0);
  int get interactionOnlineCount => _int('interactionOnlineCount', 0);
  int get interactionCatalogVersion => _int('interactionCatalogVersion', 0);
  int get interactionLastSyncAt => _int('interactionLastSyncAt', 0);
  int get trialSeconds => _int('trialSeconds', 0);
  String get personality => _string('personality', 'lively');
  String get interactionMode => _string('interactionMode', 'standard');
  String get interactionSyncError => _string('interactionSyncError', '');
  String get activePet => _string('activePet', '');
  String get wordPack => _string('wordPack', '');
  String get installationSuffix => _string('installationSuffix', '');
  String get version => _string('version', '1.1.0');

  List<PetItem> get pets {
    final raw = _data['pets'];
    if (raw is! List) return const [];
    return raw
        .whereType<Map>()
        .map((item) {
          final value = Map<String, dynamic>.from(item);
          return PetItem(
            id: '${value['id'] ?? ''}',
            name: '${value['name'] ?? ''}',
          );
        })
        .where((pet) => pet.id.isNotEmpty)
        .toList(growable: false);
  }

  List<String> get wordPacks {
    final raw = _data['wordPacks'];
    if (raw is! List) return const [];
    return raw.map((item) => '$item').toList(growable: false);
  }

  PetItem? get selectedPet {
    for (final pet in pets) {
      if (pet.id == activePet) return pet;
    }
    return pets.isEmpty ? null : pets.first;
  }

  String get petStatus {
    if (!overlayAllowed) return '等待悬浮窗权限';
    if (!running) return '桌宠尚未启动';
    if (clickThrough) return '桌宠正在穿透显示';
    if (hidden) return '桌宠已隐藏';
    return '桌宠正在陪你';
  }

  String get licenseLabel {
    if (activated) return '已激活';
    if (trialSeconds > 0) return '体验中';
    return '基础版';
  }

  bool _bool(String key) => _data[key] == true;
  int _int(String key, int fallback) =>
      (_data[key] as num?)?.toInt() ?? fallback;
  String _string(String key, String fallback) => '${_data[key] ?? fallback}';
}

class HostApi {
  static const MethodChannel _channel = MethodChannel(
    'com.zhuodazi.android/host',
  );

  Future<HostSnapshot> snapshot() => _snapshotCall('snapshot');

  Future<Uint8List?> petGif(String petId) async {
    if (petId.isEmpty) return null;
    return _channel.invokeMethod<Uint8List>('petGif', {'petId': petId});
  }

  Future<HostSnapshot> setSetting(String key, Object value) =>
      _snapshotCall('setSetting', {'key': key, 'value': value});

  Future<HostSnapshot> serviceAction(String action) =>
      _snapshotCall('serviceAction', {'action': action});

  Future<HostSnapshot> react(String reaction) =>
      _snapshotCall('react', {'reaction': reaction});

  Future<HostSnapshot> syncInteractions() => _snapshotCall('syncInteractions');

  Future<void> requestOverlayPermission() =>
      _channel.invokeMethod<void>('requestOverlayPermission');

  Future<void> requestNotificationPermission() =>
      _channel.invokeMethod<void>('requestNotificationPermission');

  Future<void> openAppSettings() =>
      _channel.invokeMethod<void>('openAppSettings');

  Future<HostSnapshot> importGif() => _snapshotCall('importGif');

  Future<HostSnapshot> deleteCustom(String petId) =>
      _snapshotCall('deleteCustom', {'petId': petId});

  Future<HostSnapshot> activate(String code) =>
      _snapshotCall('activate', {'code': code});

  Future<HostSnapshot> checkTrial() => _snapshotCall('checkTrial');

  Future<Map<String, dynamic>> companionRefresh() =>
      _mapCall('companionRefresh');

  Future<Map<String, dynamic>> companionUpdateName(String name) =>
      _mapCall('companionUpdateName', {'name': name});

  Future<Map<String, dynamic>> companionPair(String code) =>
      _mapCall('companionPair', {'code': code});

  Future<Map<String, dynamic>> companionUnpair() => _mapCall('companionUnpair');

  Future<String> companionSend() async =>
      await _channel.invokeMethod<String>('companionSend') ?? '搭子';

  Future<HostSnapshot> _snapshotCall(
    String method, [
    Map<String, Object>? arguments,
  ]) async {
    final raw = await _channel.invokeMethod<Map<dynamic, dynamic>>(
      method,
      arguments,
    );
    if (raw == null) throw const HostFailure('未收到安卓端状态');
    return HostSnapshot(Map<String, dynamic>.from(raw));
  }

  Future<Map<String, dynamic>> _mapCall(
    String method, [
    Map<String, Object>? arguments,
  ]) async {
    final raw = await _channel.invokeMethod<Map<dynamic, dynamic>>(
      method,
      arguments,
    );
    if (raw == null) throw const HostFailure('未收到搭子资料');
    return Map<String, dynamic>.from(raw);
  }
}

class HostFailure implements Exception {
  const HostFailure(this.message);

  final String message;

  @override
  String toString() => message;
}

String readableHostError(Object error) {
  if (error is PlatformException) return error.message ?? '操作失败，请稍后重试';
  if (error is HostFailure) return error.message;
  return error.toString().replaceFirst('Exception: ', '');
}
