using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PBZKeyForge
{
    public static class AgentPrompt
    {
        public static string Create(string directory, string source, string snapshot, string selected, string active, string device, bool running, bool enhanced)
        {
            string cli = Path.Combine(directory, "PBZKeyForge-cli.exe");
            string input = snapshot ?? source ?? "new";
            string selection = (selected == null ? "" : " --scheme \"" + selected + "\"")
                + (device == null ? "" : " --device \"" + device + "\"");
            return I18n.Str("agent.task") + "\r\n\r\n"
                + "PBZKeyForge: " + Program.Version + "\r\nCLI: " + cli + "\r\n"
                + "Source: " + (source ?? "new") + "\r\nWorking input: " + input + "\r\n"
                + "Selected scheme: " + (selected ?? "unknown") + "\r\nActive game scheme: " + (active ?? "unknown") + "\r\n"
                + "Device: " + device + "\r\nGame running: " + running + "\r\nEnhancement enabled: " + enhanced + "\r\n\r\n"
                + "User interface language: " + I18n.Current + ". Respond to the user in this language.\r\n\r\n"
                + I18n.Str("agent.workflow") + "\r\n"
                + "& \"" + cli + "\" --version --json\r\n"
                + "& \"" + cli + "\" --agent-help\r\n"
                + "& \"" + cli + "\" --inspect \"" + input + "\"" + selection + " --json\r\n"
                + "& \"" + cli + "\" --catalog \"" + input + "\"" + selection + " --json\r\n"
                + "& \"" + cli + "\" --keys --json\r\n\r\n"
                + (snapshot == null ? "" : I18n.Str("agent.snapshot") + "\r\n")
                + I18n.Str("agent.apply") + "\r\n\r\n"
                + "Runtime readiness: --runtime-status <installed.sav> --json. Key enhancement: --component <installed.sav> --action status|enable|disable|uninstall. Gameplay Mod: --gameplay-mod <installed.sav> --action status|enable|disable. Component writes require the game to be closed. For live binding edits, inspect readiness, preview with --live --dry-run, then apply with --live.\r\n"
                + "Use --version source metadata to obtain the matching source revision and documentation. If source.exact=false, obtain local modifications from the builder; the latest branch may differ from this executable.\r\n\r\n"
                + Path.Combine(directory, "docs", "cli.md") + "\r\n" + ProjectLinks.Project;
        }
    }

    internal sealed class AgentPromptDialog : EditorWindow
    {
        public readonly TextBox Prompt;
        public AgentPromptDialog(string prompt)
        {
            Text = I18n.Str("ai.title"); Font = new Font("Microsoft YaHei UI", 10f); AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96); BackColor = EditorTheme.Background; ForeColor = EditorTheme.Ink;
            ClientSize = new Size(820, 620); MinimumSize = new Size(620, 460); StartPosition = FormStartPosition.CenterParent;
            var introduction = new Label { Dock = DockStyle.Top, Height = 72, Padding = new Padding(18, 12, 18, 4),
                Text = I18n.Str("agent.intro"), ForeColor = EditorTheme.Muted, UseMnemonic = false };
            var heading = new Label { Dock = DockStyle.Top, Height = 30, Padding = new Padding(18, 3, 18, 3),
                Text = I18n.Str("agent.preview"), ForeColor = EditorTheme.KeyEnhancement, UseMnemonic = false };
            var frame = new EditorPanel { Dock = DockStyle.Fill, Padding = new Padding(18, 6, 18, 10), BackColor = EditorTheme.Surface };
            Prompt = new TextBox { Name = "agentPrompt", Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = EditorTheme.Floating,
                ForeColor = EditorTheme.Ink, Text = prompt, HideSelection = true };
            frame.Controls.Add(Prompt);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 68, Padding = new Padding(12, 10, 12, 12),
                FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            var copy = new EditorButton { Name = "copyPrompt", Text = I18n.Str("agent.copy"), Width = 210, Height = 42,
                Primary = true, AccentColor = EditorTheme.KeyEnhancementStrong, ForeColor = EditorTheme.Ink };
            var close = new EditorButton { Text = I18n.Str("editor.close"), Name = "close", AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(78, 34), Margin = new Padding(4), Height = 42 };
            copy.Click += delegate { Clipboard.SetText(Prompt.Text); copy.Text = I18n.Str("agent.copied"); };
            close.Click += delegate { Close(); };
            footer.Controls.Add(copy); footer.Controls.Add(close);
            Controls.Add(frame); Controls.Add(heading); Controls.Add(introduction); Controls.Add(footer);
            AcceptButton = copy; CancelButton = close;
            Shown += delegate { Prompt.Select(0, 0); copy.Focus(); };
        }
    }
}
