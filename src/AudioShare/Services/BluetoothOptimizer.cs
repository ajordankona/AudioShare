using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using AudioShare.Models;
using Serilog;

namespace AudioShare.Services;

public interface IBluetoothOptimizer
{
    List<string> DiscoverHfpServicesForDevices(IEnumerable<AudioDevice> devices);
    bool DisableHfpServices(IList<string> serviceInstanceIds);
    bool RestoreHfpServices(IList<string> serviceInstanceIds);
}

/// <summary>
/// When two Bluetooth audio devices are selected as outputs and one of them has the
/// Hands-Free Profile (HFP / "Hands-Free AG") enabled, Windows often refuses to
/// render to its A2DP endpoint — the radio can only carry one stream at a time and
/// the HFP-claimed device wins. The user sees one device play and the other silently
/// fail with AUDCLNT_E_UNSUPPORTED_FORMAT (0x8889000F).
///
/// This service detects those HFP services for the selected outputs, disables them
/// via an elevated PowerShell call (single UAC prompt per session), and remembers
/// what it disabled so Stop Sharing can restore them.
/// </summary>
public class BluetoothOptimizer : IBluetoothOptimizer
{
    private static readonly string[] HfpServiceMarkers =
    {
        "Hands-Free AG",
        "Handsfree AG",
        "Hands-Free",
        "Headset AG",
    };

    public List<string> DiscoverHfpServicesForDevices(IEnumerable<AudioDevice> devices)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var dev in devices)
        {
            var baseName = ExtractBaseName(dev.Name);
            if (string.IsNullOrWhiteSpace(baseName)) continue;

            var hits = QueryHfpInstancesByDeviceName(baseName);
            foreach (var id in hits)
            {
                if (seen.Add(id)) result.Add(id);
            }
        }

        if (result.Count > 0)
            Log.Information("Discovered {Count} HFP service(s) to disable: {Ids}", result.Count, result);

        return result;
    }

    public bool DisableHfpServices(IList<string> serviceInstanceIds)
    {
        if (serviceInstanceIds.Count == 0) return true;
        return RunElevatedPnp(serviceInstanceIds, "Disable-PnpDevice");
    }

    public bool RestoreHfpServices(IList<string> serviceInstanceIds)
    {
        if (serviceInstanceIds.Count == 0) return true;
        return RunElevatedPnp(serviceInstanceIds, "Enable-PnpDevice");
    }

    private static string ExtractBaseName(string deviceName)
    {
        // Strip leading qualifiers like "Headphones (Crusher Evo)" → "Crusher Evo"
        var m = Regex.Match(deviceName, @"\(([^)]+)\)");
        if (m.Success) return m.Groups[1].Value.Trim();
        return deviceName.Trim();
    }

    private static List<string> QueryHfpInstancesByDeviceName(string baseName)
    {
        var result = new List<string>();
        // Build a -or chain across our HFP markers, scoped to a name match against baseName.
        var orClauses = string.Join(" -or ", HfpServiceMarkers.Select(m => $"$_.FriendlyName -like '*{m}*'"));
        var script =
            $"Get-PnpDevice -ErrorAction SilentlyContinue | " +
            $"Where-Object {{ $_.FriendlyName -like '*{baseName}*' -and ({orClauses}) }} | " +
            $"Select-Object -ExpandProperty InstanceId";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            if (proc is null) return result;

            var stdout = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(8000);

            foreach (var raw in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = raw.Trim();
                if (trimmed.Length > 0) result.Add(trimmed);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "HFP discovery failed for {Name}", baseName);
        }

        return result;
    }

    private static bool RunElevatedPnp(IList<string> instanceIds, string verb)
    {
        if (instanceIds.Count == 0) return true;

        try
        {
            // Build a semicolon-chained PowerShell command.
            // Single-quoted strings escape ' to ''.
            var commands = string.Join("; ",
                instanceIds.Select(id =>
                    $"{verb} -InstanceId '{id.Replace("'", "''")}' -Confirm:$false -ErrorAction SilentlyContinue"));

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{commands}\"",
                Verb = "runas",            // Triggers UAC
                UseShellExecute = true,    // Required for "runas"
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            using var proc = Process.Start(psi);
            if (proc is null) return false;
            proc.WaitForExit(20_000);

            var ok = proc.ExitCode == 0;
            Log.Information("{Verb} {Count} BT services → exitCode={Code}", verb, instanceIds.Count, proc.ExitCode);
            return ok;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED — user declined UAC
            Log.Warning("User declined UAC for {Verb}", verb);
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "{Verb} failed", verb);
            return false;
        }
    }
}
