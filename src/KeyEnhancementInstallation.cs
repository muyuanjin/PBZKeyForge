using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace PBZKeyForge
{
    public enum KeyEnhancementInstallState { Disabled, Enabled, Incomplete, Conflict }
    public enum KeyEnhancementLoaderState { Missing, Compatible, Incomplete, Conflict }
    public sealed class KeyEnhancementUninstallResult
    {
        public string[] RemovedFiles, RetainedFiles;
        public bool SharedLoaderRetained;
        public bool Complete { get { return RetainedFiles.Length == 0; } }
    }

    public sealed partial class KeyEnhancementInstallation
    {
        const string ModName = "PBZInputRouter";
        const string ModPrefix = "ue4ss/Mods/PBZInputRouter/";
        const string ConfigurationFile = "configuration.json";
        const string RetainedConfigurationFile = "PBZKeyForge_KeyEnhancement.configuration.json";
        const string LuaConfigurationFile = "Scripts/router-config.lua";
        const string OwnershipFile = "PBZKeyForge_KeyEnhancement.install.json";
        public sealed class InstallationSource
        {
            public string Release;
            public List<string> Files = new List<string>();
            public string PayloadBackup;
            public string RuntimeArchive;
            public bool ProxySuspended;
            public string ProxyBytes;
            public Dictionary<string, string> OriginalActivation = new Dictionary<string, string>();
            public Dictionary<string, string> WrittenActivation = new Dictionary<string, string>();
        }
        static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        static readonly Dictionary<string, byte[]> Payload = ReadPayload();
        readonly string runtimeDirectory;
        bool configurationLoaded;
        bool modifiedManagedFiles;

        public string GameDirectory { get; private set; }
        public string InstallDirectory { get; private set; }
        public string ConfigurationPath { get { return Path.Combine(InstallDirectory, ConfigurationFile); } }
        public string RetainedConfigurationPath { get { return Path.Combine(runtimeDirectory, RetainedConfigurationFile); } }
        public string ConfigurationText { get; private set; }
        string retainedConfigurationText;
        public bool ConfigurationChanged { get { return configurationLoaded && (ReadConfigurationText() != ConfigurationText
            || ConfigurationText == null && ReadRetainedConfigurationText() != retainedConfigurationText); } }
        public bool InstalledEnabled { get { return ReadState() == KeyEnhancementInstallState.Enabled; } }

        public KeyEnhancementInstallation(string gameDirectory)
        {
            GameDirectory = Path.GetFullPath(gameDirectory);
            runtimeDirectory = Path.Combine(GameDirectory, @"platform\steam");
            ConfigurePaths();
        }

        public static string FindGameDirectory(string inputSave)
        { return PerfectDeflectNoRapidClickPenaltyMod.FindGameDirectory(inputSave); }

        public static bool IsRuntimeConfigurationPath(string path)
        {
            string target = Path.GetFullPath(path);
            return Path.GetFileName(target).Equals(RetainedConfigurationFile, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(target).Equals(ConfigurationFile, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(Path.GetDirectoryName(target)).Equals(ModName, StringComparison.OrdinalIgnoreCase);
        }

        static Dictionary<string, byte[]> ReadPayload()
        {
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            using (var stream = typeof(KeyEnhancementInstallation).Assembly.GetManifestResourceStream("PBZKeyForge.KeyEnhancement.runtime.zip"))
            {
                if (stream == null) throw new InvalidDataException("Key enhancement runtime is missing.");
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                    foreach (var entry in archive.Entries)
                    {
                        string name = entry.FullName.Replace('\\', '/');
                        const string prefix = "runtime/platform/steam/";
                        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || name.EndsWith("/")) continue;
                        name = name.Substring(prefix.Length);
                        if (name.Split('/').Any(part => part == ".." || part == ""))
                            throw new InvalidDataException("Invalid key enhancement runtime path.");
                        if (name.Equals("ue4ss/Mods/mods.txt", StringComparison.OrdinalIgnoreCase)
                            || name.Equals("ue4ss/Mods/mods.json", StringComparison.OrdinalIgnoreCase)) continue;
                        using (var input = entry.Open())
                        using (var output = new MemoryStream())
                        { input.CopyTo(output); files.Add(name, output.ToArray()); }
                    }
            }
            foreach (string required in new[] { "dwmapi.dll", "ue4ss/UE4SS.dll", ModPrefix + "Scripts/main.lua",
                ModPrefix + "Scripts/backend.lua", ModPrefix + "Scripts/router.lua", ModPrefix + "Scripts/native-input.lua",
                ModPrefix + "Scripts/native-abilities.lua", ModPrefix + "Scripts/physical-input.lua", ModPrefix + "Scripts/object-pool.lua",
                ModPrefix + "Scripts/complete-moves.lua", ModPrefix + "Scripts/resource-costs.lua", ModPrefix + "Scripts/script-context.lua",
                ModPrefix + "lib/pbzinput-focus.dll", ModPrefix + "lib/pbzability-context.dll" })
                if (!files.ContainsKey(required)) throw new InvalidDataException("Key enhancement runtime is incomplete: " + required);
            return files;
        }

        string ReadConfigurationText()
        { return File.Exists(ConfigurationPath) ? File.ReadAllText(ConfigurationPath, Utf8) : null; }

        string ReadRetainedConfigurationText()
        { return File.Exists(RetainedConfigurationPath) ? File.ReadAllText(RetainedConfigurationPath, Utf8) : null; }

        public KeyEnhancementConfiguration LoadConfiguration()
        {
            string text = ReadConfigurationText();
            ConfigurationText = text;
            retainedConfigurationText = text == null ? ReadRetainedConfigurationText() : null;
            configurationLoaded = true;
            if (text != null) return KeyEnhancementConfiguration.FromJson(text);
            if (retainedConfigurationText == null) return new KeyEnhancementConfiguration();
            var configuration = KeyEnhancementConfiguration.FromJson(retainedConfigurationText);
            configuration.SetEnabled(false);
            return configuration;
        }

        public KeyEnhancementInstallState ReadState()
        {
            var state = Inspect(InstallDirectory);
            if (state == KeyEnhancementInstallState.Enabled
                && (ReadLoaderState() != KeyEnhancementLoaderState.Compatible || LoaderSuspended || !ActivationEnabled()))
                return KeyEnhancementInstallState.Incomplete;
            if (state == KeyEnhancementInstallState.Disabled && ActivationEnabled()) return KeyEnhancementInstallState.Incomplete;
            return state;
        }

        public string ConflictDetail { get; private set; }
        KeyEnhancementInstallState FileConflict(string path)
        { ConflictDetail = I18n.Str("mod.conflict") + Environment.NewLine + path; return KeyEnhancementInstallState.Conflict; }

        static string LocalPath(string root, string path)
        { return Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)); }

        static bool IsLiveFile(string relative)
        { return new[] { "live-status.txt", "live-request.lua", "live-request.id", "live-response.txt", "live-edit.lock", "scheme-state.txt" }.Contains(relative, StringComparer.OrdinalIgnoreCase); }

        static bool IsGenerated(string relative)
        {
            if (IsLiveFile(relative)) return true;
            return relative.Equals(ConfigurationFile, StringComparison.OrdinalIgnoreCase)
                || relative.Equals(LuaConfigurationFile, StringComparison.OrdinalIgnoreCase)
                || relative.Equals("enabled.txt", StringComparison.OrdinalIgnoreCase);
        }

        static Dictionary<string, byte[]> ModFiles()
        {
            return Payload.Where(pair => pair.Key.StartsWith(ModPrefix, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(pair => pair.Key.Substring(ModPrefix.Length), pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        }

        KeyEnhancementInstallState Inspect(string directory)
        {
            ConflictDetail = null;
            modifiedManagedFiles = false;
            if (File.Exists(directory)) return FileConflict(directory);
            if (!Directory.Exists(directory)) return KeyEnhancementInstallState.Disabled;
            var expected = ModFiles();
            Dictionary<string, byte[]> previous = null;
            var source = ReadOwnership();
            Func<Dictionary<string, byte[]>> previousFiles = delegate { return previous ?? (previous = OwnedPayload(source, ModPrefix)); };
            bool incomplete = false;
            foreach (string path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                string relative = path.Substring(directory.Length + 1).Replace('\\', '/');
                byte[] bytes;
                if (IsGenerated(relative)) continue;
                byte[] actual = File.ReadAllBytes(path);
                if (!expected.TryGetValue(relative, out bytes) || !actual.SequenceEqual(bytes))
                {
                    byte[] managed;
                    if (!previousFiles().TryGetValue(ModPrefix + relative, out managed)) return FileConflict(path);
                    if (!actual.SequenceEqual(managed))
                    {
                        if (string.IsNullOrEmpty(source.PayloadBackup)) return FileConflict(path);
                        modifiedManagedFiles = true;
                    }
                    incomplete = true;
                }
            }
            foreach (string child in Directory.GetDirectories(directory, "*", SearchOption.AllDirectories))
            {
                string relative = child.Substring(directory.Length + 1).Replace('\\', '/') + "/";
                if (!expected.Keys.Any(path => path.StartsWith(relative, StringComparison.OrdinalIgnoreCase))
                    && !previousFiles().Keys.Any(path => path.StartsWith(ModPrefix + relative, StringComparison.OrdinalIgnoreCase)))
                    return FileConflict(child);
            }
            foreach (string relative in expected.Keys.Where(path => !IsGenerated(path)))
                if (!File.Exists(LocalPath(directory, relative))) incomplete = true;
            string jsonPath = Path.Combine(directory, ConfigurationFile);
            if (!File.Exists(jsonPath))
            {
                string luaConfig = LocalPath(directory, LuaConfigurationFile);
                if (File.Exists(luaConfig)) return FileConflict(luaConfig);
                return Directory.EnumerateFileSystemEntries(directory).Any() ? KeyEnhancementInstallState.Incomplete : KeyEnhancementInstallState.Disabled;
            }
            KeyEnhancementConfiguration configuration;
            string compiled;
            try
            {
                configuration = KeyEnhancementConfiguration.FromJson(File.ReadAllText(jsonPath, Utf8));
                compiled = configuration.CompileLua();
            }
            catch (Exception ex) { FileConflict(jsonPath); ConflictDetail += Environment.NewLine + ex.Message; return KeyEnhancementInstallState.Conflict; }
            string luaPath = LocalPath(directory, LuaConfigurationFile);
            if (!File.Exists(luaPath)) incomplete = true;
            else if (File.ReadAllText(luaPath, Utf8) != compiled) incomplete = true;
            return incomplete ? KeyEnhancementInstallState.Incomplete
                : configuration.Enabled ? KeyEnhancementInstallState.Enabled : KeyEnhancementInstallState.Disabled;
        }

        void EnsureConfigurationUnchanged()
        {
            if (ConfigurationChanged) throw new IOException(I18n.Str("enhancement.configchanged") + "\n" + ConfigurationPath);
        }

        void RemoveEmptyParents(string directory)
        {
            while (!string.IsNullOrEmpty(directory) && !string.Equals(directory, runtimeDirectory, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            { Directory.Delete(directory); directory = Path.GetDirectoryName(directory); }
        }

        static void WriteNew(string path, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            bool created = false;
            try
            {
                using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    created = true;
                    output.Write(bytes, 0, bytes.Length);
                }
            }
            catch
            {
                if (created && File.Exists(path)) File.Delete(path);
                throw;
            }
        }

        IOException LoaderConflict()
        { return new IOException(I18n.Str("enhancement.lifecycle.incompatible") + "\n" + runtimeDirectory); }

        static IOException Conflict(string path)
        { return new IOException(I18n.Str("mod.conflict") + "\n" + path); }

        static void EnsureGameClosed()
        { if (EditorSession.IsGameRunning) throw new IOException(I18n.Str("mod.running")); }
    }
}
