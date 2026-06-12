using System;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Serilog;

namespace AudioShare.Services;

/// <summary>
/// One audio output endpoint: BufferedWaveProvider fed by the loopback capture,
/// drained by WasapiOut in Shared mode. Supports a manual delay (silence prepend)
/// for cross-device synchronization.
/// </summary>
internal sealed class OutputChannel : IDisposable
{
    private readonly string _deviceId;
    private readonly string _deviceName;
    private readonly MMDevice _device;
    private readonly WaveFormat _format;
    private readonly BufferedWaveProvider _buffer;
    private readonly Action<string, Exception> _onError;
    private readonly Action<string, double> _onLevel;
    private WasapiOut? _out;
    private int _delayMs;
    private long _silenceRemaining;
    private long _lastLevelTicks;

    public OutputChannel(string deviceId, string deviceName, MMDevice device, WaveFormat captureFormat,
        int delayMs, Action<string, Exception> onError, Action<string, double> onLevel)
    {
        _deviceId = deviceId;
        _deviceName = deviceName;
        _device = device;
        _format = captureFormat;
        _onError = onError;
        _onLevel = onLevel;
        _delayMs = Math.Max(0, delayMs);

        _buffer = new BufferedWaveProvider(captureFormat)
        {
            BufferDuration = TimeSpan.FromSeconds(5),
            DiscardOnBufferOverflow = true,
            ReadFully = true
        };

        _silenceRemaining = BytesFromMs(_delayMs);
    }

    private long BytesFromMs(int ms) => (long)_format.AverageBytesPerSecond * ms / 1000;

    public void SetDelay(int delayMs)
    {
        delayMs = Math.Max(0, delayMs);
        var diff = delayMs - _delayMs;
        _delayMs = delayMs;
        if (diff > 0)
        {
            // Add more silence to push everything later
            var bytes = BytesFromMs(diff);
            Interlocked.Add(ref _silenceRemaining, bytes);
            // Pre-buffer the silence so it actually plays
            AppendSilenceToBuffer(bytes);
        }
        // If diff < 0 we can't pull samples back; the change takes effect implicitly by
        // not adding new silence on top. The audible result is a one-time skew.
    }

    private void AppendSilenceToBuffer(long bytes)
    {
        const int chunk = 4096;
        var silence = new byte[chunk];
        while (bytes > 0)
        {
            var n = (int)Math.Min(chunk, bytes);
            _buffer.AddSamples(silence, 0, n);
            bytes -= n;
        }
    }

    public void Start()
    {
        try
        {
            _out = new WasapiOut(_device, AudioClientShareMode.Shared, useEventSync: true, latency: 100);
            _out.PlaybackStopped += (s, e) =>
            {
                if (e.Exception is not null)
                {
                    Log.Warning(e.Exception, "Output {Name} stopped with error", _deviceName);
                    _onError(_deviceId, e.Exception);
                }
            };
            _out.Init(_buffer);

            if (_silenceRemaining > 0)
            {
                AppendSilenceToBuffer(_silenceRemaining);
                _silenceRemaining = 0;
            }

            _out.Play();
            Log.Information("Output started: {Name} (delay {Delay}ms)", _deviceName, _delayMs);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Output {Name} failed to start", _deviceName);
            _onError(_deviceId, ex);
            throw;
        }
    }

    public void Push(byte[] data, int count)
    {
        if (_out is null || _out.PlaybackState != PlaybackState.Playing) return;

        try
        {
            _buffer.AddSamples(data, 0, count);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Push failed for {Name}", _deviceName);
            _onError(_deviceId, ex);
            return;
        }

        // Cheap signal level sampling — peak of first/mid/last sample, throttled to ~10Hz
        var now = Environment.TickCount64;
        if (now - _lastLevelTicks > 100)
        {
            _lastLevelTicks = now;
            _onLevel(_deviceId, ComputePeak(data, count));
        }
    }

    private double ComputePeak(byte[] data, int count)
    {
        // Assumes IEEE float (WASAPI loopback default). If it isn't, the value is informational only.
        if (_format.Encoding != WaveFormatEncoding.IeeeFloat || count < 4) return 0;
        double max = 0;
        for (var i = 0; i < count - 3; i += 4)
        {
            var f = Math.Abs(BitConverter.ToSingle(data, i));
            if (f > max) max = f;
        }
        return Math.Min(1.0, max);
    }

    public void Dispose()
    {
        try { _out?.Stop(); } catch (Exception ex) { Log.Debug(ex, "Out.Stop threw"); }
        try { _out?.Dispose(); } catch (Exception ex) { Log.Debug(ex, "Out.Dispose threw"); }
        try { _device?.Dispose(); } catch (Exception ex) { Log.Debug(ex, "Device dispose threw"); }
        _out = null;
    }
}
