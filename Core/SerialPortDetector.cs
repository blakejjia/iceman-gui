using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Management;
using Microsoft.Win32;

namespace iceman_gui.Core;

public class PortItem
{
    public string PortName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsProxmark { get; set; }

    public override string ToString()
    {
        return IsProxmark ? $"{PortName} [Proxmark3]" : $"{PortName} ({Description})";
    }
}

public static class SerialPortDetector
{
    public static List<PortItem> GetAvailablePorts()
    {
        var result = new List<PortItem>();
        var portNames = SerialPort.GetPortNames().Distinct().OrderBy(p => p).ToList();

        // Try getting descriptions from registry or WMI
        var portDetails = GetPortDetailsFromRegistry();

        foreach (var port in portNames)
        {
            var item = new PortItem { PortName = port };
            if (portDetails.TryGetValue(port, out var desc))
            {
                item.Description = desc;
                if (desc.Contains("ICEMAN", StringComparison.OrdinalIgnoreCase) ||
                    desc.Contains("PROXMARK", StringComparison.OrdinalIgnoreCase) ||
                    desc.Contains("9AC4", StringComparison.OrdinalIgnoreCase) ||
                    desc.Contains("2D2D", StringComparison.OrdinalIgnoreCase))
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

        // Put Proxmark3 ports first
        return result.OrderByDescending(p => p.IsProxmark).ThenBy(p => p.PortName).ToList();
    }

    private static Dictionary<string, string> GetPortDetailsFromRegistry()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT DeviceID, Name, Description, PNPDeviceID FROM Win32_SerialPort");
            foreach (ManagementObject obj in searcher.Get())
            {
                string port = obj["DeviceID"]?.ToString() ?? string.Empty;
                string pnp = obj["PNPDeviceID"]?.ToString() ?? string.Empty;
                string desc = obj["Description"]?.ToString() ?? obj["Name"]?.ToString() ?? string.Empty;

                if (!string.IsNullOrEmpty(port))
                {
                    dict[port] = $"{desc} {pnp}";
                }
            }
        }
        catch
        {
            // Fallback: Registry enumeration
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
                if (key != null)
                {
                    foreach (var valName in key.GetValueNames())
                    {
                        var port = key.GetValue(valName)?.ToString();
                        if (!string.IsNullOrEmpty(port) && !dict.ContainsKey(port))
                        {
                            dict[port] = valName;
                        }
                    }
                }
            }
            catch { }
        }

        return dict;
    }
}
