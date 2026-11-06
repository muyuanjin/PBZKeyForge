using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using PBZKeyForge;

public static class Ue4ssUpdateTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static void Reject(Action action, string message)
    { try { action(); } catch (IOException) { checks++; return; } catch (InvalidDataException) { checks++; return; } throw new Exception(message); }
    static void Write(string path, byte[] bytes)
    { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, bytes); }
    static string Game(string root, string name)
    {
        string game = Path.Combine(root, name);
        Write(Path.Combine(game, @"platform\steam\PBZ.exe"), new byte[0]); return game;
    }
    static Dictionary<string, byte[]> Snapshot(string directory)
    { return Directory.GetFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes); }
    static void Unchanged(Dictionary<string, byte[]> files)
    { foreach (var pair in files) Check(File.Exists(pair.Key) && File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value), "File changed: " + pair.Key); }
    static string Package(string output, string name, Dictionary<string, byte[]> files)
    {
        string path = Path.Combine(output, name + ".zip");
        using (var stream = File.Create(path)) using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            foreach (var pair in files) using (var writer = zip.CreateEntry(pair.Key).Open()) writer.Write(pair.Value, 0, pair.Value.Length);
        return path;
    }
    public static int Main(string[] args)
    {
        try
        {
            I18n.Current = "zh-Hans";
            string output = Path.GetFullPath(args[0]);
            if (Directory.Exists(output)) throw new Exception("Use a new fixture directory.");
            Directory.CreateDirectory(output);
            string baselineArchive = Path.Combine(output, "baseline.zip");
            using (var stream = typeof(Ue4ssRelease).Assembly.GetManifestResourceStream("PBZKeyForge.KeyEnhancement.runtime.zip"))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            using (var file = File.Create(baselineArchive)) using (var target = new ZipArchive(file, ZipArchiveMode.Create))
                foreach (var entry in zip.Entries.Where(entry => entry.FullName.StartsWith("runtime/platform/steam/") && !entry.FullName.Contains("/Mods/")))
                    using (var input = entry.Open()) using (var writer = target.CreateEntry(entry.FullName.Substring("runtime/platform/steam/".Length)).Open()) input.CopyTo(writer);
            var package = Ue4ssRelease.ReadPackage(baselineArchive);
            package["ue4ss/UE4SS.dll"][32] ^= 1;
            package["dwmapi.dll"][32] ^= 1;
            package["ue4ss/Mods/Unwanted/Scripts/main.lua"] = new byte[] { 1 };
            string archive = Package(output, "new-release", package);
            Check(!Ue4ssRelease.ReadPackage(archive).ContainsKey("ue4ss/Mods/Unwanted/Scripts/main.lua"), "Updater imported packaged example Mods.");
            var release = new Ue4ssRelease { Provider = "fixture", Version = "test-new", Published = "2099-01-01T00:00:00Z" };
            Check(release.IsNewerThan(Ue4ssRelease.Bundled) && !Ue4ssRelease.Bundled.IsNewerThan(release), "Version ordering offers a downgrade.");
            Check(release.IsNewerThan(null), "An unrecognized runtime cannot be explicitly switched.");
            string game = Game(output, "owned");
            var installation = new KeyEnhancementInstallation(game); var config = installation.LoadConfiguration(); config.Enabled = true; installation.Apply(config);
            string runtime = Path.Combine(game, @"platform\steam"), library = Path.Combine(runtime, @"ue4ss\UE4SS.dll");
            string settings = Path.Combine(runtime, @"ue4ss\UE4SS-settings.ini"); File.AppendAllText(settings, "\r\n; user configuration\r\n");
            string userMod = Path.Combine(runtime, @"ue4ss\Mods\OtherMod\Scripts\main.lua"); Write(userMod, new byte[] { 7, 8, 9 });
            var mods = Snapshot(Path.Combine(runtime, @"ue4ss\Mods")); byte[] settingsBefore = File.ReadAllBytes(settings);
            string backup = installation.UpdateRuntime(archive, release);
            Check(File.Exists(backup), "No original runtime backup was created.");
            Check(File.ReadAllBytes(library).SequenceEqual(package["ue4ss/UE4SS.dll"]), "Library was not updated.");
            Check(File.ReadAllBytes(settings).SequenceEqual(settingsBefore), "Update changed UE4SS settings."); Unchanged(mods);
            Check(installation.CurrentRuntimeRelease().Version == release.Version, "Installed release was not identified.");
            config.Rules.Add(new KeyEnhancementRule { Id = "after-update", Keys = new List<string> { "F9" },
                Outputs = new List<KeyEnhancementOutput> { new KeyEnhancementOutput { Kind = "action", Path = "/PBZInput/InputActions/IA_Jump.IA_Jump" } } });
            installation.Apply(config);
            Check(installation.CurrentRuntimeRelease().Version == release.Version, "Applying bindings reset runtime provenance.");
            installation.Uninstall();
            Check(File.ReadAllBytes(library).SequenceEqual(package["ue4ss/UE4SS.dll"]) && File.Exists(userMod), "Uninstall damaged a shared updated loader.");

            game = Game(output, "rollback"); installation = new KeyEnhancementInstallation(game);
            config = installation.LoadConfiguration(); config.Enabled = true; installation.Apply(config);
            runtime = Path.Combine(game, @"platform\steam"); library = Path.Combine(runtime, @"ue4ss\UE4SS.dll");
            var before = Snapshot(runtime);
            using (var held = new FileStream(library, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Reject(() => installation.UpdateRuntime(archive, release), "Locked runtime was replaced.");
            Unchanged(before);
            package["ue4ss/blocked"] = new byte[] { 9 };
            string blockedArchive = Package(output, "blocked-release", package);
            Directory.CreateDirectory(Path.Combine(runtime, @"ue4ss\blocked"));
            Reject(() => installation.UpdateRuntime(blockedArchive, release), "A directory collision did not abort the update.");
            Unchanged(before);
            Check(!Directory.GetFiles(runtime, "*.PBZKeyForge-update", SearchOption.AllDirectories).Any(), "Failed update left staging files.");
            string invalid = Package(output, "invalid", new Dictionary<string, byte[]> { { "ue4ss/UE4SS.dll", new byte[] { 1 } } });
            Reject(() => installation.UpdateRuntime(invalid, release), "Incomplete ZIP was accepted."); Unchanged(before);
            Directory.Delete(Path.Combine(runtime, @"ue4ss\blocked"));
            backup = installation.UpdateRuntime(archive, release);
            config.Enabled = false; installation.Apply(config);
            Check(installation.LoaderSuspended, "Updated owned proxy could not be suspended.");
            installation.Uninstall();
            Check(!File.Exists(library) && !File.Exists(Path.Combine(runtime, "dwmapi.dll.PBZKeyForge-disabled")), "Owned updated runtime could not be uninstalled.");

            game = Game(output, "fresh update while off"); installation = new KeyEnhancementInstallation(game); installation.LoadConfiguration();
            installation.UpdateRuntime(archive, release);
            Check(installation.LoaderSuspended, "Updating while enhancement is off activated a fresh standalone loader.");
            config = installation.LoadConfiguration(); config.Enabled = true; installation.Apply(config);
            Check(installation.InstalledEnabled && !installation.LoaderSuspended, "A freshly updated loader could not enable enhancement.");
            installation.Uninstall();

            game = Game(output, "foreign runtime update"); runtime = Path.Combine(game, @"platform\steam");
            foreach (var pair in Ue4ssRelease.ReadPackage(baselineArchive)) Write(Path.Combine(runtime, pair.Key.Replace('/', Path.DirectorySeparatorChar)), pair.Value);
            string signature = Path.Combine(runtime, @"ue4ss\UE4SS_Signatures\User.lua"); Write(signature, new byte[] { 6, 7 });
            userMod = Path.Combine(runtime, @"ue4ss\Mods\OtherMod\Scripts\main.lua"); Write(userMod, new byte[] { 8, 9 });
            installation = new KeyEnhancementInstallation(game); installation.LoadConfiguration();
            installation.UpdateRuntime(archive, release);
            Check(!installation.LoaderSuspended && File.Exists(Path.Combine(runtime, "dwmapi.dll")), "Updating a foreign loader suspended other Mods.");
            Check(File.ReadAllBytes(signature).SequenceEqual(new byte[] { 6, 7 }), "Update changed foreign signatures.");
            config = installation.LoadConfiguration(); config.Enabled = true; installation.Apply(config); installation.Uninstall();
            Check(File.ReadAllBytes(Path.Combine(runtime, @"ue4ss\UE4SS.dll")).SequenceEqual(package["ue4ss/UE4SS.dll"]) && File.Exists(userMod), "Uninstall removed an explicitly updated foreign runtime.");

            string api = "[{\"draft\":false,\"prerelease\":true,\"published_at\":\"2100\",\"assets\":[]},{\"draft\":false,\"prerelease\":false,\"published_at\":\"2099\",\"tag_name\":\"new\",\"html_url\":\"https://github.com/KintaroModding/ue4ss/releases/tag/new\",\"assets\":[{\"id\":42,\"updated_at\":\"2099\",\"name\":\"phantombladezero_ue4ss_new.zip\",\"browser_download_url\":\"https://github.com/KintaroModding/ue4ss/releases/download/new/phantombladezero_ue4ss_new.zip\"}]}]";
            Check(Ue4ssRelease.ParseLatest(api).Version == "new", "Release discovery did not select the stable PBZ asset.");
            if (args.Contains("--network"))
            {
                var latest = Ue4ssRelease.CheckLatest();
                string downloaded = latest.DownloadPackage(route => Console.WriteLine("Download route: " + route));
                Check(Ue4ssRelease.ReadPackage(downloaded).ContainsKey("ue4ss/UE4SS.dll"), "Downloaded release is incomplete.");
                bool contacted = false;
                Check(latest.DownloadPackage(route => contacted = true) == downloaded && !contacted, "Unchanged release was downloaded again instead of using the cache.");
                Console.WriteLine("Network release: " + latest.Version);
            }
            Console.WriteLine("Ue4ssUpdateTests PASS: " + checks + " assertions; fixture=" + output); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
