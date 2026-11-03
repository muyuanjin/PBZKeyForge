using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

// Integration checks against the built application and copies of authenticated game settings.
// Usage: <tool.exe> <game.exe> <shared.sav> <input.sav> <new test directory on game's volume>
public static class SchemeActivationTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static Type Settings;
    static string GameExe;
    static int checks;

    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }

    static bool Same(byte[] a, byte[] b) { return a.SequenceEqual(b); }
    static object Load(string path) { return Settings.GetMethod("Load").Invoke(null, new object[] { path, GameExe }); }
    static string Activate(object settings, string profile)
    { return (string)Settings.GetMethod("Activate").Invoke(settings, new object[] { profile }); }
    static string Profile(object settings) { return (string)Settings.GetProperty("ProfileId").GetValue(settings, null); }
    static string GameBuild(object settings) { return (string)Settings.GetProperty("GameBuild").GetValue(settings, null); }
    static string Json(object settings) { return (string)Settings.GetField("json", Hidden).GetValue(settings); }
    static string WithoutScheme(string json)
    { return Regex.Replace(json, "(\"KeyBindingScheme\"\\s*:\\s*)\"[^\"]*\"", "$1\"<scheme>\""); }

    static void MustFail(Action action, string message)
    {
        bool failed = false;
        try { action(); }
        catch (TargetInvocationException ex) { failed = ex.InnerException is IOException || ex.InnerException is InvalidDataException || ex.InnerException is System.Security.Cryptography.CryptographicException; }
        Assert(failed, message);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateHardLink(string name, string existing, IntPtr security);

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--hold")
        {
            Console.WriteLine("ready");
            System.Threading.Thread.Sleep(30000);
            return 0;
        }
        if (args.Length == 5 && args[0] == "--read-only")
        {
            try { return ReadOnly(args); }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        try { return Run(args); }
        catch (Exception ex)
        {
            while (ex != null)
            {
                Console.Error.WriteLine(ex.GetType().FullName + ": " + ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
                ex = ex.InnerException;
            }
            return 1;
        }
    }

    static int ReadOnly(string[] args)
    {
        Application.EnableVisualStyles();
        var assembly = Assembly.LoadFrom(Path.GetFullPath(args[1]));
        Settings = assembly.GetType("PBZKeyForge.GameSchemeSettings", true);
        GameExe = Path.GetFullPath(args[2]);
        string source = Path.GetFullPath(args[3]);
        string root = Path.GetFullPath(args[4]);
        Assert(!Directory.Exists(root), "Read-only test directory must be new.");
        byte[] original = File.ReadAllBytes(source);
        Directory.CreateDirectory(root);
        string copy = Path.Combine(root, "PBZSharedSetting.copy.sav");
        File.WriteAllBytes(copy, original);
        object settings = Load(copy);
        Assert(Profile(settings) == "InputUserSettings.Profiles.Default"
            || Profile(settings) == "PBZ.KeyProfile.Custom1"
            || Profile(settings) == "PBZ.KeyProfile.Custom2", "Authenticated settings returned an unknown scheme.");
        Assert(GameBuild(settings).StartsWith("Steam CL-301524_JKS-17219", StringComparison.Ordinal), "Known key fingerprint was not resolved.");

        string unknownExe = Path.Combine(root, "unsupported-build.exe");
        File.WriteAllBytes(unknownExe, new byte[] { 0x4d, 0x5a, 0x00 });
        MustFail(delegate { Settings.GetMethod("Load").Invoke(null, new object[] { copy, unknownExe }); }, "Unknown executable build must be rejected before key lookup.");
        Assert(Same(original, File.ReadAllBytes(copy)), "Unknown-build rejection changed the copied settings.");

        foreach (int position in new[] { 0, 4, 8, 20, 36, original.Length - 1 })
        {
            byte[] bad = (byte[])original.Clone(); bad[position] ^= 1;
            File.WriteAllBytes(copy, bad);
            MustFail(delegate { Load(copy); }, "Unauthenticated settings accepted at offset " + position + ".");
            Assert(Same(bad, File.ReadAllBytes(copy)), "Authentication failure changed the copied file.");
        }
        Assert(Same(original, File.ReadAllBytes(source)), "Read-only PBZS checks changed the source save.");
        Console.WriteLine("PASS: " + checks + " read-only PBZS assertions; source untouched; artifacts: " + root);
        return 0;
    }

    static int Run(string[] args)
    {
        Application.EnableVisualStyles();
        var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        Settings = assembly.GetType("PBZKeyForge.GameSchemeSettings", true);
        GameExe = Path.GetFullPath(args[1]);
        string sharedSource = Path.GetFullPath(args[2]), inputSource = Path.GetFullPath(args[3]);
        string root = Path.GetFullPath(args[4]);
        Assert(!Directory.Exists(root), "Test directory must be new.");
        byte[] source = File.ReadAllBytes(sharedSource), input = File.ReadAllBytes(inputSource);
        Directory.CreateDirectory(root);
        string test = Path.Combine(root, "PBZSharedSetting.test.sav");
        File.WriteAllBytes(test, source);
        Assert(GameBuild(Load(test)) == "Steam CL-301524_JKS-17219", "A verified executable was not identified by its registered build.");
        string baseline = WithoutScheme(Json(Load(test)));
        string[] profiles = { "InputUserSettings.Profiles.Default", "PBZ.KeyProfile.Custom1", "PBZ.KeyProfile.Custom2" };

        foreach (string profile in profiles)
        {
            byte[] before = File.ReadAllBytes(test);
            string backup = Activate(Load(test), profile);
            if (backup != null) Assert(Same(before, File.ReadAllBytes(backup)), "Backup must contain exact preimage.");
            var after = Load(test);
            Assert(Profile(after) == profile, "Wrong scheme after authenticated read-back.");
            Assert(WithoutScheme(Json(after)) == baseline, "Another shared setting changed.");
            byte[] stable = File.ReadAllBytes(test);
            DateTime time = File.GetLastWriteTimeUtc(test);
            Assert(Activate(after, profile) == null, "Already selected scheme must be a no-op.");
            Assert(Same(stable, File.ReadAllBytes(test)) && File.GetLastWriteTimeUtc(test) == time, "No-op rewrote file.");
            File.Copy(test, Path.Combine(root, "verified-" + profile.Split('.').Last() + ".sav"));
        }

        var stale = Load(test);
        byte[] external = File.ReadAllBytes(test);
        external[external.Length - 1] ^= 1;
        File.WriteAllBytes(test, external);
        MustFail(delegate { Activate(stale, profiles[0]); }, "Stale settings must be rejected.");
        Assert(Same(external, File.ReadAllBytes(test)), "Rejected stale write changed file.");
        foreach (int position in new[] { 0, 4, 8, 20, 36, source.Length - 1 })
        {
            byte[] bad = (byte[])source.Clone(); bad[position] ^= 1;
            File.WriteAllBytes(test, bad);
            MustFail(delegate { Load(test); }, "Tampered container accepted at offset " + position + ".");
            Assert(Same(bad, File.ReadAllBytes(test)), "Loading invalid settings wrote to disk.");
        }
        File.WriteAllBytes(test, source);
        string unknownExe = Path.Combine(root, "unsupported-build.exe");
        File.WriteAllBytes(unknownExe, new byte[] { 0x4d, 0x5a, 0x00 });
        MustFail(delegate { Settings.GetMethod("Load").Invoke(null, new object[] { test, unknownExe }); }, "Unknown executable build must be rejected before key lookup.");
        Assert(Same(source, File.ReadAllBytes(test)), "Unknown-build rejection changed the settings file.");
        MustFail(delegate { Activate(Load(test), "UnknownProfile"); }, "Invalid scheme was accepted.");
        Assert(Same(source, File.ReadAllBytes(test)), "Invalid scheme changed file.");

        string fakeGame = Path.Combine(root, "PBZ.exe");
        File.Copy(Assembly.GetExecutingAssembly().Location, fakeGame);
        using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fakeGame, "--hold") {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
        }))
        {
            try
            {
                Assert(process.StandardOutput.ReadLine() == "ready", "Game-process fixture did not start.");
                MustFail(delegate { Activate(Load(test), profiles[0]); }, "Running-game guard did not block writing.");
                Assert(Same(source, File.ReadAllBytes(test)), "Running-game rejection changed file.");
            }
            finally { if (!process.HasExited) process.Kill(); process.WaitForExit(); }
        }

        // Real GUI: deliberately disagree with Enhanced Input metadata and verify the game's selector wins.
        string saved = Path.Combine(root, @"ProjectPBZ\Saved"); Directory.CreateDirectory(saved);
        string exeLink = Path.Combine(root, @"platform\steam\PBZ.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(exeLink));
        Assert(CreateHardLink(exeLink, GameExe, IntPtr.Zero), "Cannot create read-only executable fixture link.");
        string guiInput = Path.Combine(saved, "EnhancedInputUserSettings.sav");
        string guiShared = Path.Combine(saved, "PBZSharedSetting.sav");
        File.WriteAllBytes(guiInput, input); File.WriteAllBytes(guiShared, source);
        Activate(Load(guiShared), profiles[2]);
        Type formType = assembly.GetType("PBZKeyForge.MainForm", true);
        using (var form = (Form)Activator.CreateInstance(formType))
        {
            form.ShowInTaskbar = false; form.Opacity = 0;
            formType.GetMethod("UiSmoke").Invoke(form, new object[] { guiInput });
            form.Show(); Application.DoEvents();
            var session = formType.GetProperty("Session").GetValue(form, null);
            Type sessionType = session.GetType();
            Assert((string)sessionType.GetProperty("SelectedProfile").GetValue(session, null) == profiles[2], "GUI trusted the stale profile identifier.");
            Assert((string)sessionType.GetProperty("ActiveProfile").GetValue(session, null) == profiles[2], "GUI active marker source is wrong.");
            sessionType.GetMethod("SetKeyboard").Invoke(session, new object[] { "IA_Jump", 0, "F8" });
            formType.GetMethod("RenderSession").Invoke(form, null);
            // Refresh and navigation must preserve drafts without writing either file.
            byte[] same = File.ReadAllBytes(guiShared);
            formType.GetMethod("RefreshFromDisk").Invoke(form, null);
            formType.GetMethod("SelectProfile").Invoke(form, new object[] { profiles[1] });
            formType.GetMethod("SelectProfile").Invoke(form, new object[] { profiles[2] });
            Assert((string)sessionType.GetMethod("GetKeyboard").Invoke(session, new object[] { "IA_Jump", 0 }) == "F8", "Navigation or refresh lost pending edits.");
            Assert(Same(same, File.ReadAllBytes(guiShared)) && Same(input, File.ReadAllBytes(guiInput)), "Viewing or drafting wrote to disk.");
            sessionType.GetMethod("Undo").Invoke(session, null);
            formType.GetMethod("RenderSession").Invoke(form, null);
            foreach (string profile in profiles)
            {
                formType.GetMethod("SelectProfile").Invoke(form, new object[] { profile });
                sessionType.GetMethod("SetActivation").Invoke(session, new object[] { true });
                formType.GetMethod("RenderSession").Invoke(form, null);
                var apply = (Button)formType.GetField("btnApply", Hidden).GetValue(form);
                if (apply.Enabled) apply.PerformClick();
                Application.DoEvents();
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                while ((bool)formType.GetProperty("IsApplying").GetValue(form, null) && elapsed.Elapsed.TotalSeconds < 15)
                { Application.DoEvents(); System.Threading.Thread.Sleep(1); }
                Assert(!(bool)formType.GetProperty("IsApplying").GetValue(form, null), "GUI apply did not complete.");
                var settings = Load(guiShared);
                Assert(Profile(settings) == profile, "GUI activate did not reach the game's selector.");
                Assert(WithoutScheme(Json(settings)) == baseline, "GUI activation changed unrelated settings.");
                Assert((string)sessionType.GetProperty("ActiveProfile").GetValue(session, null) == profile, "Active marker is wrong.");
                Assert(Same(input, File.ReadAllBytes(guiInput)), "Activating a scheme changed input bindings.");
            }
            var languages = (ComboBox)formType.GetField("cbLang", Hidden).GetValue(form);
            int languageCount = args.Contains("--all-languages") ? languages.Items.Count : 1;
            for (int i = 0; i < languageCount; i++) languages.SelectedIndex = i;
            Assert(Profile(Load(guiShared)) == profiles[2] && Same(input, File.ReadAllBytes(guiInput)), "Language switch changed a save.");
        }
        Assert(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories).Length == 0, "Temporary write residue.");
        Assert(Same(source, File.ReadAllBytes(sharedSource)) && Same(input, File.ReadAllBytes(inputSource)), "Real saves were modified by tests.");
        Console.WriteLine("PASS: " + checks + " assertions; originals untouched; artifacts: " + root);
        return 0;
    }
}
