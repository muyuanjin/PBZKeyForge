using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using PBZKeyForge;

public static class KeyEnhancementPrerequisitesPerformanceTests
{
    [StructLayout(LayoutKind.Sequential)]
    struct Io { public ulong Reads, Writes, Other, ReadBytes, WriteBytes, OtherBytes; }
    [DllImport("kernel32.dll")] static extern bool GetProcessIoCounters(IntPtr process, out Io counters);
    static Func<long> allocated;
    static Action<string, Stream[]> streamCheck;

    static void CheckMapped(string runtime)
    {
        using (var library = MemoryMappedFile.CreateFromFile(Path.Combine(runtime, @"ue4ss\UE4SS.dll"), FileMode.Open, null, 0, MemoryMappedFileAccess.Read))
        using (var proxy = MemoryMappedFile.CreateFromFile(Path.Combine(runtime, "dwmapi.dll"), FileMode.Open, null, 0, MemoryMappedFileAccess.Read))
        using (var input = library.CreateViewStream(0, 0, MemoryMappedFileAccess.Read))
        using (var proxyInput = proxy.CreateViewStream(0, 0, MemoryMappedFileAccess.Read))
            streamCheck(runtime, new Stream[] { input, proxyInput });
    }
    static Dictionary<string, object> Measure(string name, int count, Action action)
    {
        var times = new List<double>(); var cpu = Process.GetCurrentProcess().TotalProcessorTime;
        Io before, after; GetProcessIoCounters(Process.GetCurrentProcess().Handle, out before);
        long memory = allocated(); int gen2 = GC.CollectionCount(2);
        for (int i = 0; i < count; i++)
        { var watch = Stopwatch.StartNew(); action(); times.Add(watch.Elapsed.TotalMilliseconds); }
        long bytes = allocated() - memory;
        GetProcessIoCounters(Process.GetCurrentProcess().Handle, out after);
        times.Sort();
        return new Dictionary<string, object> {
            { "name", name }, { "iterations", count }, { "medianMs", times[count / 2] },
            { "p95Ms", times[(int)Math.Ceiling(count * .95) - 1] }, { "maxMs", times[count - 1] },
            { "cpuMsPerCall", (Process.GetCurrentProcess().TotalProcessorTime - cpu).TotalMilliseconds / count },
            { "allocatedBytesPerCall", bytes / count }, { "explicitReadBytesPerCall", (after.ReadBytes - before.ReadBytes) / (ulong)count },
            { "readOperationsPerCall", (after.Reads - before.Reads) / (ulong)count }, { "gen2Collections", GC.CollectionCount(2) - gen2 }
        };
    }
    public static int Main(string[] args)
    {
        try
        {
            I18n.Current = "zh-Hans";
            allocated = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread"));
            var prerequisiteType = typeof(KeyEnhancementInstallation).Assembly.GetType("PBZKeyForge.KeyEnhancementPrerequisites");
            var streamMethod = prerequisiteType.GetMethod("EnsureAvailable", BindingFlags.NonPublic | BindingFlags.Static, null,
                new[] { typeof(string), typeof(Stream[]) }, null);
            streamCheck = (Action<string, Stream[]>)Delegate.CreateDelegate(typeof(Action<string, Stream[]>), streamMethod);
            if (args[0] == "--cold")
            {
                Console.WriteLine(new JavaScriptSerializer().Serialize(Measure("first-check-new-process", 1, () => CheckMapped(args[1]))));
                return 0;
            }
            var result = new List<object>();
            string fixture = Path.GetFullPath(args[0]);
            if (Directory.Exists(fixture)) throw new Exception("Use a new fixture directory.");
            string game = Path.Combine(fixture, "game"), runtime = Path.Combine(game, @"platform\steam");
            Directory.CreateDirectory(runtime); File.WriteAllText(Path.Combine(runtime, "PBZ.exe"), "fixture, never launched");
            var installation = new KeyEnhancementInstallation(game);
            var configuration = installation.LoadConfiguration(); configuration.Enabled = true;
            installation.Apply(configuration);
            byte[] library = File.ReadAllBytes(Path.Combine(runtime, @"ue4ss\UE4SS.dll"));
            byte[] proxy = File.ReadAllBytes(Path.Combine(runtime, "dwmapi.dll"));
            var method = prerequisiteType.GetMethod("EnsureAvailable", BindingFlags.NonPublic | BindingFlags.Static, null,
                new[] { typeof(string), typeof(byte[][]) }, null);
            var check = (Action<string, byte[][]>)Delegate.CreateDelegate(typeof(Action<string, byte[][]>), method);
            byte[][] libraries = { library, proxy };
            result.Add(new { nativeBytes = library.Length + proxy.Length });
            for (int i = 0; i < 3; i++)
            {
                var start = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, "--cold \"" + runtime + "\"") {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                };
                using (var child = Process.Start(start))
                {
                    if (!child.WaitForExit(15000)) { child.Kill(); throw new Exception("Cold prerequisite check did not finish."); }
                    string output = child.StandardOutput.ReadToEnd(), error = child.StandardError.ReadToEnd();
                    if (child.ExitCode != 0) throw new Exception(error);
                    result.Add(new JavaScriptSerializer().DeserializeObject(output));
                }
            }
            var preloaded = Measure("preloaded-check", 30, () => check(runtime, libraries));
            result.Add(preloaded);
            result.Add(Measure("full-file-read-control", 30, () => check(runtime, new[] {
                File.ReadAllBytes(Path.Combine(runtime, @"ue4ss\UE4SS.dll")), File.ReadAllBytes(Path.Combine(runtime, "dwmapi.dll")) })));
            var mapped = Measure("check-with-mapped-files", 30, () => CheckMapped(runtime));
            var apply = Measure("unchanged-apply", 20, () => installation.Apply(configuration));
            result.Add(mapped); result.Add(apply);
            string json = new JavaScriptSerializer().Serialize(result);
            File.WriteAllText(Path.Combine(fixture, "performance.json"), json);
            foreach (object row in result) Console.WriteLine(new JavaScriptSerializer().Serialize(row));
            installation.Uninstall();
            if (Convert.ToInt64(preloaded["allocatedBytesPerCall"]) > 1024 * 1024
                || Convert.ToInt64(mapped["allocatedBytesPerCall"]) > 1024 * 1024)
                throw new Exception("Prerequisite checking allocates more than 1 MiB per call.");
            if (Convert.ToUInt64(apply["explicitReadBytesPerCall"]) >= (ulong)(library.Length + proxy.Length) / 2)
                throw new Exception("Unchanged Apply reads a substantial fraction of the native DLLs.");
            Console.WriteLine("KeyEnhancementPrerequisitesPerformanceTests PASS");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
