using System;
using System.Collections.Generic;
using AudioShare.Models;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using Serilog;

namespace AudioShare.Services;

public class DeviceMonitor : IDeviceMonitor, IMMNotificationClient
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private bool _registered;

    public event EventHandler? DevicesChanged;

    public void Start()
    {
        if (_registered) return;
        _enumerator.RegisterEndpointNotificationCallback(this);
        _registered = true;
    }

    public void Stop()
    {
        if (!_registered) return;
        try
        {
            _enumerator.UnregisterEndpointNotificationCallback(this);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "UnregisterEndpointNotificationCallback failed");
        }
        _registered = false;
    }

    public IReadOnlyList<AudioDevice> Enumerate()
    {
        var result = new List<AudioDevice>();
        // Only enumerate Active endpoints. Disabled/Unplugged/NotPresent endpoints can take
        // 3-5 seconds each to activate via IMMDevice::Activate before failing — they used to
        // dominate startup time. If we ever need to surface them, they should be queried
        // lazily off the UI thread.
        foreach (var mm in _enumerator.EnumerateAudioEndPoints(DataFlow.Render,
                     NAudio.CoreAudioApi.DeviceState.Active))
        {
            try
            {
                result.Add(Map(mm));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to map device {Id}", SafeId(mm));
            }
        }
        return result;
    }

    private static string SafeId(MMDevice mm)
    {
        try { return mm.ID; } catch { return "?"; }
    }

    private static AudioDevice Map(MMDevice mm)
    {
        // Note: we deliberately do NOT call mm.AudioClient?.MixFormat here. That call
        // activates the WASAPI audio client per device and can take ~1 second each on
        // some drivers (especially virtual audio devices like Elgato Wave Link). Across
        // 10-15 Active endpoints on a heavy system, that synchronous probe was burning
        // 15-18 seconds of startup. Format details are fetched lazily later when the
        // engine actually starts streaming.
        return new AudioDevice
        {
            Id = mm.ID,
            Name = mm.FriendlyName,
            FriendlyName = mm.DeviceFriendlyName,
            Transport = DetectTransport(mm),
            State = MapState(mm.State),
            SampleRate = 0,
            Channels = 0,
            BitsPerSample = 0,
        };
    }

    private static DeviceTransport DetectTransport(MMDevice mm)
    {
        var name = (mm.FriendlyName + " " + mm.DeviceFriendlyName).ToLowerInvariant();

        if (name.Contains("bluetooth") || name.Contains("airpod") || name.Contains("hands-free") || name.Contains("a2dp"))
            return DeviceTransport.Bluetooth;
        if (name.Contains("hdmi"))
            return DeviceTransport.Hdmi;
        if (name.Contains("displayport") || name.Contains("dp audio"))
            return DeviceTransport.DisplayPort;
        if (name.Contains("usb"))
            return DeviceTransport.Usb;
        if (name.Contains("virtual") || name.Contains("vb-audio") || name.Contains("voicemeeter"))
            return DeviceTransport.Virtual;
        if (name.Contains("realtek") || name.Contains("speakers") || name.Contains("internal"))
            return DeviceTransport.BuiltIn;

        return DeviceTransport.Unknown;
    }

    private static Models.DeviceState MapState(NAudio.CoreAudioApi.DeviceState s) => s switch
    {
        NAudio.CoreAudioApi.DeviceState.Active => Models.DeviceState.Active,
        NAudio.CoreAudioApi.DeviceState.Disabled => Models.DeviceState.Disabled,
        NAudio.CoreAudioApi.DeviceState.NotPresent => Models.DeviceState.NotPresent,
        NAudio.CoreAudioApi.DeviceState.Unplugged => Models.DeviceState.Unplugged,
        _ => Models.DeviceState.Active
    };

    public void OnDeviceStateChanged(string deviceId, NAudio.CoreAudioApi.DeviceState newState) => Raise();
    public void OnDeviceAdded(string pwstrDeviceId) => Raise();
    public void OnDeviceRemoved(string deviceId) => Raise();
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => Raise();
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    private void Raise() => DevicesChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        Stop();
        _enumerator.Dispose();
        GC.SuppressFinalize(this);
    }
}
