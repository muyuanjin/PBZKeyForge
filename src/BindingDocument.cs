using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PBZKeyForge
{
    public sealed class BindingDocument
    {
        public EditorSession Session;
        public KeyEnhancementConfiguration Configuration = new KeyEnhancementConfiguration();
        public KeyEnhancementInstallation Installation;

        public static BindingDocument Open(string path)
        {
            var document = new BindingDocument();
            if (path == "new") document.Session = EditorSession.CreateOffline();
            else if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using (var stream = File.OpenRead(path)) using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    string build = Read(zip, "defaults.txt", false) ?? "CL-301524";
                    byte[] save; var entry = zip.GetEntry("EnhancedInputUserSettings.sav");
                    if (entry == null) throw new InvalidDataException("Missing EnhancedInputUserSettings.sav");
                    using (var input = entry.Open()) using (var output = new MemoryStream()) { input.CopyTo(output); save = output.ToArray(); }
                    document.Configuration = KeyEnhancementConfiguration.FromJson(Read(zip, "key-enhancement.json", true));
                    document.Session = EditorSession.ImportOffline(save, build == "CL-302256" ? InputDefaultSet.Current : InputDefaultSet.Legacy, Read(zip, "scheme.txt", false));
                }
            }
            else if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
            { document.Session = EditorSession.CreateOffline(); document.Configuration = KeyEnhancementConfiguration.FromJson(File.ReadAllText(path)); }
            else
            {
                document.Session = EditorSession.Open(path);
                try
                {
                    document.Installation = new KeyEnhancementInstallation(KeyEnhancementInstallation.FindGameDirectory(path));
                    document.Configuration = document.Installation.LoadConfiguration();
                }
                catch (FileNotFoundException) { }
            }
            return document;
        }

        static string Read(ZipArchive zip, string name, bool required)
        {
            var entry = zip.GetEntry(name);
            if (entry == null) { if (required) throw new InvalidDataException("Missing " + name); return null; }
            using (var input = new StreamReader(entry.Open(), Encoding.UTF8)) return input.ReadToEnd();
        }
        static void Write(ZipArchive zip, string name, byte[] value)
        { using (var output = zip.CreateEntry(name, CompressionLevel.Optimal).Open()) output.Write(value, 0, value.Length); }

        public void Export(string path)
        {
            string target = Path.GetFullPath(path);
            if (Session.InputPath != null && target.Equals(Session.InputPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Use --apply to replace an installed save.");
            if (KeyEnhancementInstallation.IsRuntimeConfigurationPath(target))
                throw new InvalidOperationException("Use an installed save with --apply or --live to change the runtime configuration.");
            if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                if (Session.HasAnyDrafts) throw new InvalidOperationException("JSON cannot contain native save edits. Export ZIP instead.");
                KeyEnhancementInstallation.WriteAtomicText(path, Configuration.ToJson()); return;
            }
            if (!Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                if (Configuration.ToJson() != new KeyEnhancementConfiguration().ToJson()) throw new InvalidOperationException("Export ZIP to retain enhanced bindings.");
                Session.ExportCurrentScheme(path); return;
            }
            string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = File.Create(temporary)) using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    Write(zip, "EnhancedInputUserSettings.sav", Session.ExportCurrentScheme());
                    Write(zip, "key-enhancement.json", Encoding.UTF8.GetBytes(Configuration.ToJson()));
                    Write(zip, "router-config.lua", Encoding.UTF8.GetBytes(Configuration.CompileLua()));
                    Write(zip, "scheme.txt", Encoding.UTF8.GetBytes(Session.SelectedProfile));
                    Write(zip, "defaults.txt", Encoding.UTF8.GetBytes(Session.Defaults == InputDefaultSet.Current ? "CL-302256" : "CL-301524"));
                }
                if (File.Exists(target)) File.Replace(temporary, target, null); else File.Move(temporary, target);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
