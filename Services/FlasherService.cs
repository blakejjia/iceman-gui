using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using iceman_gui.Core;

namespace iceman_gui.Services;

public class FlasherService
{
    private static FlasherService? _instance;
    public static FlasherService Instance => _instance ??= new FlasherService();

    public (bool hasBootrom, bool hasFullimage, string bootromPath, string fullimagePath) CheckFirmwareFiles()
    {
        var env = Pm3EnvironmentResolver.Instance;
        if (!env.IsResolved) env.Resolve();

        string bootrom = Path.Combine(env.ClientDirectory, "bootrom.elf");
        string fullimage = Path.Combine(env.ClientDirectory, "fullimage.elf");

        return (File.Exists(bootrom), File.Exists(fullimage), bootrom, fullimage);
    }

    public async Task<string> FlashDeviceAsync(string port, bool flashBootloader, CancellationToken ct = default)
    {
        var env = Pm3EnvironmentResolver.Instance;
        if (!env.IsResolved) env.Resolve();

        var (hasBoot, hasFull, bootPath, fullPath) = CheckFirmwareFiles();
        if (!hasFull)
        {
            return "[!] Error: fullimage.elf not found in " + env.ClientDirectory;
        }

        string bootArg = flashBootloader && hasBoot ? "--unlock-bootloader --image bootrom.elf " : "";
        string args = $"{port} --flash {bootArg}--image fullimage.elf";

        var proc = Pm3ProcessService.Instance;
        var psi = proc.CreateBaseStartInfo(args);

        using var process = new System.Diagnostics.Process { StartInfo = psi };
        var sb = new System.Text.StringBuilder();

        process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null) sb.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null) sb.AppendLine(e.Data);
        };

        process.Start();
        ChildProcessTracker.AddProcess(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(ct);
        return sb.ToString();
    }
}
