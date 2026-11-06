using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PBZKeyForge
{
    internal sealed class ResourceModToggle : CheckBox
    {
        public struct HeaderLayout
        {
            public Rectangle Title, Badge, Options;
        }
        public string Detail;
        public bool Compact;
        public Rectangle DetailBounds { get; private set; }
        public ResourceModToggle()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand; AutoSize = false; TabStop = true;
        }
        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        public HeaderLayout MeasureHeader(Graphics graphics)
        {
            float scale = graphics.DpiX / 96f;
            int textLeft = (int)(24 * scale), top = (int)(5 * scale);
            int badgeGap = (int)(6 * scale), helpGap = (int)(4 * scale), helpWidth = (int)(24 * scale);
            int badgeWidth;
            using (var badgeFont = new Font(Font.FontFamily, 8f))
                badgeWidth = TextRenderer.MeasureText(graphics, I18n.Str("mod.badge"), badgeFont).Width + (int)(8 * scale);
            int titleWidth = Math.Min(TextRenderer.MeasureText(graphics, Text, Font).Width,
                Math.Max(1, Width - textLeft - badgeWidth - badgeGap - helpGap - helpWidth - (int)(28 * scale)));
            var title = new Rectangle(textLeft, top, titleWidth, (int)(22 * scale));
            var badge = new Rectangle(title.Right + badgeGap, top + (int)(2 * scale), badgeWidth, (int)(18 * scale));
            return new HeaderLayout { Title = title, Badge = badge,
                Options = new Rectangle(badge.Right + helpGap, (int)(4 * scale), helpWidth, (int)(24 * scale)) };
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            float scale = e.Graphics.DpiX / 96f;
            int box = (int)(16 * scale), textLeft = (int)(24 * scale), top = (int)(5 * scale);
            Color accent = Color.FromArgb(143, 165, 184);
            EditorArtwork.ClearSurface(this, e.Graphics, BackColor);
            var check = new RectangleF(1, top + 3 * scale, box, box);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var shape = EditorArtwork.Rounded(check, 3 * scale))
            using (var brush = new SolidBrush(Checked ? EditorTheme.Line : EditorTheme.Floating))
            using (var pen = new Pen(Enabled ? accent : EditorTheme.Line))
            { e.Graphics.FillPath(brush, shape); e.Graphics.DrawPath(pen, shape); }
            if (Checked)
                using (var pen = new Pen(EditorTheme.Ink, 2 * scale))
                    e.Graphics.DrawLines(pen, new[] { new PointF(4 * scale, check.Top + 8 * scale),
                        new PointF(7 * scale, check.Top + 11 * scale), new PointF(13 * scale, check.Top + 5 * scale) });
            var header = MeasureHeader(e.Graphics);
            using (var badgeFont = new Font(Font.FontFamily, 8f))
            {
                string badge = I18n.Str("mod.badge");
                TextRenderer.DrawText(e.Graphics, Text, Font, header.Title, Enabled ? EditorTheme.Ink : EditorTheme.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                using (var shape = EditorArtwork.Rounded(header.Badge, 3 * scale))
                using (var pen = new Pen(Color.FromArgb(66, 83, 96))) e.Graphics.DrawPath(pen, shape);
                TextRenderer.DrawText(e.Graphics, badge, badgeFont, header.Badge, accent,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);
            }
            if (!Compact) using (var detailFont = new Font(Font.FontFamily, 9f))
            {
                DetailBounds = new Rectangle(textLeft, (int)(31 * scale), Math.Max(1, Width - textLeft), Math.Max(1, Height - (int)(31 * scale)));
                TextRenderer.DrawText(e.Graphics, Detail, detailFont, DetailBounds, EditorTheme.Muted,
                    TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
            }
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, header.Title);
        }
    }

    public partial class MainForm
    {
        readonly ResourceModToggle chkPerfectDeflectNoRapidClickPenalty = new ResourceModToggle();
        PerfectDeflectNoRapidClickPenaltyMod footerMod;
        EditorSession footerModSession;
        string footerModError;
        ResourceModState footerModState;
        bool footerModReady, footerModInitialized, renderingResourceMod;
        bool? resourceModDraft;
        bool resourceModBaseline;

        bool ResourceModPending { get { return resourceModDraft.HasValue; } }
        bool ResourceModEnabled { get { return resourceModDraft ?? resourceModBaseline; } }

        void RefreshFooterResourceMod(bool force = false)
        {
            string input = Session != null && !Session.IsOffline ? Session.InputPath : null;
            if (!footerModInitialized || footerModSession != Session || force)
            {
                if (footerModSession != Session)
                {
                    resourceModDraft = null; footerModState = ResourceModState.Disabled;
                    resourceModBaseline = false;
                }
                footerModInitialized = true; footerModSession = Session;
                footerMod = null; footerModReady = false; footerModError = null;
                if (input != null)
                {
                    try
                    {
                        footerMod = new PerfectDeflectNoRapidClickPenaltyMod(PerfectDeflectNoRapidClickPenaltyMod.FindGameDirectory(input));
                        footerModState = footerMod.ReadState(); footerModReady = footerMod.HasTargetResource();
                        if (footerModState != ResourceModState.Conflict)
                            resourceModBaseline = footerModState == ResourceModState.Enabled || footerModState == ResourceModState.Incomplete;
                        if (resourceModDraft == resourceModBaseline
                            && (footerModState == ResourceModState.Enabled || footerModState == ResourceModState.Disabled)) resourceModDraft = null;
                        if (!resourceModDraft.HasValue && footerModState == ResourceModState.Incomplete) resourceModDraft = true;
                    }
                    catch (IOException ex) { footerModError = ex.Message; }
                    catch (UnauthorizedAccessException ex) { footerModError = ex.Message; }
                    catch (ArgumentException ex) { footerModError = ex.Message; }
                }
            }
            renderingResourceMod = true;
            try { chkPerfectDeflectNoRapidClickPenalty.Checked = ResourceModEnabled; }
            finally { renderingResourceMod = false; }
            chkPerfectDeflectNoRapidClickPenalty.Text = I18n.Str("mod.checkbox");
            chkPerfectDeflectNoRapidClickPenalty.Detail = I18n.Str("mod.warning");
            btnResourceModMore.AccessibleName = I18n.Str("mod.title") + " · " + I18n.Str("component.options");
            chkPerfectDeflectNoRapidClickPenalty.AccessibleName = chkPerfectDeflectNoRapidClickPenalty.Text;
            chkPerfectDeflectNoRapidClickPenalty.AccessibleDescription = I18n.Str(ResourceModEnabled ? "mod.enabled" : "mod.disabled")
                + (ResourceModPending ? " · " + I18n.Str("views.pending") : "") + "\n" + chkPerfectDeflectNoRapidClickPenalty.Detail;
            SetTip(chkPerfectDeflectNoRapidClickPenalty, "");
            UpdateResourceModAvailability(); chkPerfectDeflectNoRapidClickPenalty.Invalidate();
        }

        void ShowResourceModHelp()
        {
            string body = I18n.Str("mod.warning") + "\n\n" + I18n.Str("mod.protectionhelp") + "\n\n"
                + I18n.Str("mod.drafthint") + " " + I18n.Str("mod.note");
            string issue = footerModError ?? (footerModState == ResourceModState.Incomplete ? I18n.Str("mod.incomplete")
                : footerModState == ResourceModState.Conflict ? I18n.Str("mod.conflict")
                : footerMod != null && !footerModReady ? I18n.Str("mod.unsupported") : null);
            if (!string.IsNullOrEmpty(issue)) body += "\n\n" + issue;
            modalActive = true;
            try
            {
                using (var dialog = new BindingHelpDialog(I18n.Str("mod.title") + " · " + I18n.Str("mod.help"), body))
                    dialog.ShowDialog(this);
            }
            finally { EndModal(); }
        }

        void UpdateResourceModAvailability()
        {
            chkPerfectDeflectNoRapidClickPenalty.Enabled = !IsApplying && footerMod != null;
        }

        void StageResourceMod()
        {
            if (loading || renderingResourceMod) return;
            RememberBindingEdit();
            bool enabled = chkPerfectDeflectNoRapidClickPenalty.Checked;
            resourceModDraft = enabled != resourceModBaseline || enabled && footerModState == ResourceModState.Incomplete ? (bool?)enabled : null;
            DiscardUnchangedBindingEdit();
            RenderSession();
        }
    }
}
