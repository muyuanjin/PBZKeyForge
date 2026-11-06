using System;
using System.Collections;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using PBZKeyForge;

class BindingIdentityUiTests
{
    const string Action = "action:/SGameCombatSystem/Weapon/Input/IA_ChangeArmed";
    const string Ability = "ability:Ability.UpperBody.Hun.ChangeArmed";
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int checks;
    static string output;
    static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static string Assignment(UnifiedActionChoiceDialog dialog, string id)
    {
        var list = Field<ListBox>(dialog, "functions");
        var function = list.Items.Cast<BindingFunction>().Single(item => item.Id == id);
        list.ClearSelected(); list.SelectedItem = function; Application.DoEvents();
        return Field<TextBox>(dialog, "selectionDetails").Text;
    }
    static UnifiedActionChoiceDialog Picker(BindingWorkspace workspace, string key)
    {
        var dialog = new UnifiedActionChoiceDialog(workspace, key, null);
        dialog.Show(); Field<TextBox>(dialog, "search").Text = "收"; Application.DoEvents(); return dialog;
    }
    static void Capture(Form form, string name)
    {
        form.Refresh(); Application.DoEvents();
        using (var bitmap = new Bitmap(form.Width, form.Height))
        { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(output, name)); }
    }
    static void ClickAdd(UnifiedActionChoiceDialog dialog)
    { Field<Button>(dialog, "accept").PerformClick(); Application.DoEvents(); }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); I18n.Current = "zh-Hans";
            output = args.FirstOrDefault() ?? "dist/binding-identity-check"; Directory.CreateDirectory(output);
            var session = EditorSession.CreateOffline();
            var config = new KeyEnhancementConfiguration { Enabled = true };
            var workspace = new BindingWorkspace(session, config, config.Clone(), false, -1);
            CatalogNames(workspace);
            Check(workspace.Functions.Single(item => item.Id == Action).Label == "收起武器"
                && workspace.Functions.Single(item => item.Id == Ability).Label == "收/拔武器",
                "Weapon input and ability names must describe their distinct behaviors");
            using (var picker = Picker(workspace, "B"))
            {
                string action = Assignment(picker, Action), ability = Assignment(picker, Ability);
                Check(action.Contains("输入动作") && action.Contains("IA_ChangeArmed"), "Input action identity is missing");
                Check(ability.Contains("技能") && ability.Contains("Ability.UpperBody.Hun.ChangeArmed"), "Ability identity is missing");
                Check(action.Contains(I18n.Str("editor.unbound")) && ability.Contains(I18n.Str("editor.unbound")), "Factory keys were mistaken for additional bindings");
                Assignment(picker, Action); ClickAdd(picker);
                Check(picker.DialogResult == DialogResult.OK && workspace.Entries(Action).Any(entry => entry.Keys.Contains("B")), "Picker did not add the chosen input action");
            }
            using (var picker = Picker(workspace, "B"))
            {
                Check(Assignment(picker, Action).Contains("已绑定此键：B"), "Reopened picker lost this-key assignment");
                Check(Assignment(picker, Ability).Contains(I18n.Str("editor.unbound")), "Assignment leaked between weapon input and ability");
                ClickAdd(picker);
                Check(workspace.Entries(Ability).Any(entry => entry.Keys.Contains("B")), "Ability could not share the same key");
            }
            KeyView(session, workspace);
            using (var picker = Picker(workspace, "B"))
            {
                Check(Assignment(picker, Action).Contains("已绑定此键"), "This-key status missing");
                Check(Assignment(picker, Ability).Contains("已绑定此键"), "Weapon ability status missing");
                Capture(picker, "assigned-picker.png");
                int count = workspace.Entries(Ability).Length; ClickAdd(picker);
                Check(workspace.Entries(Ability).Length == count + 1, "Assignment status prevented a repeated binding");
            }
            using (var picker = Picker(workspace, "N"))
                Check(Assignment(picker, Action).Contains("已绑定：B") && !Assignment(picker, Action).Contains("已绑定此键"), "Other-key binding was shown as the current key");
            workspace.Add(Ability, new BindingEntry { Keys = new[] { "B", "LeftControl" }, Trigger = "hold", Seconds = .5, Enabled = false });
            using (var picker = Picker(workspace, "B"))
            {
                string details = Assignment(picker, Ability);
                Check(details.Contains("0.5 s") && details.Contains(I18n.Str("unified.inactive")) && details.Contains("Left Ctrl"), "Disabled chord/trigger details were lost");
                picker.Size = picker.MinimumSize; Application.DoEvents();
                Check(Field<ListBox>(picker, "functions").Height >= Field<ListBox>(picker, "functions").ItemHeight,
                    "Minimum size hides the function list");
                Check(picker.ClientRectangle.Contains(picker.RectangleToClient(Field<TextBox>(picker, "selectionDetails").RectangleToScreen(Field<TextBox>(picker, "selectionDetails").ClientRectangle))), "Selection details overflow the dialog");
                Capture(picker, "minimum-picker.png");
                picker.Scale(new SizeF(1.5f, 1.5f)); picker.ClientSize = picker.MinimumSize; Application.DoEvents();
                Capture(picker, "picker-150.png");
                var accept = Field<Button>(picker, "accept");
                Check(picker.RectangleToScreen(picker.ClientRectangle).Contains(accept.RectangleToScreen(accept.ClientRectangle)), "Add button clipped at 150%");
            }
            config.Enabled = false;
            using (var picker = Picker(workspace, "B"))
                Check(Assignment(picker, Action).Contains(I18n.Str("unified.inactive")), "Disabled enhancement was shown as active");
            config.Enabled = true;
            foreach (var entry in workspace.Entries(Action)) workspace.Remove(entry);
            using (var picker = Picker(workspace, "B"))
                Check(Assignment(picker, Action).Contains(I18n.Str("editor.unbound")), "Removed binding left a stale assigned badge");
            var controller = new BindingWorkspace(session, config, config.Clone(), true, 0);
            using (var picker = Picker(controller, "Gamepad_FaceButton_Left"))
                Check(Assignment(picker, Ability).Contains(I18n.Str("editor.unbound")), "Keyboard assignments leaked into the controller view");
            Console.WriteLine("BindingIdentityUiTests PASS " + checks); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static void CatalogNames(BindingWorkspace workspace)
    {
        using (var picker = new UnifiedActionChoiceDialog(workspace, "B", null))
        {
            picker.Show();
            foreach (string query in new[] { "偃月刀", "妙手飞刀", "探花步", "凝气于刃" })
            {
                Field<TextBox>(picker, "search").Text = query; Application.DoEvents();
                var list = Field<ListBox>(picker, "functions");
                Check(list.Items.Count > 1, "Move-name search lost the distinct entries for " + query);
                list.SelectedIndex = 0; Application.DoEvents();
                Check(Field<TextBox>(picker, "selectionDetails").Text.Contains(((BindingFunction)list.SelectedItem).TechnicalName),
                    "Selection details lost the selected move identity for " + query);
                Capture(picker, "catalog-" + query + ".png");
            }
        }
    }

    static void KeyView(EditorSession session, BindingWorkspace workspace)
    {
        var type = typeof(MainForm).Assembly.GetType("PBZKeyForge.KeyBindingView");
        using (var host = new Form { ClientSize = new Size(1420, 760), Font = new Font("Microsoft YaHei UI", 10f) })
        using (var view = (Control)Activator.CreateInstance(type, true))
        {
            type.GetField("Workspace").SetValue(view, workspace); host.Controls.Add(view); host.Show();
            type.GetField("selectedKey", Hidden).SetValue(view, "B");
            type.GetMethod("Render", new[] { typeof(EditorSession), typeof(bool), typeof(int) }).Invoke(view, new object[] { session, false, -1 });
            Application.DoEvents();
            var rows = Field<IList>(view, "actionRows").Cast<object>().ToArray();
            Check(rows.Length == 2, "Key view merged the weapon input and ability");
            var buttons = rows.Select(row => Field<Control>(row, "Edit")).ToArray();
            Check(buttons.Select(button => button.Text).OrderBy(text => text).SequenceEqual(new[] { "收起武器", "收/拔武器" }.OrderBy(text => text)),
                "Bound rows lost the weapon input and ability names");
            var identities = buttons.Select(button => Field<string>(button, "IdentityText")).ToArray();
            Check(identities.Any(text => text.Contains("输入动作") && text.Contains("IA_ChangeArmed"))
                && identities.Any(text => text.Contains("技能") && text.Contains("Ability.UpperBody.Hun.ChangeArmed")), "Bound key rows lack distinguishing identities");
            Check(buttons.All(button => Field<string>(button, "DetailText").Contains("B")), "Binding input details were replaced by identities");
            Capture(host, "bound-key.png");
        }
    }
}
