using System;
using System.Drawing;
using System.Collections.Generic;
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
        readonly UnifiedActionView unifiedActions = new UnifiedActionView();
        BindingWorkspace currentBindings;
        readonly List<BindingWorkspace> bindingWorkspaces = new List<BindingWorkspace>();
        KeyEnhancementConfiguration workspaceBaseline;
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
            keyBindings.EditBindingRequested += delegate(BindingEntry entry) { EditActionBinding(entry.FunctionId, entry); };
            keyBindings.RemoveBindingRequested += delegate(BindingEntry entry) { TryAction(delegate { currentBindings.Remove(entry); }); };
            keyBindings.RestoreBindingRequested += delegate(BindingEntry entry) { TryAction(delegate { currentBindings.Restore(entry); }); };
            keyBindings.EditRelation += delegate(string action, string key, bool add)
            {
                if (Session == null || Session.IsResetPending) return;
                TryAction(delegate {
                    if (currentBindings == null) return;
                    if (add) currentBindings.Add(action, new BindingEntry { Keys = new[] { key }, Trigger = "default" });
                    else currentBindings.RemoveRelationship(action, key);
                });
            };
            keyBindings.MoveSelectedKey += delegate(string key, bool swap) {
                if (Session == null || currentBindings == null) return;
                modalActive = true;
                try
                {
                    using (var dialog = new BindingDialog(I18n.Str(swap ? "editing.swap" : "editing.move"), gamepad, key, new string[0], false))
                        if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dialog.Result) && dialog.Result != "None")
                            TryAction(delegate { currentBindings.MoveKey(key, dialog.Result, swap); });
                }
                finally { EndModal(); RenderSession(); }
            };
            keyBindings.AddFunctionRequested += AddFunctionToKey;
            unifiedActions.EditAction += ShowActionRelations;
            unifiedActions.EditRequested += delegate(BindingEntry entry) { EditActionBinding(entry.FunctionId, entry); };
            unifiedActions.AddRequested += delegate(string action) { EditActionBinding(action, null); };
            keyBindings.ActionViewRequested += delegate { ShowActionView(true); };
            keyBindings.RestoreSelectedKey += delegate(string key)
            {
                if (Session != null) TryAction(delegate {
                    if (currentBindings != null) currentBindings.RestoreKey(key);
                });
            };
            bindingWorkspace.Dock = DockStyle.Fill;
            bindingWorkspace.Controls.Add(kbHost); bindingWorkspace.Controls.Add(padHost); bindingWorkspace.Controls.Add(keyBindings);
            bindingWorkspace.Controls.Add(schemeDiff);
            unifiedActions.Dock = DockStyle.Fill; bindingWorkspace.Controls.Add(unifiedActions);
            bindingWorkspace.Layout += delegate
            {
                float scale = CurrentAutoScaleDimensions.Width / 96f;
                schemeDiff.Height = Math.Max((int)((actionView ? 48 : 64) * scale), Math.Min((int)((actionView ? 56 : 138) * scale), bindingWorkspace.ClientSize.Height / 4));
            };
            body.Controls.Add(bindingWorkspace); body.Controls.Add(viewBar);
        }

        public void ShowActionView(bool show)
        { actionView = show; RenderSession(); }

        void RenderBindingViews()
        {
            bool loaded = Session != null;
            kbHost.Visible = false;
            padHost.Visible = false;
            unifiedActions.Visible = loaded && actionView;
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
            bindingWorkspace.PerformLayout();
            if (loaded)
            {
                if (workspaceBaseline != keyEnhancementBaseline || bindingWorkspaces.Any(workspace => workspace.Session != Session || workspace.Configuration != keyEnhancement))
                    bindingWorkspaces.Clear();
                workspaceBaseline = keyEnhancementBaseline;
                currentBindings = ViewWorkspace(gamepad, SelectedFamilyIndex);
                keyBindings.Workspace = currentBindings;
                var scopes = new[] { ViewWorkspace(false, -1) }.Concat(Session.ControllerFamilies.Select(family => ViewWorkspace(true, family.Index))).ToArray();
                var snapshots = scopes.Select(workspace => workspace.Snapshot()).ToArray();
                keyboardBindingChanges = snapshots.Where(snapshot => !snapshot.Gamepad).SelectMany(snapshot => snapshot.Differences(false)).Select(change => change.Action).Distinct().Count();
                gamepadBindingChanges = snapshots.Where(snapshot => snapshot.Gamepad).SelectMany(snapshot => snapshot.Differences(false)).Select(change => change.Action).Distinct().Count();
                if (actionView) { unifiedActions.Render(currentBindings); unifiedActions.BringToFront(); }
                else { keyBindings.Render(Session, snapshots.First(snapshot => snapshot.Gamepad == gamepad && (!gamepad || snapshot.FamilyIndex == SelectedFamilyIndex))); keyBindings.BringToFront(); }
                schemeDiff.Render(Session, snapshots);
                bindingWorkspace.PerformLayout();
            }
        }

        BindingWorkspace ViewWorkspace(bool pad, int family)
        {
            if (bindingWorkspaces.Any(item => item.Configuration != keyEnhancement || item.Session != Session) || workspaceBaseline != keyEnhancementBaseline)
                bindingWorkspaces.Clear();
            workspaceBaseline = keyEnhancementBaseline;
            var workspace = bindingWorkspaces.FirstOrDefault(item => item.Gamepad == pad && item.FamilyIndex == family);
            if (workspace != null) return workspace;
            workspace = new BindingWorkspace(Session, keyEnhancement, keyEnhancementBaseline, pad, family);
            workspace.BeforeChange = RememberBindingEdit;
            workspace.LiveEditingProvider = delegate {
                return !Session.IsOffline && lastRunning && keyEnhancement.Enabled && keyEnhancementBaseline.Enabled;
            };
            bindingWorkspaces.Add(workspace);
            return workspace;
        }

        void ShowActionRelations(string action)
        {
            if (Session == null) return;
            if (currentBindings.LiveEditing)
            {
                PresentBindingWindow(new UnifiedActionBindingsDialog(currentBindings, action, EnableKeyEnhancementEditing, delegate { RenderSession(); }), this, null);
                return;
            }
            modalActive = true;
            try
            {
                using (var dialog = new UnifiedActionBindingsDialog(currentBindings, action, EnableKeyEnhancementEditing, delegate { RenderSession(); })) dialog.ShowDialog(this);
            }
            finally { EndModal(); RenderSession(); }
        }

        bool EnableKeyEnhancementEditing()
        {
            keyEnhancement.Enabled = true;
            keyEnhancementUninstall = false;
            return true;
        }

        void EditActionBinding(string action, BindingEntry entry)
        {
            if (Session == null || currentBindings == null || Session.IsResetPending) return;
            if (currentBindings.LiveEditing)
            {
                var editor = new UnifiedBindingEditDialog(currentBindings, action, entry, null, EnableKeyEnhancementEditing);
                PresentBindingWindow(editor, this, delegate {
                    if (editor.DialogResult == DialogResult.OK)
                        TryAction(delegate { if (editor.RemoveRequested && editor.CurrentBinding != null) currentBindings.Remove(editor.CurrentBinding); });
                });
                return;
            }
            modalActive = true;
            try
            {
                using (var dialog = new UnifiedBindingEditDialog(currentBindings, action, entry, null, EnableKeyEnhancementEditing))
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        TryAction(delegate {
                            if (dialog.RemoveRequested) currentBindings.Remove(entry);
                            else if (entry == null) currentBindings.Add(action, dialog.Result);
                            else currentBindings.Replace(entry, dialog.Result);
                        });
            }
            finally { EndModal(); RenderSession(); }
        }

        void AddFunctionToKey(string key)
        {
            if (currentBindings == null || Session == null) return;
            modalActive = true;
            try
            {
                using (var dialog = new UnifiedActionChoiceDialog(currentBindings, key, EnableKeyEnhancementEditing))
                    dialog.ShowDialog(this);
            }
            finally { EndModal(); RenderSession(); }
        }

        void ShowRecords()
        {
            if (Session == null) return;
            modalActive = true;
            try
            {
                using (var dialog = new BindingRecordsDialog(Session, gamepad, SelectedFamilyIndex, delegate { RenderSession(); })) dialog.ShowDialog(this);
            }
            finally { EndModal(); RenderSession(); }
        }
    }
}
