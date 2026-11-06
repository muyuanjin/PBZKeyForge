using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace PBZKeyForge
{
    public sealed partial class KeyEnhancementInstallation
    {
        string loaderDirectory, modsDirectory, controllingList;
        readonly List<string> modDirectories = new List<string>();
        const string SuspendedProxy = "dwmapi.dll.PBZKeyForge-disabled";
        public bool LoaderSuspended { get { return File.Exists(Path.Combine(runtimeDirectory, SuspendedProxy)); } }

        void ConfigurePaths()
        {
            loaderDirectory = Path.Combine(runtimeDirectory, "ue4ss");
            modsDirectory = Path.Combine(loaderDirectory, "Mods");
            string settings = Path.Combine(loaderDirectory, "UE4SS-settings.ini");
            var additional = new List<string>();
            if (File.Exists(settings))
                foreach (string line in File.ReadAllLines(settings))
                {
                    var match = Regex.Match(line, @"^\s*(ModsFolderPath|[+-]ModsFolderPaths|ControllingModsTxt)\s*=\s*([^;]*?)\s*(?:;.*)?$", RegexOptions.IgnoreCase);
                    if (!match.Success || match.Groups[2].Value.Trim().Length == 0) continue;
                    string value = ResolveLoaderPath(match.Groups[2].Value.Trim().Trim('"'));
                    string key = match.Groups[1].Value;
                    if (key.Equals("ModsFolderPath", StringComparison.OrdinalIgnoreCase)) modsDirectory = value;
                    else if (key.Equals("ControllingModsTxt", StringComparison.OrdinalIgnoreCase)) controllingList = value;
                    else if (key[0] == '+') additional.Add(value);
                    else additional.RemoveAll(path => path.Equals(value, StringComparison.OrdinalIgnoreCase));
                }
            modDirectories.Add(modsDirectory);
            modDirectories.AddRange(additional);
            string environmentPaths = Environment.GetEnvironmentVariable("UE4SS_MODS_PATHS");
            if (!string.IsNullOrEmpty(environmentPaths))
                modDirectories.AddRange(environmentPaths.Split(';').Where(path => !string.IsNullOrWhiteSpace(path)).Select(ResolveLoaderPath));
            var existing = modDirectories.Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => Path.Combine(path, ModName)).Where(Directory.Exists).ToArray();
            if (existing.Length > 1) throw Conflict(string.Join("\n", existing));
            InstallDirectory = existing.Length == 1 ? existing[0] : Path.Combine(modsDirectory, ModName);
        }

        string ResolveLoaderPath(string value)
        { return Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(loaderDirectory, value)); }

        IEnumerable<string> ActivationLists()
        {
            if (controllingList != null) yield return controllingList;
            foreach (string directory in modDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (controllingList == null) yield return Path.Combine(directory, "mods.txt");
                yield return Path.Combine(directory, "mods.json");
            }
        }

        string PrimaryList { get { return controllingList ?? Path.Combine(Path.GetDirectoryName(InstallDirectory), "mods.txt"); } }
        static readonly Regex OwnTextEntry = new Regex(@"(?im)^(\s*PBZInputRouter\s*:\s*)([01])(\s*(?:;[^\r\n]*)?)$");

        bool ActivationEnabled()
        {
            if (File.Exists(Path.Combine(InstallDirectory, "enabled.txt"))) return true;
            foreach (string path in ActivationLists().Where(File.Exists))
            {
                string text = File.ReadAllText(path);
                if (Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    if (OwnTextEntry.Matches(text).Cast<Match>().Any(match => match.Groups[2].Value == "1")) return true;
                }
                else if (text.IndexOf(ModName, StringComparison.OrdinalIgnoreCase) >= 0 && JsonEntries(text).Any(entry => IsOwnJson(entry.Text) && Regex.IsMatch(entry.Text, @"""mod_enabled""\s*:\s*true"))) return true;
            }
            return false;
        }

        sealed class JsonEntry { public int Start, Length; public string Text; }
        static List<JsonEntry> JsonEntries(string text)
        {
            var result = new List<JsonEntry>();
            if (!(new JavaScriptSerializer().DeserializeObject(text) is object[])) throw new IOException("Invalid UE4SS mod list.");
            bool quoted = false, escaped = false;
            int depth = 0, start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') quoted = false;
                    continue;
                }
                if (c == '"') { quoted = true; continue; }
                if (c == '{') { if (depth == 0) start = i; depth++; }
                else if (c == '}' && --depth == 0) result.Add(new JsonEntry { Start = start, Length = i - start + 1, Text = text.Substring(start, i - start + 1) });
            }
            return result;
        }

        static bool IsOwnJson(string text)
        {
            var entry = new JavaScriptSerializer().DeserializeObject(text) as Dictionary<string, object>;
            object name;
            return entry != null && entry.TryGetValue("mod_name", out name) && string.Equals(name as string, ModName, StringComparison.OrdinalIgnoreCase);
        }

        static string UpdateOwnJson(string text, bool enabled, bool remove)
        {
            foreach (var entry in JsonEntries(text).Where(entry => IsOwnJson(entry.Text)).OrderByDescending(entry => entry.Start))
            {
                if (!remove)
                {
                    string replacement = Regex.Replace(entry.Text, @"(""mod_enabled""\s*:\s*)(true|false)", "$1" + (enabled ? "true" : "false"));
                    if (replacement == entry.Text && !Regex.IsMatch(entry.Text, @"""mod_enabled""\s*:"))
                        replacement = entry.Text.Insert(entry.Text.Length - 1, ",\"mod_enabled\":" + (enabled ? "true" : "false"));
                    text = text.Remove(entry.Start, entry.Length).Insert(entry.Start, replacement);
                }
                else
                {
                    int start = entry.Start, end = start + entry.Length, next = end;
                    while (next < text.Length && char.IsWhiteSpace(text[next])) next++;
                    if (next < text.Length && text[next] == ',') end = next + 1;
                    else
                    {
                        int previous = start - 1;
                        while (previous >= 0 && char.IsWhiteSpace(text[previous])) previous--;
                        if (previous >= 0 && text[previous] == ',') start = previous;
                    }
                    text = text.Remove(start, end - start);
                }
            }
            return text;
        }

        Dictionary<string, byte[]> PrepareActivationChanges(bool enabled)
        {
            var changes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in ActivationLists().Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists))
            {
                string original = File.ReadAllText(path), updated;
                if (Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase))
                    updated = OwnTextEntry.Replace(original, match => match.Groups[1].Value + (enabled ? "1" : "0") + match.Groups[3].Value);
                else updated = original.IndexOf(ModName, StringComparison.OrdinalIgnoreCase) < 0 ? original : UpdateOwnJson(original, enabled, false);
                if (updated != original) changes[path] = EncodeLikeFile(path, updated);
            }
            string primary = PrimaryList;
            string primaryText = File.Exists(primary) ? File.ReadAllText(primary) : "";
            if (changes.ContainsKey(primary)) primaryText = Decode(changes[primary]);
            if (enabled && !OwnTextEntry.IsMatch(primaryText))
            {
                string newline = primaryText.Contains("\r\n") || primaryText.Length == 0 ? "\r\n" : "\n";
                string updated = primaryText + (primaryText.Length > 0 && !primaryText.EndsWith("\n") ? newline : "") + ModName + " : 1" + newline;
                changes[primary] = EncodeLikeFile(primary, updated);
            }
            return changes;
        }

        static byte[] EncodeLikeFile(string path, string text)
        {
            if (!File.Exists(path)) return Utf8.GetBytes(text);
            byte[] original = File.ReadAllBytes(path);
            if (original.Length >= 2 && original[0] == 255 && original[1] == 254)
                return System.Text.Encoding.Unicode.GetPreamble().Concat(System.Text.Encoding.Unicode.GetBytes(text)).ToArray();
            if (original.Length >= 3 && original[0] == 239 && original[1] == 187 && original[2] == 191)
                return new byte[] {239, 187, 191}.Concat(Utf8.GetBytes(text)).ToArray();
            return Utf8.GetBytes(text);
        }

        static string Decode(byte[] bytes)
        { using (var reader = new StreamReader(new MemoryStream(bytes), true)) return reader.ReadToEnd(); }

        public KeyEnhancementLoaderState ReadLoaderState()
        {
            string library = Path.Combine(loaderDirectory, "UE4SS.dll");
            string proxy = Path.Combine(runtimeDirectory, "dwmapi.dll");
            if (File.Exists(library))
            {
                if (!IsPe(library)) return KeyEnhancementLoaderState.Conflict;
                var source = ReadOwnership();
                if (source.Files.Any(relative => !File.Exists(LocalPath(runtimeDirectory, relative))
                    && !(relative.Equals("dwmapi.dll", StringComparison.OrdinalIgnoreCase) && LoaderSuspended))) return KeyEnhancementLoaderState.Incomplete;
                return KeyEnhancementLoaderState.Compatible;
            }
            if (File.Exists(proxy) || LoaderSuspended || Directory.Exists(library)) return KeyEnhancementLoaderState.Conflict;
            return Directory.Exists(loaderDirectory) && Directory.EnumerateFileSystemEntries(loaderDirectory).Any(path => !path.Equals(modsDirectory, StringComparison.OrdinalIgnoreCase))
                ? KeyEnhancementLoaderState.Incomplete : KeyEnhancementLoaderState.Missing;
        }

        static bool IsPe(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream))
            {
                if (stream.Length < 64 || reader.ReadUInt16() != 0x5a4d) return false;
                stream.Position = 60; int offset = reader.ReadInt32();
                if (offset < 64 || offset + 6 > stream.Length) return false;
                stream.Position = offset;
                return reader.ReadUInt32() == 0x4550 && reader.ReadUInt16() == 0x8664;
            }
        }

        Dictionary<string, byte[]> OwnedPayload(InstallationSource source)
        {
            if (string.IsNullOrEmpty(source.PayloadBackup)) return Payload;
            var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            using (var stream = File.OpenRead(source.PayloadBackup))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                foreach (var entry in zip.Entries)
                    using (var input = entry.Open()) using (var output = new MemoryStream())
                    { input.CopyTo(output); result[entry.FullName] = output.ToArray(); }
            return result;
        }

        bool MatchesManaged(string relative, byte[] actual)
        {
            if (MatchesLegacy(relative, actual)) return true;
            var source = ReadOwnership(); byte[] previous;
            return source.PayloadBackup != null && OwnedPayload(source).TryGetValue(ModPrefix + relative, out previous) && actual.SequenceEqual(previous);
        }

        string SavePayloadBackup()
        {
            string path = Path.Combine(GameSchemeSettings.BackupDirectory(), "key-enhancement-" + Guid.NewGuid().ToString("N") + ".zip");
            using (var stream = new FileStream(path, FileMode.CreateNew))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                foreach (var pair in Payload)
                    using (var output = zip.CreateEntry(pair.Key).Open()) output.Write(pair.Value, 0, pair.Value.Length);
            return path;
        }

        InstallationSource ReadOwnership()
        {
            string path = Path.Combine(runtimeDirectory, OwnershipFile);
            if (!File.Exists(path)) return new InstallationSource();
            InstallationSource source;
            try { source = new JavaScriptSerializer().Deserialize<InstallationSource>(File.ReadAllText(path, Utf8)); }
            catch (ArgumentException) { throw new IOException(I18n.Str("enhancement.lifecycle.sourceconflict")); }
            catch (InvalidOperationException) { throw new IOException(I18n.Str("enhancement.lifecycle.sourceconflict")); }
            if (source == null || source.Files == null) throw new IOException(I18n.Str("enhancement.lifecycle.sourceconflict"));
            foreach (string relative in source.Files)
                if (Path.IsPathRooted(relative) || relative.Split('/', '\\').Any(part => part == "..")) throw new IOException(I18n.Str("enhancement.lifecycle.sourceconflict"));
            if (source.OriginalActivation == null) source.OriginalActivation = new Dictionary<string, string>();
            if (source.WrittenActivation == null) source.WrittenActivation = new Dictionary<string, string>();
            return source;
        }

        void WriteOwnership(InstallationSource source)
        {
            string path = Path.Combine(runtimeDirectory, OwnershipFile), temporary = path + ".tmp";
            try
            {
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(source), Utf8);
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        bool OtherModsPresent()
        {
            foreach (string directory in modDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
                if (Directory.Exists(directory) && Directory.GetDirectories(directory).Any(path => !Path.GetFileName(path).Equals(ModName, StringComparison.OrdinalIgnoreCase)
                    && (Directory.Exists(Path.Combine(path, "Scripts")) || Directory.Exists(Path.Combine(path, "dlls"))))) return true;
            foreach (string path in ActivationLists().Where(File.Exists))
            {
                string text = File.ReadAllText(path);
                if (Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    if (Regex.Matches(text, @"(?im)^\s*([^;\s][^:\r\n]*)\s*:\s*[01]").Cast<Match>()
                        .Any(match => !match.Groups[1].Value.Trim().Equals(ModName, StringComparison.OrdinalIgnoreCase))) return true;
                }
                else
                {
                    try { if (JsonEntries(text).Any(entry => !IsOwnJson(entry.Text))) return true; }
                    catch (ArgumentException) { return true; }
                    catch (InvalidOperationException) { return true; }
                    catch (IOException) { return true; }
                }
            }
            if (Directory.Exists(Path.Combine(GameDirectory, @"ProjectPBZ\Content\Paks\LogicMods"))
                && Directory.EnumerateFileSystemEntries(Path.Combine(GameDirectory, @"ProjectPBZ\Content\Paks\LogicMods")).Any()) return true;
            return false;
        }

        void SetProxySuspended(InstallationSource source, bool suspended)
        {
            string active = Path.Combine(runtimeDirectory, "dwmapi.dll"), parked = Path.Combine(runtimeDirectory, SuspendedProxy);
            if (source.ProxyBytes == null) return;
            byte[] original = Convert.FromBase64String(source.ProxyBytes);
            if (suspended)
            {
                if (!File.Exists(active)) return;
                if (!File.ReadAllBytes(active).SequenceEqual(original) || File.Exists(parked)) return;
                File.Move(active, parked); source.ProxySuspended = true;
            }
            else if (source.ProxySuspended)
            {
                if (File.Exists(active))
                {
                    if (!File.Exists(parked) || !File.ReadAllBytes(parked).SequenceEqual(original)) throw LoaderConflict();
                    File.Delete(parked);
                    source.Files.RemoveAll(path => path.Equals("dwmapi.dll", StringComparison.OrdinalIgnoreCase));
                    source.ProxyBytes = null; source.ProxySuspended = false;
                    return;
                }
                if (!File.Exists(parked) || !File.ReadAllBytes(parked).SequenceEqual(original)) throw LoaderConflict();
                File.Move(parked, active); source.ProxySuspended = false;
            }
        }

        public void Apply(KeyEnhancementConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException("configuration");
            var candidate = KeyEnhancementConfiguration.FromJson(configuration.ToJson());
            string json = candidate.ToJson(), lua = candidate.CompileLua();
            EnsureGameClosed(); EnsureConfigurationUnchanged();
            if (!configurationLoaded) LoadConfiguration();
            if (!File.Exists(Path.Combine(runtimeDirectory, "PBZ.exe"))) throw new DirectoryNotFoundException(I18n.Str("mod.unsupported"));
            var state = ReadState();
            if (state == KeyEnhancementInstallState.Conflict) throw new IOException(ConflictDetail ?? I18n.Str("mod.conflict") + "\n" + InstallDirectory);
            var loaderState = ReadLoaderState();
            if (candidate.Enabled && (loaderState == KeyEnhancementLoaderState.Conflict
                || loaderState == KeyEnhancementLoaderState.Incomplete && ReadOwnership().Files.Count == 0)) throw LoaderConflict();
            if (candidate.Enabled)
            {
                string library = Path.Combine(loaderDirectory, "UE4SS.dll");
                string proxy = Path.Combine(runtimeDirectory, LoaderSuspended ? SuspendedProxy : "dwmapi.dll");
                var runtime = File.Exists(library) && File.Exists(proxy) ? null : OwnedPayload(ReadOwnership());
                using (var libraryFile = File.Exists(library) ? MemoryMappedFile.CreateFromFile(library, FileMode.Open, null, 0, MemoryMappedFileAccess.Read) : null)
                using (var proxyFile = File.Exists(proxy) ? MemoryMappedFile.CreateFromFile(proxy, FileMode.Open, null, 0, MemoryMappedFileAccess.Read) : null)
                using (Stream libraryStream = libraryFile != null ? (Stream)libraryFile.CreateViewStream(0, 0, MemoryMappedFileAccess.Read) : new MemoryStream(runtime["ue4ss/UE4SS.dll"], false))
                using (Stream proxyStream = proxyFile != null ? (Stream)proxyFile.CreateViewStream(0, 0, MemoryMappedFileAccess.Read) : new MemoryStream(runtime["dwmapi.dll"], false))
                    KeyEnhancementPrerequisites.EnsureAvailable(runtimeDirectory, libraryStream, proxyStream);
            }
            if (ConfigurationText == json && state == (candidate.Enabled ? KeyEnhancementInstallState.Enabled : KeyEnhancementInstallState.Disabled)
                && !(LoaderSuspended && OtherModsPresent())) return;
            string staging = Path.Combine(Path.GetDirectoryName(InstallDirectory), ".PBZKeyForge-" + Guid.NewGuid().ToString("N"));
            string retired = staging + ".old";
            string sourcePath = Path.Combine(runtimeDirectory, OwnershipFile);
            byte[] oldSource = File.Exists(sourcePath) ? File.ReadAllBytes(sourcePath) : null;
            var source = ReadOwnership();
            bool otherMods = OtherModsPresent();
            var activation = PrepareActivationChanges(candidate.Enabled);
            var originals = activation.ToDictionary(pair => pair.Key, pair => File.Exists(pair.Key) ? File.ReadAllBytes(pair.Key) : null);
            var created = new List<string>();
            string newBackup = null;
            bool replaced = false, complete = false, wasSuspended = source.ProxySuspended;
            try
            {
                var previousPayload = OwnedPayload(source);
                if (source.PayloadBackup == null || !ModFiles().All(pair => {
                    byte[] previous;
                    return previousPayload.TryGetValue(ModPrefix + pair.Key, out previous) && previous.SequenceEqual(pair.Value);
                }))
                {
                    newBackup = SavePayloadBackup();
                    if (source.PayloadBackup != null)
                    {
                        var previous = previousPayload;
                        using (var stream = new FileStream(newBackup, FileMode.Open, FileAccess.ReadWrite))
                        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update))
                            foreach (string relative in source.Files)
                            {
                                byte[] value;
                                if (!previous.TryGetValue(relative, out value)) continue;
                                var old = zip.GetEntry(relative); if (old != null) old.Delete();
                                using (var output = zip.CreateEntry(relative).Open()) output.Write(value, 0, value.Length);
                            }
                    }
                    source.PayloadBackup = newBackup;
                }
                if (source.Release == null) source.Release = new JavaScriptSerializer().Serialize(Ue4ssRelease.Bundled);
                Directory.CreateDirectory(staging);
                foreach (var pair in ModFiles().Where(pair => !IsGenerated(pair.Key))) WriteNew(LocalPath(staging, pair.Key), pair.Value);
                File.WriteAllText(Path.Combine(staging, ConfigurationFile), json, Utf8);
                File.WriteAllText(LocalPath(staging, LuaConfigurationFile), lua, Utf8);
                if (candidate.Enabled)
                    foreach (string relative in source.Files)
                    {
                        string path = LocalPath(runtimeDirectory, relative); byte[] bytes;
                        if (!File.Exists(path) && !(relative.Equals("dwmapi.dll", StringComparison.OrdinalIgnoreCase) && source.ProxySuspended)
                            && previousPayload.TryGetValue(relative, out bytes))
                        { WriteNew(path, bytes); created.Add(path); }
                    }
                if (candidate.Enabled && loaderState == KeyEnhancementLoaderState.Missing)
                {
                    foreach (var pair in Payload.Where(pair => !pair.Key.StartsWith(ModPrefix, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(pair => pair.Key.Equals("dwmapi.dll", StringComparison.OrdinalIgnoreCase) ? 1 : 0))
                    {
                        string path = LocalPath(runtimeDirectory, pair.Key);
                        if (File.Exists(path)) throw Conflict(path);
                        WriteNew(path, pair.Value); created.Add(path); source.Files.Add(pair.Key);
                        if (pair.Key.Equals("dwmapi.dll", StringComparison.OrdinalIgnoreCase)) source.ProxyBytes = Convert.ToBase64String(pair.Value);
                    }
                }
                if (candidate.Enabled || otherMods) SetProxySuspended(source, false);
                EnsureGameClosed(); EnsureConfigurationUnchanged();
                if (Directory.Exists(InstallDirectory)) Directory.Move(InstallDirectory, retired);
                Directory.Move(staging, InstallDirectory); replaced = true;
                foreach (var pair in activation)
                {
                    string lastWritten;
                    if (!source.OriginalActivation.ContainsKey(pair.Key) || !source.WrittenActivation.TryGetValue(pair.Key, out lastWritten)
                        || originals[pair.Key] == null || !originals[pair.Key].SequenceEqual(Convert.FromBase64String(lastWritten)))
                        source.OriginalActivation[pair.Key] = originals[pair.Key] == null ? null : Convert.ToBase64String(originals[pair.Key]);
                    Directory.CreateDirectory(Path.GetDirectoryName(pair.Key)); File.WriteAllBytes(pair.Key, pair.Value);
                    source.WrittenActivation[pair.Key] = Convert.ToBase64String(pair.Value);
                }
                string proxy = Path.Combine(runtimeDirectory, "dwmapi.dll");
                if (!candidate.Enabled && !otherMods)
                {
                    byte[] managedProxy;
                    if (source.ProxyBytes == null && source.Files.Contains("dwmapi.dll", StringComparer.OrdinalIgnoreCase) && File.Exists(proxy)
                        && previousPayload.TryGetValue("dwmapi.dll", out managedProxy) && File.ReadAllBytes(proxy).SequenceEqual(managedProxy))
                        source.ProxyBytes = Convert.ToBase64String(managedProxy);
                    SetProxySuspended(source, true);
                }
                WriteOwnership(source);
                var expected = candidate.Enabled ? KeyEnhancementInstallState.Enabled : KeyEnhancementInstallState.Disabled;
                if (ReadState() != expected) throw new IOException(I18n.Str("mod.verifyfailed"));
                ConfigurationText = json; configurationLoaded = true; complete = true;
            }
            finally
            {
                if (!complete)
                {
                    if (replaced && Directory.Exists(InstallDirectory)) Directory.Delete(InstallDirectory, true);
                    if (Directory.Exists(retired)) Directory.Move(retired, InstallDirectory);
                    if (source.ProxySuspended != wasSuspended) SetProxySuspended(source, wasSuspended);
                    foreach (var pair in originals)
                        if (pair.Value == null) { if (File.Exists(pair.Key)) File.Delete(pair.Key); }
                        else File.WriteAllBytes(pair.Key, pair.Value);
                    foreach (string path in created.AsEnumerable().Reverse()) if (File.Exists(path)) File.Delete(path);
                    if (oldSource != null) File.WriteAllBytes(sourcePath, oldSource); else if (File.Exists(sourcePath)) File.Delete(sourcePath);
                    if (newBackup != null) File.Delete(newBackup);
                }
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                if (complete && Directory.Exists(retired)) Directory.Delete(retired, true);
            }
        }

        public KeyEnhancementUninstallResult Uninstall()
        {
            EnsureGameClosed(); EnsureConfigurationUnchanged();
            var source = ReadOwnership(); var expected = OwnedPayload(source);
            var removed = new List<string>(); var retained = new List<string>();
            foreach (string path in ActivationLists().Concat(source.WrittenActivation.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists))
            {
                byte[] actual = File.ReadAllBytes(path); string written, original;
                if (source.WrittenActivation.TryGetValue(path, out written) && actual.SequenceEqual(Convert.FromBase64String(written))
                    && source.OriginalActivation.TryGetValue(path, out original))
                {
                    if (original == null) File.Delete(path);
                    else
                    {
                        byte[] originalBytes = Convert.FromBase64String(original);
                        string text = Decode(originalBytes);
                        string updated = RemoveOwnActivation(path, text);
                        File.WriteAllBytes(path, updated == text ? originalBytes : EncodeLikeFile(path, updated));
                    }
                }
                else
                {
                    string text = Decode(actual);
                    string updated = RemoveOwnActivation(path, text);
                    if (updated != text) File.WriteAllBytes(path, EncodeLikeFile(path, updated));
                }
            }
            string compiled = null;
            if (File.Exists(ConfigurationPath))
                try { compiled = KeyEnhancementConfiguration.FromJson(File.ReadAllText(ConfigurationPath, Utf8)).CompileLua(); } catch (ArgumentException) { }
            if (Directory.Exists(InstallDirectory))
                foreach (string path in Directory.GetFiles(InstallDirectory, "*", SearchOption.AllDirectories))
                {
                    string relative = path.Substring(InstallDirectory.Length + 1).Replace('\\', '/');
                    byte[] value, actual = File.ReadAllBytes(path);
                    bool owned = expected.TryGetValue(ModPrefix + relative, out value) && actual.SequenceEqual(value)
                        || MatchesLegacy(relative, actual) || relative == ConfigurationFile && compiled != null
                        || relative == LuaConfigurationFile && compiled != null && File.ReadAllText(path, Utf8) == compiled
                        || relative == "enabled.txt" && actual.Length == 0 || IsLiveFile(relative);
                    if (owned) { File.Delete(path); removed.Add(path); } else retained.Add(path);
                }
            bool shared = OtherModsPresent();
            if (shared) SetProxySuspended(source, false);
            var remaining = new List<string>();
            foreach (string relative in source.Files)
            {
                string path = LocalPath(runtimeDirectory, relative);
                if (relative.Equals("dwmapi.dll", StringComparison.OrdinalIgnoreCase) && source.ProxySuspended) path = Path.Combine(runtimeDirectory, SuspendedProxy);
                if (!File.Exists(path)) continue;
                byte[] value;
                if (!shared && expected.TryGetValue(relative, out value) && File.ReadAllBytes(path).SequenceEqual(value))
                { File.Delete(path); removed.Add(path); }
                else { remaining.Add(relative); retained.Add(path); }
            }
            bool externalProxy = !source.Files.Contains("dwmapi.dll", StringComparer.OrdinalIgnoreCase);
            if (externalProxy) SetProxySuspended(source, false);
            source.Files = remaining;
            if (remaining.Count > 0 || retained.Count > 0) WriteOwnership(source);
            else
            {
                string path = Path.Combine(runtimeDirectory, OwnershipFile); if (File.Exists(path)) File.Delete(path);
                if (source.PayloadBackup != null && File.Exists(source.PayloadBackup)) File.Delete(source.PayloadBackup);
            }
            foreach (string path in removed) RemoveEmptyParents(Path.GetDirectoryName(path));
            ConfigurationText = ReadConfigurationText(); configurationLoaded = true;
            return new KeyEnhancementUninstallResult { RemovedFiles = removed.ToArray(), RetainedFiles = retained.ToArray(), SharedLoaderRetained = shared && remaining.Count > 0 };
        }

        static string RemoveOwnActivation(string path, string text)
        {
            return Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase)
                ? Regex.Replace(text, @"(?im)^[ \t]*PBZInputRouter[ \t]*:[ \t]*[01][ \t]*(?:;[^\r\n]*)?\r?\n?", "")
                : text.IndexOf(ModName, StringComparison.OrdinalIgnoreCase) < 0 ? text : UpdateOwnJson(text, false, true);
        }
    }
}
