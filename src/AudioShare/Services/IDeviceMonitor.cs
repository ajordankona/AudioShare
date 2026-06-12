using System;
using System.Collections.Generic;
using AudioShare.Models;

namespace AudioShare.Services;

public interface IDeviceMonitor : IDisposable
{
    event EventHandler? DevicesChanged;
    IReadOnlyList<AudioDevice> Enumerate();
    void Start();
    void Stop();
}
