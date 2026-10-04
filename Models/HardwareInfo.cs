using System;

namespace iceman_gui.Models;

public class HardwareInfo
{
    // Client & Environment
    public string ClientVersion { get; set; } = string.Empty;
    public string ClientCompiler { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;

    // OS & Firmware
    public string BootromVersion { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public string OsCompiler { get; set; } = string.Empty;
    public string FpgaBuild { get; set; } = string.Empty;

    // Hardware & Microcontroller
    public string Model { get; set; } = string.Empty;
    public string Processor { get; set; } = string.Empty;
    public string EmbeddedProcessor { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public string FlashMemory { get; set; } = string.Empty;
    public string SramSize { get; set; } = string.Empty;

    // Antenna Resonance
    public double LfVoltage { get; set; }
    public double HfVoltage { get; set; }
    public string LfTuneStatus { get; set; } = "Not Measured";
    public string HfTuneStatus { get; set; } = "Not Measured";

    public string RawStatus { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; } = DateTime.Now;

    public bool IsVerifiedIceman =>
        !string.IsNullOrWhiteSpace(Model) ||
        !string.IsNullOrWhiteSpace(OsVersion) ||
        !string.IsNullOrWhiteSpace(BootromVersion);
}
