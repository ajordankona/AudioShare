using CommunityToolkit.Mvvm.ComponentModel;

namespace AudioShare.Models;

public enum DeviceTransport
{
    Unknown,
    BuiltIn,
    Bluetooth,
    Usb,
    Hdmi,
    DisplayPort,
    Network,
    Virtual,
    Aux
}

public enum DeviceState
{
    Active,
    Disabled,
    NotPresent,
    Unplugged
}

public partial class AudioDevice : ObservableObject
{
    public required string Id { get; init; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _friendlyName = string.Empty;

    [ObservableProperty]
    private DeviceTransport _transport;

    [ObservableProperty]
    private DeviceState _state;

    [ObservableProperty]
    private int _sampleRate;

    [ObservableProperty]
    private int _channels;

    [ObservableProperty]
    private int _bitsPerSample;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isStreaming;

    [ObservableProperty]
    private int _manualDelayMs;

    [ObservableProperty]
    private int _measuredLatencyMs;

    [ObservableProperty]
    private double _signalLevel;

    [ObservableProperty]
    private string? _lastError;

    public string TransportLabel => Transport switch
    {
        DeviceTransport.Bluetooth => "Bluetooth",
        DeviceTransport.Usb => "USB",
        DeviceTransport.Hdmi => "HDMI",
        DeviceTransport.DisplayPort => "DisplayPort",
        DeviceTransport.BuiltIn => "Built-In",
        DeviceTransport.Network => "Network",
        DeviceTransport.Virtual => "Virtual",
        DeviceTransport.Aux => "AUX",
        _ => "Other"
    };

    public string FormatLabel => SampleRate > 0
        ? $"{SampleRate / 1000.0:0.#} kHz · {Channels}ch · {BitsPerSample}-bit"
        : "—";
}
