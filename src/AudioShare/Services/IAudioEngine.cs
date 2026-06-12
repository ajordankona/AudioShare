using System;
using System.Collections.Generic;
using AudioShare.Models;

namespace AudioShare.Services;

public interface IAudioEngine : IDisposable
{
    bool IsRunning { get; }
    event EventHandler<AudioEngineErrorEventArgs>? Error;
    event EventHandler<AudioEngineDeviceEventArgs>? DeviceStateChanged;
    event EventHandler? StateChanged;

    void Start(IEnumerable<AudioDevice> outputs, IReadOnlyDictionary<string, int> manualDelaysMs);
    void Stop();
    void UpdateDelay(string deviceId, int delayMs);
    AudioEngineStats GetStats();
}

public class AudioEngineErrorEventArgs : EventArgs
{
    public string DeviceId { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public Exception? Exception { get; init; }
}

public class AudioEngineDeviceEventArgs : EventArgs
{
    public string DeviceId { get; init; } = string.Empty;
    public bool IsStreaming { get; init; }
    public double SignalLevel { get; init; }
}

public record AudioEngineStats(int ActiveOutputs, double CpuPercent, long MemoryBytes, double BytesPerSecond);
