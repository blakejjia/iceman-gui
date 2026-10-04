using System;
using System.Collections.Generic;
using System.IO.Ports;
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
        var portNames = SerialPort.GetPortNames().Distinct().OrderBy(p => p).ToList();

        var details = GetPortDetailsFromRegistry();

        foreach (var port in portNames)
        {
            var item = new PortItem { PortName = port };
            if (details.TryGetValue(port, out var desc))
            {
                item.Description = desc;
                if (desc.Contains("USBSER", StringComparison.OrdinalIgnoreCase) ||
                    desc.Contains("USB", StringComparison.OrdinalIgnoreCase) ||
                    desc.Contains("ICEMAN", StringComparison.OrdinalIgnoreCase) ||
                    desc.Contains("PROXMARK", StringComparison.OrdinalIgnoreCase))
                {
                    item.IsProxmark = true;
                }
            }
            else
            {
                item.Description = "Serial Port";
            }

            result.Add(item);
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
