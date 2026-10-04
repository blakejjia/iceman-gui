using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace iceman_gui.Core;

public static class ChildProcessTracker
{
    private static readonly IntPtr s_jobHandle;

    static ChildProcessTracker()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        string jobName = "IcemanGuiJob_" + Process.GetCurrentProcess().Id;
        s_jobHandle = CreateJobObject(IntPtr.Zero, jobName);

        var info = new JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        };

        var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = info
        };

        int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
        IntPtr pExtendedInfo = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(extendedInfo, pExtendedInfo, false);

            if (!SetInformationJobObject(s_jobHandle, JobObjectInfoType.ExtendedLimitInformation, pExtendedInfo, (uint)length))
            {
                throw new Win32Exception();
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pExtendedInfo);
        }
    }

    public static void AddProcess(Process process)
    {
        if (s_jobHandle != IntPtr.Zero && process != null && !process.HasExited)
        {
            try
            {
                bool success = AssignProcessToJobObject(s_jobHandle, process.Handle);
                if (!success && !process.HasExited)
                {
                    // Failed to assign
                }
            }
            catch
            {
                // Process may have already exited
            }
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, JobObjectInfoType infoType, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    private enum JobObjectInfoType
    {
        ExtendedLimitInformation = 9
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryLimit;
        public UIntPtr PeakJobMemoryLimit;
    }
}
