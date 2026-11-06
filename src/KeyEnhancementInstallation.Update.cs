using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Web.Script.Serialization;

namespace PBZKeyForge
{
    public sealed partial class KeyEnhancementInstallation
    {
        public Ue4ssRelease CurrentRuntimeRelease()
        {
            string library = Path.Combine(loaderDirectory, "UE4SS.dll");
            if (!File.Exists(library)) return Ue4ssRelease.Bundled;
            byte[] actual = File.ReadAllBytes(library);
            var source = ReadOwnership();
            if (source.Release != null)
            {
                byte[] expected = null;
                if (source.RuntimeArchive != null && File.Exists(source.RuntimeArchive))
                    expected = Ue4ssRelease.ReadPackage(source.RuntimeArchive)["ue4ss/UE4SS.dll"];
                else if (source.Files.Contains("ue4ss/UE4SS.dll", StringComparer.OrdinalIgnoreCase))
                    OwnedPayload(source).TryGetValue("ue4ss/UE4SS.dll", out expected);
                if (expected != null && actual.SequenceEqual(expected)) return new JavaScriptSerializer().Deserialize<Ue4ssRelease>(source.Release);
            }
            return actual.SequenceEqual(Payload["ue4ss/UE4SS.dll"]) ? Ue4ssRelease.Bundled : null;
        }

        public string UpdateRuntime(string archive, Ue4ssRelease release)
        {
            EnsureGameClosed(); EnsureConfigurationUnchanged();
            var package = Ue4ssRelease.ReadPackage(archive);
            KeyEnhancementPrerequisites.EnsureAvailable(runtimeDirectory, package["ue4ss/UE4SS.dll"], package["dwmapi.dll"]);
            var source = ReadOwnership();
            var previous = OwnedPayload(source);
            var replacements = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var originals = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in package)
            {
                string path = LocalPath(runtimeDirectory, pair.Key);
                bool core = pair.Key.Equals("ue4ss/UE4SS.dll", StringComparison.OrdinalIgnoreCase) || pair.Key.Equals("dwmapi.dll", StringComparison.OrdinalIgnoreCase);
                if (pair.Key.Equals("dwmapi.dll", StringComparison.OrdinalIgnoreCase) && source.ProxySuspended)
                    path = Path.Combine(runtimeDirectory, SuspendedProxy);
                byte[] actual = File.Exists(path) ? File.ReadAllBytes(path) : null, owned;
                if (!core && actual != null && (pair.Key.Equals("ue4ss/UE4SS-settings.ini", StringComparison.OrdinalIgnoreCase)
                    || !source.Files.Contains(pair.Key, StringComparer.OrdinalIgnoreCase)
                    || !previous.TryGetValue(pair.Key, out owned) || !actual.SequenceEqual(owned))) continue;
                if (actual != null && actual.SequenceEqual(pair.Value)) continue;
                originals[path] = actual; replacements[path] = pair.Value;
                // Detect locked runtime files before replacing any part of the installation.
                if (actual != null) using (var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            }
            if (replacements.Count == 0)
            {
                source.Release = new JavaScriptSerializer().Serialize(release); source.RuntimeArchive = Path.GetFullPath(archive);
                WriteOwnership(source); return null;
            }
            string backup = Path.Combine(GameSchemeSettings.BackupDirectory(), "ue4ss-update-" + Guid.NewGuid().ToString("N") + ".zip");
            string ownershipPath = Path.Combine(runtimeDirectory, OwnershipFile);
            byte[] oldOwnership = File.Exists(ownershipPath) ? File.ReadAllBytes(ownershipPath) : null;
            using (var stream = File.Create(backup))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                foreach (var pair in originals.Where(pair => pair.Value != null))
                    using (var output = zip.CreateEntry(pair.Key.Substring(runtimeDirectory.Length + 1).Replace('\\', '/')).Open())
                        output.Write(pair.Value, 0, pair.Value.Length);
                using (var writer = new StreamWriter(zip.CreateEntry("update.json").Open()))
                    writer.Write(new JavaScriptSerializer().Serialize(new { RuntimeDirectory = runtimeDirectory,
                        AddedFiles = originals.Where(pair => pair.Value == null).Select(pair => pair.Key).ToArray(), Release = release }));
                if (oldOwnership != null) using (var output = zip.CreateEntry(OwnershipFile).Open()) output.Write(oldOwnership, 0, oldOwnership.Length);
            }
            string newPayload = null;
            bool wasSuspended = source.ProxySuspended;
            var written = new List<string>();
            try
            {
                EnsureGameClosed();
                foreach (var pair in replacements)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(pair.Key));
                    ReplaceRuntimeFile(pair.Key, pair.Value); written.Add(pair.Key);
                    string relative = pair.Key.Substring(runtimeDirectory.Length + 1).Replace('\\', '/');
                    if (relative == SuspendedProxy) relative = "dwmapi.dll";
                    if (originals[pair.Key] == null && !source.Files.Contains(relative, StringComparer.OrdinalIgnoreCase)) source.Files.Add(relative);
                    if (source.Files.Contains(relative, StringComparer.OrdinalIgnoreCase)) previous[relative] = pair.Value;
                    if (relative.Equals("dwmapi.dll", StringComparison.OrdinalIgnoreCase) && source.Files.Contains(relative, StringComparer.OrdinalIgnoreCase))
                        source.ProxyBytes = Convert.ToBase64String(pair.Value);
                }
                if (source.Files.Count > 0)
                {
                    newPayload = Path.Combine(GameSchemeSettings.BackupDirectory(), "key-enhancement-" + Guid.NewGuid().ToString("N") + ".zip");
                    using (var stream = File.Create(newPayload)) using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                        foreach (var pair in previous)
                            using (var output = zip.CreateEntry(pair.Key).Open()) output.Write(pair.Value, 0, pair.Value.Length);
                    source.PayloadBackup = newPayload;
                }
                source.Release = new JavaScriptSerializer().Serialize(release);
                source.RuntimeArchive = Path.GetFullPath(archive);
                bool enabled = File.Exists(ConfigurationPath) && KeyEnhancementConfiguration.FromJson(File.ReadAllText(ConfigurationPath, Utf8)).Enabled;
                if (!enabled && !OtherModsPresent()) SetProxySuspended(source, true);
                WriteOwnership(source);
                return backup;
            }
            catch
            {
                if (source.ProxySuspended != wasSuspended) SetProxySuspended(source, wasSuspended);
                foreach (string path in written.AsEnumerable().Reverse())
                    if (originals[path] == null) File.Delete(path); else ReplaceRuntimeFile(path, originals[path]);
                if (oldOwnership == null) { if (File.Exists(ownershipPath)) File.Delete(ownershipPath); }
                else ReplaceRuntimeFile(ownershipPath, oldOwnership);
                if (newPayload != null && File.Exists(newPayload)) File.Delete(newPayload);
                throw;
            }
        }

        static void ReplaceRuntimeFile(string path, byte[] bytes)
        {
            string temporary = path + ".PBZKeyForge-update";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
