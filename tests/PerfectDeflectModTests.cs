using System;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using PBZKeyForge;

public static class PerfectDeflectModTests
{
    public sealed class Metadata { public string[] RequiredChunks { get; set; } }
    static int checks;
    static void Assert(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static void Reject(Action action, string message)
    {
        try { action(); } catch (IOException) { checks++; return; } catch (InvalidDataException) { checks++; return; }
        throw new Exception(message);
    }
    public static void WriteToc(string path)
    {
        string[] chunks;
        using (var stream = typeof(PerfectDeflectMod).Assembly.GetManifestResourceStream("PBZKeyForge.PerfectDeflect.json"))
        using (var reader = new StreamReader(stream))
            chunks = new JavaScriptSerializer().Deserialize<Metadata>(reader.ReadToEnd()).RequiredChunks;
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("-==--==--==--==-"));
            writer.Write((byte)9); writer.Write(new byte[3]); writer.Write(176); writer.Write(chunks.Length);
            writer.Write(new byte[176 - 28]);
            foreach (string chunk in chunks)
                for (int i = 0; i < chunk.Length; i += 2) writer.Write(Convert.ToByte(chunk.Substring(i, 2), 16));
        }
    }
    public static int Main(string[] args)
    {
        try
        {
            I18n.Current = "zh-Hans";
            string output = Path.GetFullPath(args[0]);
            if (Directory.Exists(output)) throw new Exception("Use a new test directory.");
            string root = Path.Combine(output, "游戏 with spaces");
            string paks = Path.Combine(root, @"ProjectPBZ\Content\Paks");
            Directory.CreateDirectory(paks); Directory.CreateDirectory(Path.Combine(root, @"platform\steam"));
            string exe = Path.Combine(root, @"platform\steam\PBZ.exe");
            File.WriteAllText(exe, "fixture, never launched");
            string toc = Path.Combine(paks, "Fixture.utoc"); WriteToc(toc);
            byte[] originalToc = File.ReadAllBytes(toc);
            string save = Path.Combine(root, @"ProjectPBZ\Saved\EnhancedInputUserSettings.sav");
            Directory.CreateDirectory(Path.GetDirectoryName(save)); File.Copy("samples/vanilla-base.sav", save);
            byte[] originalSave = File.ReadAllBytes(save);
            string other = Path.Combine(paks, @"~mods\OtherMod\untouched.pak");
            Directory.CreateDirectory(Path.GetDirectoryName(other)); File.WriteAllText(other, "preserve");
            var mod = new PerfectDeflectMod(root);
            Assert(PerfectDeflectMod.FindGameDirectory(save) == root, "Game directory resolution.");
            Assert(mod.HasTargetResource(), "All five resources in a TOC with an extended header must be found.");
            Assert(mod.ReadState() == ResourceModState.Disabled, "Default state must be disabled.");
            mod.Apply(false); mod.Apply(true);
            Assert(mod.ReadState() == ResourceModState.Enabled && Directory.GetFiles(mod.InstallDirectory).Length == 3, "Enable must install exactly three containers.");
            var files = Directory.GetFiles(mod.InstallDirectory);
            var times = files.Select(File.GetLastWriteTimeUtc).ToArray(); mod.Apply(true);
            Assert(times.SequenceEqual(files.Select(File.GetLastWriteTimeUtc)), "Repeated enable must not rewrite files.");
            string payload = files.Single(f => f.EndsWith(".ucas"));
            File.Delete(payload);
            Assert(mod.ReadState() == ResourceModState.Incomplete, "Missing container must be repairable.");
            mod.Apply(true); Assert(mod.ReadState() == ResourceModState.Enabled, "Repair did not finish.");
            string foreign = Path.Combine(mod.InstallDirectory, "foreign.txt"); File.WriteAllText(foreign, "preserve");
            Assert(mod.ReadState() == ResourceModState.Conflict, "Foreign files must be a conflict.");
            Reject(() => mod.Apply(false), "Uninstall deleted a foreign file.");
            Reject(() => mod.Apply(true), "Enable overwrote a foreign file.");
            Assert(File.ReadAllText(foreign) == "preserve", "Foreign file changed."); File.Delete(foreign);
            byte[] original = File.ReadAllBytes(payload), edited = (byte[])original.Clone(); edited[0] ^= 1;
            File.WriteAllBytes(payload, edited);
            Assert(mod.ReadState() == ResourceModState.Conflict, "Modified container must be a conflict.");
            Reject(() => mod.Apply(false), "Modified container deleted."); File.WriteAllBytes(payload, original);
            using (var locked = new FileStream(payload, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Reject(() => mod.Apply(false), "Locked resource must reject replacement.");
            Assert(mod.ReadState() == ResourceModState.Enabled, "Lock failure damaged the installation.");
            mod.Apply(false); mod.Apply(false);
            Assert(mod.ReadState() == ResourceModState.Disabled && Directory.GetFiles(mod.InstallDirectory).Length == 0, "Uninstall left mounted files.");
            File.WriteAllBytes(toc, originalToc.Take(originalToc.Length - 12).ToArray());
            Assert(!mod.HasTargetResource(), "An incomplete source resource set must not be supported.");
            Reject(() => mod.Apply(true), "Enabled against a missing difficulty.");
            File.WriteAllBytes(toc, originalToc);
            mod.Apply(true); mod.Apply(false);
            Assert(File.ReadAllBytes(save).SequenceEqual(originalSave) && File.ReadAllBytes(toc).SequenceEqual(originalToc), "Mod operation changed source saves or archives.");
            Assert(File.ReadAllText(exe) == "fixture, never launched" && File.ReadAllText(other) == "preserve", "Unrelated files changed.");
            Assert(!Directory.GetDirectories(Path.Combine(root, @"ProjectPBZ\Content"), "*.tmp*").Any(), "Staging directory leaked.");
            Console.WriteLine("PerfectDeflectModTests PASS: " + checks + " assertions; fixture=" + root);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
