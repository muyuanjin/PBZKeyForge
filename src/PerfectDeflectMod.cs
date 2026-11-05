using System;
using System.IO;
using System.Linq;
using System.Text;

namespace PBZKeyForge
{
    public enum ResourceModState { Disabled, Enabled, Incomplete, Conflict }

    public sealed class PerfectDeflectMod
    {
        const string Stem = "PBZKeyForge_PerfectDeflect_P";
        static readonly string[] Extensions = { ".pak", ".utoc", ".ucas" };
        public string GameDirectory { get; private set; }
        public string InstallDirectory { get; private set; }
        readonly string contentDirectory, paksDirectory;

        public PerfectDeflectMod(string gameDirectory)
        {
            GameDirectory = Path.GetFullPath(gameDirectory);
            contentDirectory = Path.Combine(GameDirectory, @"ProjectPBZ\Content");
            paksDirectory = Path.Combine(contentDirectory, "Paks");
            InstallDirectory = Path.Combine(paksDirectory, @"~mods\PBZKeyForge_PerfectDeflect");
        }

        public static string FindGameDirectory(string inputSave)
        {
            string exe = GameSchemeSettings.FindExecutable(Path.GetDirectoryName(Path.GetFullPath(inputSave)));
            return Directory.GetParent(Path.GetDirectoryName(exe)).Parent.FullName;
        }

        public bool HasTargetResource()
        {
            return HasTargetResources();
        }

        bool HasTargetResources()
        {
            if (!File.Exists(Path.Combine(GameDirectory, @"platform\steam\PBZ.exe")) || !Directory.Exists(paksDirectory)) return false;
            var required = new System.Collections.Generic.HashSet<string>(RequiredChunks, StringComparer.OrdinalIgnoreCase);
            foreach (string path in Directory.EnumerateFiles(paksDirectory, "*.utoc"))
            {
                ReadResourceIdentities(path, required);
                if (required.Count == 0) return true;
            }
            return false;
        }

        static void ReadResourceIdentities(string path, System.Collections.Generic.HashSet<string> required)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536))
            using (var reader = new BinaryReader(stream))
            {
                byte[] header = reader.ReadBytes(28);
                if (header.Length != 28 || Encoding.ASCII.GetString(header, 0, 16) != "-==--==--==--==-") return;
                uint headerSize = BitConverter.ToUInt32(header, 20), count = BitConverter.ToUInt32(header, 24);
                if (headerSize < 28 || headerSize + (long)count * 12 > stream.Length) return;
                stream.Position = headerSize;
                while (count > 0)
                {
                    int entries = (int)Math.Min(count, 4096u);
                    byte[] block = reader.ReadBytes(entries * 12);
                    if (block.Length != entries * 12) return;
                    for (int i = 0; i < entries; i++)
                    {
                        required.Remove(BitConverter.ToString(block, i * 12, 12).Replace("-", ""));
                    }
                    count -= (uint)entries;
                }
            }
        }

        sealed class Metadata { public string[] RequiredChunks { get; set; } }
        static readonly string[] RequiredChunks = ReadMetadata();
        static readonly System.Collections.Generic.Dictionary<string, byte[]> Payload = ReadPayload();

        static string[] ReadMetadata()
        {
            using (var stream = typeof(PerfectDeflectMod).Assembly.GetManifestResourceStream("PBZKeyForge.PerfectDeflect.json"))
            using (var reader = new StreamReader(stream))
                return new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Metadata>(reader.ReadToEnd()).RequiredChunks;
        }

        static System.Collections.Generic.Dictionary<string, byte[]> ReadPayload()
        {
            var result = new System.Collections.Generic.Dictionary<string, byte[]>();
            foreach (string extension in Extensions)
            using (var stream = typeof(PerfectDeflectMod).Assembly.GetManifestResourceStream("PBZKeyForge.PerfectDeflect" + extension))
            using (var buffer = new MemoryStream())
            { stream.CopyTo(buffer); result.Add(extension, buffer.ToArray()); }
            return result;
        }

        public ResourceModState ReadState() { return Inspect(InstallDirectory); }

        static ResourceModState Inspect(string directory)
        {
            if (File.Exists(directory)) return ResourceModState.Conflict;
            if (!Directory.Exists(directory)) return ResourceModState.Disabled;
            if (Directory.EnumerateDirectories(directory).Any()) return ResourceModState.Conflict;
            string[] files = Directory.GetFiles(directory);
            foreach (string path in files)
                if (!Extensions.Any(ext => string.Equals(Path.GetFileName(path), Stem + ext, StringComparison.OrdinalIgnoreCase)))
                    return ResourceModState.Conflict;
            foreach (string extension in Extensions)
            {
                string path = Path.Combine(directory, Stem + extension);
                if (File.Exists(path) && (new FileInfo(path).Length != Payload[extension].Length
                    || !File.ReadAllBytes(path).SequenceEqual(Payload[extension]))) return ResourceModState.Conflict;
            }
            return files.Length == 0 ? ResourceModState.Disabled
                : files.Length == Extensions.Length ? ResourceModState.Enabled : ResourceModState.Incomplete;
        }

        public void Apply(bool enabled)
        {
            EnsureGameClosed();
            var state = Inspect(InstallDirectory);
            if (state == ResourceModState.Conflict) throw new IOException(I18n.Str("mod.conflict") + "\n" + InstallDirectory);
            var expected = enabled ? ResourceModState.Enabled : ResourceModState.Disabled;
            if (state == expected) return;
            if (enabled && !HasTargetResource()) throw new InvalidDataException(I18n.Str("mod.unsupported"));
            if (!Directory.Exists(contentDirectory)) throw new DirectoryNotFoundException(I18n.Str("mod.unsupported"));
            string staging = Path.Combine(contentDirectory, "PBZKeyForge_PerfectDeflect_" + Guid.NewGuid().ToString("N") + ".tmp");
            string retired = staging + ".old";
            bool replaced = false;
            try
            {
                Directory.CreateDirectory(staging);
                if (enabled) foreach (string extension in Extensions) File.WriteAllBytes(Path.Combine(staging, Stem + extension), Payload[extension]);
                if (Inspect(staging) != expected) throw new IOException(I18n.Str("mod.verifyfailed"));
                Directory.CreateDirectory(Path.GetDirectoryName(InstallDirectory));
                EnsureGameClosed();
                if (Inspect(InstallDirectory) != state) throw new IOException(I18n.Str("mod.conflict"));
                if (Directory.Exists(InstallDirectory)) Directory.Move(InstallDirectory, retired);
                try { Directory.Move(staging, InstallDirectory); replaced = true; }
                catch
                {
                    if (Directory.Exists(retired)) Directory.Move(retired, InstallDirectory);
                    throw;
                }
            }
            finally
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                if (replaced && Directory.Exists(retired)) Directory.Delete(retired, true);
            }
            if (Inspect(InstallDirectory) != expected) throw new IOException(I18n.Str("mod.verifyfailed"));
        }

        static void EnsureGameClosed()
        { if (EditorSession.IsGameRunning) throw new IOException(I18n.Str("mod.running")); }
    }
}
