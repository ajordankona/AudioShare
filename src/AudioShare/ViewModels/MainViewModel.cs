using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using AudioShare.Models;
using AudioShare.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AudioShare.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IDeviceMonitor _monitor;
    private readonly IAudioEngine _engine;
    private readonly ISettingsService _settings;
    private readonly IGroupService _groups;
    private readonly IBluetoothOptimizer _btOptimizer;
    private readonly DispatcherTimer _statsTimer;
    private List<string> _disabledHfpServices = new();

    public ObservableCollection<AudioDevice> Devices { get; } = new();
    public ObservableCollection<DeviceGroup> Groups { get; } = new();

    [ObservableProperty]
    private bool _isSharing;

    [ObservableProperty]
    private int _activeOutputCount;

    [ObservableProperty]
    private long _memoryBytes;

    [ObservableProperty]
    private double _throughputKBps;

    [ObservableProperty]
    private string _statusMessage = "Idle";

    [ObservableProperty]
    private DeviceGroup? _selectedGroup;

    [ObservableProperty]
    private bool _darkMode;

    public MainViewModel(
        IDeviceMonitor monitor,
        IAudioEngine engine,
        ISettingsService settings,
        IGroupService groups,
        IBluetoothOptimizer btOptimizer)
    {
        _monitor = monitor;
        _engine = engine;
        _settings = settings;
        _groups = groups;
        _btOptimizer = btOptimizer;

        _settings.Load();
        DarkMode = _settings.Current.DarkMode;

        _monitor.DevicesChanged += (_, _) => OnUiThread(RefreshDevices);
        _engine.StateChanged += (_, _) => OnUiThread(() => IsSharing = _engine.IsRunning);
        _engine.Error += (_, e) => OnUiThread(() =>
        {
            StatusMessage = $"Error: {e.Message}";
            var match = Devices.FirstOrDefault(d => d.Id == e.DeviceId);
            if (match is not null) match.LastError = e.Message;
        });
        _engine.DeviceStateChanged += (_, e) => OnUiThread(() =>
        {
            var match = Devices.FirstOrDefault(d => d.Id == e.DeviceId);
            if (match is not null)
            {
                match.IsStreaming = e.IsStreaming;
                match.SignalLevel = e.SignalLevel;
            }
        });

        _monitor.Start();
        RefreshDevices();
        RefreshGroups();

        _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _statsTimer.Tick += (_, _) => UpdateStats();
        _statsTimer.Start();
    }

    private static void OnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    [RelayCommand]
    private void RefreshDevices()
    {
        var fresh = _monitor.Enumerate();
        var selected = Devices.Where(d => d.IsSelected).Select(d => d.Id).ToHashSet();
        Devices.Clear();
        foreach (var d in fresh)
        {
            if (_settings.Current.RenamedDevices.TryGetValue(d.Id, out var custom))
                d.FriendlyName = custom;
            if (_settings.Current.ManualDelaysMs.TryGetValue(d.Id, out var delay))
                d.ManualDelayMs = delay;
            if (selected.Contains(d.Id)) d.IsSelected = true;
            d.PropertyChanged += DevicePropertyChanged;
            Devices.Add(d);
        }
    }

    private void DevicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not AudioDevice device) return;
        if (e.PropertyName == nameof(AudioDevice.ManualDelayMs))
        {
            _settings.Current.ManualDelaysMs[device.Id] = device.ManualDelayMs;
            _settings.Save();
            if (_engine.IsRunning) _engine.UpdateDelay(device.Id, device.ManualDelayMs);
        }
        if (e.PropertyName == nameof(AudioDevice.FriendlyName))
        {
            _settings.Current.RenamedDevices[device.Id] = device.FriendlyName;
            _settings.Save();
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task StartSharing()
    {
        try
        {
            var selected = Devices.Where(d => d.IsSelected && d.State == DeviceState.Active).ToList();
            if (selected.Count == 0)
            {
                StatusMessage = "Select at least one device";
                return;
            }

            // Windows-level fix: a Bluetooth device with HFP enabled blocks A2DP rendering
            // when another BT device is also streaming. Auto-detect and disable HFP for
            // each selected output that needs it. One UAC prompt per session.
            if (_settings.Current.BluetoothOptimization)
            {
                var hfp = _btOptimizer.DiscoverHfpServicesForDevices(selected);
                if (hfp.Count > 0)
                {
                    StatusMessage = $"Optimizing Bluetooth ({hfp.Count} HFP service(s))…";
                    var ok = await System.Threading.Tasks.Task.Run(() => _btOptimizer.DisableHfpServices(hfp));
                    if (ok)
                    {
                        _disabledHfpServices = hfp;
                        // Give the BT stack a moment to re-enumerate without HFP claiming the radio.
                        await System.Threading.Tasks.Task.Delay(2500);
                        RefreshDevices();
                        // Reselect by ID — endpoint IDs are stable across HFP toggle.
                        var ids = selected.Select(d => d.Id).ToHashSet();
                        foreach (var d in Devices) d.IsSelected = ids.Contains(d.Id);
                        selected = Devices.Where(d => d.IsSelected && d.State == DeviceState.Active).ToList();
                    }
                    else
                    {
                        StatusMessage = "Bluetooth optimization skipped (UAC declined or no privileges).";
                    }
                }
            }

            var delays = selected.ToDictionary(d => d.Id, d => d.ManualDelayMs);
            _engine.Start(selected, delays);
            StatusMessage = $"Sharing to {selected.Count} device(s)";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Start failed");
            StatusMessage = $"Failed to start: {ex.Message}";
        }
    }

    [RelayCommand]
    private void StopSharing()
    {
        _engine.Stop();

        if (_disabledHfpServices.Count > 0)
        {
            var toRestore = _disabledHfpServices;
            _disabledHfpServices = new List<string>();
            // Don't await — Stop should be immediate. Restoration runs in background.
            System.Threading.Tasks.Task.Run(() =>
            {
                _btOptimizer.RestoreHfpServices(toRestore);
            });
        }

        StatusMessage = "Stopped";
    }

    [RelayCommand]
    private void RefreshGroups()
    {
        Groups.Clear();
        foreach (var g in _groups.Groups) Groups.Add(g);
    }

    [RelayCommand]
    private void ActivateGroup(DeviceGroup? group)
    {
        if (group is null) return;
        foreach (var d in Devices)
        {
            d.IsSelected = group.DeviceIds.Contains(d.Id);
            if (group.ManualDelaysMs.TryGetValue(d.Id, out var ms))
                d.ManualDelayMs = ms;
        }
        SelectedGroup = group;
        StatusMessage = $"Loaded group: {group.Name}";
    }

    [RelayCommand]
    private void SaveCurrentAsGroup()
    {
        var name = $"Group {Groups.Count + 1}";
        var group = _groups.Create(name);
        group.DeviceIds = Devices.Where(d => d.IsSelected).Select(d => d.Id).ToList();
        group.ManualDelaysMs = Devices.Where(d => d.IsSelected).ToDictionary(d => d.Id, d => d.ManualDelayMs);
        _groups.Save(group);
        RefreshGroups();
        SelectedGroup = group;
        StatusMessage = $"Saved group: {name}";
    }

    [RelayCommand]
    private void DeleteGroup(DeviceGroup? group)
    {
        if (group is null) return;
        _groups.Delete(group.Id);
        RefreshGroups();
        StatusMessage = $"Deleted group: {group.Name}";
    }

    [RelayCommand]
    private void ToggleDarkMode()
    {
        DarkMode = !DarkMode;
        _settings.Current.DarkMode = DarkMode;
        _settings.Save();
        ApplyTheme();
    }

    public void ApplyTheme()
    {
        var app = Application.Current;
        if (app is null) return;
        var dict = app.Resources.MergedDictionaries;
        var themeUri = DarkMode
            ? new Uri("pack://application:,,,/Themes/Dark.xaml")
            : new Uri("pack://application:,,,/Themes/Light.xaml");
        for (var i = dict.Count - 1; i >= 0; i--)
        {
            var src = dict[i].Source?.ToString() ?? "";
            if (src.Contains("Themes/Dark.xaml") || src.Contains("Themes/Light.xaml"))
                dict.RemoveAt(i);
        }
        dict.Add(new ResourceDictionary { Source = themeUri });
    }

    private void UpdateStats()
    {
        var stats = _engine.GetStats();
        ActiveOutputCount = stats.ActiveOutputs;
        MemoryBytes = stats.MemoryBytes;
        ThroughputKBps = stats.BytesPerSecond / 1024.0;
    }

    public void Dispose()
    {
        _statsTimer.Stop();
        _engine.Dispose();
        _monitor.Dispose();

        // Best-effort restore of any HFP services we disabled this session.
        if (_disabledHfpServices.Count > 0)
        {
            try { _btOptimizer.RestoreHfpServices(_disabledHfpServices); }
            catch (Exception ex) { Log.Warning(ex, "HFP restore on dispose failed"); }
        }
    }
}
