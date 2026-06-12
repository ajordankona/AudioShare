using System.Collections.Generic;

namespace AudioShare.Models;

public enum LatencyMode
{
    LowLatency,
    Balanced,
    Stable
}

public enum SyncMode
{
    Manual,
    Automatic
}

public class AppSettings
{
    public int BufferSizeMs { get; set; } = 100;
    public LatencyMode LatencyMode { get; set; } = LatencyMode.Balanced;
    public SyncMode SyncMode { get; set; } = SyncMode.Manual;
    public bool BluetoothOptimization { get; set; } = true;
    public bool AutoReconnect { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;
    public bool DarkMode { get; set; } = true;
    public string Language { get; set; } = "en";
    public string? LastActiveGroupId { get; set; }
    public List<DeviceGroup> Groups { get; set; } = new();
    public Dictionary<string, string> RenamedDevices { get; set; } = new();
    public Dictionary<string, int> ManualDelaysMs { get; set; } = new();
}
