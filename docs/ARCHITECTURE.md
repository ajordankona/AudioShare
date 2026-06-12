# Architecture

## Goal

Capture Windows system audio once, broadcast it to N output devices simultaneously, with per-device delay alignment so audio doesn't echo across devices with different latencies.

## Component diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                          App.xaml.cs                            │
│  - DI container                                                 │
│  - Serilog setup                                                │
└──────────────────┬──────────────────────────────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────────────────────────┐
│                       MainViewModel                             │
│  - Observable Devices, Groups collections                       │
│  - Start/Stop/Refresh commands                                  │
│  - Wires monitor → engine, propagates errors to UI              │
└──────┬──────────────┬─────────────────┬─────────────────────────┘
       │              │                 │
       ▼              ▼                 ▼
┌────────────┐  ┌────────────┐  ┌──────────────────┐
│  Device    │  │  Audio     │  │   Settings /     │
│  Monitor   │  │  Engine    │  │   Groups         │
└────────────┘  └─────┬──────┘  └──────────────────┘
                      │
                      ▼
              ┌────────────────────┐
              │   OutputChannel    │  (one per selected output)
              │  - WasapiOut       │
              │  - BufferedWave    │
              │  - delay buffer    │
              └────────────────────┘
```

## Audio pipeline

1. **Capture** — `WasapiLoopbackCapture` on the default render endpoint. Format is whatever Windows mixes at (typically IEEE float, 48 kHz, 2 ch).
2. **Fan-out** — `WasapiAudioEngine.OnDataAvailable` is the hot path. For each `OutputChannel` it calls `Push(buffer, count)`, which appends to that channel's `BufferedWaveProvider`. No format conversion in this step.
3. **Per-device output** — each `OutputChannel` owns a `WasapiOut` in **Shared** mode (`useEventSync: true`, 100 ms hardware buffer). WASAPI's audio engine handles resampling from capture format to each device's mix format.
4. **Delay** — when a device has a non-zero `ManualDelayMs`, the channel prepends `bytes = avgBytesPerSec * ms / 1000` of silence into its buffer at start (or when the slider is dragged up). The result is that the device "trails" the others by that many ms, letting you align a Bluetooth lagger with the laggiest reference device.

## Threading

| Thread | What runs on it |
|---|---|
| UI (Dispatcher) | All ViewModel updates, theme apply, commands |
| Capture thread (NAudio internal) | `WasapiLoopbackCapture.DataAvailable` → channel push (lock-free reads of `_outputs`) |
| Per-output threads (NAudio internal) | Each `WasapiOut` runs its own pull loop from its `BufferedWaveProvider` |
| Dispatcher timer (750 ms) | Stats sampling (memory, throughput) |

The engine uses a single `lock` around `Start`/`Stop` and a `ConcurrentDictionary` for the output map. Pushes to channels do not lock — `BufferedWaveProvider` is thread-safe internally.

## Synchronization model

Three real-world latency sources:

- **Wired devices** (built-in speakers, USB DACs, HDMI): tens of ms
- **Bluetooth A2DP**: 100–250 ms depending on codec (SBC/AAC/aptX/LDAC)
- **Bluetooth low-latency modes** (aptX LL): ~40 ms

Audio Share's strategy is "delay everyone to match the slowest". The user identifies the laggiest device by listening, leaves it at 0 ms, and pushes the others up.

### Manual mode (v0.1, shipping)
- Per-device slider, 0–500 ms.
- Value is persisted per device GUID, so once you tune your earbuds you don't have to re-tune them tomorrow.

### Automatic mode (v0.2, roadmap)
- On `Start`, briefly play a known reference tone through each device.
- Use `WasapiOut.GetPosition()` clock + a simple ramp probe to estimate the time-to-render gap.
- Apply offsets such that everyone aligns to the slowest measured device.
- Doesn't solve Bluetooth jitter, just the steady-state offset.

## File layout

```
src/AudioShare/
├── App.xaml(.cs)               Entry point, DI, logging
├── MainWindow.xaml(.cs)        Single primary view
├── Models/
│   ├── AudioDevice.cs          Observable model for a render endpoint
│   ├── AppSettings.cs          JSON-serialized settings
│   └── DeviceGroup.cs          Saved selection + delay map
├── ViewModels/
│   └── MainViewModel.cs        Owns the engine + monitor, exposes commands
├── Services/
│   ├── IAudioEngine.cs         Engine interface
│   ├── WasapiAudioEngine.cs    Loopback capture + fan-out
│   ├── OutputChannel.cs        Per-device worker
│   ├── IDeviceMonitor.cs       Endpoint enumeration interface
│   ├── DeviceMonitor.cs        IMMNotificationClient registration
│   ├── ISettingsService.cs     Config interface
│   ├── SettingsService.cs      JSON persistence
│   └── GroupService.cs         Group CRUD
├── Views/                      (reserved for future SettingsWindow etc.)
├── Themes/
│   ├── Dark.xaml
│   └── Light.xaml
└── Converters/
    └── Converters.cs           Bool→Visibility, Bytes→MB, State→Color
```

## Settings file

Lives at `%AppData%\AudioShare\settings.json`. Schema:

```json
{
  "bufferSizeMs": 100,
  "latencyMode": "Balanced",
  "syncMode": "Manual",
  "bluetoothOptimization": true,
  "autoReconnect": true,
  "darkMode": true,
  "groups": [
    {
      "id": "abc",
      "name": "Movie Night",
      "deviceIds": ["{0.0.0.00000000}.{guid}", "..."],
      "manualDelaysMs": { "{0.0.0...}": 180 }
    }
  ],
  "renamedDevices": { "{0.0.0...}": "Living Room TV" },
  "manualDelaysMs": { "{0.0.0...}": 180 }
}
```

## Logging

Serilog rolling file at `%AppData%\AudioShare\logs\audioshare-YYYYMMDD.log`, 7-day retention. Captures engine start/stop, per-output errors, format negotiation, settings load failures.
