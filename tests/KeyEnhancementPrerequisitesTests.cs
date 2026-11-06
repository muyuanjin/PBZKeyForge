using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using PBZKeyForge;

public static class KeyEnhancementPrerequisitesTests
{
    static int checks;
    static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }

    static string Reject(Action action, string expected)
    {
        try { action(); }
        catch (IOException ex)
        {
            Check(ex.Message.Contains(expected), "Missing diagnostic: " + expected + "\n" + ex.Message);
            return ex.Message;
        }
        throw new Exception("Operation accepted unavailable prerequisites: " + expected);
    }

    static Dictionary<string, byte[]> Snapshot(string root)
    { return Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes); }

    static void Unchanged(string root, Dictionary<string, byte[]> before)
    {
        var after = Snapshot(root);
        Check(after.Count == before.Count && before.All(pair => after.ContainsKey(pair.Key) && after[pair.Key].SequenceEqual(pair.Value)),
            "Rejected prerequisite check changed the installation.");
        Check(!Directory.GetDirectories(root, ".PBZKeyForge-*", SearchOption.AllDirectories).Any(), "Rejected check left staging directories.");
    }

    static byte[] ReplaceName(byte[] bytes, string original, string replacement)
    {
        Check(original.Length == replacement.Length, "Fixture import names must have equal lengths.");
        byte[] result = (byte[])bytes.Clone(), from = Encoding.ASCII.GetBytes(original + "\0"), to = Encoding.ASCII.GetBytes(replacement + "\0");
        int matches = 0;
        for (int i = 0; i <= result.Length - from.Length; i++)
        {
            if (!from.Where((value, index) => result[i + index] != value).Any())
            { Buffer.BlockCopy(to, 0, result, i, to.Length); matches++; }
        }
        Check(matches > 0, "Fixture runtime does not import " + original);
        return result;
    }

    static string Package(string path, Dictionary<string, byte[]> files)
    {
        using (var output = File.Create(path))
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
            foreach (var pair in files)
                using (var entry = zip.CreateEntry(pair.Key).Open()) entry.Write(pair.Value, 0, pair.Value.Length);
        return path;
    }

    public static int Main(string[] args)
    {
        try
        {
            I18n.Current = "zh-Hans";
            if (args.Contains("--all-languages"))
            {
                var translations = (Dictionary<string, Dictionary<string, string>>)typeof(I18n)
                    .GetField("T", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                foreach (string language in I18n.Languages)
                    foreach (string key in new[] { "ue4ss.requires64", "ue4ss.vcredist" })
                        Check(translations[language].ContainsKey(key) && !string.IsNullOrWhiteSpace(translations[language][key]),
                            "Missing prerequisite translation: " + language + "/" + key);
            }
            string output = Path.GetFullPath(args[0]);
            if (Directory.Exists(output)) throw new Exception("Use a new fixture directory.");
            string runtime = Path.Combine(output, @"游戏 with spaces\platform\steam");
            Directory.CreateDirectory(runtime);
            File.WriteAllText(Path.Combine(runtime, "PBZ.exe"), "fixture, never launched");
            string game = Path.GetDirectoryName(Path.GetDirectoryName(runtime));
            var installation = new KeyEnhancementInstallation(game);
            var configuration = installation.LoadConfiguration(); configuration.Enabled = true;
            if (IntPtr.Size != 8)
            {
                var before = Snapshot(runtime);
                Reject(() => installation.Apply(configuration), I18n.Str("ue4ss.requires64"));
                Unchanged(runtime, before);
                configuration.Enabled = false; installation.Apply(configuration); installation.Uninstall();
                Console.WriteLine("KeyEnhancementPrerequisitesTests x86 PASS: " + checks + " assertions");
                return 0;
            }

            var files = new Dictionary<string, byte[]>();
            using (var stream = typeof(Ue4ssRelease).Assembly.GetManifestResourceStream("PBZKeyForge.KeyEnhancement.runtime.zip"))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                foreach (var entry in zip.Entries)
                {
                    const string prefix = "runtime/platform/steam/";
                    if (!entry.FullName.StartsWith(prefix) || entry.FullName.Contains("/Mods/")) continue;
                    using (var input = entry.Open()) using (var buffer = new MemoryStream())
                    { input.CopyTo(buffer); files.Add(entry.FullName.Substring(prefix.Length), buffer.ToArray()); }
                }

            const string crt = "MSVCP140_CODECVT_IDS.dll", missing = "MSVCP140_PBZ_MISSING.dll";
            string library = Path.Combine(runtime, @"ue4ss\UE4SS.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(library));
            File.WriteAllBytes(library, ReplaceName(files["ue4ss/UE4SS.dll"], crt, missing));
            File.WriteAllBytes(Path.Combine(runtime, "dwmapi.dll"), files["dwmapi.dll"]);
            var initial = Snapshot(runtime);
            string diagnostic = Reject(() => installation.Apply(configuration), missing);
            Check(diagnostic.Contains("Visual C++") && diagnostic.Contains("vc_redist.x64.exe"), "Missing runtime repair guidance.");
            Unchanged(runtime, initial);

            var invalid = new Dictionary<string, byte[]>(files);
            invalid["ue4ss/UE4SS.dll"] = File.ReadAllBytes(library);
            string archive = Package(Path.Combine(output, "missing-runtime.zip"), invalid);
            Reject(() => installation.UpdateRuntime(archive, Ue4ssRelease.Bundled), missing);
            Unchanged(runtime, initial);

            string symbol = "__CxxFrameHandler4", absent = "PBZMissingExportXX";
            File.WriteAllBytes(library, ReplaceName(files["ue4ss/UE4SS.dll"], symbol, absent));
            initial = Snapshot(runtime);
            Reject(() => installation.Apply(configuration), absent);
            Unchanged(runtime, initial);

            File.WriteAllBytes(library, files["ue4ss/UE4SS.dll"]);
            string localCrt = Path.Combine(runtime, crt);
            File.WriteAllText(localCrt, "invalid app-local runtime");
            initial = Snapshot(runtime);
            Reject(() => installation.Apply(configuration), localCrt);
            Unchanged(runtime, initial);
            File.Delete(localCrt);

            installation.Apply(configuration);
            Check(installation.InstalledEnabled, "Repair did not allow retry.");
            string json = File.ReadAllText(installation.ConfigurationPath);
            File.WriteAllText(localCrt, "invalid app-local runtime");
            initial = Snapshot(runtime);
            Reject(() => installation.Apply(configuration), localCrt);
            Unchanged(runtime, initial);
            Check(File.ReadAllText(installation.ConfigurationPath) == json, "Rejected reapply changed configuration.");
            configuration.Enabled = false; installation.Apply(configuration);
            Check(!installation.InstalledEnabled, "Broken CRT prevented disable.");
            installation.Uninstall();
            Check(File.Exists(localCrt) && File.Exists(library), "Uninstall removed third-party runtime files.");
            Check(!Directory.Exists(installation.InstallDirectory), "Broken CRT prevented uninstall.");

            Console.WriteLine("KeyEnhancementPrerequisitesTests PASS: " + checks + " assertions; fixture=" + output);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
