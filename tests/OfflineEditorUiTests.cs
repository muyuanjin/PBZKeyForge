using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using PBZKeyForge;

public static class OfflineEditorUiTests
{
    static int checks;
    static void Assert(bool condition, string message)
    { checks++; if (!condition) throw new Exception(message); }
    static T Field<T>(object owner, string name)
    { return (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner); }
    static void Shot(MainForm form, string path)
    {
        Application.DoEvents(); form.Refresh();
        using (var bitmap = new Bitmap(form.Width, form.Height))
        { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(path); }
    }
    static void CheckSchemeNavigation(MainForm form, string root)
    {
        var families = Field<ComboBox>(form, "cbFamily");
        var session = form.Session;
        string original = session.Profiles.Single(profile => profile.EndsWith(".Default"));
        string first = session.Profiles.Single(profile => profile.EndsWith("Custom1"));
        string second = session.Profiles.Single(profile => profile.EndsWith("Custom2"));
        form.SelectProfile(original); form.ShowGamepad(true);
        families.SelectedIndex = Enumerable.Range(0, families.Items.Count)
            .Single(index => ((ControllerFamily)families.Items[index]).Name == "PS");
        string[] profiles = { first, second };
        string[] keys = { "Gamepad_FaceButton_Top", "Gamepad_FaceButton_Right" };
        for (int i = 0; i < profiles.Length; i++)
        {
            form.SelectProfile(profiles[i]); Application.DoEvents();
            var selected = (ControllerFamily)families.SelectedItem;
            Assert(selected.Name == "PS" && session.ControllerFamilies.Any(family => family.Name == selected.Name && family.Index == selected.Index),
                "Scheme navigation must keep PS selected using the current profile's family identity.");
            Assert(session.ControllerFamilies.All(family => !session.Relations("IA_Guard", true, family.Index).Keys.Contains(keys[i])),
                "The next scheme must start without another scheme's edits.");
            var edit = Field<Action<string, string, bool>>(Field<Control>(form, "keyBindings"), "EditRelation");
            edit("IA_Guard", keys[i], true);
            Assert(session.Relations("IA_Guard", true, selected.Index).Keys.Contains(keys[i]), "The UI must edit the newly selected scheme's PS mapping.");
            int xbox = session.ControllerFamilies.Single(family => family.Name == "Xbox").Index;
            Assert(!session.Relations("IA_Guard", true, xbox).Keys.Contains(keys[i]), "PS edits must not change Xbox.");
            string path = Path.Combine(root, "navigation-" + i + ".sav");
            session.ExportCurrentScheme(path);
            var reopened = EditorSession.Open(path);
            foreach (string profile in new[] { original, first, second })
            {
                reopened.SwitchProfile(profile);
                int ps = reopened.ControllerFamilies.Single(family => family.Name == "PS").Index;
                int otherXbox = reopened.ControllerFamilies.Single(family => family.Name == "Xbox").Index;
                Assert(reopened.Relations("IA_Guard", true, ps).Keys.Contains(keys[i]) == (profile == profiles[i]),
                    "Export must retain the edited PS scheme and isolate all other schemes.");
                Assert(!reopened.Relations("IA_Guard", true, otherXbox).Keys.Contains(keys[i]), "Export must retain Xbox isolation in every scheme.");
            }
        }
        form.SelectProfile(first);
        int currentPs = form.SelectedFamilyIndex;
        Assert(session.Relations("IA_Guard", true, currentPs).Keys.Contains(keys[0])
            && !session.Relations("IA_Guard", true, currentPs).Keys.Contains(keys[1]), "Returning to Custom 1 must restore its own PS draft.");
        form.SelectProfile(original);
        Assert(!session.Relations("IA_Guard", true, form.SelectedFamilyIndex).Keys.Intersect(keys).Any(), "Default PS must remain unchanged.");
        session.ResetAll(); form.RenderSession();
    }

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            string root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
            using (var form = new MainForm(new EditorPreferences(Path.Combine(root, "preferences.txt"))))
            {
                form.UiOffline();
                I18n.Current = "zh-Hans";
                form.Show(); form.RenderSession(); Application.DoEvents();
                Assert(form.Session != null && form.Session.IsOffline && form.Session.InputPath == null,
                    "Offline entry must create a session without a save path.");
                Assert(!Field<Control>(form, "empty").Visible && Field<Control>(form, "cbPreset").Enabled
                    && Field<Control>(form, "btnPresetManager").Enabled, "Offline preset editing must be available.");
                Assert(Field<Control>(form, "btnApply").Enabled && Field<Control>(form, "btnApply").Text == I18n.Str("offline.export"),
                    "An unchanged offline scheme must be exportable.");
                Assert(!Field<Control>(form, "chkActivate").Visible && Field<Control>(form, "lblOffline").Visible,
                    "Offline state must not show game activation.");
                Assert(Field<Control>(form, "btnRefresh").Text == I18n.Str("editor.choose"), "Offline header must offer to open a save.");
                Assert(Field<Control>(form, "lblPath").Text == I18n.Str("offline.location"), "Offline location must be explicit.");
                var families = Field<ComboBox>(form, "cbFamily");
                Assert(!families.Visible, "Controller selector must stay hidden on the keyboard page.");
                Shot(form, Path.Combine(root, "offline-keyboard.png"));
                form.ShowGamepad(true); Application.DoEvents();
                Assert(families.Visible && families.Enabled && families.Items.Count >= 2, "Offline Xbox and PlayStation must both be available.");
                foreach (string name in new[] { "Xbox", "PlayStation" })
                {
                    int index = Enumerable.Range(0, families.Items.Count).First(i => families.Items[i].ToString() == name);
                    families.SelectedIndex = index; form.RenderSession();
                    var session = form.Session;
                    session.ChangeRelationship("IA_Guard", "Gamepad_FaceButton_Left", true, true, form.SelectedFamilyIndex);
                    form.RenderSession();
                    Assert(session.Relations("IA_Guard", true, form.SelectedFamilyIndex).Keys.Contains("Gamepad_FaceButton_Left"),
                        name + " must allow edits without hardware or stored family records.");
                    Assert(Field<Control>(form, "btnApply").Enabled, "Offline controller edits must remain exportable.");
                    Shot(form, Path.Combine(root, "offline-" + name.ToLowerInvariant() + ".png"));
                }
                form.Session.Undo();
                using (var menu = (ContextMenuStrip)typeof(MainForm).GetMethod("CreateMoreMenu", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null))
                {
                    var item = menu.Items.Cast<ToolStripItem>().Single(entry => entry.Name == "offline.new");
                    Assert(item.Text == I18n.Str("offline.new"), "The normal menu must offer a new offline scheme.");
                    item.PerformClick();
                    Assert(form.Session.IsOffline && !form.Session.HasChanges, "New offline scheme must start with factory bindings.");
                }
                CheckSchemeNavigation(form, root);
                form.Dispose();
            }
            Console.WriteLine("PASS: " + checks + " offline editor UI checks");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
