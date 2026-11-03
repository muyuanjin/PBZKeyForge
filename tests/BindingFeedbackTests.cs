using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using PBZKeyForge;

public static class BindingFeedbackTests
{
    static int checks;
    static readonly Assembly App = typeof(MainForm).Assembly;
    static void Assert(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }
    static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        { yield return child; foreach (Control nested in Descendants(child)) yield return nested; }
    }
    static T Field<T>(object owner, string name)
    { return (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(owner); }
    static Form Choice(EditorSession session, bool pad, int family, params string[] actions)
    {
        var dialog = (Form)Activator.CreateInstance(App.GetType("PBZKeyForge.ActionChoiceDialog"),
            new object[] { session, actions, pad, family });
        dialog.Opacity = 0; dialog.ShowInTaskbar = false; dialog.Show(); Application.DoEvents(); return dialog;
    }
    static void Shot(Control control, string path)
    {
        Application.DoEvents();
        using (var bitmap = new Bitmap(control.Width, control.Height))
        { control.DrawToBitmap(bitmap, control.ClientRectangle); bitmap.Save(path); }
    }
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); I18n.Current = "zh-Hans";
            string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
            byte[] fixture = File.ReadAllBytes(args[0]);
            string input = Path.Combine(output, "EnhancedInputUserSettings.sav"); File.WriteAllBytes(input, fixture);
            File.WriteAllBytes(Path.Combine(output, "PBZSharedSetting.sav"), new byte[] { 1 });
            var session = EditorSession.Open(input);
            string profile = session.Profiles.First(p => p.EndsWith("Custom1")); session.SwitchProfile(profile);
            session.RestoreAction("IA_Guard", false, -1);
            var capacity = session.RelationshipCapacity("IA_Guard", false, -1);
            Assert(capacity.Used == 1 && capacity.Limit == 3 && capacity.CanAdd, "Duplicate factory slots leave capacity for two new keys");
            for (int i = 0; i < 3; i++) session.SetKeyboard("IA_Guard", i, "F" + (i + 8));
            int changes = session.ScopeChangeCount;
            capacity = session.RelationshipCapacity("IA_Guard", false, -1);
            Assert(capacity.Used == 3 && capacity.Limit == 3 && !capacity.CanAdd, "Three distinct editable keyboard keys fill capacity");
            Assert(session.ScopeChangeCount == changes, "Capacity inspection must not edit draft");
            bool rejected = false;
            try { session.ChangeRelationship("IA_Guard", "F11", true, false, -1); }
            catch (InvalidOperationException ex)
            {
                rejected = true;
                Assert(ex.Message == I18n.Str("views.bindingfull", I18n.Str("act.IA_Guard"), 3), "Fourth-key error names action, limit and recovery");
            }
            Assert(rejected && session.ScopeChangeCount == changes && !session.Relations("IA_Guard", false, -1).Keys.Contains("F11"), "Fourth-key rejection keeps all existing bindings");
            session.ChangeRelationship("IA_Guard", "F8", true, false, -1);
            Assert(session.ScopeChangeCount == changes, "Adding an existing key remains a no-op at capacity");
            using (var dialog = Choice(session, false, -1, "IA_Guard", "IA_HeavyAttack"))
            {
                var controls = Descendants(dialog).ToArray();
                var confirm = (Button)controls.Single(c => c.Name == "addSelectedAction");
                Assert(!confirm.Enabled && controls.Single(c => c.Name == "bindingCapacity").Text.Contains("3 / 3"), "Action picker visibly explains full keyboard capacity before adding");
                Assert(controls.Single(c => c.Name == "capacityHint").Text.Contains("替换"), "Full picker offers removal/replacement recovery");
                Shot(dialog, Path.Combine(output, "add-action-full.png"));
                ((ComboBox)controls.Single(c => c.Name == "actionChoice")).SelectedIndex = 1; Application.DoEvents();
                Assert(confirm.Enabled && !controls.Single(c => c.Name == "bindingCapacity").Text.Contains("3 / 3"), "Changing selected action refreshes capacity and enables add");
                Shot(dialog, Path.Combine(output, "add-action-available.png"));
            }
            using (var dialog = (Form)Activator.CreateInstance(App.GetType("PBZKeyForge.RelationEditorDialog"),
                new object[] { session, "IA_Guard", false, -1, (Action)delegate { }, (Action)delegate { } }))
            {
                dialog.Opacity = 0; dialog.ShowInTaskbar = false; dialog.Show(); Application.DoEvents();
                Assert(!Field<Button>(dialog, "add").Enabled && Field<Label>(dialog, "info").Text.Contains("3 / 3")
                    && Field<Label>(dialog, "info").Text.Contains("替换"), "Action details display full capacity and recovery beside the disabled add control");
                foreach (Control child in dialog.Controls)
                    Assert(dialog.ClientRectangle.Contains(child.Bounds), "Action details control fits: " + child.GetType().Name);
                Shot(dialog, Path.Combine(output, "action-details-full.png"));
            }
            session.ChangeRelationship("IA_Guard", "F10", false, false, -1);
            Assert(session.RelationshipCapacity("IA_Guard", false, -1).CanAdd, "Removing one key reopens capacity");
            session.ChangeRelationship("IA_Guard", "F11", true, false, -1);
            Assert(session.Relations("IA_Guard", false, -1).Keys.Contains("F11"), "Reopened capacity accepts a new key");
            int family = session.ControllerFamilies[0].Index;
            session.RestoreAction("IA_Jump", true, family);
            capacity = session.RelationshipCapacity("IA_Jump", true, family);
            Assert(capacity.Used == 1 && capacity.Limit == 1 && !capacity.CanAdd, "Controller capacity uses registered slots rather than assuming three");
            using (var dialog = Choice(session, true, family, "IA_Jump"))
            {
                Assert(Descendants(dialog).Single(c => c.Name == "bindingCapacity").Text.Contains("1 / 1"), "Controller picker shows its actual limit");
                Shot(dialog, Path.Combine(output, "add-action-controller-full.png"));
            }
            using (var form = new MainForm())
            {
                form.Opacity = 0; form.ShowInTaskbar = false; form.UiSmoke(input); form.Show(); form.SelectProfile(profile);
                Field<ComboBox>(form, "cbLang").SelectedIndex = 0;
                form.Session.RestoreAction("IA_Guard", false, -1);
                form.Session.ChangeRelationship("IA_Guard", "RightMouseButton", false, false, -1); form.RenderSession();
                var view = Field<Control>(form, "keyBindings");
                view.GetType().GetField("selectedKey", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(view, "RightMouseButton");
                form.RenderSession(); Application.DoEvents();
                var removed = Descendants(view).Single(c => c.GetType().Name == "RelationshipButton" && c.Text == I18n.Str("act.IA_Guard"));
                Assert((bool)removed.GetType().GetField("Removed").GetValue(removed) && (bool)removed.GetType().GetField("Pending").GetValue(removed), "Pending removal remains accessible in key view");
                using (var bitmap = new Bitmap(removed.Width, removed.Height))
                {
                    removed.DrawToBitmap(bitmap, removed.ClientRectangle);
                    int colored = 0;
                    for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++)
                    {
                        Color pixel = bitmap.GetPixel(x, y);
                        if (pixel.ToArgb() == Color.FromArgb(240, 178, 78).ToArgb()
                            || pixel.ToArgb() == Color.FromArgb(64, 158, 255).ToArgb()) colored++;
                    }
                    Assert(colored == 0, "Removed icon, text and status use neutral gray instead of active or pending colors");
                    bitmap.Save(Path.Combine(output, "removed-action.png"));
                }
                Shot(form, Path.Combine(output, "removed-key-overview.png"));
            }
            Assert(fixture.SequenceEqual(File.ReadAllBytes(input)) && fixture.SequenceEqual(File.ReadAllBytes(args[0])), "Feedback interactions never write input saves");
            Console.WriteLine("BindingFeedbackTests PASS: " + checks + " checks; artifacts " + output); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
