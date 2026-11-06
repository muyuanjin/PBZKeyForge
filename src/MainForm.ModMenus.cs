using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PBZKeyForge
{
    public partial class MainForm
    {
        readonly EditorButton btnKeyEnhancementMore = new EditorButton();
        readonly EditorButton btnResourceModMore = new EditorButton();
        ContextMenuStrip keyEnhancementMenu, resourceModMenu;

        ContextMenuStrip ModMenu()
        {
            return new ContextMenuStrip { Font = Font, BackColor = EditorTheme.Floating, ForeColor = EditorTheme.Ink,
                Renderer = new EditorMenuRenderer(), ShowImageMargin = false, Padding = new Padding(5) };
        }

        void BuildModMenus()
        {
            btnKeyEnhancementMore.Text = "…"; btnResourceModMore.Text = "…";
            btnKeyEnhancementMore.AccessibleName = I18n.Str("enhancement.title") + " · " + I18n.Str("component.options");
            btnResourceModMore.AccessibleName = I18n.Str("mod.title") + " · " + I18n.Str("component.options");
            btnKeyEnhancementMore.Click += delegate { ShowKeyEnhancementMenu(); };
            btnResourceModMore.Click += delegate { ShowResourceModMenu(); };
            keyEnhancementBar.Controls.Add(btnKeyEnhancementMore);
            footer.Controls.Add(btnResourceModMore); btnResourceModMore.BringToFront();
        }

        ContextMenuStrip CreateKeyEnhancementMenu()
        {
            var menu = ModMenu();
            menu.Items.Add(I18n.Str("enhancement.help"), null, delegate { ShowKeyEnhancementHelp(); }).Name = "enhancement.help";
            menu.Items.Add(I18n.Str("enhancement.flames"), null, delegate { TryAction(ShowAppearanceSettings); }).Name = "enhancement.flames";
            menu.Items.Add(I18n.Str("component.openfolder"), null, delegate { TryAction(delegate { OpenComponentFolder(keyEnhancementInstallation.InstallDirectory); }); }).Name = "component.openfolder";
            menu.Items.Add(I18n.Str("ue4ss.title"), null, delegate { ManageRuntimeUpdate(); }).Name = "ue4ss.title";
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(I18n.Str("enhancement.lifecycle.uninstall"), null, delegate {
                if (keyEnhancementInstallation == null) return;
                RememberBindingEdit();
                keyEnhancement.Enabled = false; keyEnhancementUninstall = true; RenderSession();
            }).Name = "enhancement.lifecycle.uninstall";
            return menu;
        }

        void ShowKeyEnhancementMenu()
        {
            cbPreset.DroppedDown = false;
            if (keyEnhancementMenu == null) keyEnhancementMenu = CreateKeyEnhancementMenu();
            foreach (ToolStripItem item in keyEnhancementMenu.Items)
                if (item is ToolStripMenuItem) item.Text = I18n.Str(item.Name);
            keyEnhancementMenu.Items["component.openfolder"].Enabled = keyEnhancementInstallation != null;
            keyEnhancementMenu.Items["enhancement.lifecycle.uninstall"].Enabled = keyEnhancementInstallation != null && !IsApplying;
            keyEnhancementMenu.Items["ue4ss.title"].Enabled = keyEnhancementInstallation != null && !IsApplying;
            keyEnhancementMenu.Show(btnKeyEnhancementMore, Point.Empty, ToolStripDropDownDirection.AboveRight);
        }

        ContextMenuStrip CreateResourceModMenu()
        {
            var menu = ModMenu();
            menu.Items.Add(I18n.Str("mod.help"), null, delegate { ShowResourceModHelp(); }).Name = "mod.help";
            menu.Items.Add(I18n.Str("component.openfolder"), null, delegate { TryAction(delegate { OpenComponentFolder(footerMod.InstallDirectory); }); }).Name = "component.openfolder";
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(I18n.Str("component.uninstall"), null, delegate {
                if (footerMod == null) return;
                RememberBindingEdit();
                resourceModDraft = false; RenderSession();
            }).Name = "component.uninstall";
            return menu;
        }

        void ShowResourceModMenu()
        {
            cbPreset.DroppedDown = false;
            if (resourceModMenu == null) resourceModMenu = CreateResourceModMenu();
            foreach (ToolStripItem item in resourceModMenu.Items)
                if (item is ToolStripMenuItem) item.Text = I18n.Str(item.Name);
            resourceModMenu.Items["component.openfolder"].Enabled = footerMod != null;
            resourceModMenu.Items["component.uninstall"].Enabled = footerMod != null && !IsApplying;
            resourceModMenu.Show(btnResourceModMore, Point.Empty, ToolStripDropDownDirection.AboveRight);
        }

        static void OpenComponentFolder(string directory)
        {
            while (!Directory.Exists(directory))
            {
                directory = Path.GetDirectoryName(directory);
                if (string.IsNullOrEmpty(directory)) return;
            }
            Process.Start("explorer.exe", "\"" + directory + "\"");
        }
    }
}
