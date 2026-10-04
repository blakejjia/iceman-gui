using System;

namespace iceman_gui.Models;

public class HardwareInfo
{
    public string ClientVersion { get; set; } = string.Empty;
    public string BootromVersion { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Processor { get; set; } = string.Empty;
    public string FlashMemory { get; set; } = string.Empty;
    public string SramSize { get; set; } = string.Empty;

    public double LfVoltage { get; set; }
    public double HfVoltage { get; set; }
    public string LfTuneStatus { get; set; } = "Not Measured";
    public string HfTuneStatus { get; set; } = "Not Measured";

    public string RawStatus { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; } = DateTime.Now;
}
