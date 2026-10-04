using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using iceman_gui.Core;
using iceman_gui.Models;

namespace iceman_gui.Services;

public class HardwareService
{
    private static HardwareService? _instance;
    public static HardwareService Instance => _instance ??= new HardwareService();

    public async Task<HardwareInfo> GetHardwareInfoAsync(CancellationToken ct = default)
    {
        var proc = Pm3ProcessService.Instance;
        string output = await proc.ExecuteCommandAsync("hw version", ct, 15000);
        string clean = AnsiColorParser.StripAnsi(output);

        var info = new HardwareInfo { RawStatus = output };

        // Client version & compiler & platform
        var clientMatch = Regex.Match(clean, @"Iceman/master/([^\s]+)");
        if (clientMatch.Success) info.ClientVersion = clientMatch.Groups[1].Value;

        var clientCompMatch = Regex.Match(clean, @"Compiler\.\.+\s*(MinGW[^\r\n]+)");
        if (clientCompMatch.Success) info.ClientCompiler = clientCompMatch.Groups[1].Value.Trim();

        var platMatch = Regex.Match(clean, @"Platform\.\.+\s*([^\r\n]+)");
        if (platMatch.Success) info.Platform = platMatch.Groups[1].Value.Trim();

        // Bootrom, OS & Compiler
        var bootMatch = Regex.Match(clean, @"Bootrom\.\.\.\.\s*([^\r\n]+)");
        if (bootMatch.Success) info.BootromVersion = bootMatch.Groups[1].Value.Trim();

        var osMatch = Regex.Match(clean, @"OS\.\.\.\.\.\.\.\.\.\s*([^\r\n]+)");
        if (osMatch.Success) info.OsVersion = osMatch.Groups[1].Value.Trim();

        var armCompMatch = Regex.Match(clean, @"Compiler\.\.+\s*(GCC[^\r\n]+)");
        if (armCompMatch.Success) info.OsCompiler = armCompMatch.Groups[1].Value.Trim();

        // FPGA
        var fpgaMatch = Regex.Match(clean, @"fpga_pm3_hf\.ncd image\s*([^\r\n]+)");
        if (fpgaMatch.Success) info.FpgaBuild = fpgaMatch.Groups[1].Value.Trim();

        // Model
        var modelMatch = Regex.Match(clean, @"Firmware\.\.\.\.\.\.\.\.\.\.\.\.\.\.\.\.\.\.\s*([^\r\n]+)");
        if (modelMatch.Success) info.Model = modelMatch.Groups[1].Value.Trim();

        // Hardware / Microcontroller & Architecture
        var ucMatch = Regex.Match(clean, @"--=\s*uC:\s*([^\r\n]+)");
        if (ucMatch.Success) info.Processor = ucMatch.Groups[1].Value.Trim();

        var epMatch = Regex.Match(clean, @"--=\s*Embedded Processor:\s*([^\r\n]+)");
        if (epMatch.Success) info.EmbeddedProcessor = epMatch.Groups[1].Value.Trim();

        var archMatch = Regex.Match(clean, @"--=\s*Architecture identifier:\s*([^\r\n]+)");
        if (archMatch.Success) info.Architecture = archMatch.Groups[1].Value.Trim();

        var sramMatch = Regex.Match(clean, @"--=\s*Internal SRAM size:\s*([^\r\n]+)");
        if (sramMatch.Success) info.SramSize = sramMatch.Groups[1].Value.Trim();

        var flashMatch = Regex.Match(clean, @"--=\s*Embedded flash memory\s*([^\r\n]+)");
        if (flashMatch.Success) info.FlashMemory = flashMatch.Groups[1].Value.Trim();

        return info;
    }

    public async Task MeasureAntennaAsync(HardwareInfo info, CancellationToken ct = default)
    {
        var proc = Pm3ProcessService.Instance;
        string output = await proc.ExecuteCommandAsync("hw tune", ct, 20000);
        string clean = AnsiColorParser.StripAnsi(output);

        // LF Antenna Voltage: e.g. "125.00 kHz ........... 34.50 V"
        var lfMatch = Regex.Match(clean, @"125\.00\s*kHz[^\d]*([\d\.]+)\s*V", RegexOptions.IgnoreCase);
        if (lfMatch.Success && double.TryParse(lfMatch.Groups[1].Value, out double lfVolts))
        {
            info.LfVoltage = lfVolts;
            info.LfTuneStatus = lfVolts >= 15.0 ? $"Good ({lfVolts:F1}V)" : $"Weak ({lfVolts:F1}V)";
        }

        // HF Antenna Voltage: e.g. "13.56 MHz ............ 28.20 V"
        var hfMatch = Regex.Match(clean, @"13\.56\s*MHz[^\d]*([\d\.]+)\s*V", RegexOptions.IgnoreCase);
        if (hfMatch.Success && double.TryParse(hfMatch.Groups[1].Value, out double hfVolts))
        {
            info.HfVoltage = hfVolts;
            info.HfTuneStatus = hfVolts >= 10.0 ? $"Good ({hfVolts:F1}V)" : $"Weak ({hfVolts:F1}V)";
        }
    }
}
