# Audio Share

A lightweight Windows desktop app that captures all PC audio via WASAPI loopback and broadcasts it to **multiple output devices simultaneously** — Bluetooth earbuds, USB headphones, HDMI TVs, built-in speakers, virtual devices.

> One stream in. Many speakers out. No artificial device limit.

## Use cases

- Movie nights with several Bluetooth headphones
- Silent disco / silent events
- Group listening
- Worship team monitoring
- Conference interpretation
- Training sessions, presentations
- Accessibility (multiple hearing devices)
- Testing audio devices side-by-side

## Features

- **WASAPI loopback capture** of the Windows default render device
- **Fan-out to N outputs** — no hardcoded device limit; constrained only by hardware
- **Per-device manual delay** (0–500 ms) to compensate for Bluetooth/HDMI latency
- **Device groups** — save selections like "Movie Night" or "Conference Room" and recall with one click
- **Live device detection** — Bluetooth connect/disconnect, plug/unplug, sample format
- **Signal-level meters** per device
- **Persistent settings** — selected devices, manual delays, renamed labels, groups
- **Dark & light themes**
- **Detailed logging** to `%AppData%\AudioShare\logs\`

## Install / run

### Option 1 — Unpacked folder (recommended, fast startup)

```powershell
./build.ps1 -Unpacked
```

Produces `release\win-unpacked\` (20 files, ~22 MB). Double-click `AudioShare.exe`. **Cold start ~1-2 seconds.** Requires the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) on the target machine.

### Option 2 — Unpacked self-contained (no runtime needed)

```powershell
./build.ps1 -Unpacked -SelfContained
```

Produces `release\win-unpacked\` (~480 files, ~220 MB) with the .NET 8 runtime bundled. Use this for distribution to machines without .NET 8 installed. Cold start is slower (~3-5 seconds) because of the larger payload to memory-map.

### Option 3 — Single-file exe

```powershell
./build.ps1                # framework-dependent
./build.ps1 -SelfContained # self-contained, single ~175 MB exe
```

Produces `publish\AudioShare.exe`.

### Option 4 — Run from source

```powershell
dotnet run --project src/AudioShare/AudioShare.csproj
```

## How to use

1. Launch **Audio Share**.
2. Check the devices you want to broadcast to.
3. Click **Start Sharing**. Audio playing on your PC is now broadcast to every selected device.
4. If you hear echo from a Bluetooth device, increase its **Delay** slider until other devices "catch up" to the laggy one.
5. (Optional) **Save selection as group** to recall this set later with one click.

## Tech stack

| Layer | Choice | Why |
|---|---|---|
| Language | C# 12 / .NET 8 | Mature WPF + WASAPI bindings |
| UI | WPF + MVVM (CommunityToolkit.Mvvm) | Single-file publish friendly, no packaging overhead |
| Audio | [NAudio 2.2.1](https://github.com/naudio/NAudio) | `WasapiLoopbackCapture` + `WasapiOut` |
| Logging | Serilog | Per-day rolling file logs |
| Config | JSON in `%AppData%\AudioShare\settings.json` | Human-editable, portable |

**WPF instead of WinUI 3?** WinUI 3 was the original target, but it has packaging friction (`MSIX`/`Windows App SDK` runtime requirements) that gets in the way of a "double-click and run" experience. WPF gives identical capabilities for this app's needs and ships cleanly as a single `.exe`. The MVVM split keeps a future WinUI 3 port mechanical — only the `Views/` and `Themes/` layer would change.

## Architecture

```
              ┌──────────────────────────┐
              │  WasapiLoopbackCapture   │  ← default render endpoint
              │  (default playback dev)  │
              └────────────┬─────────────┘
                           │ DataAvailable event (raw IEEE float)
                           ▼
              ┌──────────────────────────┐
              │     WasapiAudioEngine    │
              │  - copies bytes to each  │
              │    OutputChannel         │
              └────────────┬─────────────┘
                ┌──────────┼──────────┐
                ▼          ▼          ▼
          ┌──────────┐┌──────────┐┌──────────┐
          │ Output 1 ││ Output 2 ││ Output N │  ← per-device BufferedWaveProvider
          │ +delay   ││ +delay   ││ +delay   │     and WasapiOut (Shared)
          └─────┬────┘└────┬─────┘└────┬─────┘
                ▼          ▼           ▼
            Bluetooth   USB DAC      HDMI TV
```

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for class diagrams and the synchronization design.

## Synchronization

True sample-accurate sync across mixed devices on Windows is essentially impossible without exclusive-mode access to every endpoint and a shared clock. The pragmatic approach Audio Share takes:

- **Manual mode** (default, ships in v0.1): per-device delay slider. You tune it once and the value persists.
- **Automatic mode** (roadmap): the engine queries each `WasapiOut.GetPosition()` clock latency and applies offsets. See [ROADMAP.md](docs/ROADMAP.md).

## Performance targets

| Metric | Target | Status |
|---|---|---|
| Startup | < 2s | ✓ |
| Memory (idle) | < 150 MB | ✓ |
| CPU | < 10% | ✓ for ≤8 outputs |
| Outputs supported | unlimited (hardware-bound) | ✓ no hardcoded cap |

## Known limitations (v0.1)

- Automatic latency measurement is a stub — manual delay sliders are the working path.
- Bluetooth bandwidth heuristics are not enforced; over-subscription will simply produce dropouts on the weakest link.
- No system-tray icon yet (planned for v0.2).
- Format mismatch between capture (float, 48 kHz) and exotic outputs may produce silence — open an issue with the device name and `%AppData%\AudioShare\logs\` attached.

## Roadmap

See [docs/ROADMAP.md](docs/ROADMAP.md).

## License

MIT. NAudio is also MIT.
