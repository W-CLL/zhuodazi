using Microsoft.Win32;
using ZhuoDazi.Services;

namespace ZhuoDazi;

public sealed partial class AppController
{
    private DeviceHeartbeatLoop? _deviceHeartbeat;

    private void StartDeviceHeartbeat()
    {
        if (IsLocalPreview || _deviceHeartbeat is not null) return;
        _deviceHeartbeat = new DeviceHeartbeatLoop(_licenses.SendHeartbeatAsync);
        SystemEvents.PowerModeChanged += OnActivityPowerModeChanged;
        SystemEvents.SessionSwitch += OnActivitySessionSwitch;
        _deviceHeartbeat.Start();
    }

    public void RefreshDeviceActivity() => _deviceHeartbeat?.IdentityRefreshed();

    private void OnActivityPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) _deviceHeartbeat?.Suspend();
        else if (e.Mode == PowerModes.Resume) _deviceHeartbeat?.Resume();
    }

    private void OnActivitySessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionUnlock) _deviceHeartbeat?.Resume();
    }

    private void StopDeviceHeartbeat()
    {
        SystemEvents.PowerModeChanged -= OnActivityPowerModeChanged;
        SystemEvents.SessionSwitch -= OnActivitySessionSwitch;
        _deviceHeartbeat?.Dispose();
        _deviceHeartbeat = null;
    }
}
