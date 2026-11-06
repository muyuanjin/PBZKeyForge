using System;
using System.Collections;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using PBZKeyForge;

class UnifiedActionViewTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int checks;
    static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static int CountControls(Control owner) { return owner.Controls.Cast<Control>().Sum(child => 1 + CountControls(child)); }
    static TreeNode Find(TreeNodeCollection nodes, string path)
    {
        foreach (TreeNode node in nodes)
        {
            if ((string)node.Tag == path) return node;
            var child = Find(node.Nodes, path); if (child != null) return child;
        }
        return null;
    }
    static void CellClick(UnifiedActionView view, int column, int row, int x, int y)
    {
        var args = new DataGridViewCellMouseEventArgs(column, row, x, y, new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
        view.GetType().GetMethod("CellClick", Hidden).Invoke(view, new object[] { view, args });
    }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); I18n.Current = "zh-Hans";
            string output = args.Length == 0 ? "dist/action-view-check" : args[0]; Directory.CreateDirectory(output);
            using (var compact = new UnifiedActionView())
            {
                compact.Size = new Size(150, 100); compact.Font = new Font("Microsoft YaHei UI", 10f); compact.CreateControl(); compact.Size = new Size(900, 400);
                var panels = Field<SplitContainer>(compact, "split");
                Check(panels.SplitterDistance >= panels.Panel1MinSize && panels.SplitterDistance <= panels.Width - panels.Panel2MinSize - panels.SplitterWidth, "Action view cannot initialize through the parent's compact construction size");
                var enabled = new KeyEnhancementConfiguration { Enabled = true };
                var enabledWorkspace = new BindingWorkspace(EditorSession.CreateOffline(), enabled, enabled.Clone(), false, -1);
                compact.Render(enabledWorkspace);
                var catalogTree = Field<TreeView>(compact, "groups");
                Check((string)catalogTree.SelectedNode.Tag == "" && Field<DataGridView>(compact, "actions").RowCount == 359,
                    "Enhanced catalog must initially show all 359 functions");
                Check(catalogTree.Nodes.Count == 12 && catalogTree.Nodes[0].Nodes.Count == 0 && catalogTree.Nodes[0].Text.EndsWith("(359)"),
                    "All-functions entry must be a visible sibling of complete purpose groups, with the full count");
            }
            var config = new KeyEnhancementConfiguration();
            var workspace = new BindingWorkspace(EditorSession.CreateOffline(), config, config.Clone(), false, -1);
            using (var form = new Form { ClientSize = new Size(1040, 650), Font = new Font("Microsoft YaHei UI", 10f) })
            using (var view = new UnifiedActionView())
            {
                form.Controls.Add(view); form.Show(); Application.DoEvents();
                var timer = Stopwatch.StartNew(); view.Render(workspace); Application.DoEvents(); timer.Stop();
                var grid = Field<DataGridView>(view, "actions"); var tree = Field<TreeView>(view, "groups"); var search = Field<TextBox>(view, "search");
                int basicCount = workspace.Functions.Count(function => function.GroupPath[0] == "native");
                Console.WriteLine("Table: {0}x{1}, displayed={2}, first row={3}, font={4}", grid.Width, grid.Height, grid.DisplayedRowCount(false), grid.Rows[0].Height, grid.Font.SizeInPoints);
                Capture(form, Path.Combine(output, "actions-native.png"));
                Check(grid.VirtualMode && grid.ColumnCount == 4, "Actions must use one virtual aligned table");
                Check(grid.RowCount == basicCount && basicCount == 21, "Standard mode group must exactly match its 21 ordinary actions");
                Check(Field<IList>(view, "rows").Cast<object>().Take(21).All(row => Field<BindingFunction>(row, "Function").NativeAction != null), "Disabled key enhancement buries ordinary save bindings behind extra functions");
                Check(grid.DisplayedRowCount(false) >= 8, "Basic table density fits fewer than eight rows at system DPI");
                Check(grid.Columns[1].HeaderText == I18n.Str("unified.grid.factory") && grid.Columns[2].HeaderText == I18n.Str("unified.grid.bindings"), "Factory/current headers missing");
                Check(CountControls(view) < 30, "Action view creates per-function controls");
                Check(grid.Rows[0].Cells[1].Value != null, "Factory key column empty");
                foreach (string path in new[] { "locomotion", "targeting", "offense", "defense", "weapons", "shadow", "interaction", "scene", "interface" })
                {
                    var node = Find(tree.Nodes, path);
                    Check(node != null, "Key enhancement-off mode hides catalog group: " + path);
                    Check(!node.ForeColor.IsEmpty && node.ForeColor != tree.ForeColor && node.ToolTipText.Contains(I18n.Str("unified.requiresbrief")), "Inactive catalog group lacks muted state or enable hint: " + path);
                }
                tree.SelectedNode = Find(tree.Nodes, "shadow"); Application.DoEvents();
                Check(grid.RowCount > 0 && Field<Label>(view, "keyEnhancementNotice").Visible, "Inactive groups cannot be browsed or lack an enable notice");
                Capture(form, Path.Combine(output, "actions-shadow-inactive.png"));
                search.Text = "瞬身"; Application.DoEvents();
                Check(grid.RowCount > 0, "Key enhancement-off search hides stance dash");
                int dashRow = Enumerable.Range(0, grid.RowCount).First(index => Convert.ToString(grid.Rows[index].Cells[0].Value).Contains("SpecialDash"));
                var inactiveTip = (DataGridViewCellToolTipTextNeededEventArgs)Activator.CreateInstance(typeof(DataGridViewCellToolTipTextNeededEventArgs), Hidden, null, new object[] { 0, dashRow, "" }, null);
                view.GetType().GetMethod("CellTip", Hidden).Invoke(view, new object[] { view, inactiveTip });
                Check(inactiveTip.ToolTipText.Contains(I18n.Str("unified.requiresbrief")) && inactiveTip.ToolTipText.Contains("SpecialDash"), "Inactive action tooltip loses the enable hint or technical code");
                Capture(form, Path.Combine(output, "actions-search-inactive.png"));
                search.Text = ""; tree.SelectedNode = Find(tree.Nodes, "native"); Application.DoEvents();
                Check(!Field<Label>(view, "keyEnhancementNotice").Visible, "Ordinary controls incorrectly require key enhancement");
                string details = null, added = null; BindingEntry edited = null;
                view.EditAction += delegate(string id) { details = id; }; view.AddRequested += delegate(string id) { added = id; }; view.EditRequested += delegate(BindingEntry entry) { edited = entry; };
                CellClick(view, 0, 0, 12, 12); Check(details != null, "Function cell does not open action details");
                CellClick(view, 3, 0, 12, 12); Check(added == details, "Add cell does not open the add binding flow");
                var rowList = Field<IList>(view, "rows"); object first = rowList[0]; var hits = Field<IList>(first, "Hits");
                if (hits.Count > 0)
                {
                    Rectangle hit = Field<Rectangle>(hits[0], "Bounds"); CellClick(view, 2, 0, hit.X + 2, hit.Y + 2);
                    Check(edited != null && edited.FunctionId == details, "Specific binding does not open its editor");
                }
                config.Enabled = true; view.Render(workspace); Application.DoEvents();
                Capture(form, Path.Combine(output, "actions-enabled.png"));
                Check(grid.RowCount == basicCount, "Enabling key enhancement must initially retain the basic group");
                search.Text = "WASD"; Application.DoEvents();
                Check(grid.RowCount == 5 && Convert.ToString(grid.Rows[0].Cells[0].Value).Contains("向前移动"), "WASD search must lead with four directions and retain the raw keyboard axis");
                Capture(form, Path.Combine(output, "actions-wasd.png"));
                search.Text = "收刀"; Application.DoEvents();
                Check(grid.RowCount == 3 && Field<BindingFunction>(Field<IList>(view, "rows")[0], "Function").Id == "action:/SGameCombatSystem/Weapon/Input/IA_ChangeArmed", "Sheathing is missing or buried in cross-group search");
                Capture(form, Path.Combine(output, "actions-sheathe.png"));
                search.Text = ""; Application.DoEvents();
                Check(Find(tree.Nodes, "native") != null && tree.Nodes.Cast<TreeNode>().Any(node => (string)node.Tag == ""), "Standard or all-functions group missing");
                tree.SelectedNode = Find(tree.Nodes, "native"); Application.DoEvents();
                Check(grid.RowCount == 21 && Field<IList>(view, "rows").Cast<object>().All(row => !Field<BindingFunction>(row, "Function").RequiresKeyEnhancement), "Standard group mixes in enhanced functions");
                Capture(form, Path.Combine(output, "actions-basic-enabled.png"));
                tree.SelectedNode = Find(tree.Nodes, "defense"); Application.DoEvents();
                Check(grid.RowCount == 13 && Field<IList>(view, "rows").Cast<object>().Any(row => Field<BindingFunction>(row, "Function").Id == "ability:Ability.Action.Hun.SpecialDash"), "Defense group must contain all guard and dodge operations");
                Capture(form, Path.Combine(output, "actions-defense.png"));
                tree.SelectedNode = Find(tree.Nodes, "locomotion"); tree.SelectedNode.Expand(); Application.DoEvents();
                Check(grid.RowCount == 16, "Movement group omits sprint, walk, crouch, jump or movement axes");
                Capture(form, Path.Combine(output, "actions-movement.png"));
                int controlCount = CountControls(view); TreeNode originalRoot = tree.Nodes[0];
                tree.SelectedNode = originalRoot; Application.DoEvents(); Check(grid.RowCount == workspace.Functions.Length, "All-functions tree node omitted functions");
                Capture(form, Path.Combine(output, "actions-all.png"));
                var weaponA = workspace.Functions.Single(function => function.Id == "ability:Ability.Action.Hun.Heavy.SnakeDual.DB");
                var weaponB = workspace.Functions.Single(function => function.Id == "ability:Ability.Action.Hun.Heavy.BronzeSword.DB");
                Check(weaponA.GroupPath[0] == "weapons" && weaponB.GroupPath[0] == "weapons" && weaponA.GroupPath[1] != weaponB.GroupPath[1], "Different weapons share one undifferentiated group");
                var weaponNode = Find(tree.Nodes, string.Join("/", weaponA.GroupPath.Take(2))); Check(weaponNode != null && weaponNode.Text.StartsWith("白蟒赤练 ("), "Weapon group lacks its real localized identity and count");
                tree.SelectedNode = weaponNode; Application.DoEvents();
                Check(grid.RowCount > 0 && Enumerable.Range(0, grid.RowCount).All(index => Convert.ToString(grid.Rows[index].Cells[0].Value).Contains("SnakeDual")), "Weapon group mixes unrelated weapon actions");
                foreach (string ability in new[] { "ability:Ability.Action.Hun.ShachiAbility.PiPa.Skill", "ability:Ability.Action.Hun.ShachiAbility.ShachiHook.HookMove" })
                {
                    var function = workspace.Functions.Single(item => item.Id == ability); var node = Find(tree.Nodes, string.Join("/", function.GroupPath));
                    Check(node != null && node.Text.StartsWith((ability.Contains("PiPa") ? "催心琵琶" : "追命锁镰") + " ("), "Shadow weapon group missing its localized name and count");
                    tree.SelectedNode = node; Application.DoEvents();
                    Capture(form, Path.Combine(output, ability.Contains("PiPa") ? "actions-pipa.png" : "actions-hook.png"));
                    Check(grid.RowCount >= (ability.Contains("PiPa") ? 4 : 9), "Shadow weapon group omitted supported actions");
                    int index = Enumerable.Range(0, grid.RowCount).First(row => Convert.ToString(grid.Rows[row].Cells[0].Value).Contains(ability.Contains("PiPa") ? "PiPa.Skill" : "ShachiHook.HookMove"));
                    var tip = (DataGridViewCellToolTipTextNeededEventArgs)Activator.CreateInstance(typeof(DataGridViewCellToolTipTextNeededEventArgs), Hidden, null, new object[] { 0, index, "" }, null); view.GetType().GetMethod("CellTip", Hidden).Invoke(view, new object[] { view, tip });
                    Check(tip.ToolTipText.StartsWith(function.TechnicalName, StringComparison.Ordinal) && tip.ToolTipText.Contains(ability.Substring("ability:".Length)), "Technical tooltip truncates the full code");
                }
                search.Text = "天玄青铜剑"; Application.DoEvents(); Check(grid.RowCount > 0, "Parent weapon name is not searchable across groups");
                search.Text = "瞬身"; Application.DoEvents();
                Check(grid.RowCount > 0 && Enumerable.Range(0, grid.RowCount).Any(index => Convert.ToString(grid.Rows[index].Cells[0].Value).Contains("瞬身")), "Chinese search did not cross groups");
                Check(Convert.ToString(grid.Rows[0].Cells[0].Value).Contains("\n"), "Additional action label and code must occupy separate lines");
                search.Text = "SpecialDash"; Application.DoEvents(); Check(grid.RowCount > 0, "Technical-name search did not cross groups");
                search.Text = ""; Application.DoEvents();
                var extra = workspace.Functions.First(function => function.RequiresKeyEnhancement && function.GroupPath.Length > 0);
                string groupPath = string.Join("/", extra.GroupPath); var extraNode = Find(tree.Nodes, groupPath); Check(extraNode != null, "Catalog group missing from tree");
                tree.SelectedNode = extraNode; Application.DoEvents();
                Check(grid.RowCount > 0 && grid.RowCount < workspace.Functions.Length, "Group selection flattened the full catalog");
                Check(CountControls(view) == controlCount && object.ReferenceEquals(originalRoot, tree.Nodes[0]), "Changing group rebuilt the tree or controls");
                int beforeScroll = CountControls(view); if (grid.RowCount > 8) grid.FirstDisplayedScrollingRowIndex = 5; Application.DoEvents();
                Check(CountControls(view) == beforeScroll && object.ReferenceEquals(originalRoot, tree.Nodes[0]), "Scrolling rebuilt the tree or controls");
                Capture(form, Path.Combine(output, "actions-grouped.png"));
                tree.SelectedNode = Find(tree.Nodes, "native"); Application.DoEvents();
                form.Scale(new SizeF(1.5f, 1.5f)); form.ClientSize = new Size(1100, 720); Application.DoEvents();
                Check(grid.DisplayedRowCount(false) >= 8, "150% basic table density fits fewer than eight rows");
                Capture(form, Path.Combine(output, "actions-native-150.png"));
                search.Text = "SpecialDash"; Application.DoEvents(); Capture(form, Path.Combine(output, "actions-search-150.png"));
                config.Enabled = false; search.Text = ""; view.Render(workspace); Application.DoEvents();
                Check(grid.RowCount == basicCount && Find(tree.Nodes, "weapons") != null && Find(tree.Nodes, "shadow") != null, "Disabling key enhancement hides catalog groups or changes basic controls");
                tree.SelectedNode = Find(tree.Nodes, "shadow"); Application.DoEvents(); int inactiveCount = grid.RowCount;
                config.Enabled = true; view.Render(workspace); Application.DoEvents();
                var scopeNotice = Field<Label>(view, "keyEnhancementNotice");
                Check((string)tree.SelectedNode.Tag == "shadow" && grid.RowCount == inactiveCount && scopeNotice.Visible
                    && scopeNotice.Text.Contains(I18n.Str("binding.scope.global")) && scopeNotice.Text.Contains(I18n.Str("binding.scope.scheme")),
                    "Enabling key enhancement loses the selected group, its actions, or the binding scope notice");
                config.Enabled = false; view.Render(workspace); Application.DoEvents();
                Check((string)tree.SelectedNode.Tag == "shadow" && grid.RowCount == inactiveCount && Field<Label>(view, "keyEnhancementNotice").Visible,
                    "Disabling key enhancement hides the selected group or fails to show its inactive notice");
                I18n.Current = "en"; view.Render(workspace); Check(Field<Label>(view, "searchTitle").Text == "Search functions", "Search header does not follow language");
                Console.WriteLine("Initial render including UI events: {0} ms", timer.ElapsedMilliseconds);
            }
            I18n.Current = "zh-Hans";
            string sample = Path.Combine(output, "view-fixture.sav"); File.Copy("samples/vanilla-base.sav", sample, true);
            using (var main = new MainForm(new EditorPreferences(Path.Combine(output, "preferences.txt"))))
            {
                main.UiSmoke(Path.GetFullPath(sample)); main.Show(); main.ShowActionView(true); Application.DoEvents();
                Capture(main, Path.Combine(output, "main-actions-basic.png"));
                Field<KeyEnhancementConfiguration>(main, "keyEnhancement").Enabled = true; main.ShowActionView(true); Application.DoEvents();
                Capture(main, Path.Combine(output, "main-actions-enabled.png"));
                var mainView = Field<UnifiedActionView>(main, "unifiedActions");
                Check(Field<DataGridView>(mainView, "actions").DisplayedRowCount(false) >= 8, "Main form action table shows fewer than eight rows");
            }
            Console.WriteLine("UnifiedActionViewTests PASS " + checks); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static void Capture(Form form, string path)
    {
        using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(path); }
    }
}
