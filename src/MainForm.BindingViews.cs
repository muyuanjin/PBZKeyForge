using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PBZKeyForge
{
    public partial class MainForm
    {
        readonly Panel viewBar = new EditorPanel();
        readonly Panel bindingWorkspace = new EditorPanel();
        readonly EditorTab tabKeys = new EditorTab(), tabActions = new EditorTab();
        readonly ComboBox cbFamily = new EditorComboBox();
        readonly Button btnRecords = new EditorButton();
        readonly BindingHelpButton bindingHelp = new BindingHelpButton("help.slots");
        readonly KeyBindingView keyBindings = new KeyBindingView();
        readonly SchemeDiffView schemeDiff = new SchemeDiffView();
        bool actionView;
        public bool ActionView { get { return actionView; } }
        public int SelectedFamilyIndex
        { get { var family = cbFamily.SelectedItem as ControllerFamily; return family == null ? -1 : family.Index; } }

        void BuildBindingViews()
        {
            viewBar.Dock = DockStyle.Top; viewBar.Height = 44; viewBar.BackColor = EditorTheme.Floating;
            tabKeys.SetBounds(0, 0, 130, 44); tabActions.SetBounds(130, 0, 130, 44);
            tabKeys.Click += delegate { ShowActionView(false); }; tabActions.Click += delegate { ShowActionView(true); };
            cbFamily.DropDownStyle = ComboBoxStyle.DropDownList;
            cbFamily.SetBounds(276, 7, 190, 30);
            cbFamily.SelectedIndexChanged += delegate { if (!loading) RenderSession(); };
            btnRecords.SetBounds(1040, 5, 136, 34); btnRecords.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            btnRecords.Click += delegate { ShowRecords(); };
            viewBar.Controls.AddRange(new Control[] { tabKeys, tabActions, cbFamily, btnRecords, bindingHelp });
            viewBar.Layout += delegate
            {
                float scale = CurrentAutoScaleDimensions.Width / 96f;
                btnRecords.SetBounds(viewBar.Width - (int)(140 * scale), (int)(5 * scale), (int)(132 * scale), (int)(34 * scale));
                bindingHelp.SetBounds(viewBar.Width - (int)(178 * scale), (int)(5 * scale), (int)(30 * scale), (int)(34 * scale));
            };
            keyBindings.EditAction += delegate(string action) { ShowActionRelations(action); };
            keyBindings.EditRelation += delegate(string action, string key, bool add)
            {
                if (Session == null || Session.IsResetPending) return;
                TryAction(delegate { Session.ChangeRelationship(action, key, add, gamepad, SelectedFamilyIndex); });
            };
            keyBindings.ActionViewRequested += delegate { ShowActionView(true); };
            keyBindings.RestoreSelectedKey += delegate(string key)
            {
                if (Session != null) TryAction(delegate {
                    foreach (string inputKey in BindingViewText.InputKeys(key)) Session.RestoreKey(inputKey, gamepad, SelectedFamilyIndex);
                });
            };
            bindingWorkspace.Dock = DockStyle.Fill;
            bindingWorkspace.Controls.Add(kbHost); bindingWorkspace.Controls.Add(padHost); bindingWorkspace.Controls.Add(keyBindings);
            bindingWorkspace.Controls.Add(schemeDiff);
            bindingWorkspace.Layout += delegate
            { schemeDiff.Height = (int)(138 * CurrentAutoScaleDimensions.Width / 96f); };
            body.Controls.Add(bindingWorkspace); body.Controls.Add(viewBar);
        }

        public void ShowActionView(bool show)
        { actionView = show; RenderSession(); }

        void RenderBindingViews()
        {
            bool loaded = Session != null;
            kbHost.Visible = loaded && actionView && !gamepad;
            padHost.Visible = loaded && actionView && gamepad;
            bindingWorkspace.Visible = loaded;
            viewBar.Visible = loaded; keyBindings.Visible = loaded && !actionView;
            tabKeys.Text = I18n.Str("views.keyview"); tabActions.Text = I18n.Str("views.actionview");
            tabKeys.Selected = !actionView; tabActions.Selected = actionView;
            btnRecords.Text = I18n.Str("views.records");
            btnRecords.Visible = actionView;
            bindingHelp.RefreshText();
            int selectedFamily = SelectedFamilyIndex;
            var previousFamily = cbFamily.SelectedItem as ControllerFamily;
            var families = loaded ? Session.ControllerFamilies : new ControllerFamily[0];
            if (!cbFamily.Items.Cast<ControllerFamily>().Select(f => f.Index + "\u001f" + f.Name)
                .SequenceEqual(families.Select(f => f.Index + "\u001f" + f.Name)))
            {
                cbFamily.Items.Clear(); foreach (var family in families) cbFamily.Items.Add(family);
                cbFamily.SelectedIndex = Array.FindIndex(families, f => f.Index == selectedFamily);
                if (cbFamily.SelectedIndex < 0 && previousFamily != null)
                    cbFamily.SelectedIndex = Array.FindIndex(families, f => f.Name == previousFamily.Name);
                if (cbFamily.SelectedIndex < 0 && cbFamily.Items.Count > 0) cbFamily.SelectedIndex = 0;
            }
            cbFamily.Visible = gamepad; cbFamily.Enabled = loaded && families.Length > 0;
            cbFamily.AccessibleName = I18n.Str("editor.gamepad");
            viewBar.PerformLayout();
            if (loaded)
            {
                var snapshots = Session.BindingSnapshots();
                var selected = snapshots.Single(s => s.Gamepad == gamepad && (!gamepad || s.FamilyIndex == SelectedFamilyIndex));
                if (actionView) RenderActionTable(selected);
                else keyBindings.Render(Session, selected);
                schemeDiff.Render(Session, snapshots);
            }
        }

        void ShowActionRelations(string action)
        {
            if (Session == null) return;
            modalActive = true;
            try
            {
                using (var dialog = new RelationEditorDialog(Session, action, gamepad, SelectedFamilyIndex,
                    delegate { RenderSession(); }, delegate { ShowActionView(true); })) dialog.ShowDialog(this);
            }
            finally { modalActive = false; RenderSession(); }
        }

        void ShowRecords()
        {
            if (Session == null) return;
            modalActive = true;
            try
            {
                using (var dialog = new BindingRecordsDialog(Session, gamepad, SelectedFamilyIndex, delegate { RenderSession(); })) dialog.ShowDialog(this);
            }
            finally { modalActive = false; RenderSession(); }
        }
    }
}
