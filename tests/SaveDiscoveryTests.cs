using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using PBZKeyForge;

public static class SaveDiscoveryTests
{
    static int checks;
    static readonly Type Discovery = typeof(EditorSession).Assembly.GetType("PBZKeyForge.SaveDiscovery", true);
    static readonly BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
    static void Assert(bool condition, string message)
    { checks++; if (!condition) throw new Exception(message); }
    static IEnumerable<string> Installed(string steam, IEnumerable<string> fallback)
    { return (IEnumerable<string>)Discovery.GetMethod("InstalledSaves", Hidden).Invoke(null, new object[] { steam, fallback }); }
    static EditorSession Open(EditorPreferences preferences, IEnumerable<string> paths)
    {
        try { return (EditorSession)Discovery.GetMethod("OpenCandidates", Hidden).Invoke(null, new object[] { preferences, paths, true }); }
        catch (TargetInvocationException ex) { throw ex.InnerException; }
    }
    static string Save(string library, string install, byte[] bytes)
    {
        string path = Path.Combine(library, @"steamapps\common", install, @"ProjectPBZ\Saved\EnhancedInputUserSettings.sav");
        Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, bytes); return path;
    }
    static void Manifest(string library, string install)
    {
        Directory.CreateDirectory(Path.Combine(library, "steamapps"));
        File.WriteAllText(Path.Combine(library, @"steamapps\appmanifest_4115450.acf"),
            "\"AppState\"\n{\n\"appid\" \"4115450\"\n\"installdir\" \"" + install + "\"\n}");
    }
    static IEnumerable<string> MustNotEnumerate()
    { throw new Exception("Disk fallback must remain unvisited after a successful Steam match."); }
    static IEnumerable<string> StopAfter(string path)
    {
        yield return path;
        throw new Exception("Discovery continued after a successful candidate.");
    }
    static IEnumerable<string> LazyFallback()
    {
        foreach (string path in MustNotEnumerate()) yield return path;
    }

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            string root = Path.GetFullPath(args[1]); Directory.CreateDirectory(root);
            byte[] original = File.ReadAllBytes(args[0]);
            string steam = Path.Combine(root, "Steam client"), library = Path.Combine(root, "游戏库"), fallback = Path.Combine(root, "Fallback");
            Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
            string folders = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
            File.WriteAllText(folders, "\"libraryfolders\"\n{\n\"0\" { \"path\" \"" + steam.Replace("\\", "\\\\")
                + "\" }\n\"1\" { \"path\" \"" + library.Replace("\\", "\\\\") + "\" }\n}");
            string active = Save(library, "Phantom Blade Zero 自定义", original);
            Manifest(library, "Phantom Blade Zero 自定义");
            string stale = Save(steam, "PBZ", original);
            string fallbackSave = Save(fallback, "PBZ", original);
            var preferences = new EditorPreferences(Path.Combine(root, "config", "preset-mode.txt"));
            Assert(preferences.LoadInputPath() == null, "New preferences have no remembered save.");
            preferences.SavePresetMode(PresetApplyMode.Replace);

            var session = Open(preferences, Installed(steam, LazyFallback()));
            Assert(session.InputPath == active, "Manifest install directory wins over an old PBZ folder in another library.");
            Assert(new EditorPreferences(Path.Combine(root, "config", "preset-mode.txt")).LoadInputPath() == active, "A new preferences instance restores the successful path.");
            Assert(preferences.LoadPresetMode() == PresetApplyMode.Replace, "Remembering a path preserves the preset mode.");
            Assert(Open(preferences, StopAfter(active)).InputPath == active, "Successful candidates return immediately.");

            string corrupt = Path.Combine(root, "bad.sav"); File.WriteAllBytes(corrupt, new byte[] { 0, 1, 2 });
            preferences.SaveInputPath(corrupt);
            session = Open(preferences, new[] { preferences.LoadInputPath(), Path.Combine(root, "missing.sav"), active });
            Assert(session.InputPath == active && preferences.LoadInputPath() == active, "A corrupt remembered save falls through and updates its path.");
            bool failed = false;
            try { Open(preferences, new[] { corrupt }); } catch (Exception) { failed = true; }
            Assert(failed && preferences.LoadInputPath() == active, "An unreadable candidate reports failure without replacing the remembered good path.");
            Assert(Open(preferences, new[] { Path.Combine(root, "missing.sav") }) == null, "Missing saves leave discovery empty.");

            File.Move(active, active + ".moved");
            session = Open(preferences, new[] { preferences.LoadInputPath() }.Concat(Installed(steam, LazyFallback())));
            Assert(session.InputPath == stale, "A moved remembered path falls back to a remaining installation.");
            File.Move(active + ".moved", active);
            string manifest = Path.Combine(library, @"steamapps\appmanifest_4115450.acf");
            using (var locked = new FileStream(manifest, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Assert(Open(preferences, Installed(steam, LazyFallback())).InputPath == stale, "An unreadable manifest does not block directory fallback.");
            using (var locked = new FileStream(folders, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Assert(Open(preferences, Installed(steam, LazyFallback())).InputPath == stale, "An unreadable library list preserves the primary Steam location.");
            File.Delete(stale);
            using (var locked = new FileStream(folders, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Assert(Open(preferences, Installed(steam, new[] { fallback })).InputPath == fallbackSave, "An unreadable library list does not block disk fallback.");
            Assert(Open(preferences, Installed(null, new[] { fallback })).InputPath == fallbackSave, "Disk fallback works without a Steam registry root.");
            Manifest(fallback, "Manifest fallback");
            string manifestFallback = Save(fallback, "Manifest fallback", original);
            Assert(Open(preferences, Installed(null, new[] { fallback })).InputPath == manifestFallback, "Disk fallback also honors actual install directories.");

            File.WriteAllText(folders, "not a library list");
            Assert(Open(preferences, Installed(steam, new[] { fallback })).InputPath == manifestFallback, "Malformed library metadata does not block other sources.");
            File.WriteAllText(Path.Combine(fallback, @"steamapps\appmanifest_4115450.acf"), "not a manifest");
            Assert(Open(preferences, Installed(null, new[] { fallback })).InputPath == fallbackSave, "Malformed manifests fall back to the known PBZ layout.");

            preferences.SaveInputPath(active);
            var candidates = Discovery.GetMethod("Candidates", Hidden);
            Assert(((IEnumerable<string>)candidates.Invoke(null, new object[] { preferences, true })).First() == active,
                "Normal discovery checks the remembered path before reading system configuration.");
            Assert(!((IEnumerable<string>)candidates.Invoke(null, new object[] { preferences, false })).Contains(active),
                "Explicit rediscovery skips the remembered custom path.");
            using (var form = MainForm.OpenDefault(preferences))
                Assert(form.Session.InputPath == active && !form.Session.HasAnyDrafts, "Startup restores the remembered custom file before showing the window.");
            using (var form = new MainForm(preferences))
            {
                string other = Save(Path.Combine(root, "Manual"), "PBZ", original);
                typeof(MainForm).GetMethod("OpenPath", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, new object[] { other, true });
                Assert(preferences.LoadInputPath() == other, "Manually opening a file remembers the successful path.");
                form.UiSmoke(active);
                Assert(preferences.LoadInputPath() == other, "Screenshot and fixture helpers do not replace the player's last path.");
                typeof(MainForm).GetMethod("OpenPath", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, new object[] { corrupt, true });
                Assert(preferences.LoadInputPath() == other && form.Session.InputPath == active, "A failed manual open preserves the path and current session.");
                form.NewOfflineScheme();
                Assert(preferences.LoadInputPath() == other, "Offline editing preserves the remembered installed file.");
            }

            string blocked = Path.Combine(root, "blocked-config"); File.WriteAllText(blocked, "file");
            var unavailable = new EditorPreferences(Path.Combine(blocked, "mode.txt"));
            unavailable.SaveInputPath(active);
            Assert(Open(unavailable, new[] { active }).InputPath == active, "An unwritable preferences directory cannot prevent opening a valid save.");
            foreach (string path in new[] { active, fallbackSave, manifestFallback })
                Assert(File.ReadAllBytes(path).SequenceEqual(original), "Discovery changed a source save.");
            Console.WriteLine("PASS: " + checks + " save discovery assertions; artifacts: " + root);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
