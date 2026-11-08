using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

internal static class RunGuiTest
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct StartupInfo
    {
        public int Size;
        public string Reserved, Desktop, Title;
        public int X, Y, Width, Height, Columns, Rows, Fill, Flags;
        public short Show, ReservedSize;
        public IntPtr ReservedData, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInformation { public IntPtr Process, Thread; public int ProcessId, ThreadId; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, int flags, uint access, IntPtr security);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcess(string application, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity,
        bool inheritHandles, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInformation process);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr process, out uint code);
    [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr process, uint code);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

    static string Quote(string value)
    {
        var result = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in value)
        {
            if (c == '\\') { slashes++; continue; }
            result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
            result.Append(c); slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    public static int Main(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("RunGuiTest <test.exe> <log.txt> [test arguments]"); return 2; }
        IntPtr desktop = IntPtr.Zero;
        try
        {
            string name = "PBZKeyForgeTests-" + Guid.NewGuid().ToString("N");
            desktop = CreateDesktop(name, IntPtr.Zero, IntPtr.Zero, 0, 0x10000000, IntPtr.Zero);
            if (desktop == IntPtr.Zero) throw new Win32Exception();
            using (var log = new FileStream(args[1], FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            {
                IntPtr output = log.SafeFileHandle.DangerousGetHandle();
                if (!SetHandleInformation(output, 1, 1)) throw new Win32Exception();
                var startup = new StartupInfo { Size = Marshal.SizeOf(typeof(StartupInfo)), Desktop = name,
                    Flags = 0x100, Output = output, Error = output };
                string executable = Path.GetFullPath(args[0]);
                var command = new StringBuilder(Quote(executable) + " " + string.Join(" ", args.Skip(2).Select(Quote)));
                ProcessInformation process;
                if (!CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero, true, 0x08000000, IntPtr.Zero,
                    Environment.CurrentDirectory, ref startup, out process)) throw new Win32Exception();
                try
                {
                    if (WaitForSingleObject(process.Process, 180000) != 0)
                    {
                        TerminateProcess(process.Process, 1);
                        WaitForSingleObject(process.Process, 5000);
                        throw new TimeoutException("GUI test exceeded three minutes.");
                    }
                    uint code;
                    if (!GetExitCodeProcess(process.Process, out code)) throw new Win32Exception();
                    return (int)code;
                }
                finally { CloseHandle(process.Thread); CloseHandle(process.Process); }
            }
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
        finally { if (desktop != IntPtr.Zero) CloseDesktop(desktop); }
    }
}
