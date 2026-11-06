using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.CSharp;
using System.CodeDom.Compiler;
using PBZKeyForge;

class BindingWorkflowTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int checks;
    static string output;
    static string mode = "ok", seen = "";
    static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Flags).GetValue(owner); }
    static void Set(object owner, string name, object value) { owner.GetType().GetField(name, Flags).SetValue(owner, value); }
    static object Call(object owner, string name, params object[] args) { return owner.GetType().GetMethod(name, Flags).Invoke(owner, args); }
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static IEnumerable<Control> All(Control owner)
    { foreach (Control child in owner.Controls) { yield return child; foreach (Control item in All(child)) yield return item; } }
    static Button Button(Control owner, string name) { return All(owner).OfType<Button>().Single(item => item.Name == name); }
    static void Click(Control owner, string name) { Button(owner, name).PerformClick(); Application.DoEvents(); }
    static void Pump(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition() && timer.ElapsedMilliseconds < 12000) { Application.DoEvents(); Thread.Sleep(2); }
        Check(condition(), "UI operation did not finish");
    }
    static void Pin(MainForm form)
    {
        typeof(EditorSession).GetProperty("ActiveProfile").SetValue(form.Session, form.Session.SelectedProfile, null);
        typeof(EditorSession).GetProperty("SchemeError").SetValue(form.Session, null, null);
        Set(form, "lastRunning", true); Set(form, "modalActive", true);
        Field<System.Windows.Forms.Timer>(form, "gameTimer").Stop();
    }
    static void Shot(Form form, string name)
    {
        Application.DoEvents(); form.Refresh();
        using (var bitmap = new Bitmap(form.Width, form.Height))
        { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(output, name + ".png")); }
    }
    static void Apply(MainForm form, bool success)
    {
        Pin(form);
        var task = (Task<bool>)Call(form, "ApplyChangesAsync"); Pump(() => task.IsCompleted);
        Check(task.Result == success, "Unexpected apply result: " + Field<string>(form, "fullFeedback"));
        Pin(form);
    }
    [STAThread] static int Main(string[] args)
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.EnableVisualStyles(); Control.CheckForIllegalCrossThreadCalls = true;
        int result = 1; EventHandler run = null;
        run = delegate {
            Application.Idle -= run;
            try
            {
                output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output); I18n.Current = "zh-Hans";
                Check(!EditorSession.IsGameRunning, "Close the game before running isolated workflow tests");
                Localization(); Capture(); Workflow();
                Console.WriteLine("BindingWorkflowTests PASS " + checks); result = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            Application.ExitThread();
        };
        Application.Idle += run; Application.Run(); return result;
    }
    static void Localization()
    {
        var method = typeof(I18n).GetMethod("WorkflowLanguage", BindingFlags.Static | BindingFlags.NonPublic);
        Check(method != null, "Workflow translations missing");
        string[] keys = { "notconnected", "notconnected.detail", "applyreturn", "return", "pending", "pending.advanced", "clean", "components.pending", "deferred.withbindings", "deferred.only", "saved.deferred", "draft.hint", "test.hint", "failed.details", "connection.title", "connection.open", "connection.help", "connection.files", "restore.live", "restore.confirm", "close.question", "close.hint", "close.unavailable", "close.apply", "close.export", "close.discard", "close.stay", "close.remaining", "record", "record.active" };
        var dictionary = (Dictionary<string, Dictionary<string, string>>)typeof(I18n).GetField("T", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        foreach (string language in I18n.Languages)
        {
            foreach (string key in keys) Check(dictionary[language].ContainsKey("flow." + key), "Missing translation " + language + " " + key);
            Check(dictionary[language].ContainsKey("live.rawconflict"), "Missing raw conflict translation: " + language);
        }
    }
    static void Capture()
    {
        Type type = typeof(MainForm).Assembly.GetType("PBZKeyForge.GamepadCapture");
        using (var capture = (IDisposable)Activator.CreateInstance(type, true))
        {
            var pressed = new List<string>(); var released = new List<string>(); var axes = new List<string>();
            type.GetEvent("Pressed").AddEventHandler(capture, (Action<string>)pressed.Add);
            type.GetEvent("Released").AddEventHandler(capture, (Action<string>)released.Add);
            type.GetEvent("Axis").AddEventHandler(capture, (Action<string, double>)delegate(string key, double value) { axes.Add(key + ":" + Math.Sign(value)); });
            Action<Dictionary<string, double>> feed = sample => Call(capture, "Feed", "fixture", sample);
            feed(new Dictionary<string, double> { { "Gamepad_FaceButton_Bottom", 1 } });
            Check(pressed.Count == 0, "Recording captured a button held before it began");
            feed(new Dictionary<string, double>()); Check(released.Contains("Gamepad_FaceButton_Bottom"), "Held controller button was not released");
            var sample2 = new Dictionary<string, double> { { "Gamepad_LeftShoulder", 1 }, { "Gamepad_FaceButton_Bottom", 1 }, { "Gamepad_LeftX", -.8 } };
            feed(sample2); feed(sample2);
            Check(pressed.SequenceEqual(new[] { "Gamepad_LeftShoulder", "Gamepad_FaceButton_Bottom" }), "Controller chord repeated or lost an input");
            Check(axes.SequenceEqual(new[] { "Gamepad_LeftX:-1" }), "Axis threshold repeated or lost direction");
            feed(new Dictionary<string, double> { { "Gamepad_LeftX", .8 } });
            Check(axes.Last() == "Gamepad_LeftX:1" && released.Contains("Gamepad_LeftShoulder"), "Axis reversal or chord release lost");
        }
        var session = EditorSession.CreateOffline(); var config = new KeyEnhancementConfiguration { Enabled = true };
        var workspace = new BindingWorkspace(session, config, config.Clone(), true, session.ControllerFamilies.First().Index);
        using (var dialog = new UnifiedBindingEditDialog(workspace, "IA_Jump", null, null, null))
        {
            dialog.Show(); Application.DoEvents();
            Call(Field<Control>(dialog, "capture"), "OnGotFocus", EventArgs.Empty);
            Set(dialog, "recording", true); Call(dialog, "Captured", "Gamepad_LeftShoulder"); Call(dialog, "Captured", "Gamepad_FaceButton_Bottom");
            Click(dialog, "saveBinding");
            Check(dialog.Result.Keys.Contains("Gamepad_LeftShoulder") && dialog.Result.Keys.Contains("Gamepad_FaceButton_Bottom"), "Controller chord not saved by editor");
        }
        using (var dialog = new UnifiedBindingEditDialog(workspace, "IA_Jump", null, null, null))
        {
            dialog.Show(); Application.DoEvents(); Field<ComboBox>(dialog, "trigger").SelectedIndex = 9;
            Set(dialog, "recording", true); Call(dialog, "CapturedAxis", "Gamepad_LeftX", -.9); Shot(dialog, "axis-recording"); Click(dialog, "saveBinding");
            Check(dialog.Result.Axis == "Gamepad_LeftX" && dialog.Result.Rule.AxisDirection == "negative" && dialog.Result.Keys.Length == 0, "Axis recording lost engine name, direction or retained hidden keys");
        }
    }
    static void Workflow()
    {
        string root = Path.Combine(output, "game"), save = Path.Combine(root, @"ProjectPBZ\Saved\EnhancedInputUserSettings.sav");
        Directory.CreateDirectory(Path.GetDirectoryName(save)); File.Copy("samples/vanilla-base.sav", save, true);
        AuthenticatedSchemeFixture.Create(root, "Custom1");
        string shared = AuthenticatedSchemeFixture.Shared(root);
        string runtime = Path.Combine(root, "inert-process"); Directory.CreateDirectory(runtime);
        string exe = Path.Combine(runtime, "PBZ.exe");
        using (var provider = new CSharpCodeProvider())
        {
            var options = new CompilerParameters { GenerateExecutable = true, OutputAssembly = exe };
            var compiled = provider.CompileAssemblyFromSource(options, "class Fixture { static void Main() { System.Threading.Thread.Sleep(120000); } }");
            Check(!compiled.Errors.HasErrors, "Could not compile inert game-process fixture");
        }
        var install = new KeyEnhancementInstallation(root); Directory.CreateDirectory(Path.Combine(install.InstallDirectory, "Scripts"));
        var baseline = new KeyEnhancementConfiguration { Enabled = true };
        File.WriteAllText(install.ConfigurationPath, baseline.ToJson()); File.WriteAllText(Path.Combine(install.InstallDirectory, @"Scripts\router-config.lua"), baseline.CompileLua()); install.LoadConfiguration();
        var never = install.LiveStatus(); Check(never.NeverConnected && !never.Waiting && !never.Available, "First connection is falsely presented as temporary loss");
        string heartbeat = Path.Combine(install.InstallDirectory, "live-status.txt"); File.WriteAllText(heartbeat, "PBZ-LIVE-1\nfixture\nready\n");
        bool stop = false;
        var responder = Task.Run(delegate {
            while (!Volatile.Read(ref stop))
            {
                try
                {
                    File.WriteAllText(heartbeat, "PBZ-LIVE-1\nfixture\nready\n");
                    string request = Path.Combine(install.InstallDirectory, "live-request.id");
                    string id = File.Exists(request) ? File.ReadAllText(request) : "";
                    if (id != "" && id != seen && !id.StartsWith("cancel-"))
                    { seen = id; File.WriteAllText(Path.Combine(install.InstallDirectory, "live-response.txt"), id + "\n" + mode + "\nfixture rejection"); }
                }
                catch (IOException) { }
                Thread.Sleep(30);
            }
        });
        byte[] original = File.ReadAllBytes(save), originalShared = File.ReadAllBytes(shared);
        using (var fakeGame = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }))
        try
        {
            var priorSession = new KeyEnhancementInstallation(Path.Combine(output, "prior-session"));
            Directory.CreateDirectory(priorSession.InstallDirectory);
            string oldHeartbeat = Path.Combine(priorSession.InstallDirectory, "live-status.txt");
            File.WriteAllText(oldHeartbeat, "PBZ-LIVE-1\nprevious-session\nready\n");
            File.SetLastWriteTimeUtc(oldHeartbeat, DateTime.UtcNow.AddMinutes(-5));
            var oldStatus = priorSession.LiveStatus();
            Check(oldStatus.NeverConnected && !oldStatus.Waiting, "Old session heartbeat hides first-connection failure");
            using (var form = new MainForm(new EditorPreferences(Path.Combine(output, "preferences.txt"))))
            {
                form.UiSmoke(save); form.Show(); Application.DoEvents(); Pin(form);
                Set(form, "keyEnhancement", baseline.Clone()); Set(form, "keyEnhancementBaseline", baseline.Clone()); Set(form, "keyEnhancementSession", form.Session);
                Set(form, "keyEnhancementInstallation", install); Set(form, "keyEnhancementState", KeyEnhancementInstallState.Enabled);
                Set(form, "messageError", false); form.RenderSession(); Pin(form);
                var ws = Field<BindingWorkspace>(form, "currentBindings");
                ws.Replace(ws.Entries("IA_Jump").First(item => item.Active), new BindingEntry { Keys = new[] { "J" } }); form.RenderSession();
                Check(Field<Control>(form, "lblScope").Text.Contains("键鼠 1") && !Field<Control>(form, "lblScope").Text.Contains(I18n.Str("flow.clean")), "Pending summary contradicts runtime draft");
                Check(Field<bool>(Field<Control>(form, "tabKb"), "dirty"), "Keyboard tab omits enhanced draft");
                Set(form, "resourceModDraft", (bool?)true); Call(form, "RenderGameStatus");
                Check(Field<Button>(form, "btnApply").Enabled, "Deferred component blocks valid live bindings");
                Shot(form, "live-pending"); Apply(form, true);
                Check(!Field<Button>(form, "btnApply").Enabled, "Component-only draft presents an actionable live apply");
                Check(Field<bool?>(form, "resourceModDraft") == true && KeyEnhancementConfiguration.FromJson(File.ReadAllText(install.ConfigurationPath)).NativeBindings.Single().Keys.Contains("J"), "Live apply lost queued component or binding");
                Check(Field<Control>(form, "lblStatus").Text == I18n.Str("flow.saved.deferred"), "Success is hidden by generic connection status");
                Check(Field<Button>(form, "btnUndo").Visible, "Successful live apply loses undo");
                Call(form, "UndoBindingEdit"); form.RenderSession(); Pin(form);
                Check(Field<BindingWorkspace>(form, "currentBindings").Entries("IA_Jump").Any(item => item.Active && item.Keys.Contains("SpaceBar")), "Undo after apply did not restore previous effective input");
                Apply(form, true);
                Check(!KeyEnhancementConfiguration.FromJson(File.ReadAllText(install.ConfigurationPath)).NativeBindings.Any(), "Undo draft did not apply to the runtime");
                ws = Field<BindingWorkspace>(form, "currentBindings");
                ws.Replace(ws.Entries("IA_Jump").First(item => item.Active), new BindingEntry { Keys = new[] { "K" } });
                mode = "error"; Apply(form, false); mode = "ok";
                Check(Field<Control>(form, "feedbackPanel").Visible, "Rejection lacks visible feedback");
                Click(form, "dismissFeedback");
                Check(Field<Control>(form, "lblStatus").Visible && Field<Control>(form, "lblStatus").Text == I18n.Str("flow.failed.details"), "Dismissed error loses details entry");
                Apply(form, true);
                Set(form, "resourceModDraft", null);
                Details(form); Pin(form); DirectEditor(form); Pin(form); RawConflict(form); Pin(form); ScopeRefresh(form, root); Pin(form);
                Call(form, "RestoreLiveBindings"); form.RenderSession(); Pin(form);
                var reset = Field<KeyEnhancementConfiguration>(form, "keyEnhancement");
                Check(reset.Enabled && !form.Session.IsResetPending && reset.Rules.Count == 0 && reset.NativeBindings.First(item => item.FunctionId == "IA_Jump" && item.Device == "keyboard").Keys.Contains("SpaceBar"), "Live restore disables enhancement or resets saves");
                Check(Field<Button>(form, "btnApply").Enabled, "Live factory reset cannot be applied");
                Shot(form, "restore-draft"); Call(form, "UndoBindingEdit"); form.RenderSession(); Pin(form);
                Check(Field<BindingWorkspace>(form, "currentBindings").Entries("IA_Jump").Any(item => item.Active && item.Keys.Contains("L")), "Live reset cannot be undone");
                Closing(form); Pin(form);
                Check(File.ReadAllBytes(save).SequenceEqual(original) && File.ReadAllBytes(shared).SequenceEqual(originalShared), "Live workflow changed a game save");
                form.Dispose();
            }
        }
        finally
        {
            Volatile.Write(ref stop, true); responder.GetAwaiter().GetResult();
            if (!fakeGame.HasExited) fakeGame.Kill(); fakeGame.WaitForExit();
        }
    }
    static void Details(MainForm form)
    {
        var ws = Field<BindingWorkspace>(form, "currentBindings");
        using (var dialog = new UnifiedActionBindingsDialog(ws, "IA_Jump", null, delegate { form.RenderSession(); }))
        using (var timer = new System.Windows.Forms.Timer { Interval = 80 })
        {
            var deadline = Stopwatch.StartNew();
            int phase = 0;
            timer.Tick += delegate {
                Check(deadline.ElapsedMilliseconds < 10000, "Details test timed out");
                Pin(form);
                if (phase == 0)
                {
                    phase++;
                    Check(Button(dialog, "close").Text == I18n.Str("flow.return"), "Details do not offer returning to game");
                    ws.Replace(ws.Entries("IA_Jump").First(item => item.Active), new BindingEntry { Keys = new[] { "L" } }); form.RenderSession(); Call(dialog, "RenderBindings");
                }
                else if (phase == 1 && Button(dialog, "close").Text == I18n.Str("flow.applyreturn"))
                { phase++; Shot(dialog, "live-details"); Click(dialog, "close"); }
                else if (phase == 2 && !form.IsApplying && form.WindowState == FormWindowState.Minimized)
                {
                    phase++; Check(!dialog.IsDisposed, "Applying closed the editor instead of preserving position");
                    Call(form, "ToggleCompanion");
                }
                else if (phase == 3 && dialog.Visible)
                {
                    timer.Stop();
                    Check(All(dialog).OfType<Label>().Any(label => label.Text.Contains("L")), "Returning to details shows stale binding");
                    dialog.Close();
                }
            };
            timer.Start(); Call(form, "PresentBindingWindow", dialog, form, null); Pump(() => dialog.IsDisposed);
            Check(phase == 3, "Details interaction did not complete");
        }
    }
    static void DirectEditor(MainForm form)
    {
        var ws = Field<BindingWorkspace>(form, "currentBindings");
        var current = ws.Entries("IA_Jump").First(item => item.Active && item.Keys.Contains("L"));
        using (var dialog = new UnifiedBindingEditDialog(ws, "IA_Jump", current, null, null))
        {
            Call(form, "PresentBindingWindow", dialog, form, null); Application.DoEvents(); Pin(form);
            Check(Button(dialog, "saveBinding").Text == I18n.Str("flow.applyreturn"), "Direct editor lacks live apply");
            Call(dialog, "SetKey", "M"); Click(dialog, "saveBinding");
            Pump(() => !form.IsApplying); Pin(form);
            Check(!dialog.IsDisposed && form.WindowState == FormWindowState.Minimized, "Direct apply does not preserve editor");
            Call(form, "ToggleCompanion"); Pin(form);
            Check((bool)Call(dialog, "UndoLiveEdit"), "Direct editor cannot undo an applied edit");
            Check(Field<ComboBox>(dialog, "mainKey").Text == "L", "Direct undo leaves stale editor fields: " + Field<ComboBox>(dialog, "mainKey").Text + " / " + string.Join(",", Field<BindingWorkspace>(form, "currentBindings").Entries("IA_Jump").Select(item => item.Id + "=" + string.Join("+", item.Keys) + ":" + item.Active)));
            Click(dialog, "saveBinding"); Pump(() => !form.IsApplying); Pin(form); Call(form, "ToggleCompanion"); Pin(form);
            Check(Field<BindingWorkspace>(form, "currentBindings").Entries("IA_Jump").Any(item => item.Active && item.Keys.Contains("L")), "Undo did not reapply old input");
            mode = "error"; Call(dialog, "SetKey", "N"); Click(dialog, "saveBinding"); Pump(() => !form.IsApplying); Pin(form); mode = "ok";
            Check(!dialog.IsDisposed && dialog.Enabled && Field<Label>(dialog, "status").Text == I18n.Str("flow.failed.details"), "Failed direct apply loses editor or error entry");
            Check(Field<BindingWorkspace>(form, "currentBindings").Entries("IA_Jump").Any(item => item.Keys.Contains("N")), "Failed direct apply loses draft");
            Click(dialog, "saveBinding"); Pump(() => !form.IsApplying); Pin(form);
            Check(Field<BindingWorkspace>(form, "currentBindings").Entries("IA_Jump").Count(item => item.Active && item.Keys.Contains("N")) == 1, "Direct apply retry duplicated binding");
            Call(form, "ToggleCompanion"); Pin(form); Call(dialog, "UndoLiveEdit");
            Click(dialog, "saveBinding"); Pump(() => !form.IsApplying); Pin(form); Call(form, "ToggleCompanion"); Pin(form);
            Shot(dialog, "live-editor"); dialog.Close();
        }
    }
    static void RawConflict(MainForm form)
    {
        Call(form, "TryAction", new Action(delegate { form.Session.SetKeyboard("IA_Jump", 0, "Q"); }));
        var install = Field<KeyEnhancementInstallation>(form, "keyEnhancementInstallation");
        string request = File.ReadAllText(Path.Combine(install.InstallDirectory, "live-request.id"));
        Apply(form, false);
        Check(form.Session.HasChanges && form.Session.GetKeyboard("IA_Jump", 0) == "Q", "Rejected raw edit lost its draft");
        Check(File.ReadAllText(Path.Combine(install.InstallDirectory, "live-request.id")) == request, "Conflicting raw edit reached the runtime");
        var config = new KeyEnhancementConfiguration { Enabled = true };
        config.NativeBindings.Add(new NativeBindingOverride { FunctionId = "IA_Jump", Device = "keyboard", Keys = new List<string> { "M" } });
        var preset = new Preset { Id = "raw-recovery", UserName = "raw-recovery", BaselineId = 1,
            Scopes = new List<PresetScope> { new PresetScope() },
            UserMappings = new List<PresetMapping> { new PresetMapping { Action = "IA_Jump", Added = new[] { "K" }, Removed = new[] { "SpaceBar" } } }, KeyEnhancement = config };
        Call(form, "StageBindingPreset", preset, PresetApplyMode.Replace); Apply(form, true);
        Check(!form.Session.HasChanges && Field<KeyEnhancementConfiguration>(form, "keyEnhancement").NativeBindings.Any(item => item.FunctionId == "IA_Jump" && item.Keys.Contains("M")), "A conflicted earlier checkpoint broke successful preset application");
        Call(form, "UndoFromBindingEditor"); Pin(form);
        Check(form.Session.GetKeyboard("IA_Jump", 0) == "Q" && form.Session.HasChanges, "Undo swallowed a previously rejected raw draft");
        Apply(form, false); Call(form, "UndoFromBindingEditor"); Pin(form);
        Check(!form.Session.HasChanges, "Undo could not return to the clean save baseline");
        Apply(form, true);
    }
    static void ScopeRefresh(MainForm form, string root)
    {
        var ws = Field<BindingWorkspace>(form, "currentBindings");
        ws.Add("IA_Jump", new BindingEntry { Keys = new[] { "F8" }, Trigger = "hold" }); form.RenderSession();
        using (var dialog = new UnifiedActionBindingsDialog(ws, "IA_Jump", null, delegate { form.RenderSession(); }))
        {
            Call(form, "PresentBindingWindow", dialog, form, null); Application.DoEvents(); Pin(form);
            var install = Field<KeyEnhancementInstallation>(form, "keyEnhancementInstallation");
            string json = File.ReadAllText(install.ConfigurationPath);
            string lua = File.ReadAllText(Path.Combine(install.InstallDirectory, @"Scripts\router-config.lua"));
            string request = File.ReadAllText(Path.Combine(install.InstallDirectory, "live-request.id"));
            AuthenticatedSchemeFixture.Write(root, "Custom2");
            Check(form.Session.ActiveProfile == form.Session.SelectedProfile, "Fixture did not leave the cached scheme stale");
            var task = (Task<bool>)Call(form, "ApplyChangesAsync"); Pump(() => task.IsCompleted);
            Check(!task.Result && form.Session.ActiveProfile == "PBZ.KeyProfile.Custom2" && form.Session.SelectedProfile == "PBZ.KeyProfile.Custom1", "Live apply used a cached scheme or switched the edited scheme");
            Check(!dialog.IsDisposed && Field<KeyEnhancementConfiguration>(form, "keyEnhancement").Rules.Any(rule => rule.Keys.Contains("F8")), "Scope rejection lost the open editor or draft");
            Check(File.ReadAllText(install.ConfigurationPath) == json && File.ReadAllText(Path.Combine(install.InstallDirectory, @"Scripts\router-config.lua")) == lua
                && File.ReadAllText(Path.Combine(install.InstallDirectory, "live-request.id")) == request, "Scope mismatch sent a request or changed persistent configuration");
            File.WriteAllText(AuthenticatedSchemeFixture.Shared(root), "invalid"); Pin(form);
            task = (Task<bool>)Call(form, "ApplyChangesAsync"); Pump(() => task.IsCompleted);
            Check(!task.Result && form.Session.ActiveProfile == null && form.Session.SchemeError != null, "Authentication failure retained stale scheme authorization");
            Check(File.ReadAllText(Path.Combine(install.InstallDirectory, "live-request.id")) == request, "Authentication failure sent a live request");
            AuthenticatedSchemeFixture.Write(root, "Custom1"); Apply(form, true);
            Check(!dialog.IsDisposed && KeyEnhancementConfiguration.FromJson(File.ReadAllText(install.ConfigurationPath)).Rules.Any(rule => rule.Keys.Contains("F8")), "Scope recovery could not retry the preserved draft");
            dialog.Close();
        }
    }
    static void Closing(MainForm form)
    {
        var ws = Field<BindingWorkspace>(form, "currentBindings"); ws.Add("IA_Jump", new BindingEntry { Keys = new[] { "F9" }, Trigger = "hold" }); form.RenderSession();
        using (var timer = new System.Windows.Forms.Timer { Interval = 80 })
        {
            timer.Tick += delegate {
                var prompt = Application.OpenForms.Cast<Form>().FirstOrDefault(window => window.GetType().Name == "PendingEditsDialog");
                if (prompt == null) return;
                timer.Stop(); Shot(prompt, "pending-close");
                Check(Button(prompt, "applyAndContinue").Visible && Button(prompt, "discardEdits").Text == I18n.Str("flow.close.discard"), "Close prompt lacks concrete choices");
                Click(prompt, "keepEditing");
            };
            timer.Start(); Check(!(bool)Call(form, "ConfirmDiscard"), "Keep editing discarded the draft");
        }
        Check(Field<KeyEnhancementConfiguration>(form, "keyEnhancement").Rules.Any(rule => rule.Keys.Contains("F9")), "Close cancellation lost draft");
        using (var timer = new System.Windows.Forms.Timer { Interval = 80 })
        {
            timer.Tick += delegate {
                var prompt = Application.OpenForms.Cast<Form>().FirstOrDefault(window => window.GetType().Name == "PendingEditsDialog");
                if (prompt == null) return;
                timer.Stop(); Click(prompt, "applyAndContinue");
            };
            timer.Start(); Check((bool)Call(form, "ConfirmDiscard"), "Apply-and-continue did not complete after game acknowledgment");
        }
        Check(KeyEnhancementConfiguration.FromJson(File.ReadAllText(Field<KeyEnhancementInstallation>(form, "keyEnhancementInstallation").ConfigurationPath)).Rules.Any(rule => rule.Keys.Contains("F9")), "Close prompt continued without applying");
        Set(form, "resourceModDraft", (bool?)true);
        ws.Add("IA_Jump", new BindingEntry { Keys = new[] { "F10" }, Trigger = "hold" }); form.RenderSession();
        using (var timer = new System.Windows.Forms.Timer { Interval = 80 })
        {
            int phase = 0; var deadline = Stopwatch.StartNew();
            timer.Tick += delegate {
                Check(deadline.ElapsedMilliseconds < 10000, "Deferred close test timed out");
                var prompt = Application.OpenForms.Cast<Form>().FirstOrDefault(window => window.GetType().Name == "PendingEditsDialog");
                if (prompt == null) return;
                if (phase == 0) { phase++; Click(prompt, "applyAndContinue"); }
                else if (!form.IsApplying && !Button(prompt, "applyAndContinue").Enabled)
                { timer.Stop(); Check(Field<bool?>(form, "resourceModDraft") == true, "Closing loses deferred component intent"); Click(prompt, "keepEditing"); }
            };
            timer.Start(); Check(!(bool)Call(form, "ConfirmDiscard"), "Closing skipped a remaining component change");
        }
    }
}
