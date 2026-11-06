using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using PBZKeyForge;

public static class KeyEnhancementInstallationTests
{
    static int checks;
    static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }
    static void Reject(Action action, string message)
    {
        try { action(); }
        catch (IOException) { checks++; return; }
        catch (UnauthorizedAccessException) { checks++; return; }
        throw new Exception(message);
    }
    static string Game(string output, string name)
    {
        string root = Path.Combine(output, name);
        string native = Path.Combine(root, @"platform\steam\PBZ.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(native));
        File.WriteAllText(native, "fixture, never launched");
        return root;
    }
    static void Write(string root, string relative, string text)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, text);
    }
    static void CopyLoader(string root)
    {
        using (var stream = typeof(KeyEnhancementInstallation).Assembly.GetManifestResourceStream("PBZKeyForge.KeyEnhancement.runtime.zip"))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            foreach (var entry in archive.Entries)
            {
                const string prefix = "runtime/platform/steam/";
                if (!entry.FullName.StartsWith(prefix) || entry.FullName.Contains("/Mods/")) continue;
                string path = Path.Combine(root, entry.FullName.Substring(prefix.Length).Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var input = entry.Open()) using (var output = File.Create(path)) input.CopyTo(output);
            }
    }
    static void InspectPrototype(string output)
    {
        string root = Game(output, "prototype inspection");
        var installation = new KeyEnhancementInstallation(root);
        using (var stream = typeof(KeyEnhancementInstallation).Assembly.GetManifestResourceStream("PBZKeyForge.KeyEnhancement.legacy.zip"))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            foreach (var entry in archive.Entries)
            {
                string path = Path.Combine(installation.InstallDirectory, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var input = entry.Open()) using (var file = File.Create(path)) input.CopyTo(file);
            }
        Check(installation.ReadState() == KeyEnhancementInstallState.Incomplete, "The original prototype was not recognized.");
        string configPath = Path.Combine(installation.InstallDirectory, @"Scripts\router-config.lua");
        string original = File.ReadAllText(configPath);
        string disabled = original.Replace("enabled = true,", "enabled = false,");
        File.WriteAllText(configPath, disabled, new System.Text.UTF8Encoding(false));
        Check(installation.ReadState() == KeyEnhancementInstallState.Incomplete, "A disabled original prototype was rejected as a foreign modification.");
        Check(!installation.LoadConfiguration().Enabled, "An unconfigured prototype automatically enabled enhancement.");
        File.WriteAllText(configPath, disabled.Replace("jump-f9", "custom-jump"), new System.Text.UTF8Encoding(false));
        Check(installation.ReadState() == KeyEnhancementInstallState.Conflict, "A user-modified prototype was accepted for overwrite.");
        File.WriteAllText(configPath, disabled, new System.Text.UTF8Encoding(false));
        using (var stream = typeof(KeyEnhancementInstallation).Assembly.GetManifestResourceStream("PBZKeyForge.KeyEnhancement.runtime.zip"))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            foreach (string name in new[] { "native-input.lua", "native-abilities.lua", "physical-input.lua", "complete-moves.lua", "object-pool.lua" })
                Check(archive.Entries.Any(entry => entry.FullName.EndsWith("/Scripts/" + name, StringComparison.Ordinal)),
                    "Runtime package is missing " + name);
    }
    public static int Main(string[] args)
    {
        try
        {
            I18n.Current = "zh-Hans";
            string output = Path.GetFullPath(args[0]);
            if (Directory.Exists(output)) throw new Exception("Use a new test directory.");
            InspectPrototype(output);
            if (args.Contains("--inspect-only"))
            {
                Console.WriteLine("KeyEnhancementInstallationTests inspection: " + checks + " assertions passed");
                return 0;
            }
            string root = Game(output, "游戏 with spaces");
            string runtime = Path.Combine(root, @"platform\steam");
            var installation = new KeyEnhancementInstallation(root);
            var configuration = installation.LoadConfiguration();
            Check(!configuration.Enabled && installation.ReadState() == KeyEnhancementInstallState.Disabled, "A fresh installation must be off.");
            configuration.Enabled = true;
            installation.Apply(configuration);
            Check(installation.ReadState() == KeyEnhancementInstallState.Enabled && installation.InstalledEnabled, "Enable did not install the runtime.");
            Check(File.Exists(Path.Combine(runtime, "dwmapi.dll")) && File.Exists(Path.Combine(runtime, @"ue4ss\UE4SS.dll")), "Loader is incomplete.");
            Check(!Directory.Exists(Path.Combine(root, @"ProjectPBZ\Content\Paks")), "Installing key enhancement added a gameplay resource patch.");
            Check(File.ReadAllText(Path.Combine(installation.InstallDirectory, @"Scripts\router-config.lua")) == configuration.CompileLua(), "Installed Lua is not compiled from the saved model.");
            string main = Path.Combine(installation.InstallDirectory, @"Scripts\main.lua");
            var time = File.GetLastWriteTimeUtc(main);
            installation.Apply(configuration);
            Check(time == File.GetLastWriteTimeUtc(main), "Repeated apply rewrote an unchanged installation.");
            configuration.Enabled = false;
            installation.Apply(configuration);
            Check(installation.ReadState() == KeyEnhancementInstallState.Disabled && !File.Exists(Path.Combine(installation.InstallDirectory, "enabled.txt")), "Disable left its activation marker.");
            Check(File.Exists(installation.ConfigurationPath) && !File.Exists(Path.Combine(runtime, "dwmapi.dll")) && installation.LoaderSuspended, "Disable did not park the owned standalone proxy while retaining rules.");
            Check(File.Exists(Path.Combine(runtime, "PBZKeyForge_KeyEnhancement.install.json")), "Fresh loader installation has no source record.");
            configuration.Enabled = true;
            installation.Apply(configuration);
            File.Delete(main);
            Check(installation.ReadState() == KeyEnhancementInstallState.Incomplete, "A missing managed script was not repairable.");
            installation.Apply(configuration);
            Check(installation.ReadState() == KeyEnhancementInstallState.Enabled, "Repair did not complete.");
            Check(!File.Exists(Path.Combine(installation.InstallDirectory, "enabled.txt")) && File.ReadAllText(Path.Combine(runtime, @"ue4ss\Mods\mods.txt")).Contains("PBZInputRouter : 1"), "Enable did not use the ordered native mod list.");
            string lua = Path.Combine(installation.InstallDirectory, @"Scripts\router-config.lua");
            File.WriteAllText(lua, "incomplete generated configuration");
            Check(installation.ReadState() == KeyEnhancementInstallState.Incomplete, "Generated Lua drift must be repairable from JSON.");
            installation.Apply(configuration);
            Check(File.ReadAllText(lua) == configuration.CompileLua(), "Repair did not regenerate Lua.");
            string originalJson = File.ReadAllText(installation.ConfigurationPath);
            File.AppendAllText(installation.ConfigurationPath, "\n");
            Check(installation.ConfigurationChanged, "External configuration changes were not detected.");
            Reject(() => installation.Apply(configuration), "Apply overwrote an external JSON change.");
            Check(File.ReadAllText(installation.ConfigurationPath) == originalJson + "\n", "Conflict handling changed the external JSON.");
            configuration = installation.LoadConfiguration();
            installation.Apply(configuration);
            Check(!installation.ConfigurationChanged, "Explicit reload did not allow applying the refreshed configuration.");
            string mainText = File.ReadAllText(main);
            File.WriteAllText(main, "user modification");
            Check(installation.ReadState() == KeyEnhancementInstallState.Conflict, "An unknown script modification was not a conflict.");
            Check(installation.ConflictDetail.Contains(main), "Modified script conflict omits its file path.");
            Reject(() => installation.Apply(configuration), "Apply overwrote a modified managed script.");
            Check(File.ReadAllText(main) == "user modification", "Modified script was not preserved.");
            File.WriteAllText(main, mainText, new System.Text.UTF8Encoding(false));
            string foreign = Path.Combine(installation.InstallDirectory, "foreign.txt");
            File.WriteAllText(foreign, "preserve");
            Check(installation.ReadState() == KeyEnhancementInstallState.Conflict, "Foreign files were not a conflict.");
            Check(installation.ConflictDetail.Contains(foreign), "Foreign file conflict omits its file path.");
            Reject(() => installation.Apply(configuration), "Apply deleted a foreign file.");
            Check(File.ReadAllText(foreign) == "preserve", "Foreign file was modified.");
            File.Delete(foreign);
            using (var stream = typeof(KeyEnhancementInstallation).Assembly.GetManifestResourceStream("PBZKeyForge.KeyEnhancement.legacy.zip"))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                foreach (var entry in archive.Entries)
                    using (var input = entry.Open())
                    using (var file = File.Create(Path.Combine(installation.InstallDirectory, entry.FullName.Replace('/', Path.DirectorySeparatorChar)))) input.CopyTo(file);
            File.Delete(installation.ConfigurationPath);
            installation = new KeyEnhancementInstallation(root);
            Check(installation.ReadState() == KeyEnhancementInstallState.Incomplete, "The known prototype was not recognized for upgrade.");
            installation.LoadConfiguration();
            installation.Apply(configuration);
            Check(installation.ReadState() == KeyEnhancementInstallState.Enabled, "Known prototype upgrade did not complete.");

            string existing = Game(output, "existing loader");
            string existingRuntime = Path.Combine(existing, @"platform\steam");
            CopyLoader(existingRuntime);
            var existingProxy = File.ReadAllBytes(Path.Combine(existingRuntime, "dwmapi.dll"));
            var existingLibrary = File.ReadAllBytes(Path.Combine(existingRuntime, @"ue4ss\UE4SS.dll"));
            Write(existingRuntime, @"ue4ss\UE4SS-settings.ini", "user settings");
            Write(existingRuntime, @"ue4ss\Mods\mods.txt", "OtherMod : 1\r\n");
            Write(existingRuntime, @"ue4ss\Mods\mods.json", "[{\"mod_name\":\"OtherMod\",\"mod_enabled\":true}]");
            var shared = new KeyEnhancementInstallation(existing);
            shared.LoadConfiguration(); shared.Apply(configuration);
            Check(File.ReadAllBytes(Path.Combine(existingRuntime, "dwmapi.dll")).SequenceEqual(existingProxy)
                && File.ReadAllBytes(Path.Combine(existingRuntime, @"ue4ss\UE4SS.dll")).SequenceEqual(existingLibrary)
                && File.ReadAllText(Path.Combine(existingRuntime, @"ue4ss\UE4SS-settings.ini")) == "user settings", "An existing shared loader was overwritten.");
            Check(File.ReadAllText(Path.Combine(existingRuntime, @"ue4ss\Mods\mods.txt")) == "OtherMod : 1\r\nPBZInputRouter : 1\r\n"
                && File.ReadAllText(Path.Combine(existingRuntime, @"ue4ss\Mods\mods.json")) == "[{\"mod_name\":\"OtherMod\",\"mod_enabled\":true}]", "Another mod's activation list changed.");
            Write(existingRuntime, @"ue4ss\Mods\mods.txt", "OtherMod : 1\r\nPBZInputRouter : 0\r\n");
            Check(shared.ReadState() == KeyEnhancementInstallState.Incomplete, "An explicit loader-level disable was reported as enabled.");
            shared.Apply(configuration);
            Check(shared.ReadState() == KeyEnhancementInstallState.Enabled && File.ReadAllText(Path.Combine(existingRuntime, @"ue4ss\Mods\mods.txt")) == "OtherMod : 1\r\nPBZInputRouter : 1\r\n", "Enable did not update only the Router's textual activation.");
            Write(existingRuntime, @"ue4ss\Mods\mods.txt", "OtherMod : 1\r\n");
            Write(existingRuntime, @"ue4ss\Mods\mods.json", "[{\"mod_name\":\"OtherMod\",\"mod_enabled\":true,\"custom\":7},{\"mod_name\":\"PBZInputRouter\",\"mod_enabled\":false}]");
            Check(shared.ReadState() == KeyEnhancementInstallState.Incomplete, "JSON loader-level disable was reported as enabled.");
            shared.Apply(configuration);
            string activatedJson = File.ReadAllText(Path.Combine(existingRuntime, @"ue4ss\Mods\mods.json"));
            Check(shared.ReadState() == KeyEnhancementInstallState.Enabled && activatedJson.Contains("\"custom\":7") && activatedJson.Contains("\"OtherMod\""), "Enable did not preserve another mod's JSON settings.");
            configuration.Enabled = false;
            shared.Apply(configuration);
            Check(shared.ReadState() == KeyEnhancementInstallState.Disabled, "Disable was blocked by the loader's disabled entry.");
            var unowned = shared.Uninstall();
            Check(File.Exists(Path.Combine(existingRuntime, "dwmapi.dll")) && !Directory.Exists(shared.InstallDirectory), "Uninstall removed an existing unowned loader.");

            string incompatibleRoot = Game(output, "incompatible loader");
            string incompatibleRuntime = Path.Combine(incompatibleRoot, @"platform\steam");
            Write(incompatibleRuntime, "dwmapi.dll", "unrelated proxy"); Write(incompatibleRuntime, @"ue4ss\UE4SS.dll", "unsupported loader");
            var incompatible = new KeyEnhancementInstallation(incompatibleRoot); incompatible.LoadConfiguration();
            configuration.Enabled = true;
            Check(incompatible.ReadLoaderState() == KeyEnhancementLoaderState.Conflict, "Unknown loader was considered compatible.");
            Reject(() => incompatible.Apply(configuration), "Unknown complete loader was accepted.");
            Check(!Directory.Exists(incompatible.InstallDirectory), "Incompatible loader received a partial router install.");

            string customRoot = Game(output, "redirected mod path"); string customRuntime = Path.Combine(customRoot, @"platform\steam"); CopyLoader(customRuntime);
            Write(customRuntime, @"ue4ss\UE4SS-settings.ini", "[Overrides]\r\nModsFolderPath = ../Elsewhere\r\n");
            var redirected = new KeyEnhancementInstallation(customRoot); redirected.LoadConfiguration();
            redirected.Apply(configuration);
            Check(Directory.Exists(Path.Combine(customRuntime, @"ue4ss\..\Elsewhere\PBZInputRouter")), "A redirected native mod path was not honored.");
            redirected.Uninstall();
            Write(customRuntime, @"ue4ss\UE4SS-settings.ini", "[EngineVersionOverride]\r\nMinorVersion = 0\r\n");
            Check(redirected.ReadLoaderState() == KeyEnhancementLoaderState.Compatible, "An external loader was rejected because its settings differ from the bundled package.");
            Write(customRuntime, @"ue4ss\UE4SS-settings.ini", "[EngineVersionOverride]\r\nMajorVersion =\r\nMinorVersion =\r\nDebugBuild =\r\nStats =\r\nPointerHash = Auto ; automatic detection\r\n");
            Check(redirected.ReadLoaderState() == KeyEnhancementLoaderState.Compatible, "Automatic engine detection was not accepted.");

            var cleanRoot = Game(output, "owned uninstall"); var clean = new KeyEnhancementInstallation(cleanRoot); clean.LoadConfiguration(); clean.Apply(configuration);
            var cleanResult = clean.Uninstall();
            Check(cleanResult.Complete && !Directory.Exists(clean.InstallDirectory) && !File.Exists(Path.Combine(cleanRoot, @"platform\steam\dwmapi.dll")), "Owned unchanged components were not fully removed.");

            var retainedRoot = Game(output, "shared uninstall"); var retainedInstall = new KeyEnhancementInstallation(retainedRoot); retainedInstall.LoadConfiguration(); retainedInstall.Apply(configuration);
            string retainedRuntime = Path.Combine(retainedRoot, @"platform\steam");
            Write(retainedRuntime, @"ue4ss\Mods\OtherMod\Scripts\main.lua", "preserve");
            var retainedResult = retainedInstall.Uninstall();
            Check(retainedResult.SharedLoaderRetained && File.Exists(Path.Combine(retainedRuntime, "dwmapi.dll")) && !Directory.Exists(retainedInstall.InstallDirectory), "Shared loader was not retained while removing the router.");
            Check(File.ReadAllText(Path.Combine(retainedRuntime, @"ue4ss\Mods\OtherMod\Scripts\main.lua")) == "preserve", "Uninstall changed another mod.");
            Directory.Delete(Path.Combine(retainedRuntime, @"ue4ss\Mods\OtherMod"), true);
            Check(retainedInstall.Uninstall().Complete && !File.Exists(Path.Combine(retainedRuntime, "dwmapi.dll")), "Retained ownership did not allow cleanup after other mods were removed.");

            var modifiedRoot = Game(output, "modified uninstall"); var modifiedInstall = new KeyEnhancementInstallation(modifiedRoot); modifiedInstall.LoadConfiguration(); modifiedInstall.Apply(configuration);
            string modifiedRuntime = Path.Combine(modifiedRoot, @"platform\steam");
            Write(modifiedRuntime, @"ue4ss\UE4SS-settings.ini", "changed settings");
            Write(modifiedInstall.InstallDirectory, @"Scripts\main.lua", "changed script");
            var modifiedResult = modifiedInstall.Uninstall();
            Check(!modifiedResult.Complete && File.ReadAllText(Path.Combine(modifiedRuntime, @"ue4ss\UE4SS-settings.ini")) == "changed settings"
                && File.ReadAllText(Path.Combine(modifiedInstall.InstallDirectory, @"Scripts\main.lua")) == "changed script", "Uninstall discarded modified components.");
            Check(!File.Exists(Path.Combine(modifiedInstall.InstallDirectory, "enabled.txt")), "Uninstall left a modified router enabled.");

            var listedRoot = Game(output, "explicit activation uninstall"); var listedInstall = new KeyEnhancementInstallation(listedRoot); listedInstall.LoadConfiguration(); listedInstall.Apply(configuration);
            string listedRuntime = Path.Combine(listedRoot, @"platform\steam");
            Write(listedRuntime, @"ue4ss\Mods\mods.txt", "; user header\r\nPBZInputRouter : 1\r\nOtherMod : 1\r\n");
            Write(listedRuntime, @"ue4ss\Mods\mods.json", "[{\"mod_name\":\"PBZInputRouter\",\"mod_enabled\":true},{\"mod_name\":\"OtherMod\",\"mod_enabled\":true}]");
            listedInstall.Uninstall();
            Check(File.ReadAllText(Path.Combine(listedRuntime, @"ue4ss\Mods\mods.txt")) == "; user header\r\nOtherMod : 1\r\n", "Uninstall changed another textual mod entry.");
            string retainedList = File.ReadAllText(Path.Combine(listedRuntime, @"ue4ss\Mods\mods.json"));
            Check(!retainedList.Contains("PBZInputRouter") && retainedList.Contains("OtherMod"), "Uninstall left an explicit Router activation or removed another JSON entry.");

            var provenanceRoot = Game(output, "invalid source record"); var provenanceInstall = new KeyEnhancementInstallation(provenanceRoot); provenanceInstall.LoadConfiguration(); provenanceInstall.Apply(configuration);
            string provenanceRuntime = Path.Combine(provenanceRoot, @"platform\steam");
            Write(provenanceRuntime, "PBZKeyForge_KeyEnhancement.install.json", "invalid source record");
            Reject(() => provenanceInstall.Uninstall(), "Uninstall accepted an unrecognized source record.");
            Check(File.Exists(Path.Combine(provenanceRuntime, "dwmapi.dll")) && File.ReadAllText(Path.Combine(provenanceRuntime, @"ue4ss\Mods\mods.txt")).Contains("PBZInputRouter : 1"), "Source conflict removed existing components.");

            string failed = Game(output, "failed install");
            string failedRuntime = Path.Combine(failed, @"platform\steam");
            Write(failedRuntime, @"ue4ss\Mods", "obstruction");
            var interrupted = new KeyEnhancementInstallation(failed);
            interrupted.LoadConfiguration();
            configuration.Enabled = true;
            Reject(() => interrupted.Apply(configuration), "A blocked mod directory unexpectedly installed.");
            Check(File.ReadAllText(Path.Combine(failedRuntime, @"ue4ss\Mods")) == "obstruction", "Failed installation replaced an unrelated file.");
            Check(!File.Exists(Path.Combine(failedRuntime, "dwmapi.dll"))
                && !File.Exists(Path.Combine(failedRuntime, @"ue4ss\UE4SS.dll")), "Failed installation left newly installed loader files.");
            Check(!Directory.GetDirectories(failedRuntime, "PBZKeyForge_KeyEnhancement_*.tmp*").Any(), "Failed installation leaked staging directories.");
            var invalid = new KeyEnhancementInstallation(Game(output, "invalid configuration"));
            Write(invalid.InstallDirectory, "configuration.json", "invalid JSON");
            try { invalid.LoadConfiguration(); throw new Exception("An invalid JSON configuration was accepted."); }
            catch (ArgumentException) { checks++; }
            File.WriteAllText(invalid.ConfigurationPath, configuration.ToJson());
            Check(invalid.ConfigurationChanged && invalid.LoadConfiguration().Enabled, "External repair after an initial parse failure could not be reloaded.");
            Check(File.ReadAllText(Path.Combine(runtime, "PBZ.exe")) == "fixture, never launched", "Installation changed the game executable.");
            Console.WriteLine("KeyEnhancementInstallationTests PASS: " + checks + " assertions; fixture=" + output);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
