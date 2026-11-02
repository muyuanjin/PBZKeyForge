using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PBZKeyForge
{
    /// <summary>Command line surface. The GUI is the default when no arguments are given.</summary>
    public static class Cli
    {
        public static int Run(string[] args)
        {
            string cmd = args[0];
            var opts = Parse(args);

            switch (cmd)
            {
                case "--selftest":
                    return SelfTest(Arg(args, 1));

                case "--list":
                    return List(Arg(args, 1));

                case "--schemes":
                    return Schemes(Arg(args, 1));

                case "--apply-preset":
                    return ApplyPreset(args, Arg(args, 1), Arg(args, 2), Arg(args, 3), opts);

                case "--activate":
                    return Activate(Arg(args, 1), Arg(args, 2), Arg(args, 3));

                case "--make-vanilla":
                    return MakeVanilla(Arg(args, 1), Arg(args, 2));

                case "--gen-presets":
                    return GenPresets(Arg(args, 1), Arg(args, 2));

                default:
                    Console.WriteLine("usage:");
                    Console.WriteLine("  PBZKeyForge.exe                                  (GUI)");
                    Console.WriteLine("  --selftest <sav>");
                    Console.WriteLine("  --list <sav>");
                    Console.WriteLine("  --schemes <sav>");
                    Console.WriteLine("  --apply-preset <in> <out> <presetId> [--scheme <profileId>] [--activate]");
                    Console.WriteLine("  --activate <in> <out> <profileId>");
                    Console.WriteLine("  --make-vanilla <in> <out>");
                    Console.WriteLine("  --gen-presets <in> <outDir>");
                    return 2;
            }
        }

        static string Arg(string[] a, int i) { return i < a.Length ? a[i] : null; }

        static Dictionary<string, string> Parse(string[] a)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < a.Length; i++)
                if (a[i].StartsWith("--") && i + 1 < a.Length && !a[i + 1].StartsWith("--"))
                    d[a[i]] = a[i + 1];
            return d;
        }

        static bool HasFlag(string[] a, string flag)
        {
            return a.Any(x => string.Equals(x, flag, StringComparison.OrdinalIgnoreCase));
        }

        static int SelfTest(string path)
        {
            var sav = SaveFile.Load(path);
            string tmp = Path.Combine(Path.GetTempPath(), "pbzforge_" + Guid.NewGuid().ToString("N") + ".sav");
            sav.Save(tmp);
            byte[] a = File.ReadAllBytes(path), b = File.ReadAllBytes(tmp);
            bool same = a.Length == b.Length;
            if (same)
                for (int i = 0; i < a.Length; i++)
                    if (a[i] != b[i])
                    {
                        same = false;
                        Console.WriteLine("first difference at 0x" + i.ToString("X"));
                        break;
                    }
            Console.WriteLine("in=" + a.Length + " out=" + b.Length + " roundtrip=" + (same ? "IDENTICAL" : "DIFF"));
            Console.WriteLine("active=" + sav.ActiveProfile + " records=" + sav.Records.Count + " gamepadProfiles=" + sav.GamepadKeys.Count);
            File.Delete(tmp);
            return same ? 0 : 1;
        }

        static int List(string path)
        {
            var sav = SaveFile.Load(path);
            Console.WriteLine("active scheme: " + sav.ActiveProfile);
            foreach (var r in sav.Records)
            {
                Console.WriteLine("[" + r.ProfileId + "] " + r.Entries.Count + " entries");
                foreach (var e in r.Entries)
                    Console.WriteLine("    kb " + e.Name + " = " + e.Key + " @slot " + e.Slot);
            }
            foreach (var keys in sav.GamepadKeys)
                foreach (var el in keys.Items)
                    Console.WriteLine("    pad " + SaveFile.Field(el, "MappingName") + " " + SaveFile.Field(el, "Slot") +
                                      " = " + SaveFile.Field(el, "Key") + " (default " + SaveFile.Field(el, "DefaultKey") + ")");
            return 0;
        }

        static int Schemes(string path)
        {
            var sav = SaveFile.Load(path);
            foreach (var r in sav.Records)
                Console.WriteLine((r.ProfileId == sav.ActiveProfile ? "* " : "  ") + r.ProfileId);
            return 0;
        }

        static int ApplyPreset(string[] argv, string input, string output, string presetId, Dictionary<string, string> opts)
        {
            var sav = SaveFile.Load(input);
            var preset = Presets.ById(presetId);
            if (preset == null) { Console.WriteLine("unknown preset: " + presetId); return 2; }

            string scheme;
            opts.TryGetValue("--scheme", out scheme);
            if (string.IsNullOrEmpty(scheme)) scheme = sav.ActiveProfile;
            var rec = sav.RecordFor(scheme);
            if (rec == null) { Console.WriteLine("unknown scheme: " + scheme); return 2; }

            Presets.Apply(sav, preset, rec);
            if (HasFlag(argv, "--activate")) sav.SetActiveProfile(scheme);
            sav.Save(output);
            Console.WriteLine("preset=" + preset.Id + " scheme=" + scheme + (HasFlag(argv, "--activate") ? " (activated)" : "") + " -> " + output);
            return 0;
        }

        static int Activate(string input, string output, string profileId)
        {
            var sav = SaveFile.Load(input);
            if (!sav.SetActiveProfile(profileId)) { Console.WriteLine("unknown scheme: " + profileId); return 2; }
            sav.Save(output);
            Console.WriteLine("active scheme is now " + profileId);
            return 0;
        }

        static int MakeVanilla(string input, string output)
        {
            var sav = SaveFile.Load(input);
            foreach (var r in sav.Records) r.Entries = new List<BlobEntry>();
            sav.ResetGamepadToDefaults();
            sav.Save(output);
            Console.WriteLine("vanilla written: " + output);
            return 0;
        }

        static int GenPresets(string input, string outDir)
        {
            Directory.CreateDirectory(outDir);
            foreach (var p in Presets.All())
            {
                var sav = SaveFile.Load(input);
                var rec = sav.RecordFor(sav.ActiveProfile);
                Presets.Apply(sav, p, rec);
                string name = "PBZKeyForge-" + p.Id + ".sav";
                sav.Save(Path.Combine(outDir, name));
                Console.WriteLine(name);
            }
            return 0;
        }
    }
}

