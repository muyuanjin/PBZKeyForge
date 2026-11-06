using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using PBZKeyForge;

class EnhancedUsabilityTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int checks;
    static string output;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }
    static void Set(object owner, string name, object value) { owner.GetType().GetField(name, Hidden).SetValue(owner, value); }
    static object Call(object owner, string name, params object[] args) { return owner.GetType().GetMethod(name, Hidden).Invoke(owner, args); }
    static IEnumerable<Control> All(Control owner)
    { foreach (Control child in owner.Controls) { yield return child; foreach (Control descendant in All(child)) yield return descendant; } }
    static BindingEntry Key(string key) { return new BindingEntry { Keys = new[] { key } }; }
    static void Choose(ComboBox combo, string key)
    { combo.SelectedIndex = Enumerable.Range(0, combo.Items.Count).Single(index => Field<string>(combo.Items[index], "Key") == key); }
    static void Capture(Form form, string name)
    {
        bool wasTopMost = form.TopMost;
        try
        {
            form.TopMost = true; form.Activate(); form.Refresh(); Application.DoEvents();
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(output, name + ".png"));
                if (name == "main-minimum")
                {
                    using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(form.Location, Point.Empty, form.Size);
                    bitmap.Save(Path.Combine(output, name + "-desktop.png"));
                }
            }
        }
        finally { form.TopMost = wasTopMost; }
    }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); I18n.Current = "zh-Hans";
            output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
            if (args.Contains("--status-only")) Status();
            else if (args.Contains("--layout-only")) { DialogLayout(); MainLayout(); }
            else { StorageAndScope(); DynamicMode(); DialogLayout(); Navigation(); Companion(); Status(); MainLayout(); }
            if (args.Contains("--all-languages")) Localization();
            Console.WriteLine("EnhancedUsabilityTests PASS " + checks); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static void StorageAndScope()
    {
        var session = EditorSession.CreateOffline(); var config = new KeyEnhancementConfiguration { Enabled = true };
        var ws = new BindingWorkspace(session, config, config.Clone(), false, -1) { LiveEditing = true };
        var original = ws.Entries("IA_Jump").First(entry => entry.IsNative);
        byte[] save = session.ExportCurrentScheme();
        Check(!ws.UsesSaveBinding("IA_Jump", Key("J"), original), "Live replacement promises scheme-only scope");
        ws.Replace(original, Key("J"));
        Check(session.ExportCurrentScheme().SequenceEqual(save), "Live replacement wrote the input save");
        ws.LiveEditing = false; config.Enabled = false;
        string draft = config.ToJson();
        Check(ws.UsesSaveBinding("IA_Jump", Key("K"), null), "Off-mode addition still uses disabled runtime override");
        var added = ws.Add("IA_Jump", Key("K"));
        Check(added.Active && added.Pending && added.IsNative && !added.IsRuntimeBinding && session.Relations("IA_Jump", false, -1).Keys.Contains("K"), "Off-mode key was not saved as a pending effective ordinary input");
        Check(config.ToJson() == draft, "Ordinary addition modified the disabled enhancement draft");
        var replaced = ws.Replace(added, Key("L"));
        Check(replaced.Active && !replaced.IsRuntimeBinding && config.ToJson() == draft, "Off-mode replacement changed the inactive override");
        byte[] ordinary = session.ExportCurrentScheme();
        var inactive = ws.Entries("IA_Jump").Single(entry => entry.IsRuntimeBinding);
        Check(!ws.UsesSaveBinding("IA_Jump", Key("H"), inactive), "Explicit inactive override edit promises save scope");
        var changed = ws.Replace(inactive, Key("H"));
        Check(changed.IsRuntimeBinding && !changed.Active && session.ExportCurrentScheme().SequenceEqual(ordinary), "Explicit inactive override edit escaped its draft");
        ws.Add("IA_Jump", Key("H"));
        Check(ws.Entries("IA_Jump").Any(entry => !entry.IsRuntimeBinding && entry.Active && entry.Keys.Contains("H")), "An identical inactive key prevented adding an ordinary key");
        ws.MoveKey("L", "M", false);
        Check(session.Relations("IA_Jump", false, -1).Keys.Contains("M") && !session.Relations("IA_Jump", false, -1).Keys.Contains("L"), "Off-mode whole-key move used inactive override inputs");
        config.Enabled = true;
        Check(!ws.UsesSaveBinding("IA_Jump", Key("K"), null), "Enabled runtime override promises save scope");
        Check(!ws.UsesSaveBinding("IA_Guard", Key("Gamepad_FaceButton_Bottom"), null), "Cross-device input promises save scope");
        Check(!ws.UsesSaveBinding("movement:forward", Key("T"), ws.Entries("movement:forward")[0]), "Fixed movement replacement promises save scope");
        var rule = ws.Add("IA_Guard", new BindingEntry { Keys = new[] { "F9" }, Trigger = "press" });
        Check(!ws.UsesSaveBinding("IA_Guard", Key("F10"), rule), "Existing rule replacement promises save scope");
        using (var dialog = new UnifiedBindingEditDialog(ws, "IA_Jump", null, "K", null))
        {
            dialog.Show(); Application.DoEvents();
            Check(Field<Label>(dialog, "scopeHint").Text == I18n.Str("binding.scope.global"), "Runtime scope label disagrees with storage");
        }
        using (var dialog = new UnifiedBindingEditDialog(ws, "IA_Dodge", null, "K", null, true))
        {
            dialog.Show(); Application.DoEvents();
            Check(Field<Label>(dialog, "scopeHint").Text == I18n.Str("binding.scope.global"), "Multiple-output configuration promises scheme-only scope");
        }
        int family = session.ControllerFamilies.First().Index;
        var pad = new BindingWorkspace(session, config, config.Clone(), true, family) { LiveEditing = true };
        string padAction = session.BindingActions(true, family).First(action => session.RelationshipCapacity(action, true, family).CanAdd);
        var padOriginal = pad.Entries(padAction).First(entry => entry.IsNative);
        pad.Replace(padOriginal, Key("Gamepad_LeftShoulder"));
        config.Enabled = false; pad.LiveEditing = false; string padDraft = config.ToJson();
        string padKey = KeyNames.Gamepad.First(key => !string.IsNullOrEmpty(key) && key != "None" && key != "Gamepad_LeftShoulder" && !session.Relations(padAction, true, family).Keys.Contains(key));
        var padAdded = pad.Add(padAction, Key(padKey));
        Check(padAdded.Active && !padAdded.IsRuntimeBinding && padAdded.Pending && config.ToJson() == padDraft, "Off-mode controller addition modified the shared override instead of the selected family");
    }
    static void DynamicMode()
    {
        var session = EditorSession.CreateOffline(); var config = new KeyEnhancementConfiguration { Enabled = true };
        bool running = false;
        var ws = new BindingWorkspace(session, config, config.Clone(), false, -1) { LiveEditingProvider = delegate { return running; } };
        using (var dialog = new UnifiedBindingEditDialog(ws, "IA_Jump", null, "J", null))
        {
            dialog.Show(); Application.DoEvents();
            Check(Field<Label>(dialog, "scopeHint").Text == I18n.Str("binding.scope.scheme"), "Closed-game scope is wrong");
            running = true;
            typeof(Timer).GetMethod("OnTick", Hidden).Invoke(Field<Timer>(dialog, "modeTimer"), new object[] { EventArgs.Empty });
            Check(Field<Label>(dialog, "scopeHint").Text == I18n.Str("binding.scope.global"), "Open dialog retained the previous game mode");
            byte[] before = session.ExportCurrentScheme(); ws.Add("IA_Jump", Key("J"));
            Check(session.ExportCurrentScheme().SequenceEqual(before), "Starting game did not switch an existing workspace to runtime storage");
            running = false;
            var added = ws.Add("IA_Guard", Key("K"));
            Check(added.IsNative && !added.IsRuntimeBinding && added.Active, "Exiting game left existing workspace in live editing mode");
        }
    }
    static void DialogLayout()
    {
        var session = EditorSession.CreateOffline(); var config = new KeyEnhancementConfiguration { Enabled = true };
        var ws = new BindingWorkspace(session, config, config.Clone(), true, session.ControllerFamilies.First().Index) { LiveEditing = true };
        using (var dialog = new UnifiedBindingEditDialog(ws, "IA_Jump", null, "Gamepad_FaceButton_Bottom", null))
        {
            dialog.Show(); Application.DoEvents();
            var keys = Field<ComboBox>(dialog, "mainKey");
            Check(Field<string>(keys.Items[0], "Key").StartsWith("Gamepad_"), "Gamepad picker starts with keyboard inputs");
            Check(keys.Items.Cast<object>().Any(item => Field<string>(item, "Key") == "K"), "Gamepad picker removed mixed-device combinations");
            Check(Field<Control>(dialog, "capture").Text == I18n.Str("flow.record"), "Controller editor omits the recording entry");
            Check(Field<Label>(dialog, "scopeHint").Text == I18n.Str("binding.scope.gamepad"), "Gamepad runtime scope omits controller families");
            Capture(dialog, "controller-editor");
            foreach (int trigger in new[] { 0, 3, 4, 5, 8, 9, 11 })
            {
                Field<ComboBox>(dialog, "trigger").SelectedIndex = trigger; Application.DoEvents();
                foreach (var label in All(dialog).OfType<Label>().Where(label => label.Visible && label.Text.Length > 0))
                    Check(label.Height >= label.Font.Height + label.Padding.Vertical, "Label does not fit its font at system DPI: " + label.Text);
            }
            Field<ComboBox>(dialog, "trigger").SelectedIndex = 4;
            dialog.Size = dialog.MinimumSize; Application.DoEvents();
            var save = All(dialog).OfType<Button>().Single(button => button.Name == "saveBinding");
            Check(dialog.RectangleToScreen(dialog.ClientRectangle).Contains(save.RectangleToScreen(save.ClientRectangle)), "Minimum editor clips its save button");
            Field<Label>(dialog, "status").Text = I18n.Str("unified.choosekey"); Application.DoEvents();
            Check(Field<Label>(dialog, "status").Height >= dialog.Font.Height, "Validation message is clipped");
            Capture(dialog, "controller-editor-minimum");
        }
    }
    static void Navigation()
    {
        var session = EditorSession.CreateOffline(); var config = new KeyEnhancementConfiguration { Enabled = true };
        var ws = new BindingWorkspace(session, config, config.Clone(), false, -1);
        var raw = KeyEnhancementCatalog.ForMapping("IA_Guard").First(); int changes = 0;
        using (var dialog = new UnifiedActionBindingsDialog(ws, raw.Id, null, delegate { changes++; }))
        {
            dialog.Show(); Application.DoEvents();
            var link = All(dialog).OfType<LinkLabel>().Single(item => (string)item.Tag == "IA_Guard");
            Check(link.Text.Contains(BindingButton.FriendlyKey("RightMouseButton")), "Related entry omits the effective original keys");
            bool opened = false; Exception failure = null;
            using (var timer = new Timer { Interval = 50 })
            {
                timer.Tick += delegate {
                    var child = Application.OpenForms.OfType<UnifiedActionBindingsDialog>().FirstOrDefault(form => form != dialog);
                    if (child == null) return;
                    timer.Stop(); opened = true;
                    try
                    {
                        Check(Field<BindingFunction>(child, "function").Id == "IA_Guard" && Field<BindingWorkspace>(child, "workspace") == ws, "Related link opened the wrong action or discarded the workspace");
                        Call(child, "Apply", (Action)delegate { ws.Add("IA_Guard", Key("F10")); });
                    }
                    catch (Exception ex) { failure = ex; }
                    child.Close();
                };
                timer.Start(); typeof(LinkLabel).GetMethod("OnLinkClicked", Hidden).Invoke(link, new object[] { new LinkLabelLinkClickedEventArgs(new LinkLabel.Link()) });
            }
            if (failure != null) throw failure;
            Check(opened && changes == 1 && All(dialog).OfType<LinkLabel>().Any(item => (string)item.Tag == "IA_Guard" && item.Text.Contains("F10")), "Returning from related action did not refresh effective inputs");
            Capture(dialog, "related-native-action");
        }
    }
    static void Companion()
    {
        var preferences = new EditorPreferences(Path.Combine(output, "companion-preferences", "preferences.txt"));
        preferences.SaveCompanionHotkey(false);
        Check(!preferences.LoadCompanionHotkey(), "Companion shortcut preference was not persisted");
        using (var form = new MainForm(preferences))
        {
            form.UiOffline(); form.Show(); Application.DoEvents();
            Check(!Field<bool>(form, "hotkeyRegistered"), "Disabled companion shortcut was registered");
            Set(form, "companionHotkeyEnabled", true); Call(form, "RefreshCompanionHotkey");
            Check(Field<bool>(form, "hotkeyRegistered"), "Available companion shortcut did not register");
            var config = Field<KeyEnhancementConfiguration>(form, "keyEnhancement"); config.Enabled = true;
            var rule = new KeyEnhancementRule { Keys = new List<string> { "RightControl", "LeftAlt", "K" }, Trigger = "press" };
            rule.Outputs.Add(new KeyEnhancementOutput { Kind = "ability", Tag = "Ability.Action.Hun.SpecialDash" }); config.Rules.Add(rule);
            form.RenderSession();
            Check(!Field<bool>(form, "hotkeyRegistered") && Field<bool>(form, "companionHotkeyConflict"), "Game chord did not suspend the global shortcut");
            Call(form, "SetCompanionRecording", true); Call(form, "SetCompanionRecording", false);
            Check(!Field<bool>(form, "hotkeyRegistered"), "Leaving recording reclaimed a game's chord");
            Set(form, "keyEnhancementBaseline", config.Clone()); config.Rules.Clear(); form.RenderSession();
            Check(!Field<bool>(form, "hotkeyRegistered"), "Removing an unapplied draft forgot the still-active conflicting binding");
            Set(form, "keyEnhancementBaseline", config.Clone()); form.RenderSession();
            Check(Field<bool>(form, "hotkeyRegistered"), "Removing the effective conflict did not restore the shortcut");
            Call(form, "SetCompanionRecording", true);
            Check(!Field<bool>(form, "hotkeyRegistered"), "Recording did not release the global shortcut");
            Call(form, "SetCompanionRecording", false);
            Check(Field<bool>(form, "hotkeyRegistered"), "Cancelling recording did not restore a nonconflicting shortcut");
            config.Rules.Add(rule); rule.Enabled = false; form.RenderSession();
            Check(Field<bool>(form, "hotkeyRegistered"), "Inactive chord blocked the shortcut");
            config.Rules.Clear(); rule.Enabled = true; rule.Trigger = "sequence";
            rule.Steps = new List<string[]> { new[] { "LeftControl" }, new[] { "LeftAlt" }, new[] { "K" } }; config.Rules.Add(rule); form.RenderSession();
            Check(Field<bool>(form, "hotkeyRegistered"), "Separate sequence steps were mistaken for a simultaneous chord");
            using (var menu = (ContextMenuStrip)Call(form, "CreateMoreMenu"))
            {
                var item = menu.Items.OfType<ToolStripMenuItem>().Single(entry => entry.Name == "live.hotkey");
                item.PerformClick();
            }
            Check(!preferences.LoadCompanionHotkey() && !Field<bool>(form, "hotkeyRegistered"), "Shortcut menu did not persist and apply the choice");
            config.Rules.Clear(); config.Enabled = false; Set(form, "keyEnhancementBaseline", config.Clone()); form.RenderSession();
        }
    }
    static void Status()
    {
        var install = new KeyEnhancementInstallation(Path.Combine(output, "status-game")); Directory.CreateDirectory(install.InstallDirectory);
        string path = Path.Combine(install.InstallDirectory, "live-status.txt");
        File.WriteAllText(path, "PBZ-LIVE-1\n123\nsuspended\ninput pipeline unavailable\nstack details");
        var state = install.LiveStatus();
        Check(state.Available && state.Suspended && !state.RoutingEnabled && state.Detail.Contains("input pipeline unavailable"), "Fresh suspended heartbeat is reported as operational or cannot receive recovery");
        File.WriteAllText(path, "PBZ-LIVE-1\n124\ndisabled\n"); state = install.LiveStatus();
        Check(state.Available && !state.Suspended && !state.RoutingEnabled && state.Detail == I18n.Str("live.disabled"), "Runtime off state is reported as operational");
        File.WriteAllText(path, "PBZ-LIVE-1\n125\nready\n"); state = install.LiveStatus();
        Check(state.Available && !state.Suspended && state.RoutingEnabled, "Recovered status did not return to ready");
        File.WriteAllText(path, "");
        Check(install.LiveStatus().Available, "A heartbeat write in progress discards the last complete heartbeat");
        File.WriteAllText(path, "PBZ-LIVE-1\n125\nready\n");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(-10));
        state = install.LiveStatus();
        Check(!state.Available && state.Waiting, "Stale runtime status should wait for the game without claiming connection");
        File.WriteAllText(path, "PBZ-LIVE-");
        Check(!install.LiveStatus().Available, "Partial writes extend an expired heartbeat");
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Check(install.LiveStatus().Waiting, "A locked status file conceals the waiting state");
        bool waitingRejected = false;
        try { install.ApplyLive(new KeyEnhancementConfiguration { Enabled = true }).GetAwaiter().GetResult(); }
        catch (IOException ex) { waitingRejected = ex.Message == I18n.Str("live.waitingdetail"); }
        Check(waitingRejected && !File.Exists(Path.Combine(install.InstallDirectory, "live-request.id")), "Applying during loading reports exit-to-apply or leaves a delayed request");
        string save = Path.Combine(install.GameDirectory, @"ProjectPBZ\Saved\EnhancedInputUserSettings.sav");
        Directory.CreateDirectory(Path.GetDirectoryName(save)); File.Copy("samples/vanilla-base.sav", save, true);
        Directory.CreateDirectory(Path.Combine(install.GameDirectory, @"platform\steam"));
        File.WriteAllText(Path.Combine(install.GameDirectory, @"platform\steam\PBZ.exe"), "fixture, never launched");
        using (var form = new MainForm(new EditorPreferences(Path.Combine(output, "status-preferences.txt"))))
        {
            form.UiSmoke(save); form.Show(); Application.DoEvents(); Field<Timer>(form, "gameTimer").Stop();
            typeof(EditorSession).GetProperty("ActiveProfile").SetValue(form.Session, form.Session.SelectedProfile, null);
            typeof(EditorSession).GetProperty("SchemeError").SetValue(form.Session, null, null);
            var config = new KeyEnhancementConfiguration { Enabled = true };
            Set(form, "keyEnhancement", config); Set(form, "keyEnhancementBaseline", config.Clone());
            Set(form, "keyEnhancementInstallation", install); Set(form, "lastRunning", true); Set(form, "messageError", false);
            File.WriteAllText(path, "PBZ-LIVE-1\n126\nsuspended\nfixture pipeline unavailable");
            int repaints = 0; Field<Control>(form, "keyEnhancementBar").Invalidated += delegate { repaints++; };
            Call(form, "RenderGameStatus");
            var status = Field<Label>(form, "lblStatus");
            Check(Field<string>(form, "fullFeedback").Contains("fixture pipeline unavailable") && !status.Visible
                && Field<Panel>(form, "feedbackPanel").Visible, "Main window must show runtime suspension once in the feedback card");
            Check(Field<Button>(form, "btnApply").Enabled && !form.Session.HasChanges, "Suspended runtime cannot be retried without manufacturing an edit");
            Check(Field<string>(Field<Control>(form, "keyEnhancementBar"), "Status") == "", "Component repeats the runtime error");
            Check(repaints > 0, "Runtime status changed without repainting the component");
            Set(form, "fullFeedback", "previous operation"); bool details = false;
            using (var close = new Timer { Interval = 40 })
            {
                close.Tick += delegate {
                    var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(window => window != form);
                    if (dialog == null) return;
                    close.Stop(); details = All(dialog).OfType<TextBox>().Any(text => text.Text.Contains("fixture pipeline unavailable")); dialog.Close();
                };
                close.Start(); typeof(Control).GetMethod("OnClick", Hidden).Invoke(status, new object[] { EventArgs.Empty });
            }
            Check(details, "Status details show a previous operation instead of the current suspension reason");
            File.WriteAllText(path, "PBZ-LIVE-1\n127\nready\n"); Call(form, "RenderGameStatus");
            var header = Field<Label>(form, "lblGame"); var apply = Field<Button>(form, "btnApply");
            Check(header.Text.Contains(I18n.Str("live.connected")) && !header.Text.Contains(I18n.Str("editor.running")), "Connected header still asks the player to exit");
            Check(header.ForeColor == status.ForeColor && header.Font.Bold && Field<bool>(apply, "liveConnected"), "Connected state is not emphasized in header, footer and apply button");
            Check(apply.Text == I18n.Str("live.apply") && !apply.Enabled, "Connected state with no changes misrepresents apply availability");
            Capture(form, "live-connected");
            config.NativeBindings.Add(new NativeBindingOverride { FunctionId = "IA_Jump", Device = "keyboard", Keys = new List<string> { "J" } });
            Call(form, "RenderGameStatus"); Check(apply.Enabled, "Live draft cannot be applied when connected");
            string draft = config.ToJson();
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(-10)); Call(form, "RenderGameStatus");
            Check(header.Text.Contains(I18n.Str("live.waiting")) && apply.Text == I18n.Str("live.waiting") && !apply.Enabled, "Loading falls back to exit-to-apply state");
            Check(status.Text == I18n.Str("live.waitingdetail") && !Field<bool>(apply, "liveConnected"), "Waiting status claims a live connection");
            Capture(form, "live-waiting");
            File.WriteAllText(path, "PBZ-LIVE-1\n128\nready\n"); Call(form, "RenderGameStatus");
            Check(apply.Enabled && header.Text.Contains(I18n.Str("live.connected")) && config.ToJson() == draft, "Game resume did not recover live application with its draft");
            Set(form, "lastRunning", false); Call(form, "RenderGameStatus");
            Check(!Field<bool>(apply, "liveConnected") && header.Text.Contains(I18n.Str("editor.closed")), "Game exit retains connected presentation");
        }
    }
    static void MainLayout()
    {
        using (var form = new MainForm(new EditorPreferences(Path.Combine(output, "layout-preferences.txt"))))
        {
            form.UiOffline(); form.Show();
            Field<KeyEnhancementConfiguration>(form, "keyEnhancement").Enabled = true;
            form.ShowActionView(true);
            Call(form, "SetMessage", "editor.failed", true, new object[] { "资源未找到，请选择游戏根目录。" }); form.RenderSession();
            foreach (bool minimum in new[] { false, true })
            {
                if (minimum) form.Size = form.MinimumSize;
                Application.DoEvents();
                var view = Field<UnifiedActionView>(form, "unifiedActions"); var grid = Field<DataGridView>(view, "actions");
                Console.WriteLine("Main {0}: grid={1}, full rows={2}", form.Size, grid.Size, grid.DisplayedRowCount(false));
                Check(grid.DisplayedRowCount(false) >= 8, "Main action viewport is too small");
                var diff = Field<Control>(form, "schemeDiff");
                Check(diff.Visible && diff.Height <= diff.Font.Height + 20 && !Field<Control>(diff, "rows").Visible,
                    "Action view must retain a compact differences header");
                foreach (string field in new[] { "lblScope", "btnApply", "lblOffline" })
                {
                    var control = Field<Control>(form, field);
                    Check(control.Parent.ClientRectangle.Contains(control.Bounds) && control.Height >= control.Font.Height, "Footer control clipped: " + field);
                }
                Capture(form, minimum ? "main-minimum" : "main-default");
            }
            Field<KeyEnhancementConfiguration>(form, "keyEnhancement").Enabled = false; form.RenderSession();
        }
    }
    static void Localization()
    {
        foreach (string language in I18n.Languages)
        {
            I18n.Current = language;
            foreach (string key in new[] { "live.connected", "live.waiting", "live.paused", "live.waitingdetail", "live.suspended", "live.disabled", "live.hotkey", "live.hotkeyconflict", "binding.record.keyboard", "binding.record.gamepad", "binding.record.active", "binding.related.open", "binding.scope.gamepad" })
                Check(I18n.Str(key, "fixture") != key, "Missing localization: " + language + " " + key);
        }
    }
}
