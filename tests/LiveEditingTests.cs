using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PBZKeyForge;

class LiveEditingTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int checks;
    static string output;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }
    static object Call(object owner, string name, params object[] args) { return owner.GetType().GetMethod(name, Hidden).Invoke(owner, args); }
    static BindingEntry Key(string key, string trigger = "default") { return new BindingEntry { Keys = new[] { key }, Trigger = trigger }; }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            I18n.Current = "zh-Hans"; Application.EnableVisualStyles();
            output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
            if (args.Contains("--runtime-feedback-only")) RuntimeFeedback();
            else { Model(); HistoryAndFeedback(); LiveProtocol(); RuntimeFeedback(); }
            if (args.Contains("--all-languages")) Localization();
            Console.WriteLine("LiveEditingTests PASS " + checks); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static void Model()
    {
        var session = EditorSession.CreateOffline(); var config = new KeyEnhancementConfiguration();
        var ws = new BindingWorkspace(session, config, config.Clone(), false, -1);
        var inactive = ws.Add("IA_Jump", Key("J", "hold")); Check(!inactive.Active && !config.Enabled, "Editing off enabled execution");
        config.Enabled = true; var tap = ws.Add("IA_Guard", Key("J", "tap"));
        Check(config.Rules.All(rule => rule.InputGroup == tap.Rule.InputGroup && rule.Seconds == tap.Seconds) && tap.Rule.InputGroup != null, "Tap and hold do not share reservation");
        byte[] save = session.ExportCurrentScheme(); ws.LiveEditing = true;
        var native = ws.Entries("IA_Jump").First(entry => entry.IsNative);
        ws.Replace(native, Key("K"));
        foreach (string key in new[] { "L", "M", "N", "O" }) ws.Add("IA_Jump", Key(key));
        Check(config.NativeBindings.Single().Keys.Count == 5 && session.ExportCurrentScheme().SequenceEqual(save), "Live native editing changed save or retained slot limit");
        var runtimeEntry = ws.Entries("IA_Jump").First(entry => entry.IsRuntimeBinding);
        Check(runtimeEntry.Active && ws.CanAddNative("IA_Jump"), "Runtime binding does not show unlimited native editing");
        config.Enabled = false;
        Check(ws.Entries("IA_Jump").Any(entry => entry.IsNative && !entry.IsRuntimeBinding && entry.Active && entry.Keys.Contains("SpaceBar"))
            && ws.Entries("IA_Jump").Where(entry => entry.IsRuntimeBinding).All(entry => !entry.Active), "Disabled overrides misrepresent actual native inputs");
        config.Enabled = true; ws.RestoreKey("SpaceBar");
        Check(config.NativeBindings.Single().Keys.Contains("SpaceBar") && session.ExportCurrentScheme().SequenceEqual(save), "Live key restore wrote the save or missed factory key");
        ws.Restore("IA_Jump"); Check(config.NativeBindings.Single().Keys.SequenceEqual(new[] { "SpaceBar" }), "Live action restore did not use factory bindings");
        config.Rules.Clear(); ws.MoveKey("W", "T", false);
        Check(ws.Entries("movement:forward").Any(entry => !entry.IsNative && entry.Keys.Contains("T"))
            && ws.Entries("movement:forward").First(entry => entry.IsNative && entry.Keys.Contains("W")).Suppressed, "Moving a physical key left fixed movement behind");
        Check(session.ExportCurrentScheme().SequenceEqual(save), "Whole-key runtime move wrote save bytes");
        var combined = ws.AddFunctions(new[] { "IA_Jump", "IA_Guard" }, Key("F9", "press"));
        Check(combined.Length == 2 && combined.Select(entry => entry.RuleId).Distinct().Count() == 1, "Multiple actions are not one shared rule");
        ws.CopyBinding(combined[0], "IA_Dodge"); Check(ws.Entries("IA_Dodge").Any(entry => entry.Keys.Contains("F9") && !entry.IsNative), "Binding copy lost trigger/input");
        native = ws.Entries("IA_Guard").First(entry => entry.IsNative);
        ws.SetEnabled(native, false);
        var disabled = ws.Entries("IA_Guard").First(entry => !entry.IsNative && !entry.Enabled);
        Check(ws.Entries("IA_Guard").Any(entry => entry.IsNative && entry.Keys.SequenceEqual(native.Keys) && entry.Suppressed), "Disabling native entry did not block it");
        ws.SetEnabled(disabled, true); Check(ws.Entries("IA_Guard").Any(entry => entry.RuleId == disabled.RuleId && entry.Active), "Native disabled entry cannot reactivate");
        var pad = new BindingWorkspace(session, config, config.Clone(), true, session.ControllerFamilies.First().Index);
        pad.Add("IA_Jump", Key("Gamepad_FaceButton_Left", "hold"));
        ws.Add("IA_Jump", new BindingEntry { Keys = new[] { "J", "Gamepad_LeftShoulder" }, Trigger = "press" });
        var selected = EnhancementEditing.SelectDevices(config, true, false);
        Check(selected.Rules.All(rule => rule.Keys.All(key => !key.StartsWith("Gamepad_"))), "Keyboard share retained pad or mixed-device rules");
        var append = EnhancementEditing.Append(config, selected);
        Check(config.Rules.All(rule => append.Rules.Any(item => item.Id == rule.Id)), "Appending removed existing rules");
        var custom = new KeyEnhancementConfiguration(); custom.CustomFunctions.Add(new KeyEnhancementOutput { Id = "same", Name = "first", Kind = "property", Property = "x" });
        var conflicting = custom.Clone(); conflicting.CustomFunctions[0].Name = "other"; string before = custom.ToJson(); bool rejected = false;
        try { EnhancementEditing.Append(custom, conflicting); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && custom.ToJson() == before, "Append conflict partially committed");
    }
    static void HistoryAndFeedback()
    {
        using (var form = new MainForm(new EditorPreferences(Path.Combine(output, "preferences.txt"))))
        {
            form.UiOffline(); form.Show(); Application.DoEvents();
            var ws = Field<BindingWorkspace>(form, "currentBindings"); ws.Add("IA_Jump", Key("J", "hold"));
            int historyCount = ((System.Collections.ICollection)Field<object>(form, "bindingHistory")).Count;
            form.SelectProfile(form.Session.Profiles.Last());
            Check(((System.Collections.ICollection)Field<object>(form, "bindingHistory")).Count == historyCount, "Scheme navigation created a binding undo step");
            form.Session.SetKeyboard("IA_Jump", 0, "T");
            byte[] draftBytes = form.Session.ExportCurrentScheme();
            string draft = Field<KeyEnhancementConfiguration>(form, "keyEnhancement").ToJson();
            Call(form, "ResetAllBindings"); Call(form, "UndoBindingEdit");
            Check(Field<KeyEnhancementConfiguration>(form, "keyEnhancement").ToJson() == draft && form.Session.ExportCurrentScheme().SequenceEqual(draftBytes), "Undo reset lost prior native or enhancement draft");
            Call(form, "UndoBindingEdit"); Check(Field<KeyEnhancementConfiguration>(form, "keyEnhancement").Rules.Count == 0, "Second undo did not undo binding edit");
            string error = "无法应用：\n" + string.Join("\n", Enumerable.Range(0, 16).Select(i => "完整帮助第 " + i + " 行：请查看配置并重试。"));
            Call(form, "SetMessage", "editor.failed", true, new object[] { error }); form.RenderSession(); Application.DoEvents();
            var panel = Field<Panel>(form, "feedbackPanel"); var text = Field<Label>(form, "feedbackText");
            Check(panel.Visible && text.Text.Contains("无法应用") && text.AutoEllipsis && Field<string>(form, "fullFeedback").Contains("15"), "Notice lost the error summary or complete detail");
            Check(panel.Bottom <= form.ClientSize.Height && panel.Height <= form.Font.Height * 7, "Error feedback uses excessive space");
            var footer = Field<Panel>(form, "footer"); var workspace = Field<Panel>(form, "bindingWorkspace");
            Check(panel.Parent == footer && footer.ClientRectangle.Contains(panel.Bounds)
                && !panel.RectangleToScreen(panel.ClientRectangle).IntersectsWith(workspace.RectangleToScreen(workspace.ClientRectangle)), "Error panel overlaps binding workspace");
            string feedback = Field<string>(form, "fullFeedback");
            foreach (string method in new[] { "ShowAgentHelp", "ShowFullFeedback" })
            {
                bool opened = false;
                using (var close = new System.Windows.Forms.Timer { Interval = 50 })
                {
                    close.Tick += delegate {
                        var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(window => window != form);
                        if (dialog == null) return;
                        close.Stop(); opened = true;
                        var content = All(dialog).OfType<TextBox>().Single();
                        Check(Field<bool>(form, "modalActive") && content.Text.Contains(method == "ShowAgentHelp" ? "--agent-help" : "15"), "Feedback dialog lost its content or allowed background refresh");
                        dialog.Close();
                    };
                    close.Start(); Call(form, method);
                }
                Check(opened && Field<string>(form, "fullFeedback") == feedback, "Help dialog replaced the application error");
            }
            foreach (var size in new[] { new Size(1280, 900), form.MinimumSize })
            {
                form.Size = size; Application.DoEvents();
                using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(output, "feedback-" + size.Width + ".png")); }
            }
        }
    }
    static void LiveProtocol()
    {
        string root = Path.Combine(output, "game"), runtime = Path.Combine(root, @"platform\steam");
        Directory.CreateDirectory(runtime); File.WriteAllText(Path.Combine(runtime, "PBZ.exe"), "fixture, never launched");
        string save = Path.Combine(root, @"ProjectPBZ\Saved\EnhancedInputUserSettings.sav"), shared = Path.Combine(root, @"ProjectPBZ\Saved\PBZSharedSetting.sav");
        Directory.CreateDirectory(Path.GetDirectoryName(save)); File.Copy("samples/vanilla-base.sav", save, true); File.WriteAllText(shared, "authenticated settings fixture, never edited");
        byte[] saveBytes = File.ReadAllBytes(save), sharedBytes = File.ReadAllBytes(shared);
        var install = new KeyEnhancementInstallation(root); Directory.CreateDirectory(Path.Combine(install.InstallDirectory, "Scripts"));
        var baseline = new KeyEnhancementConfiguration { Enabled = true };
        File.WriteAllText(install.ConfigurationPath, baseline.ToJson());
        string lua = Path.Combine(install.InstallDirectory, @"Scripts\router-config.lua"); File.WriteAllText(lua, baseline.CompileLua()); install.LoadConfiguration();
        var candidate = baseline.Clone(); candidate.NativeBindings.Add(new NativeBindingOverride { FunctionId = "IA_Jump", Device = "keyboard", Keys = new List<string> { "J" } });
        int accepts = 0; string mode = "ok"; bool stop = false; string seen = ""; string runtimeState = "suspended";
        int lockAtAccept = -1; FileStream rollbackLock = null;
        var responder = Task.Run(delegate {
            while (!Volatile.Read(ref stop))
            {
                File.WriteAllText(Path.Combine(install.InstallDirectory, "live-status.txt"), "PBZ-LIVE-1\nfixture\n" + runtimeState + "\nfixture pipeline error");
                string request = Path.Combine(install.InstallDirectory, "live-request.id");
                try
                {
                    string id = File.Exists(request) ? File.ReadAllText(request) : "";
                    if (id != "" && id != seen && !id.StartsWith("cancel-"))
                    {
                        seen = id; string current = mode;
                        if (current != "silent")
                        {
                            if (current == "ok" && Interlocked.Increment(ref accepts) == Volatile.Read(ref lockAtAccept))
                                rollbackLock = new FileStream(lua, FileMode.Open, FileAccess.Read, FileShare.Read);
                            if (current == "ok") runtimeState = "ready";
                            File.WriteAllText(Path.Combine(install.InstallDirectory, "live-response.txt"), id + "\n" + (current == "ok" ? "ok" : "error") + "\nfixture rejected");
                        }
                    }
                }
                catch (IOException) { }
                Thread.Sleep(30);
            }
        });
        try
        {
            Thread.Sleep(100);
            Check(install.LiveStatus().Suspended, "Suspended Mod was presented as running");
            install.ApplyLive(candidate).GetAwaiter().GetResult();
            Check(accepts == 1 && KeyEnhancementConfiguration.FromJson(File.ReadAllText(install.ConfigurationPath)).NativeBindings.Single().Keys[0] == "J", "Live success did not await acknowledgment and persist");
            string saved = File.ReadAllText(install.ConfigurationPath); mode = "error"; bool rejected = false;
            candidate.NativeBindings[0].Keys[0] = "K";
            try { install.ApplyLive(candidate).GetAwaiter().GetResult(); } catch (IOException) { rejected = true; }
            Check(rejected && File.ReadAllText(install.ConfigurationPath) == saved && File.ReadAllText(lua).Contains("J"), "Rejected live request changed persistent settings");
            mode = "silent"; rejected = false;
            try { install.ApplyLive(candidate).GetAwaiter().GetResult(); } catch (IOException) { rejected = true; }
            Check(rejected && File.ReadAllText(install.ConfigurationPath) == saved && File.ReadAllText(Path.Combine(install.InstallDirectory, "live-request.id")).StartsWith("cancel-"), "Timeout persisted settings or left a delayed request");
            mode = "ok"; int beforeRollback = accepts; rejected = false;
            using (var locked = new FileStream(lua, FileMode.Open, FileAccess.Read, FileShare.Read))
                try { install.ApplyLive(candidate).GetAwaiter().GetResult(); } catch (IOException) { rejected = true; }
            Check(rejected && accepts == beforeRollback + 2 && File.ReadAllText(install.ConfigurationPath) == saved, "Persistence failure did not restore previous runtime configuration");
            beforeRollback = accepts; Volatile.Write(ref lockAtAccept, beforeRollback + 2); rejected = false;
            using (var locked = new FileStream(install.ConfigurationPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                try { install.ApplyLive(candidate).GetAwaiter().GetResult(); } catch (IOException) { rejected = true; }
            try
            {
                Check(rejected && accepts == beforeRollback + 2 && rollbackLock != null && File.ReadAllText(lua).Contains("K")
                    && File.ReadAllText(install.ConfigurationPath) == saved, "Disk rollback failure prevented runtime rollback or concealed partial persistence");
            }
            finally { if (rollbackLock != null) { rollbackLock.Dispose(); rollbackLock = null; } }
            File.WriteAllText(lua, KeyEnhancementConfiguration.FromJson(saved).CompileLua());
            File.WriteAllText(install.ConfigurationPath, baseline.ToJson()); mode = "ok"; rejected = false;
            int beforeConflict = accepts;
            try { install.ApplyLive(candidate).GetAwaiter().GetResult(); } catch (IOException) { rejected = true; }
            Check(rejected && accepts == beforeConflict, "External configuration conflict reached game");
            Check(File.ReadAllBytes(save).SequenceEqual(saveBytes) && File.ReadAllBytes(shared).SequenceEqual(sharedBytes), "Live editing changed input save or scheme selection");
        }
        finally { Volatile.Write(ref stop, true); responder.Wait(); if (rollbackLock != null) rollbackLock.Dispose(); }
    }
    static void RuntimeFeedback()
    {
        string root = Path.Combine(output, "game");
        Directory.CreateDirectory(Path.Combine(root, @"platform\steam"));
        Directory.CreateDirectory(Path.Combine(root, @"ProjectPBZ\Saved"));
        File.WriteAllText(Path.Combine(root, @"platform\steam\PBZ.exe"), "fixture, never launched");
        File.Copy("samples/vanilla-base.sav", Path.Combine(root, @"ProjectPBZ\Saved\EnhancedInputUserSettings.sav"), true);
        File.WriteAllText(Path.Combine(root, @"ProjectPBZ\Saved\PBZSharedSetting.sav"), "fixture, never edited");
        var install = new KeyEnhancementInstallation(root);
        Directory.CreateDirectory(install.InstallDirectory);
        string statusPath = Path.Combine(install.InstallDirectory, "live-status.txt");
        string error = "native phase requires four parameters\n" + string.Join("\n", Enumerable.Range(0, 25).Select(index => "stack frame " + index));
        using (var form = new MainForm(new EditorPreferences(Path.Combine(output, "runtime-preferences.txt"))))
        {
            form.UiSmoke(Path.Combine(root, @"ProjectPBZ\Saved\EnhancedInputUserSettings.sav"));
            form.Show(); Application.DoEvents();
            Field<System.Windows.Forms.Timer>(form, "gameTimer").Stop();
            form.GetType().GetField("keyEnhancementInstallation", Hidden).SetValue(form, install);
            Field<KeyEnhancementConfiguration>(form, "keyEnhancement").Enabled = true;
            Field<KeyEnhancementConfiguration>(form, "keyEnhancementBaseline").Enabled = true;
            form.GetType().GetField("lastRunning", Hidden).SetValue(form, true);
            Action<string> update = delegate(string state) {
                form.GetType().GetField("lastRunning", Hidden).SetValue(form, true);
                File.WriteAllText(statusPath, "PBZ-LIVE-1\nfixture\n" + state + "\n" + error);
                Call(form, "RenderGameStatus");
            };
            update("suspended");
            var panel = Field<Panel>(form, "feedbackPanel");
            string detail = Field<string>(form, "fullFeedback");
            Check(panel.Visible && detail.Contains("stack frame 24") && !Field<Label>(form, "lblStatus").Visible,
                "Runtime errors must use the single feedback card and retain the full traceback.");
            var bar = Field<Control>(form, "keyEnhancementBar");
            Check((string)bar.GetType().GetField("Status").GetValue(bar) == ""
                && !Field<ToolTip>(form, "tip").GetToolTip(bar).Contains("stack frame"),
                "Runtime errors must not repeat below enhancement or overflow hover text.");
            bool copied = false;
            bool copyRequested = false;
            int clipboardTicks = 0;
            using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
            {
                timer.Tick += delegate {
                    var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(window => window != form);
                    if (dialog == null) return;
                    var text = All(dialog).OfType<TextBox>().Single();
                    if (!copyRequested)
                    {
                        Check(text.ReadOnly && text.Multiline && !text.WordWrap && text.Lines.Length >= 26
                            && text.Text.Replace("\r\n", "\n") == detail.Replace("\r\n", "\n"), "Runtime details must preserve every stack frame as a selectable line.");
                        var copy = (Button)All(dialog).Single(control => control.Name == "copyFeedback");
                        Check(copy.Visible && copy.Enabled, "The full-error copy button must be available.");
                        typeof(Control).GetMethod("OnClick", Hidden).Invoke(copy, new object[] { EventArgs.Empty });
                        copyRequested = true;
                        return;
                    }
                    string clipboard = Clipboard.GetText();
                    copied = clipboard.Replace("\r\n", "\n") == detail.Replace("\r\n", "\n");
                    if (!copied && ++clipboardTicks < 40) return;
                    timer.Stop();
                    using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
                    { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(output, "runtime-details.png")); }
                    dialog.Close();
                };
                timer.Start();
                var details = (Button)All(form).Single(control => control.Name == "feedbackDetails");
                Check(details.Visible && details.Enabled && details.CanSelect, "Runtime details must be clickable: " + details.Visible + "/" + details.Enabled + "/" + details.CanSelect);
                typeof(Control).GetMethod("OnClick", Hidden).Invoke(details, new object[] { EventArgs.Empty });
            }
            Check(copied, "Copy must include the complete runtime error.");
            foreach (var size in new[] { new Size(1280, 900), form.MinimumSize })
            {
                form.Size = size; Application.DoEvents(); update("suspended");
                Check(panel.Bottom <= form.ClientSize.Height && panel.Height <= form.Font.Height * 7,
                    "Runtime feedback must fit the existing footer at minimum and normal size.");
                using (var bitmap = new Bitmap(form.Width, form.Height))
                { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(output, "runtime-feedback-" + size.Width + ".png")); }
            }
            var dismiss = (Button)All(form).Single(control => control.Name == "dismissFeedback");
            Check(dismiss.Visible && dismiss.Enabled && dismiss.CanSelect, "Dismiss must be clickable: " + dismiss.Visible + "/" + dismiss.Enabled + "/" + dismiss.CanSelect);
            typeof(Control).GetMethod("OnClick", Hidden).Invoke(dismiss, new object[] { EventArgs.Empty });
            Call(form, "SetMessage", "live.hotkeybusy", false, new object[0]);
            update("suspended");
            Check(!panel.Visible, "An unchanged heartbeat or shortcut notice must not reopen dismissed runtime feedback.");
            File.WriteAllText(statusPath, "PBZ-LIVE-1\nfixture");
            Call(form, "RenderGameStatus");
            Check(Field<string>(form, "runtimeFeedback") == detail, "A two-line partial heartbeat must not erase the error or claim recovery.");
            update("suspended");
            File.SetLastWriteTimeUtc(statusPath, DateTime.UtcNow.AddSeconds(-10));
            Call(form, "RenderGameStatus");
            Check(install.LiveStatus().Waiting && Field<string>(form, "runtimeFeedback") == detail,
                "A lost heartbeat must retain the last error for copying until explicit recovery.");
            form.GetType().GetField("lastRunning", Hidden).SetValue(form, false);
            Call(form, "RenderGameStatus");
            Check(Field<string>(form, "runtimeFeedback") == detail, "Closing the game must not erase its last error before it can be copied.");
            update("suspended");
            Call(form, "SetMessage", "editor.failed", true, new object[] { "apply failure" });
            Check(Field<string>(form, "fullFeedback").Contains("apply failure") && Field<string>(form, "fullFeedback").Contains("stack frame 24"),
                "Application failures must not hide a still-active runtime error.");
            form.GetType().GetField("messageError", Hidden).SetValue(form, false);
            form.GetType().GetField("messageKey", Hidden).SetValue(form, null);
            update("ready");
            Check(!panel.Visible && Field<string>(form, "runtimeFeedback") == null && !(Field<string>(form, "fullFeedback") ?? "").Contains("stack frame"),
                "Recovery must clear the runtime error.");
            update("suspended");
            Check(panel.Visible, "A new suspension after recovery must reopen feedback.");
            Call(form, "SetMessage", "editor.failed", true, new object[] { "apply failure" });
            update("ready");
            Check(panel.Visible && Field<string>(form, "fullFeedback").Contains("apply failure"),
                "Runtime recovery must not erase an application failure.");
        }
    }
    static void Localization()
    {
        foreach (string language in I18n.Languages)
        {
            I18n.Current = language;
            foreach (string key in new[] { "live.apply", "live.ready", "editing.copy", "editing.keytools", "ai.help", "feedback.details" })
                Check(I18n.Str(key) != key, "Missing localization " + language + " " + key);
        }
    }
    static IEnumerable<Control> All(Control owner)
    { foreach (Control child in owner.Controls) { yield return child; foreach (var item in All(child)) yield return item; } }
}
