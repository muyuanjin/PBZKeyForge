using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using PBZKeyForge;

class FeedbackAndUpdateTests
{
    const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    static int checks;
    static string output;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }
    static object Call(object owner, string name, params object[] args) { return owner.GetType().GetMethod(name, Hidden).Invoke(owner, args); }
    static IEnumerable<Control> All(Control owner) { foreach (Control child in owner.Controls) { yield return child; foreach (var nested in All(child)) yield return nested; } }
    static void Shot(Form form, string name)
    { using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(output, name + ".png")); } }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
            I18n.Current = "zh-Hans"; Application.EnableVisualStyles();
            Releases(); UpdateFiles(); Ui();
            Console.WriteLine("FeedbackAndUpdateTests PASS " + checks); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static void Releases()
    {
        var release = ApplicationRelease.Parse("{\"tag_name\":\"v9.2.1\",\"draft\":false,\"prerelease\":false,\"html_url\":\"" + ProjectLinks.Releases + "\",\"assets\":[{\"name\":\"PBZKeyForge-v9.2.1-presets-only.zip\",\"browser_download_url\":\"ignored\"},{\"name\":\"PBZKeyForge-v9.2.1.zip\",\"browser_download_url\":\"" + ProjectLinks.Project + "/releases/download/v9.2.1/PBZKeyForge-v9.2.1.zip\"}]}");
        Check(release.Version == "9.2.1" && release.Download.EndsWith("/PBZKeyForge-v9.2.1.zip") && release.IsNewerThan("1.2.3") && !release.IsNewerThan("10.0.0"), "Wrong release or asset selected");
    }
    static void Add(ZipArchive zip, string name, byte[] value)
    { using (var stream = zip.CreateEntry(name).Open()) stream.Write(value, 0, value.Length); }
    static void UpdateFiles()
    {
        string directory = Path.Combine(output, "installed"), staging = Path.Combine(output, "staging"); Directory.CreateDirectory(directory); Directory.CreateDirectory(staging);
        string assembly = typeof(MainForm).Assembly.Location;
        string archive = Path.Combine(output, "update.zip");
        using (var stream = File.Create(archive)) using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            Add(zip, "PBZKeyForge.exe", File.ReadAllBytes(assembly));
            Add(zip, "PBZKeyForge-cli.exe", File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(assembly), "PBZKeyForge-cli.exe")));
            Add(zip, "README.md", System.Text.Encoding.UTF8.GetBytes("new documentation"));
            Add(zip, "configuration.json", System.Text.Encoding.UTF8.GetBytes("must not be installed"));
        }
        string gui = Path.Combine(directory, "PBZKeyForge.exe"), cli = Path.Combine(directory, "PBZKeyForge-cli.exe");
        File.WriteAllText(gui, "previous gui"); File.WriteAllText(cli, "previous cli");
        File.WriteAllText(Path.Combine(directory, "user-preset.json"), "keep my preset");
        string manifest = ApplicationUpdate.PreparePackage(archive, staging, directory, Program.Version, 0);
        var plan = new JavaScriptSerializer().Deserialize<ApplicationUpdatePlan>(File.ReadAllText(manifest));
        Check(plan.Files.Length == 3 && !plan.Files.Contains("configuration.json"), "Update includes user or Mod files");
        bool rejected = false;
        using (var locked = new FileStream(cli, FileMode.Open, FileAccess.Read, FileShare.Read))
            try { ApplicationUpdate.ApplyPrepared(plan); } catch (IOException) { rejected = true; }
        Check(rejected && File.ReadAllText(gui) == "previous gui" && File.ReadAllText(cli) == "previous cli" && !File.Exists(Path.Combine(directory, "README.md")), "Failed update did not restore earlier replaced files");
        ApplicationUpdate.ApplyPrepared(plan);
        Check(AssemblyName.GetAssemblyName(gui).Version.ToString(3) == Program.Version && AssemblyName.GetAssemblyName(cli).Version.ToString(3) == Program.Version, "Update did not replace both executables");
        Check(File.ReadAllText(Path.Combine(directory, "user-preset.json")) == "keep my preset" && !File.Exists(Path.Combine(directory, "configuration.json")), "Update changed user data");
        rejected = false;
        try { ApplicationUpdate.PreparePackage(archive, Path.Combine(output, "wrong-version"), directory, "99.0.0", 0); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Different executable version accepted");
        using (var stream = File.Create(Path.Combine(output, "incomplete.zip"))) using (var zip = new ZipArchive(stream, ZipArchiveMode.Create)) Add(zip, "README.md", new byte[] { 1 });
        rejected = false;
        try { ApplicationUpdate.PreparePackage(Path.Combine(output, "incomplete.zip"), Path.Combine(output, "incomplete"), directory, Program.Version, 0); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Incomplete update accepted");
    }
    static void Modal(MainForm owner, string method, Action<Form> assertion)
    {
        Exception failure = null; bool opened = false;
        using (var timer = new Timer { Interval = 80 })
        {
            timer.Tick += delegate {
                Form dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(form => form != owner);
                if (dialog == null) return; timer.Stop(); opened = true;
                try { assertion(dialog); } catch (Exception ex) { failure = ex; } finally { dialog.Close(); }
            };
            timer.Start(); Call(owner, method);
        }
        Check(opened && failure == null, method + ": " + failure);
    }
    static void Ui()
    {
        using (var form = new MainForm(new EditorPreferences(Path.Combine(output, "preferences.txt"))))
        {
            form.UiOffline(); form.Show(); I18n.Current = "zh-Hans"; form.RenderSession(); Application.DoEvents();
            Check(!All(form).Any(control => control.Name == "btnModHelp"), "Duplicate component help button remains");
            var resourceOptions = Field<Button>(form, "btnResourceModMore");
            var footer = Field<Panel>(form, "footer");
            Check(resourceOptions.Visible && footer.GetChildAtPoint(new Point(resourceOptions.Left + resourceOptions.Width / 2, resourceOptions.Top + resourceOptions.Height / 2)) == resourceOptions,
                "Resource component menu covered: " + resourceOptions.Bounds);
            var config = Field<KeyEnhancementConfiguration>(form, "keyEnhancement");
            Field<BindingWorkspace>(form, "currentBindings").Add("IA_Jump", new BindingEntry { Keys = new[] { "J", "K" }, Trigger = "hold" });
            config.Enabled = true; form.RenderSession();
            Call(form, "SetMessage", "editor.failed", true, new object[] { "Mod 文件冲突：\r\nE:\\Games\\PBZ\\Scripts\\custom.lua\r\n" + new string('a', 1000) }); form.RenderSession(); Application.DoEvents();
            var notice = Field<Panel>(form, "feedbackPanel"); var summary = Field<Label>(form, "feedbackText");
            Check(notice.Visible && notice.Height < 120 && !Field<Label>(form, "lblStatus").Visible && summary.Text.Contains("custom.lua"), "Notice is large, missing path, or duplicated in footer");
            var workspace = Field<Panel>(form, "bindingWorkspace");
            Check(notice.Parent == footer && footer.ClientRectangle.Contains(notice.Bounds)
                && !notice.RectangleToScreen(notice.ClientRectangle).IntersectsWith(workspace.RectangleToScreen(workspace.ClientRectangle)), "Notice overlaps editor");
            Shot(form, "compact-notice");
            Modal(form, "ShowFullFeedback", dialog => { Check(All(dialog).OfType<TextBox>().Single().Text.Contains(new string('a', 1000)), "Complete error missing from details"); Shot(dialog, "error-details"); });
            Modal(form, "ShowAgentHelp", dialog => {
                var prompt = All(dialog).OfType<TextBox>().Single();
                var copy = All(dialog).OfType<Button>().Single(button => button.Name == "copyPrompt");
                Check(prompt.Text.Contains(AppDomain.CurrentDomain.BaseDirectory) && prompt.Text.Contains("--agent-help") && prompt.Text.Contains("current-bindings.zip"), "Prompt lacks real paths or current draft");
                Check(!prompt.Text.Contains(I18n.Str("agent.intro")) && !prompt.Text.Contains(I18n.Str("ai.help")) && prompt.SelectionLength == 0, "User instructions mixed with prompt or auto-selected");
                string prior = Clipboard.ContainsText() ? Clipboard.GetText() : null;
                try
                {
                    dialog.Activate(); copy.Focus(); copy.PerformClick();
                    string copied = Clipboard.GetText().Replace("\r\n", "\n"), expected = prompt.Text.Replace("\r\n", "\n");
                    Check(copied == expected, "Copy includes user introduction or loses prompt: copied=" + copied.Length + ", expected=" + expected.Length + ", button=" + copy.Text);
                }
                finally { if (prior != null) Clipboard.SetText(prior); else Clipboard.Clear(); }
                string path = prompt.Text.Split(new[] { "\r\n" }, StringSplitOptions.None).Single(line => line.StartsWith("Working input: ")).Substring("Working input: ".Length);
                var snapshot = BindingDocument.Open(path); Check(snapshot.Configuration.ToJson() == config.ToJson(), "Agent snapshot lost enhancement draft"); Shot(dialog, "agent-prompt");
            });
            Check(notice.Visible && Field<string>(form, "fullFeedback").Contains("custom.lua"), "Agent help replaced the application error");
            Size regular = form.Size; form.Size = form.MinimumSize; Application.DoEvents();
            Check(notice.Height < 120 && summary.Width > 200 && Field<Panel>(form, "bindingWorkspace").Height > 250, "Notice crowds the minimum-size editor");
            Shot(form, "compact-notice-minimum"); form.Size = regular; Application.DoEvents();
            Modal(form, "ShowKeyEnhancementHelp", dialog => { string help = All(dialog).OfType<TextBox>().Single().Text; Check(help.Contains("UE4SS") && help.Contains(ProjectLinks.Ue4ss) && help.Contains(ProjectLinks.Nexus), "Enhancement help lacks mechanism or links"); Shot(dialog, "enhancement-help"); });
            Modal(form, "ShowApplicationAbout", dialog => {
                Check(All(dialog).OfType<LinkLabel>().Count() == 4 && All(dialog).Any(control => control.Name == "checkUpdate"), "About lacks update entry or project links");
                Call(dialog, "ShowFailure", "fixture: network unavailable"); Check(All(dialog).OfType<TextBox>().Single().Text.Contains("Nexus Mods"), "Update failure lacks manual download choices"); Shot(dialog, "about-update-failure");
            });
            foreach (string language in I18n.Languages)
            {
                I18n.Current = language;
                foreach (string key in new[] { "agent.copy", "agent.intro", "enhancement.helpbody", "enhancement.flames", "app.credits", "app.available", "app.failed" }) Check(I18n.Str(key, "1.2.3") != key, "Missing translation: " + language + "/" + key);
            }
            I18n.Current = "zh-Hans";
            Check(I18n.Str("enhancement.flames") == "火焰特效", "Flame menu repeats component name");
            All(notice).OfType<Button>().Single(button => button.Name == "dismissFeedback").PerformClick(); Application.DoEvents(); Check(!notice.Visible, "Dismiss did not reclaim notice space");
            form.Dispose();
        }
    }
}
