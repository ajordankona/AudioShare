using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using AudioShare.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Serilog;

namespace AudioShare.Services;

public class WasapiAudioEngine : IAudioEngine
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, OutputChannel> _outputs = new();
    private WasapiLoopbackCapture? _capture;
    private WaveFormat? _captureFormat;
    private long _totalBytes;
    private DateTime _startedAt;

    public bool IsRunning { get; private set; }

    public event EventHandler<AudioEngineErrorEventArgs>? Error;
    public event EventHandler<AudioEngineDeviceEventArgs>? DeviceStateChanged;
    public event EventHandler? StateChanged;

    public void Start(IEnumerable<AudioDevice> outputs, IReadOnlyDictionary<string, int> manualDelaysMs)
    {
        lock (_lock)
        {
            if (IsRunning) Stop();

            try
            {
                var defaultRender = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _capture = new WasapiLoopbackCapture(defaultRender);
                _captureFormat = _capture.WaveFormat;
                Log.Information("Loopback capture format: {Format}", _captureFormat);

                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;

                foreach (var device in outputs)
                {
                    var delay = manualDelaysMs.TryGetValue(device.Id, out var d) ? d : 0;
                    AddOutputUnsafe(device, delay);
                }

                _capture.StartRecording();
                _startedAt = DateTime.UtcNow;
                _totalBytes = 0;
                IsRunning = true;
                StateChanged?.Invoke(this, EventArgs.Empty);
                Log.Information("Audio engine started with {Count} outputs", _outputs.Count);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to start audio engine");
                Error?.Invoke(this, new AudioEngineErrorEventArgs { Message = ex.Message, Exception = ex });
                StopUnsafe();
                throw;
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            StopUnsafe();
        }
    }

    private void StopUnsafe()
    {
        if (_capture is not null)
        {
            try { _capture.StopRecording(); } catch (Exception ex) { Log.Debug(ex, "StopRecording threw"); }
            try { _capture.Dispose(); } catch (Exception ex) { Log.Debug(ex, "Capture dispose threw"); }
            _capture = null;
        }

        foreach (var (_, channel) in _outputs)
        {
            channel.Dispose();
        }
        _outputs.Clear();

        if (IsRunning)
        {
            IsRunning = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
            Log.Information("Audio engine stopped");
        }
    }

    private void AddOutputUnsafe(AudioDevice device, int delayMs)
    {
        try
        {
            var mm = _enumerator.GetDevice(device.Id);
            if (mm is null)
            {
                Error?.Invoke(this, new AudioEngineErrorEventArgs { DeviceId = device.Id, Message = "Device not found" });
                return;
            }

            var channel = new OutputChannel(device.Id, device.Name, mm, _captureFormat!, delayMs, OnOutputError, OnLevel);
            _outputs[device.Id] = channel;
            channel.Start();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to add output {Device}", device.Name);
            Error?.Invoke(this, new AudioEngineErrorEventArgs { DeviceId = device.Id, Message = ex.Message, Exception = ex });
        }
    }

    private void OnOutputError(string deviceId, Exception ex)
    {
        Error?.Invoke(this, new AudioEngineErrorEventArgs { DeviceId = deviceId, Message = ex.Message, Exception = ex });
    }

    private void OnLevel(string deviceId, double level)
    {
        DeviceStateChanged?.Invoke(this, new AudioEngineDeviceEventArgs { DeviceId = deviceId, IsStreaming = true, SignalLevel = level });
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;
        Interlocked.Add(ref _totalBytes, e.BytesRecorded);

        // Snapshot the channels so we don't allocate enumerators on a hot path
        foreach (var (_, channel) in _outputs)
        {
            channel.Push(e.Buffer, e.BytesRecorded);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            Log.Error(e.Exception, "Capture stopped due to error");
            Error?.Invoke(this, new AudioEngineErrorEventArgs { Message = e.Exception.Message, Exception = e.Exception });
        }
    }

    public void UpdateDelay(string deviceId, int delayMs)
    {
        if (_outputs.TryGetValue(deviceId, out var channel))
        {
            channel.SetDelay(delayMs);
        }
    }

    public AudioEngineStats GetStats()
    {
        var proc = Process.GetCurrentProcess();
        var memory = proc.WorkingSet64;
        var seconds = Math.Max(0.001, (DateTime.UtcNow - _startedAt).TotalSeconds);
        var bps = IsRunning ? _totalBytes / seconds : 0;
        var cpu = 0d; // TODO: sample CPU over interval via PerformanceCounter
        return new AudioEngineStats(_outputs.Count, cpu, memory, bps);
    }

    public void Dispose()
    {
        Stop();
        _enumerator.Dispose();
        GC.SuppressFinalize(this);
    }
}
