using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using PBZKeyForge;

public static class KeyEnhancementCoexistenceTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static readonly Encoding Utf8 = new UTF8Encoding(false);
    static void Write(string path, string text)
    { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, text, Utf8); }
    static string Game(string output, string name)
    {
        string game = Path.Combine(output, name);
        Write(Path.Combine(game, @"platform\steam\PBZ.exe"), "fixture, never launched");
        return game;
    }
    static Dictionary<string, byte[]> Snapshot(string root)
    { return Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes); }
    static void Unchanged(Dictionary<string, byte[]> before, params string[] except)
    {
        foreach (var pair in before.Where(pair => !except.Contains(pair.Key)))
            Check(File.Exists(pair.Key) && pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key)), "User file changed: " + pair.Key);
    }
    static void Existing(string output, string name, string archive)
    {
        string game = Game(output, name), runtime = Path.Combine(game, @"platform\steam");
        using (var stream = archive == null ? typeof(KeyEnhancementInstallation).Assembly.GetManifestResourceStream("PBZKeyForge.KeyEnhancement.runtime.zip") : File.OpenRead(archive))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            foreach (var entry in zip.Entries)
            {
                string relative = entry.FullName.Replace('\\', '/');
                if (archive == null)
                {
                    const string prefix = "runtime/platform/steam/";
                    if (!relative.StartsWith(prefix) || relative.Contains("/PBZInputRouter/")) continue;
                    relative = relative.Substring(prefix.Length);
                }
                if (relative.EndsWith("/")) continue;
                string path = Path.Combine(runtime, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var input = entry.Open()) using (var file = File.Create(path)) input.CopyTo(file);
            }
        string list = Path.Combine(runtime, @"ue4ss\Mods\mods.txt"), json = Path.Combine(runtime, @"ue4ss\Mods\mods.json");
        Write(list, "; User order\r\nOtherMod : 1\r\nDisabledUserMod : 0\r\n");
        Write(json, "[\n  { \"mod_name\": \"OtherMod\", \"mod_enabled\": true, \"custom\": [1, 2] }\n]\n");
        Write(Path.Combine(runtime, @"ue4ss\Mods\OtherMod\Scripts\main.lua"), "print('user')");
        var baseline = Snapshot(runtime);
        var installation = new KeyEnhancementInstallation(game);
        var config = installation.LoadConfiguration(); config.Enabled = true;
        installation.Apply(config);
        Check(installation.InstalledEnabled, "Existing package did not enable: " + name);
        Unchanged(baseline, list);
        config.Enabled = false; installation.Apply(config);
        Check(installation.ReadState() == KeyEnhancementInstallState.Disabled && !installation.LoaderSuspended, "Shared loader was parked: " + name);
        Unchanged(baseline, list);
        config.Enabled = true; installation.Apply(config);
        installation.Uninstall();
        Unchanged(baseline);
        Check(!Directory.Exists(installation.InstallDirectory), "Router was retained: " + name);
    }
    public static int Main(string[] args)
    {
        try
        {
            I18n.Current = "zh-Hans";
            string output = Path.GetFullPath(args[0]);
            if (Directory.Exists(output)) throw new Exception("Use a new fixture directory.");
            Existing(output, "Kintaro existing", null);
            if (args.Length > 1) Existing(output, "Vercadi existing", args[1]);
            string game = Game(output, "standalone"), runtime = Path.Combine(game, @"platform\steam");
            var installation = new KeyEnhancementInstallation(game); var config = installation.LoadConfiguration();
            config.Enabled = true; installation.Apply(config);
            var proxy = File.ReadAllBytes(Path.Combine(runtime, "dwmapi.dll"));
            config.Enabled = false; installation.Apply(config);
            Check(installation.LoaderSuspended && !File.Exists(Path.Combine(runtime, "dwmapi.dll")), "Standalone disable left UE4SS loading.");
            Check(Directory.GetDirectories(Path.Combine(runtime, @"ue4ss\Mods")).All(path => Path.GetFileName(path) == "PBZInputRouter"), "Fresh installation added example mods.");
            installation = new KeyEnhancementInstallation(game); config = installation.LoadConfiguration();
            Check(!config.Enabled, "Restart lost the disabled state.");
            config.Enabled = true; installation.Apply(config);
            Check(proxy.SequenceEqual(File.ReadAllBytes(Path.Combine(runtime, "dwmapi.dll"))) && !installation.LoaderSuspended, "Re-enable did not restore the same proxy.");
            config.Enabled = false; installation.Apply(config);
            Write(Path.Combine(runtime, @"ue4ss\Mods\NewMod\Scripts\main.lua"), "print('new user mod')");
            installation.Apply(config);
            Check(File.Exists(Path.Combine(runtime, "dwmapi.dll")) && !installation.LoaderSuspended, "Unchanged Apply did not restore a newly shared proxy.");
            var preserved = File.ReadAllBytes(Path.Combine(runtime, @"ue4ss\UE4SS.dll"));
            installation.Uninstall();
            Check(File.Exists(Path.Combine(runtime, "dwmapi.dll")) && !installation.LoaderSuspended, "Uninstall did not restore the proxy for a newly added mod.");
            Check(preserved.SequenceEqual(File.ReadAllBytes(Path.Combine(runtime, @"ue4ss\UE4SS.dll"))), "Shared loader was replaced.");
            game = Game(output, "replaced proxy"); runtime = Path.Combine(game, @"platform\steam");
            installation = new KeyEnhancementInstallation(game); config = installation.LoadConfiguration();
            config.Enabled = true; installation.Apply(config);
            Write(Path.Combine(runtime, "dwmapi.dll"), "user replacement proxy");
            config.Enabled = false; installation.Apply(config);
            Check(File.ReadAllText(Path.Combine(runtime, "dwmapi.dll")) == "user replacement proxy" && !installation.LoaderSuspended, "Disable parked a user replacement proxy.");
            installation.Uninstall();
            Check(File.ReadAllText(Path.Combine(runtime, "dwmapi.dll")) == "user replacement proxy", "Uninstall deleted a user replacement proxy.");
            game = Game(output, "redirected"); runtime = Path.Combine(game, @"platform\steam");
            var fresh = new KeyEnhancementInstallation(game); config = fresh.LoadConfiguration(); config.Enabled = true; fresh.Apply(config); fresh.Uninstall();
            using (var stream = typeof(KeyEnhancementInstallation).Assembly.GetManifestResourceStream("PBZKeyForge.KeyEnhancement.runtime.zip"))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                string path = Path.Combine(runtime, @"ue4ss\UE4SS.dll"); Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var input = zip.GetEntry("runtime/platform/steam/ue4ss/UE4SS.dll").Open()) using (var file = File.Create(path)) input.CopyTo(file);
            }
            Write(Path.Combine(runtime, @"ue4ss\UE4SS-settings.ini"), "[Overrides]\r\nModsFolderPath = ../CustomMods\r\nControllingModsTxt = ../order.txt\r\n");
            string order = Path.Combine(runtime, "order.txt"); Write(order, "OtherMod : 1\r\n");
            var custom = new KeyEnhancementInstallation(game); config = custom.LoadConfiguration(); config.Enabled = true; custom.Apply(config);
            Check(custom.InstallDirectory == Path.Combine(runtime, @"CustomMods\PBZInputRouter"), "Custom mod path was ignored.");
            Check(File.ReadAllText(order).Contains("PBZInputRouter : 1"), "Controlling mod list was ignored.");
            File.AppendAllText(order, "UserAdded : 1\r\n", Utf8);
            config.Enabled = false; custom.Apply(config);
            config.Enabled = true; custom.Apply(config);
            custom.Uninstall();
            Check(File.ReadAllText(order) == "OtherMod : 1\r\nUserAdded : 1\r\n", "Uninstall discarded user changes to the controlling list.");
            Console.WriteLine("KeyEnhancementCoexistenceTests PASS: " + checks + " assertions; fixture=" + output);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
