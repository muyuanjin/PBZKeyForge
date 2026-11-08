using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using PBZKeyForge;

class KeyEnhancementRuleEditorTests
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            I18n.Current = "zh-Hans";
            Application.EnableVisualStyles();
            string output = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal)) ?? "dist/key-enhancement-editor-check";
            Directory.CreateDirectory(output);
            var configuration = Configuration();
            Editing(configuration, output);
            ReadOnly(configuration);
            Contexts(configuration);
            CandidatePicker();
            DefaultTriggers();
            Localization(args.Contains("--all-languages"));
            Check(KeyEnhancementCatalog.Targets.Count(entry => entry.Kind == "action") == 98, "Action catalog count");
            Check(KeyEnhancementCatalog.Targets.Count(entry => entry.Kind == "ability") == 227, "Ability catalog count");
            Console.WriteLine("KeyEnhancementRuleEditorTests PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    static void DefaultTriggers()
    {
        var configuration = new KeyEnhancementConfiguration { Enabled = true };
        var workspace = new BindingWorkspace(EditorSession.CreateOffline(), configuration, configuration.Clone(), false, -1);
        workspace.Add("move:Ability.Action.Hun.Light.Air.AA", new BindingEntry { Keys = new[] { "G" } });
        using (var editor = new KeyEnhancementRuleEditor(configuration, true, null))
        {
            editor.Show(); Application.DoEvents();
            Field<TextBox>(editor, "keys").Text = "H";
            Check(Save(editor) && editor.Configuration.Rules.Single().DefaultTrigger && editor.Configuration.Rules.Single().Trigger == "down",
                "Advanced editing must retain the complete move default when changing only its key.");
            Field<ComboBox>(editor, "trigger").SelectedIndex = 0;
            Check(Save(editor) && !editor.Configuration.Rules.Single().DefaultTrigger && editor.Configuration.Rules.Single().Trigger == "press",
                "Choosing an explicit press in the advanced editor must clear default semantics.");
        }
    }

    static KeyEnhancementConfiguration Configuration()
    {
        var configuration = new KeyEnhancementConfiguration { Enabled = true };
        var rule = new KeyEnhancementRule();
        rule.Keys.Add("F9");
        rule.Watch.Add("F12");
        rule.Outputs.Add(new KeyEnhancementOutput {
            Path = "/PBZInput/InputActions/IA_Jump.IA_Jump",
            Modifiers = new List<string> { "/Script/EnhancedInput.InputModifierNegate" }
        });
        configuration.Rules.Add(rule);
        return configuration;
    }

    static void Editing(KeyEnhancementConfiguration configuration, string output)
    {
        using (var editor = new KeyEnhancementRuleEditor(configuration, true, null))
        {
            editor.Show(); Application.DoEvents();
            Field<TextBox>(editor, "keys").Text = "J + K";
            Field<TextBox>(editor, "search").Text = "missing";
            Field<TextBox>(editor, "search").Text = "";
            Check(editor.Configuration.Rules[0].Keys.SequenceEqual(new[] { "J", "K" }), "Search discarded an input edit");
            Check(configuration.Rules[0].Keys.Single() == "F9", "Editor changed the source configuration before acceptance");
            Click(editor, "duplicate");
            Check(editor.Configuration.Rules.Count == 2, "Duplicate did not create a rule");
            Check(editor.Configuration.Rules.Select(rule => rule.Id).Distinct().Count() == 2, "Duplicate reused a rule ID");
            Check(editor.Configuration.Rules[1].Watch.Single() == "F12", "Duplicate discarded custom sampling keys");
            Check(editor.Configuration.Rules[1].Outputs[0].Modifiers.Count == 1, "Duplicate discarded output modifiers");

            Tab(editor, 2);
            var updated = editor.Configuration.Clone();
            updated.Rules[0].Interval = .8;
            Field<TextBox>(editor, "json").Text = updated.ToJson();
            Click(editor, "add");
            Check(editor.Configuration.Rules.Count == 3 && editor.Configuration.Rules[0].Interval == .8, "Adding a rule discarded pending JSON edits");
            Check(editor.Configuration.Rules[2].Keys.Count == 0 && editor.Configuration.Rules[2].Outputs.Count == 0, "New rule contains a demonstration binding");
            Click(editor, "delete");
            Check(Save(editor), "Completed configuration did not validate");
            Tab(editor, 0);
            Capture(editor, Path.Combine(output, "editor.png"));
            editor.ClientSize = new Size(940, 660); Application.DoEvents();
            Check(Field<Button>(editor, "save").RectangleToScreen(Field<Button>(editor, "save").ClientRectangle).Right <= editor.Right, "Minimum width clips the save command");
            Capture(editor, Path.Combine(output, "editor-minimum.png"));
        }
    }

    static void ReadOnly(KeyEnhancementConfiguration configuration)
    {
        bool enabled = false;
        using (var editor = new KeyEnhancementRuleEditor(configuration, false, delegate { enabled = true; }))
        {
            editor.Show(); Application.DoEvents();
            Field<TextBox>(editor, "keys").Text = "M";
            Check(Save(editor) && editor.Configuration.Rules[0].Keys.Single() == "F9", "Read-only browsing changed the configuration");
            Field<Button>(editor, "enable").PerformClick();
            Check(enabled && editor.Configuration.Enabled, "Enable did not unlock a staged configuration");
        }
    }

    static void Contexts(KeyEnhancementConfiguration configuration)
    {
        Check(KeyEnhancementCatalog.ForMapping("IA_Guard").Length == 2, "Guard must expose both native input actions");
        Check(KeyEnhancementCatalog.ForMapping("IA_LightAttack").Length == 3, "Light attack must retain its three native trigger actions");
        Check(KeyEnhancementCatalog.ForMapping("IA_Jump").Any(entry => entry.Name == "IA_Interaction_SpecialMove"), "Jump mapping omits special movement");
        using (var editor = new KeyEnhancementRuleEditor(configuration, true, null))
        {
            editor.Show(); Application.DoEvents();
            editor.FocusInput("F9");
            Check(Field<ListBox>(editor, "rules").Items.Count == 1, "Input context did not find its rule");
            Click(editor, "add");
            Check(editor.Configuration.Rules[1].Keys.Single() == "F9" && editor.Configuration.Rules[1].Outputs.Count == 0, "Input context invented an output");
            Click(editor, "delete");
            Click(editor, "all");
            editor.FocusAction("IA_Jump");
            Check(Field<ListBox>(editor, "rules").Items.Count == 1, "Action context did not recognize an object-suffixed native path");
            editor.FocusAction("IA_Guard");
            Check(Field<ListBox>(editor, "rules").Items.Count == 0, "Action context included an unrelated output");
        }

        var guards = Configuration(); guards.Rules.Clear();
        foreach (var target in KeyEnhancementCatalog.ForMapping("IA_Guard"))
        {
            var rule = new KeyEnhancementRule(); rule.Keys.Add("G");
            rule.Outputs.Add(new KeyEnhancementOutput { Path = target.Path }); guards.Rules.Add(rule);
        }
        using (var editor = new KeyEnhancementRuleEditor(guards, true, null))
        {
            editor.FocusAction("IA_Guard"); editor.Show(); Application.DoEvents();
            Check(Field<ListBox>(editor, "rules").Items.Count == 2, "Guard context lost one of its native action rules");
        }
    }

    static void CandidatePicker()
    {
        var type = typeof(KeyEnhancementRuleEditor).GetNestedType("TargetPicker", BindingFlags.NonPublic);
        using (var picker = (Form)Activator.CreateInstance(type, new object[] { KeyEnhancementCatalog.ForMapping("IA_Guard") }))
        {
            picker.Show(); Application.DoEvents();
            var choices = Field<CheckedListBox>(picker, "choices");
            Check(choices.Items.Count == 2, "Guard picker did not limit initial choices to native candidates");
            choices.SetItemChecked(0, true);
            var filter = Descendants(picker).OfType<TextBox>().Single();
            filter.Text = "missing"; filter.Text = "";
            Check(choices.CheckedItems.Count == 1, "Searching discarded the selected output");
        }
    }

    static void Localization(bool allLanguages)
    {
        var table = (Dictionary<string, Dictionary<string, string>>)typeof(I18n).GetField("T", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        var labels = table["en"].Keys.Where(key => key.StartsWith("ruleeditor.", StringComparison.Ordinal) || key == "enhancement.invalid").ToArray();
        foreach (string language in allLanguages ? I18n.Languages : new[] { "zh-Hans" })
            foreach (string key in labels) Check(table[language].ContainsKey(key), "Missing translation: " + language + " " + key);
    }

    static T Field<T>(object target, string name)
    { return (T)target.GetType().GetField(name, Private).GetValue(target); }

    static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child)) yield return descendant;
        }
    }

    static void Click(Control root, string key)
    {
        Descendants(root).OfType<Button>().Single(button => button.Text == I18n.Str("ruleeditor." + key)).PerformClick();
        Application.DoEvents();
    }

    static bool Save(KeyEnhancementRuleEditor editor)
    { return (bool)editor.GetType().GetMethod("SaveAll", Private).Invoke(editor, null); }

    static void Tab(KeyEnhancementRuleEditor editor, int index)
    {
        var tabs = Field<Control>(editor, "tabs");
        tabs.GetType().GetProperty("SelectedIndex").SetValue(tabs, index, null);
        Application.DoEvents();
    }

    static void Capture(Form form, string path)
    {
        Application.DoEvents();
        using (var bitmap = new Bitmap(form.Width, form.Height))
        { form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height)); bitmap.Save(path); }
    }

    static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }
}
