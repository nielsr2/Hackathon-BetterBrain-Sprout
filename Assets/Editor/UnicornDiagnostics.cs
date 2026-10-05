using System;
using System.Linq;
using System.Reflection;
using Gtec.Licensing.Unicorn.DotNet;
using Gtec.Unicorn;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The g.tec Device only reports "Could not connect to device". This goes one level lower
/// (UnicornDotNet + the licensing library) and logs the real error code: Bluetooth adapter,
/// paired devices, licences on this PC, and a direct open/close of each headset.
/// </summary>
public static class UnicornDiagnostics
{
    [MenuItem("Tools/BCI/Diagnose Unicorn Connection")]
    public static void Run()
    {
        Try("Bluetooth adapter", () => Dump(Unicorn.GetBluetoothAdapterInfo()));
        Try("Licences", () =>
        {
            var licences = LicensingDotNet.GetLicenses();
            return licences.Count == 0 ? "none found" : string.Join(" | ", licences.Select(l => Dump(l)));
        });

        string[] serials = null;
        Try("Paired devices", () => string.Join(", ", serials = Unicorn.GetAvailableDevices(true).ToArray()));
        if (serials == null || serials.Length == 0) return;

        foreach (var serial in serials)
        {
            Try($"Open {serial}", () =>
            {
                using var device = new Unicorn(serial);
                return "opened OK: " + Dump(device.GetDeviceInformation());
            });
        }
    }

    static void Try(string step, Func<string> action)
    {
        try { Debug.Log($"[UnicornDiag] {step}: {action()}"); }
        catch (DeviceException e) { Debug.LogError($"[UnicornDiag] {step}: DeviceException {e.ErrorCode} ({(int)e.ErrorCode}) — {e.Message}"); }
        catch (Exception e) { Debug.LogError($"[UnicornDiag] {step}: {e.GetType().Name} — {e.Message}"); }
    }

    // The g.tec info structs have no ToString; list their public fields.
    static string Dump(object o) =>
        "{ " + string.Join(", ", o.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Select(f => $"{f.Name}={Format(f.GetValue(o))}")) + " }";

    static string Format(object v) => v switch
    {
        null => "null",
        char[] c => new string(c).TrimEnd('\0'),
        byte[] b => System.Text.Encoding.ASCII.GetString(b).TrimEnd('\0'),
        Array a => "[" + string.Join(",", a.Cast<object>()) + "]",
        _ => v.ToString()
    };
}
