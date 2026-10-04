using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace iceman_gui.Core;

public class PortItem
{
    public string PortName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsProxmark { get; set; }

    public override string ToString()
    {
        return IsProxmark ? $"{PortName} [Proxmark3 USB]" : $"{PortName} ({Description})";
    }
}

public static class SerialPortDetector
{
    public static List<PortItem> GetAvailablePorts()
    {
        var result = new List<PortItem>();
        var details = GetPortDetailsFromRegistry();

        foreach (var kvp in details)
        {
            var item = new PortItem
            {
                PortName = kvp.Key,
                Description = kvp.Value
            };

            if (kvp.Value.Contains("USBSER", StringComparison.OrdinalIgnoreCase) ||
                kvp.Value.Contains("USB", StringComparison.OrdinalIgnoreCase) ||
                kvp.Value.Contains("ICEMAN", StringComparison.OrdinalIgnoreCase) ||
                kvp.Value.Contains("PROXMARK", StringComparison.OrdinalIgnoreCase))
            {
                item.IsProxmark = true;
            }

            result.Add(item);
        }

        // If no ports found in registry, fallback to common COM ports
        if (result.Count == 0)
        {
            result.Add(new PortItem { PortName = "COM9", Description = "Default Proxmark3", IsProxmark = true });
        }

        // Sort Proxmark / USB ports first
        return result.OrderByDescending(p => p.IsProxmark).ThenBy(p => p.PortName).ToList();
    }

    private static Dictionary<string, string> GetPortDetailsFromRegistry()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            if (key != null)
            {
                foreach (var valName in key.GetValueNames())
                {
                    var port = key.GetValue(valName)?.ToString();
                    if (!string.IsNullOrEmpty(port))
                    {
                        dict[port] = valName;
                    }
                }
            }
        }
        catch { }

        return dict;
    }
}
