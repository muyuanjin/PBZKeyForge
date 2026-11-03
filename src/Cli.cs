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
                    return Activate(args);

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
                    Console.WriteLine("  --apply-preset <in> <out> <presetId> [--scheme <profileId>]");
                    Console.WriteLine("  --activate <installed.sav> <profileId>");
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
            Console.WriteLine("profileIdentifier=" + sav.ActiveProfile + " records=" + sav.Records.Count + " gamepadProfiles=" + sav.GamepadKeys.Count);
            File.Delete(tmp);
            return same ? 0 : 1;
        }

        static int List(string path)
        {
            var sav = SaveFile.Load(path);
            Console.WriteLine("game scheme: " + (GameProfile(path) ?? "unknown (shared settings unavailable)"));
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
            string active = GameProfile(path);
            foreach (var r in sav.Records)
                Console.WriteLine((r.ProfileId == active ? "* " : "  ") + r.ProfileId);
            return 0;
        }

        static string GameProfile(string path)
        {
            try { return GameSchemeSettings.LoadForInputSave(path).ProfileId; }
            catch { return null; }
        }

        static int ApplyPreset(string[] argv, string input, string output, string presetId, Dictionary<string, string> opts)
        {
            if (HasFlag(argv, "--activate"))
            {
                Console.WriteLine("Use --activate <installed.sav> <profileId> separately to select the game's scheme.");
                return 2;
            }
            var sav = SaveFile.Load(input);
            var preset = Presets.ById(presetId);
            if (preset == null) { Console.WriteLine("unknown preset: " + presetId); return 2; }

            string scheme;
            opts.TryGetValue("--scheme", out scheme);
            if (string.IsNullOrEmpty(scheme)) scheme = GameProfile(input) ?? sav.ActiveProfile;
            var rec = sav.RecordFor(scheme);
            if (rec == null) { Console.WriteLine("unknown scheme: " + scheme); return 2; }

            Presets.Apply(sav, preset, rec);
            sav.Save(output);
            Console.WriteLine("preset=" + preset.Id + " scheme=" + scheme + " -> " + output);
            return 0;
        }

        static int Activate(string[] args)
        {
            if (args.Length != 3)
            { Console.WriteLine("usage: --activate <installed.sav> <profileId>"); return 2; }
            try
            {
                var settings = GameSchemeSettings.LoadForInputSave(args[1]);
                string backup = settings.Activate(args[2]);
                Console.WriteLine(backup == null ? "game scheme already selected: " + args[2]
                    : "game scheme selected: " + args[2] + "; backup=" + backup);
                return 0;
            }
            catch (Exception ex) { Console.WriteLine("activation failed: " + ex.Message); return 1; }
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
            var source = SaveFile.Load(input);
            string schemeResetFile = Path.Combine(outDir, "PBZKeyForge-vanilla_current.sav");
            if (File.Exists(schemeResetFile)) File.Delete(schemeResetFile);
            string profile = source.ActiveProfile;
            string schemeKey = profile == "InputUserSettings.Profiles.Default" ? "scheme.default"
                : profile == "PBZ.KeyProfile.Custom1" ? "scheme.custom1"
                : profile == "PBZ.KeyProfile.Custom2" ? "scheme.custom2" : null;
            var index = new StringBuilder();
            index.AppendLine("# Ready-made presets / 现成预设");
            index.AppendLine();
            index.AppendLine("Each file contains the same preset shown in the editor's hover preview: factory bindings plus that preset. It corresponds to Preset only mode. Back up your save, close the game, copy one file over EnhancedInputUserSettings.sav, and select the scheme listed below in game. Copying this file does not change the active game scheme.");
            index.AppendLine();
            index.AppendLine("每个文件与程序悬浮预览的预设一致：原厂布局加上该预设，对应“仅用预设”模式。备份存档并退出游戏，将选中的文件覆盖为 EnhancedInputUserSettings.sav，再进游戏选择表中方案。替换文件不会自动切换游戏当前方案。");
            index.AppendLine();
            index.AppendLine("| File / 文件 | Name | 名称 | Scheme / 游戏中选择 | Description / 说明 |");
            index.AppendLine("| --- | --- | --- | --- | --- |");
            string language = I18n.Current;
            try
            {
                foreach (var p in Presets.All().Where(preset => !preset.CurrentProfileVanilla))
                {
                    var sav = Presets.FactoryPreview(source, p, profile);
                    string name = "PBZKeyForge-" + p.Id + ".sav";
                    sav.Save(Path.Combine(outDir, name));
                    Console.WriteLine(name);
                    I18n.Current = "en";
                    string englishName = p.Name, englishDesc = p.Desc, englishScheme = schemeKey == null ? profile : I18n.Str(schemeKey);
                    I18n.Current = "zh-Hans";
                    string scheme = p.Vanilla ? "All schemes / 全部方案" : englishScheme + " / " + (schemeKey == null ? profile : I18n.Str(schemeKey));
                    index.AppendLine("| `" + name + "` | " + MarkdownCell(englishName) + " | " + MarkdownCell(p.Name) + " | " + scheme
                        + " | " + MarkdownCell(englishDesc) + " / " + MarkdownCell(p.Desc) + " |");
                }
            }
            finally { I18n.Current = language; }
            index.AppendLine();
            index.AppendLine("Target profile / 目标方案：`" + profile + "`. Restore all defaults resets every scheme; any scheme can then be selected. 恢复全部默认会重置全部方案，之后可选择任一方案。");
            File.WriteAllText(Path.Combine(outDir, "PRESETS.md"), index.ToString(), new UTF8Encoding(true));
            return 0;
        }

        static string MarkdownCell(string text) { return text.Replace("|", "&#124;").Replace("\r", "").Replace("\n", "<br>"); }
    }
}

