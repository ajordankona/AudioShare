# Roadmap

## v0.1 (current)

- [x] WASAPI loopback capture
- [x] Multi-device fan-out via `WasapiOut` (Shared mode)
- [x] Per-device manual delay
- [x] Device groups (create, load, delete)
- [x] Settings persistence
- [x] Live device monitoring (Bluetooth/USB/HDMI plug events)
- [x] Dark + light themes
- [x] Signal-level meters
- [x] Per-day rolling logs

## v0.2 — Polish & tray

- [ ] System tray icon with quick-toggle and group menu
- [ ] Minimize-to-tray behavior
- [ ] Start-with-Windows toggle (registry Run key)
- [ ] App icon
- [ ] Settings window split out (was previously crammed into the sidebar)
- [ ] CPU sampling via `PerformanceCounter` (currently stubbed at 0)
- [ ] Hotkeys (Start/Stop, switch group)

## v0.3 — Automatic latency

- [ ] Per-output latency probe on Start (silent ramp tone, measure `WasapiOut.GetPosition()` delta)
- [ ] Automatic sync mode (align everyone to slowest)
- [ ] Per-device measured-vs-manual readout in the UI
- [ ] Re-probe on Bluetooth reconnect

## v0.4 — Bluetooth resilience

- [ ] Detect Bluetooth disconnect mid-stream and gracefully drop the channel
- [ ] Auto-reconnect on re-pair without restarting the whole engine
- [ ] Codec hint (SBC vs AAC vs aptX) surfaced in UI
- [ ] Bandwidth estimator (warn before adding the Nth BT device based on observed bitrate)

## v0.5 — Format & quality controls

- [ ] Per-device sample-rate / bit-depth override (force-Init in Shared mode)
- [ ] Soft-limiter to prevent clipping on aggressive system audio
- [ ] Optional MediaFoundationResampler for devices that fail Shared-mode auto-convert
- [ ] Loopback source selector (capture from non-default endpoint)

## v1.0 — Packaging & distribution

- [ ] MSI installer (WiX) and MSIX variant
- [ ] Code signing
- [ ] Auto-update (Velopack or Squirrel.Windows)
- [ ] Crash report uploader

## Stretch — Network broadcasting

- [ ] Output to a network sink (Snapcast-compatible protocol) so phones on the LAN can be additional speakers
- [ ] Discoverable via mDNS

## Stretch — WinUI 3 port

The MVVM split was designed to make this mechanical. The work:
- Replace `App.xaml(.cs)` with WinUI's `Application` subclass
- Replace WPF `Window` and `ResourceDictionary` with WinUI equivalents
- Convert MVVM converters (already compatible at the C# layer)
- Ship as MSIX with Windows App SDK

Audio engine, settings, group services, view models — all unchanged.
