using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PBZKeyForge;

internal static class ScreenshotEntry
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs request) {
                return new AssemblyName(request.Name).Name == "PBZKeyForge" ? Assembly.LoadFrom(Path.GetFullPath(args[0])) : null;
            };
            Assembly.LoadFrom(Path.GetFullPath(args[0]));
            return (int)typeof(ScreenshotEntry).Assembly.GetType("CaptureScreenshots").GetMethod("Run").Invoke(null, new object[] { args });
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.GetBaseException().Message); return 1; }
    }
}

internal static class CaptureScreenshots
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    [StructLayout(LayoutKind.Sequential)]
    struct ComboInfo
    {
        public int Size;
        public Rectangle Item, Button;
        public int State;
        public IntPtr Combo, Edit, List;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetComboBoxInfo(IntPtr handle, ref ComboInfo info);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr handle, out NativeRect rectangle);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("dwmapi.dll")] static extern int DwmFlush();

    static T Field<T>(object owner, string name)
    { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }

    static System.Collections.Generic.IEnumerable<Control> Descendants(Control owner)
    {
        foreach (Control child in owner.Controls)
        {
            yield return child;
            foreach (Control nested in Descendants(child)) yield return nested;
        }
    }

    static void Click(Control owner, string name)
    { Descendants(owner).OfType<Button>().Single(button => button.Name == name).PerformClick(); }

    static void Place(Form window, int width, int height)
    {
        var work = Screen.FromControl(window).WorkingArea;
        float scale;
        using (var graphics = window.CreateGraphics()) scale = graphics.DpiX / 96f;
        window.Size = new Size(Math.Min(work.Width - 24, (int)(width * scale)), Math.Min(work.Height - 24, (int)(height * scale)));
        window.Location = new Point(work.Left + (work.Width - window.Width) / 2, work.Top + (work.Height - window.Height) / 2);
        if (!work.Contains(window.Bounds)) throw new InvalidOperationException("The desktop is too small for the editor. Use a larger display or lower Windows scaling.");
        window.TopMost = true; window.Activate(); window.Refresh();
        Cursor.Position = window.PointToScreen(new Point(16, 16));
    }

    static void Capture(Form window, string path, ComboBox dropdown, Form background)
    {
        Exception failure = null;
        bool complete = false;
        var elapsed = Stopwatch.StartNew();
        using (var timer = new Timer { Interval = 120 })
        {
            timer.Tick += delegate
            {
                try
                {
                    if (elapsed.ElapsedMilliseconds > 5000) throw new InvalidOperationException("The requested screenshot did not become ready.");
                    Rectangle bounds = background == null ? window.Bounds : Rectangle.Union(background.Bounds, window.Bounds);
                    if (dropdown != null)
                    {
                        var info = new ComboInfo { Size = Marshal.SizeOf(typeof(ComboInfo)) };
                        if (!dropdown.DroppedDown || !GetComboBoxInfo(dropdown.Handle, ref info) || !IsWindowVisible(info.List)) return;
                        // Start the list at its header, independent of current selection.
                        SendMessage(info.List, 0x197, IntPtr.Zero, IntPtr.Zero);
                        NativeRect list;
                        if (!GetWindowRect(info.List, out list)) throw new InvalidOperationException("Cannot locate the expanded list.");
                        bounds = Rectangle.Union(bounds, Rectangle.FromLTRB(list.Left, list.Top, list.Right, list.Bottom));
                    }
                    if (!Screen.FromControl(window).WorkingArea.Contains(bounds)) throw new InvalidOperationException("The screenshot extends beyond the display.");
                    window.Update(); DwmFlush();
                    using (var bitmap = new Bitmap(bounds.Width, bounds.Height))
                    {
                        using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                        bitmap.Save(path, ImageFormat.Png);
                    }
                    Console.WriteLine("Captured: " + path + (dropdown == null ? "" : " (dropdown open)"));
                    complete = true; timer.Stop();
                }
                catch (Exception ex) { failure = ex; complete = true; timer.Stop(); }
                finally { if (complete && dropdown != null) dropdown.DroppedDown = false; }
            };
            timer.Start();
            if (dropdown != null)
            {
                var click = new IntPtr(((dropdown.Height / 2) << 16) | (dropdown.Width - 12));
                PostMessage(dropdown.Handle, 0x201, new IntPtr(1), click);
                PostMessage(dropdown.Handle, 0x202, IntPtr.Zero, click);
            }
            while (!complete) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
        }
        if (failure != null) throw failure;
    }

    static void CapturePresets(MainForm form, EditorPreferences preferences, string scene, string path)
    {
        string directory = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "capture-presets-" + Guid.NewGuid().ToString("N"));
        var store = new UserPresetStore(directory);
        bool chinese = I18n.Current == "zh-Hans";
        string name = chinese ? "鼠标攻防" : "Mouse attack + guard";
        try
        {
            if (scene == "presets-save")
            {
                var type = typeof(MainForm).Assembly.GetType("PBZKeyForge.SchemeNameDialog");
                using (var dialog = (Form)Activator.CreateInstance(type, new object[] {
                    I18n.Str("schemes.savepreset"), name, new string[0], true, chinese ? "自定义 1" : "Custom 1" }))
                {
                    dialog.ShowInTaskbar = false; dialog.Show(form); Capture(dialog, path, null, null); dialog.Close();
                }
                return;
            }
            if (scene != "presets-import") store.Save(UserPresetStore.Capture(form.Session, name), name);
            using (var dialog = new UserPresetDialog(store))
            {
                dialog.ShowInTaskbar = false; dialog.StartPosition = FormStartPosition.Manual; dialog.Show(form);
                dialog.ImeMode = ImeMode.Disable;
                Place(dialog, 1040, 660);
                Field<ToolTip>(dialog, "tips").RemoveAll();
                if (scene == "presets-share")
                {
                    var scope = Field<ComboBox>(dialog, "shareScope");
                    scope.SelectedIndex = 1;
                    Capture(dialog, path, null, null);
                }
                else if (scene == "presets-import")
                {
                    var source = EditorSession.CreateOffline();
                    var configuration = new KeyEnhancementConfiguration { Enabled = true };
                    var workspace = new BindingWorkspace(source, configuration, configuration.Clone(), false, -1);
                    workspace.Add("IA_LightAttack", new BindingEntry { Keys = new[] { "F9" }, Trigger = "pulse", Interval = .15 });
                    workspace.Add("IA_HeavyAttack", new BindingEntry { Keys = new[] { "F10" }, Trigger = "pulse", Interval = .15 });
                    var preset = UserPresetStore.Capture(source, chinese ? "F9 连轻击 · F10 连重击" : "F9 light repeat · F10 heavy repeat", configuration);
                    preset = UserPresetStore.SelectScopes(preset, preset.Scopes.Where(scope => scope.Family == null));
                    Field<TextBox>(dialog, "shareBox").Text = UserPresetStore.Share(preset);
                    var timer = Field<Timer>(dialog, "importTimer");
                    var wait = Stopwatch.StartNew();
                    while (timer.Enabled && wait.ElapsedMilliseconds < 3000) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                    if (!ButtonEnabled(dialog, "usePreset")) throw new InvalidOperationException(Field<Label>(dialog, "status").Text);
                    var code = Field<TextBox>(dialog, "shareBox"); code.Select(0, 0); code.ScrollToCaret();
                    Descendants(dialog).OfType<Button>().Single(button => button.Name == "usePreset").Select();
                    Capture(dialog, path, null, null);
                }
                else Capture(dialog, path, null, null);
                dialog.Close();
            }
        }
        finally { if (Directory.Exists(directory)) { store.Delete(store.Load()); Directory.Delete(directory); } }
    }

    static bool ButtonEnabled(Control owner, string name)
    { return owner.Controls.Find(name, true).OfType<Button>().Single().Enabled; }

    static void CaptureEnhancement(MainForm form, string scene, string path)
    {
        typeof(MainForm).GetMethod("EnableKeyEnhancementEditing", Hidden).Invoke(form, null);
        var config = Field<KeyEnhancementConfiguration>(form, "keyEnhancement");
        var workspace = new BindingWorkspace(form.Session, config, config.Clone(), false, -1);
        const string move = "move:Ability.Action.Hun.CombatArt.BronzeSword.Final";
        var entry = workspace.Add(move, new BindingEntry { Keys = new[] { "G" }, Trigger = "press" });
        workspace.Add("ability:Ability.Action.Hun.SpecialDash", new BindingEntry { Keys = new[] { "V" }, Trigger = "press" });
        form.RenderSession();
        form.ShowActionView(scene == "enhancement-actions" || scene == "enhancement-moves" || scene == "enhancement-tuning");
        Application.DoEvents();
        if (scene == "enhancement-actions")
        {
            var actions = Field<object>(form, "unifiedActions");
            var groups = Field<TreeView>(actions, "groups");
            groups.SelectedNode = groups.Nodes.Cast<TreeNode>().Single(node => (string)node.Tag == "shadow");
            groups.SelectedNode.Expand();
            Application.DoEvents();
        }
        if (scene == "enhancement-moves")
        {
            using (var dialog = new UnifiedActionChoiceDialog(workspace, "G", null))
            {
                dialog.Show(form); Place(dialog, 900, 800);
                Field<TextBox>(dialog, "search").Text = I18n.Current == "zh-Hans" ? "战技" : "Combat Art";
                Application.DoEvents();
                Capture(dialog, path, null, null); dialog.Close();
            }
        }
        else if (scene == "enhancement-tuning")
        {
            using (var dialog = new UnifiedBindingEditDialog(workspace, move, entry, null, null))
            {
                dialog.Show(form); Place(dialog, 840, 840);
                Field<NumericUpDown>(dialog, "damageMultiplier").Value = 1.5m;
                Field<NumericUpDown>(dialog, "startupSpeed").Value = 1.25m;
                Field<CheckBox>(dialog, "bypassResources").Checked = true;
                Field<CheckBox>(dialog, "bypassRestrictions").Checked = true;
                Field<CheckBox>(dialog, "superArmor").Checked = true;
                Application.DoEvents();
                Capture(dialog, path, null, null); dialog.Close();
            }
        }
        else Capture(form, path, null, null);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Run(string[] args)
    {
        Application.EnableVisualStyles();
        Point pointer = Cursor.Position;
        Directory.CreateDirectory(args[2]);
        var preferences = new EditorPreferences(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "capture-mode.txt"));
        preferences.SaveFlameEffects(false);
        preferences.SaveCompanionHotkey(false);
        string input = args.Length > 6 && !string.IsNullOrEmpty(args[6]) ? args[6] : null;
        using (var form = new MainForm(preferences))
        {
            form.StartPosition = FormStartPosition.Manual;
            form.ShowInTaskbar = false;
            if (input != null) form.UiSmoke(input); else form.UiOffline();
            form.Show();
            try
            {
                byte[] original = form.Session.IsOffline ? null : File.ReadAllBytes(form.Session.InputPath);
                var language = Field<ComboBox>(form, "cbLang");
                language.SelectedIndex = Array.IndexOf(I18n.Languages, args[3]);
                form.Session.SwitchProfile("PBZ.KeyProfile.Custom1");
                form.Session.StagePreset(Presets.ById(args[4]), PresetApplyMode.OnlyPreset);
                string scene = args[5];
                bool playstation = scene.EndsWith("-playstation", StringComparison.Ordinal);
                bool gamepad = scene.EndsWith("-gamepad", StringComparison.Ordinal) || playstation;
                form.ShowGamepad(gamepad);
                if (gamepad)
                {
                    var familyPicker = Field<ComboBox>(form, "cbFamily");
                    string familyName = playstation ? "PS" : "Xbox";
                    var family = familyPicker.Items.Cast<ControllerFamily>().Single(item => item.Name == familyName);
                    familyPicker.SelectedItem = family;
                    var view = Field<object>(form, "keyBindings");
                    view.GetType().GetField("selectedKey", Hidden).SetValue(view, "Gamepad_FaceButton_Bottom");
                    form.RenderSession();
                }
                if (scene == "actions" || scene == "binding-edit") form.ShowActionView(true);
                // Keep the public capture independent of personal preset names and paths.
                var picker = Field<ComboBox>(form, "cbPreset");
                for (int index = picker.Items.Count - 1; index >= 0; index--)
                    if (picker.Items[index] is Preset && ((Preset)picker.Items[index]).IsUser) picker.Items.RemoveAt(index);
                if (!form.Session.IsOffline) Field<Label>(form, "lblPath").Text = I18n.Str("editor.location", @"…\ProjectPBZ\Saved");
                Field<ToolTip>(form, "tip").RemoveAll();
                Field<Timer>(form, "gameTimer").Stop();
                Place(form, 1280, 840);
                string path = Path.Combine(args[2], scene + "-" + args[3] + ".png");
                if (scene.StartsWith("enhancement-", StringComparison.Ordinal)) CaptureEnhancement(form, scene, path);
                else if (scene.StartsWith("presets-", StringComparison.Ordinal)) CapturePresets(form, preferences, scene, path);
                else if (scene == "binding-edit")
                {
                    var type = typeof(MainForm).Assembly.GetType("PBZKeyForge.BindingDialog");
                    string factory = form.Session.GetKeyboardDefault("IA_Guard", 0);
                    using (var dialog = (Form)Activator.CreateInstance(type, new object[] {
                        I18n.Str("act.IA_Guard"), false, factory, new[] { factory }, true }))
                    {
                        dialog.Show(form); dialog.TopMost = true;
                        dialog.Location = new Point(form.Left + (form.Width - dialog.Width) / 2, form.Top + (form.Height - dialog.Height) / 2);
                        Capture(dialog, path, Field<ComboBox>(dialog, "keys"), form); dialog.Close();
                    }
                }
                else Capture(form, path, scene.StartsWith("main", StringComparison.Ordinal) ? picker : null, null);
                if (original != null && !original.SequenceEqual(File.ReadAllBytes(form.Session.InputPath))) throw new IOException("The source save changed during capture.");
            }
            finally
            {
                Cursor.Position = pointer;
                if (form.Session != null) form.Session.Undo();
                form.Dispose();
            }
        }
        return 0;
    }
}
