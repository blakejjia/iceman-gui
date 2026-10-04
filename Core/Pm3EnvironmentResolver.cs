using System;
using System.IO;

namespace iceman_gui.Core;

public class Pm3EnvironmentResolver
{
    private static Pm3EnvironmentResolver? _instance;
    public static Pm3EnvironmentResolver Instance => _instance ??= new Pm3EnvironmentResolver();

    public string ClientDirectory { get; private set; } = string.Empty;
    public string ExecutablePath { get; private set; } = string.Empty;
    public string LibsDirectory { get; private set; } = string.Empty;
    public string ShellDirectory { get; private set; } = string.Empty;
    public string DumpsDirectory { get; private set; } = string.Empty;
    public string RootDirectory { get; private set; } = string.Empty;
    public bool IsResolved { get; private set; }

    public bool Resolve()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;

        // Candidate 1: Direct child of application directory (Portable Release: <AppDir>\client)
        string cand1 = Path.Combine(baseDir, "client");
        if (IsValidClientDir(cand1))
        {
            SetPaths(cand1, baseDir);
            return true;
        }

        // Candidate 2: Traverse up parent directories (Dev / Debug mode)
        var dirInfo = new DirectoryInfo(baseDir);
        while (dirInfo != null && dirInfo.Parent != null)
        {
            dirInfo = dirInfo.Parent;
            string cand2 = Path.Combine(dirInfo.FullName, "client");
            if (IsValidClientDir(cand2))
            {
                SetPaths(cand2, dirInfo.FullName);
                return true;
            }
        }

        // Candidate 3: Known proxmark3 location in Downloads
        string cand3 = @"C:\Users\GRAPE\Downloads\proxmark3\client";
        if (IsValidClientDir(cand3))
        {
            SetPaths(cand3, Path.GetDirectoryName(cand3) ?? cand3);
            return true;
        }

        return false;
    }

    private static bool IsValidClientDir(string dir)
    {
        return Directory.Exists(dir) && File.Exists(Path.Combine(dir, "proxmark3.exe"));
    }

    private void SetPaths(string clientDir, string rootDir)
    {
        ClientDirectory = clientDir;
        RootDirectory = rootDir;
        ExecutablePath = Path.Combine(clientDir, "proxmark3.exe");
        LibsDirectory = Path.Combine(clientDir, "libs");
        ShellDirectory = Path.Combine(LibsDirectory, "shell");

        DumpsDirectory = Path.Combine(rootDir, "dumps");
        if (!Directory.Exists(DumpsDirectory))
        {
            Directory.CreateDirectory(DumpsDirectory);
        }

        IsResolved = true;
    }
}
