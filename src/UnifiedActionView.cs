using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PBZKeyForge
{
    public sealed class UnifiedActionView : Panel
    {
        sealed class ActionRow
        {
            internal BindingFunction Function;
            internal BindingEntry[] Entries;
            internal string Factory, Code;
            internal string[] BindingTexts;
            internal readonly List<BindingHit> Hits = new List<BindingHit>();
            internal int Height;
        }
        sealed class BindingHit
        {
            internal Rectangle Bounds;
            internal int Index;
        }
        sealed class ActionGrid : DataGridView
        {
            internal ActionGrid() { DoubleBuffered = true; }
            protected override void PaintBackground(Graphics graphics, Rectangle clipBounds, Rectangle gridBounds)
            {
                base.PaintBackground(graphics, clipBounds, gridBounds);
                var state = graphics.Save();
                graphics.SetClip(clipBounds, System.Drawing.Drawing2D.CombineMode.Intersect);
                EditorArtwork.PaintEffect(this, graphics);
                graphics.Restore(state);
            }
        }

        sealed class ActionTree : TreeView
        {
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                // Native TreeView buffering includes owner-drawn rows and their backgrounds.
                SendMessage(Handle, 0x112C, new IntPtr(4), new IntPtr(4)); // TVS_EX_DOUBLEBUFFER
            }

            protected override void WndProc(ref Message message)
            {
                base.WndProc(ref message);
                if (message.Msg != 0x000F && message.Msg != 0x0318) return;
                using (var graphics = message.Msg == 0x0318 && message.WParam != IntPtr.Zero
                    ? Graphics.FromHdc(message.WParam) : Graphics.FromHwnd(Handle))
                {
                    var state = graphics.Save();
                    TreeNode node = TopNode;
                    while (node != null)
                    {
                        Rectangle bounds = node.Bounds;
                        if (bounds.Top >= ClientSize.Height) break;
                        graphics.ExcludeClip(new Rectangle(0, bounds.Top, ClientSize.Width, bounds.Height));
                        node = node.NextVisibleNode;
                    }
                    EditorArtwork.PaintEffect(this, graphics);
                    graphics.Restore(state);
                }
            }
        }

        readonly TextBox search = UnifiedBindingUi.Search();
        readonly Label searchTitle = UnifiedBindingUi.Label("");
        readonly TableLayoutPanel root = UnifiedBindingUi.Grid(1);
        readonly TreeView groups = new ActionTree();
        readonly DataGridView actions = new ActionGrid();
        readonly SplitContainer split = new SplitContainer();
        readonly Label empty = UnifiedBindingUi.Label("");
        readonly Label keyEnhancementNotice = UnifiedBindingUi.Label("");
        readonly ToolTip scopeTip = new ToolTip();
        readonly HashSet<string> keyEnhancementGroups = new HashSet<string>();
        readonly List<ActionRow> rows = new List<ActionRow>();
        ActionRow[] allRows = new ActionRow[0];
        BindingWorkspace workspace;
        string groupSignature;
        bool updatingGroups;
        Font codeFont;
        float dpiScale = 1f;
        bool sizedSplitter;
        public event Action<string> EditAction;
        public event Action<BindingEntry> EditRequested;
        public event Action<string> AddRequested;

        protected override void OnPaintBackground(PaintEventArgs e)
        { base.OnPaintBackground(e); EditorArtwork.PaintEffect(this, e.Graphics); }

        public UnifiedActionView()
        {
            Dock = DockStyle.Fill; BackColor = EditorTheme.Surface; Padding = new Padding(8, 4, 8, 4);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var searchBar = UnifiedBindingUi.Grid(2); searchBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); searchBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            search.Dock = DockStyle.None; search.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            searchTitle.AutoSize = true; searchTitle.MinimumSize = new Size(80, 0); searchBar.Controls.Add(searchTitle, 0, 0); searchBar.Controls.Add(search, 1, 0); root.Controls.Add(searchBar, 0, 0);
            split.Dock = DockStyle.Fill; split.BackColor = EditorTheme.Line; split.FixedPanel = FixedPanel.Panel1; split.SplitterWidth = 1;
            split.Panel1.Paint += delegate(object sender, PaintEventArgs e) { EditorArtwork.PaintEffect(split.Panel1, e.Graphics); };
            split.Panel2.Paint += delegate(object sender, PaintEventArgs e) { EditorArtwork.PaintEffect(split.Panel2, e.Graphics); };
            actions.Scroll += delegate { actions.Invalidate(); };
            split.Size = new Size(800, 400); split.SplitterDistance = 180; split.Panel1MinSize = 100; split.Panel2MinSize = 200;
            groups.Dock = DockStyle.Fill; groups.BorderStyle = BorderStyle.None; groups.BackColor = EditorTheme.Surface; groups.ForeColor = EditorTheme.Ink;
            groups.HideSelection = false; groups.FullRowSelect = true; groups.ShowLines = false; groups.ShowRootLines = true; groups.ShowNodeToolTips = true;
            groups.DrawMode = TreeViewDrawMode.OwnerDrawAll; groups.DrawNode += DrawGroup;
            groups.AfterSelect += delegate { if (!updatingGroups) FilterRows("group"); };
            groups.HandleCreated += delegate { EditorArtwork.DarkScrollbars(groups); };
            split.Panel1.Controls.Add(groups);
            ConfigureGrid(); split.Panel2.Controls.Add(actions);
            empty.BackColor = EditorTheme.Surface; empty.ForeColor = EditorTheme.Muted; empty.TextAlign = ContentAlignment.MiddleCenter; empty.Dock = DockStyle.Fill; empty.Visible = false;
            keyEnhancementNotice.ForeColor = EditorTheme.KeyEnhancement; keyEnhancementNotice.Padding = new Padding(26, 0, 4, 0); keyEnhancementNotice.Visible = false;
            keyEnhancementNotice.Paint += delegate(object sender, PaintEventArgs e) { UnifiedBindingUi.PaintKeyEnhancementMark(e.Graphics, new Rectangle(Scale(5), (keyEnhancementNotice.Height - Scale(16)) / 2, Scale(16), Scale(16))); };
            root.Controls.Add(keyEnhancementNotice, 0, 1);
            split.Panel2.Controls.Add(empty); root.Controls.Add(split, 0, 2); Controls.Add(root);
            search.TextChanged += delegate { FilterRows("search"); };
            actions.SizeChanged += delegate { MeasureRows(); };
            split.SizeChanged += delegate { if (!sizedSplitter) SizeSplitter(dpiScale); };
            FontChanged += delegate { UpdateScale(); };
            HandleCreated += delegate { UpdateScale(); };
            SizeChanged += delegate { LayoutNotice(); };
        }

        void ConfigureGrid()
        {
            actions.Name = "actionGrid"; actions.Dock = DockStyle.Fill; actions.VirtualMode = true; actions.ReadOnly = true;
            actions.AllowUserToAddRows = false; actions.AllowUserToDeleteRows = false; actions.AllowUserToResizeRows = false;
            actions.AllowUserToOrderColumns = false; actions.RowHeadersVisible = false; actions.MultiSelect = false; actions.SelectionMode = DataGridViewSelectionMode.CellSelect;
            actions.AutoGenerateColumns = false; actions.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None; actions.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            actions.BorderStyle = BorderStyle.None; actions.BackgroundColor = EditorTheme.Surface; actions.GridColor = EditorTheme.Line;
            actions.CellBorderStyle = DataGridViewCellBorderStyle.Single; actions.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            actions.EnableHeadersVisualStyles = false; actions.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            actions.DefaultCellStyle = new DataGridViewCellStyle { BackColor = EditorTheme.Surface, ForeColor = EditorTheme.Ink, SelectionBackColor = EditorTheme.Hover, SelectionForeColor = EditorTheme.Ink, Padding = new Padding(8, 0, 8, 0) };
            actions.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = EditorTheme.Floating, ForeColor = EditorTheme.Muted, SelectionBackColor = EditorTheme.Floating, Alignment = DataGridViewContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 8, 0) };
            actions.Columns.Add(new DataGridViewTextBoxColumn { Name = "function", FillWeight = 40, MinimumWidth = 120, SortMode = DataGridViewColumnSortMode.NotSortable });
            actions.Columns.Add(new DataGridViewTextBoxColumn { Name = "factory", FillWeight = 24, MinimumWidth = 80, SortMode = DataGridViewColumnSortMode.NotSortable });
            actions.Columns.Add(new DataGridViewTextBoxColumn { Name = "bindings", FillWeight = 36, MinimumWidth = 130, SortMode = DataGridViewColumnSortMode.NotSortable });
            actions.Columns.Add(new DataGridViewTextBoxColumn { Name = "add", AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 40, MinimumWidth = 32, SortMode = DataGridViewColumnSortMode.NotSortable });
            actions.CellValueNeeded += CellValue;
            actions.CellPainting += PaintCell;
            actions.RowHeightInfoNeeded += delegate(object sender, DataGridViewRowHeightInfoNeededEventArgs e)
            { if (e.RowIndex >= 0 && e.RowIndex < rows.Count) { e.Height = Math.Max(Scale(28), rows[e.RowIndex].Height); e.MinimumHeight = Scale(28); } };
            actions.CellMouseClick += CellClick;
            actions.CellToolTipTextNeeded += CellTip;
            actions.CellMouseEnter += delegate(object sender, DataGridViewCellEventArgs e) { actions.Cursor = e.RowIndex >= 0 ? Cursors.Hand : Cursors.Default; };
            actions.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Enter || actions.CurrentCell == null) return;
                int index = actions.CurrentCell.RowIndex; if (index < 0 || index >= rows.Count) return;
                if (actions.CurrentCell.ColumnIndex == 3) RequestAdd(rows[index]); else RequestDetails(rows[index]); e.Handled = true;
            };
            actions.HandleCreated += delegate { EditorArtwork.DarkScrollbars(actions); };
        }

        int Scale(int value) { return Math.Max(1, (int)Math.Round(value * dpiScale)); }
        void UpdateScale()
        {
            float previousScale = dpiScale;
            using (var graphics = CreateGraphics()) dpiScale = graphics.DpiX / 96f;
            if (codeFont == null || codeFont.FontFamily.Name != Font.FontFamily.Name
                || Math.Abs(codeFont.SizeInPoints - Math.Max(7f, Font.SizeInPoints - 1f)) >= .01f)
            {
                if (codeFont != null) codeFont.Dispose();
                codeFont = new Font(Font.FontFamily, Math.Max(7f, Font.SizeInPoints - 1f));
            }
            root.RowStyles[0].Height = Scale(38);
            keyEnhancementNotice.Padding = new Padding(Scale(32), 0, Scale(4), 0);
            actions.Font = Font; actions.ColumnHeadersHeight = Scale(32); actions.Columns[3].Width = Scale(40); groups.ItemHeight = Scale(29);
            if (!sizedSplitter || previousScale != dpiScale) SizeSplitter(previousScale);
            MeasureRows();
        }
        void SizeSplitter(float previousScale)
        {
            int preferred = sizedSplitter ? (int)(split.SplitterDistance * dpiScale / previousScale) : Scale(220);
            if (split.Width >= Math.Max(preferred, split.Panel1MinSize) + split.Panel2MinSize + split.SplitterWidth)
            {
                split.SplitterDistance = Math.Max(split.Panel1MinSize, Math.Min(preferred, split.Width - split.Panel2MinSize - split.SplitterWidth));
                sizedSplitter = true;
            }
        }

        public void Render(BindingWorkspace value)
        { RenderEntries(value, value.GetSnapshot()); }

        internal void RenderEntries(BindingWorkspace value, BindingEntry[] entrySnapshot)
        {
            UpdateScale();
            var timer = Stopwatch.StartNew(); workspace = value;
            searchTitle.Text = UnifiedBindingUi.Text("searchfunctions"); empty.Text = I18n.Str("unified.groups.none");
            string[] headers = { "function", "factory", "bindings", "add" };
            for (int i = 0; i < headers.Length; i++) actions.Columns[i].HeaderText = I18n.Str("unified.grid." + headers[i]);
            actions.Columns[3].HeaderText = "+"; actions.Columns[3].ToolTipText = UnifiedBindingUi.Text("addbinding");
            var snapshot = entrySnapshot.GroupBy(entry => entry.FunctionId).ToDictionary(group => group.Key, group => group.ToArray());
            allRows = value.Functions.Select(function =>
            {
                BindingEntry[] entries; if (!snapshot.TryGetValue(function.Id, out entries)) entries = new BindingEntry[0];
                string[] factory = function.FactoryKeys;
                return new ActionRow { Function = function, Entries = entries, BindingTexts = entries.Select(UnifiedBindingUi.BindingText).ToArray(),
                    Factory = factory == null ? I18n.Str("editor.defaultunknown") : BindingViewText.Keys(factory), Code = function.RequiresKeyEnhancement ? UnifiedBindingUi.FunctionIdentity(function) : "" };
            }).ToArray();
            string signature = I18n.Current + "|" + value.KeyEnhancementEnabled + "|" + string.Join("\n", allRows.Select(row => string.Join("/", row.Function.GroupPath) + ":" + string.Join("/", row.Function.GroupLabels)));
            if (groupSignature != signature) { groupSignature = signature; BuildGroups(); }
            FilterRows(null);
            timer.Stop(); Console.WriteLine("UnifiedActionView Render {0} ms; functions={1}, rows={2}", timer.ElapsedMilliseconds, allRows.Length, rows.Count);
        }

        void BuildGroups()
        {
            string selected = groups.SelectedNode == null ? workspace.KeyEnhancementEnabled ? "" : "native" : groups.SelectedNode.Tag as string;
            bool visible = groups.Visible;
            var drawMode = groups.DrawMode;
            // Owner-drawn TreeView invokes the managed painter for every hidden
            // node during the first bulk EndUpdate; build the hierarchy normally.
            groups.Visible = false;
            groups.DrawMode = TreeViewDrawMode.Normal;
            updatingGroups = true;
            groups.BeginUpdate();
            try
            {
                groups.Nodes.Clear(); keyEnhancementGroups.Clear();
                var all = new TreeNode(I18n.Str("unified.groups.all") + " (" + allRows.Length + ")") { Tag = "" }; groups.Nodes.Add(all);
                var nodes = new Dictionary<string, TreeNode>();
                var counts = new Dictionary<string, int>();
                var nativeGroups = new HashSet<string>();
                foreach (var row in allRows)
                {
                    TreeNode parent = null; string path = "";
                    for (int i = 0; i < row.Function.GroupPath.Length; i++)
                    {
                        path = path.Length == 0 ? row.Function.GroupPath[i] : path + "/" + row.Function.GroupPath[i];
                        int count; counts.TryGetValue(path, out count); counts[path] = count + 1;
                        if (!row.Function.RequiresKeyEnhancement) nativeGroups.Add(path);
                        TreeNode node;
                        if (!nodes.TryGetValue(path, out node))
                        {
                            string label = i < row.Function.GroupLabels.Length ? row.Function.GroupLabels[i] : row.Function.GroupPath[i];
                            node = new TreeNode(label) { Tag = path, ToolTipText = string.Join(" / ", row.Function.GroupLabels.Take(i + 1)),
                                ForeColor = row.Function.RequiresKeyEnhancement ? EditorTheme.KeyEnhancement : EditorTheme.Ink };
                            nodes.Add(path, node); if (parent == null) groups.Nodes.Add(node); else parent.Nodes.Add(node);
                        }
                        parent = node;
                    }
                }
                foreach (var pair in nodes)
                {
                    string path = pair.Key;
                    pair.Value.Text += " (" + counts[path] + ")";
                    if (nativeGroups.Contains(path)) continue;
                    keyEnhancementGroups.Add(path);
                    pair.Value.ForeColor = workspace.KeyEnhancementEnabled ? EditorTheme.KeyEnhancement : EditorTheme.Muted;
                    pair.Value.ToolTipText += Environment.NewLine + (workspace.KeyEnhancementEnabled ? I18n.Str("enhancement.title") : UnifiedBindingUi.KeyEnhancementHint);
                }
                TreeNode selection; if (selected == "") groups.SelectedNode = all;
                else if (selected != null && nodes.TryGetValue(selected, out selection)) groups.SelectedNode = selection;
                else groups.SelectedNode = nodes.TryGetValue("native", out selection) ? selection : all;
                if (groups.SelectedNode != null) groups.SelectedNode.EnsureVisible();
            }
            finally
            {
                groups.EndUpdate();
                groups.DrawMode = drawMode;
                groups.Visible = visible;
                updatingGroups = false;
            }
        }

        void DrawGroup(object sender, DrawTreeNodeEventArgs e)
        {
            bool marked = keyEnhancementGroups.Contains(e.Node.Tag as string ?? "");
            bool selected = (e.State & TreeNodeStates.Selected) != 0;
            var bounds = new Rectangle(0, e.Bounds.Top, groups.ClientSize.Width, e.Bounds.Height);
            using (var brush = new SolidBrush(selected ? EditorTheme.Hover : groups.BackColor)) e.Graphics.FillRectangle(brush, bounds);
            var state = e.Graphics.Save();
            e.Graphics.SetClip(bounds, System.Drawing.Drawing2D.CombineMode.Intersect);
            EditorArtwork.PaintEffect(groups, e.Graphics);
            e.Graphics.Restore(state);
            bounds.X = e.Node.Bounds.Left; bounds.Width = Math.Max(1, groups.ClientSize.Width - bounds.X);
            if (e.Node.Nodes.Count > 0 && (e.Node.Parent != null || groups.ShowRootLines))
            {
                int centerX = bounds.Left - Scale(9), centerY = bounds.Top + bounds.Height / 2;
                Point[] triangle = e.Node.IsExpanded
                    ? new[] { new Point(centerX - Scale(4), centerY - Scale(2)), new Point(centerX + Scale(4), centerY - Scale(2)), new Point(centerX, centerY + Scale(3)) }
                    : new[] { new Point(centerX - Scale(2), centerY - Scale(4)), new Point(centerX - Scale(2), centerY + Scale(4)), new Point(centerX + Scale(3), centerY) };
                using (var brush = new SolidBrush(EditorTheme.Muted)) e.Graphics.FillPolygon(brush, triangle);
            }
            if (marked)
            {
                UnifiedBindingUi.PaintKeyEnhancementMark(e.Graphics, new Rectangle(bounds.Left + Scale(2), bounds.Top + (bounds.Height - Scale(14)) / 2, Scale(14), Scale(14)));
                bounds.X += Scale(20); bounds.Width = Math.Max(1, bounds.Width - Scale(20));
            }
            TextRenderer.DrawText(e.Graphics, e.Node.Text, groups.Font, bounds, marked ? workspace.KeyEnhancementEnabled ? EditorTheme.KeyEnhancement : EditorTheme.Muted : EditorTheme.Ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        void FilterRows(string operation)
        {
            if (workspace == null) return; var timer = Stopwatch.StartNew();
            int first = actions.FirstDisplayedScrollingRowIndex;
            string anchor = operation == null && first >= 0 && first < rows.Count ? rows[first].Function.Id : null;
            string selected = operation == null && actions.CurrentCell != null && actions.CurrentCell.RowIndex < rows.Count
                ? rows[actions.CurrentCell.RowIndex].Function.Id : null;
            int column = actions.CurrentCell == null ? 0 : actions.CurrentCell.ColumnIndex;
            string query = search.Text.Trim(), group = groups.SelectedNode == null ? "" : groups.SelectedNode.Tag as string;
            rows.Clear();
            if (query.Length > 0)
            {
                var byId = allRows.ToDictionary(row => row.Function.Id);
                rows.AddRange(workspace.FindFunctions(query).Select(function => byId[function.Id]));
            }
            else rows.AddRange(allRows.Where(row => string.IsNullOrEmpty(group) || string.Join("/", row.Function.GroupPath) == group
                || string.Join("/", row.Function.GroupPath).StartsWith(group + "/", StringComparison.Ordinal)));
            keyEnhancementNotice.Text = workspace.KeyEnhancementEnabled ? I18n.Str("binding.scope.scheme") + " · " + I18n.Str("enhancement.title") + ": " + I18n.Str("binding.scope.global") : UnifiedBindingUi.KeyEnhancementHint;
            LayoutNotice();
            actions.RowCount = rows.Count; MeasureRows(); empty.Visible = rows.Count == 0; if (empty.Visible) empty.BringToFront(); else actions.BringToFront();
            if (selected != null)
            {
                int index = rows.FindIndex(row => row.Function.Id == selected);
                if (index >= 0 && (actions.CurrentCell == null || actions.CurrentCell.RowIndex != index)) actions.CurrentCell = actions[column, index];
            }
            if (anchor != null)
            {
                int index = rows.FindIndex(row => row.Function.Id == anchor);
                if (index >= 0 && actions.FirstDisplayedScrollingRowIndex != index) actions.FirstDisplayedScrollingRowIndex = index;
            }
            timer.Stop(); if (operation != null) Console.WriteLine("UnifiedActionView {0} {1} ms; rows={2}", operation, timer.ElapsedMilliseconds, rows.Count);
        }

        void MeasureRows()
        {
            if (actions.Columns.Count < 3) return;
            int width = Math.Max(20, actions.Columns[2].Width);
            foreach (var row in rows)
            {
                LayoutBindings(row, width); int linesHeight = row.Hits.Count == 0 ? 0 : row.Hits.Max(hit => hit.Bounds.Bottom) + Scale(3);
                row.Height = Math.Max(Scale(row.Function.RequiresKeyEnhancement ? 40 : 28), linesHeight);
            }
            if (actions.RowCount > 0) actions.UpdateRowHeightInfo(0, true); actions.Invalidate();
        }

        void LayoutNotice()
        {
            if (workspace == null) return;
            keyEnhancementNotice.Visible = true;
            root.RowStyles[1].Height = Scale(28);
            if (!workspace.KeyEnhancementEnabled && !rows.Any(row => row.Function.RequiresKeyEnhancement))
                keyEnhancementNotice.Text = I18n.Str("enhancement.title") + ": " + I18n.Str("mod.disabled");
            scopeTip.SetToolTip(searchTitle, keyEnhancementNotice.Text);
            scopeTip.SetToolTip(search, keyEnhancementNotice.Text);
        }

        void LayoutBindings(ActionRow row, int width)
        {
            row.Hits.Clear(); int left = Scale(7), top = Scale(3), gap = Scale(4), chipHeight = Scale(22), right = width - Scale(7);
            for (int i = 0; i < row.BindingTexts.Length; i++)
            {
                int chipWidth = Math.Min(Math.Max(Scale(30), TextRenderer.MeasureText(row.BindingTexts[i], Font).Width + Scale(15)), Math.Max(Scale(20), right - Scale(7)));
                if (left + chipWidth > right && left > Scale(7)) { left = Scale(7); top += chipHeight + gap; }
                if (top > Scale(5) + (chipHeight + gap) * 2)
                {
                    var previous = row.Hits[row.Hits.Count - 1]; previous.Index = -1; return;
                }
                row.Hits.Add(new BindingHit { Index = i, Bounds = new Rectangle(left, top, chipWidth, chipHeight) }); left += chipWidth + gap;
            }
        }

        void CellValue(object sender, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= rows.Count) return; var row = rows[e.RowIndex];
            e.Value = e.ColumnIndex == 0 ? row.Function.Label + (row.Code.Length == 0 ? "" : "\n" + row.Code) : e.ColumnIndex == 1 ? row.Factory
                : e.ColumnIndex == 2 ? string.Join(" / ", row.BindingTexts) : "+";
        }

        void PaintCell(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex == -1 && e.ColumnIndex >= 0)
            {
                e.Paint(e.ClipBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.SelectionBackground);
                var headerState = e.Graphics.Save();
                e.Graphics.SetClip(Rectangle.Intersect(e.ClipBounds, e.CellBounds), System.Drawing.Drawing2D.CombineMode.Intersect);
                EditorArtwork.PaintEffect(actions, e.Graphics);
                e.Graphics.Restore(headerState);
                e.Paint(e.ClipBounds, DataGridViewPaintParts.Border | DataGridViewPaintParts.ContentForeground);
                e.Handled = true;
                return;
            }
            if (e.RowIndex < 0 || e.RowIndex >= rows.Count || e.ColumnIndex < 0) return;
            var row = rows[e.RowIndex]; e.Paint(e.ClipBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.SelectionBackground);
            var state = e.Graphics.Save();
            e.Graphics.SetClip(Rectangle.Intersect(e.ClipBounds, e.CellBounds), System.Drawing.Drawing2D.CombineMode.Intersect);
            EditorArtwork.PaintEffect(actions, e.Graphics);
            e.Graphics.Restore(state);
            e.Paint(e.ClipBounds, DataGridViewPaintParts.Border);
            var bounds = e.CellBounds; bounds.Inflate(-Scale(8), 0);
            bool gated = row.Function.RequiresKeyEnhancement && !workspace.KeyEnhancementEnabled;
            var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            if (e.ColumnIndex == 0)
            {
                if (row.Function.RequiresKeyEnhancement)
                {
                    UnifiedBindingUi.PaintKeyEnhancementMark(e.Graphics, new Rectangle(bounds.Left, bounds.Top + Scale(3), Scale(14), Scale(14)));
                    bounds.X += Scale(20); bounds.Width = Math.Max(1, bounds.Width - Scale(20));
                }
                if (row.Code.Length == 0) TextRenderer.DrawText(e.Graphics, row.Function.Label, Font, bounds, EditorTheme.Ink, flags);
                else
                {
                    int titleHeight = Scale(21); var title = new Rectangle(bounds.Left, bounds.Top, bounds.Width, titleHeight);
                    TextRenderer.DrawText(e.Graphics, row.Function.Label, Font, title, gated ? EditorTheme.Muted : row.Function.RequiresKeyEnhancement ? EditorTheme.KeyEnhancement : EditorTheme.Ink, flags);
                    TextRenderer.DrawText(e.Graphics, row.Code, codeFont ?? Font, new Rectangle(bounds.Left, bounds.Top + titleHeight, bounds.Width, Scale(18)), EditorTheme.Muted, flags);
                }
            }
            else if (e.ColumnIndex == 1) TextRenderer.DrawText(e.Graphics, row.Factory, Font, bounds, EditorTheme.Muted, flags);
            else if (e.ColumnIndex == 3) TextRenderer.DrawText(e.Graphics, "+", Font, bounds, gated ? EditorTheme.Muted : row.Function.RequiresKeyEnhancement ? EditorTheme.KeyEnhancement : UnifiedBindingUi.Blue, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            else if (row.Entries.Length == 0) TextRenderer.DrawText(e.Graphics, UnifiedBindingUi.ShowsIndependentBindings(row.Function) ? UnifiedBindingUi.Text("additionalnone") : "—", Font, bounds, EditorTheme.Muted, flags);
            else
            {
                foreach (var hit in row.Hits)
                {
                    var rect = hit.Bounds; rect.Offset(e.CellBounds.Location);
                    BindingEntry entry = hit.Index < 0 ? null : row.Entries[hit.Index];
                    Color color = entry == null ? UnifiedBindingUi.Blue : entry.Suppressed || !entry.Enabled || !entry.Active ? EditorTheme.Muted : entry.Pending ? BindingStatus.Pending : entry.ExecutionOwner == "scheduler" ? EditorTheme.KeyEnhancement : EditorTheme.Ink;
                    using (var brush = new SolidBrush(EditorTheme.Floating)) e.Graphics.FillRectangle(brush, rect);
                    using (var pen = new Pen(EditorTheme.Line)) e.Graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
                    rect.Inflate(-Scale(4), 0); TextRenderer.DrawText(e.Graphics, hit.Index < 0 ? "…" : row.BindingTexts[hit.Index], Font, rect, color, flags);
                }
            }
            e.Handled = true;
        }

        void CellClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || e.RowIndex < 0 || e.RowIndex >= rows.Count) return; var row = rows[e.RowIndex];
            if (e.ColumnIndex == 3) { RequestAdd(row); return; }
            if (e.ColumnIndex == 2)
            {
                var hit = row.Hits.FirstOrDefault(item => item.Bounds.Contains(e.X, e.Y));
                if (hit != null && hit.Index >= 0)
                { if (EditRequested != null) EditRequested(row.Entries[hit.Index]); else RequestDetails(row); return; }
            }
            RequestDetails(row);
        }
        void RequestAdd(ActionRow row) { if (AddRequested != null) AddRequested(row.Function.Id); else RequestDetails(row); }
        void RequestDetails(ActionRow row) { if (EditAction != null) EditAction(row.Function.Id); }

        void CellTip(object sender, DataGridViewCellToolTipTextNeededEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= rows.Count) return; var row = rows[e.RowIndex];
            e.ToolTipText = e.ColumnIndex == 0 ? row.Function.TechnicalName : e.ColumnIndex == 1 ? row.Factory
                : e.ColumnIndex == 3 ? UnifiedBindingUi.Text("addbinding") : string.Join("\n", row.Entries.Select((entry, index) => row.BindingTexts[index] + " · " + UnifiedBindingUi.EntryState(entry)));
            if (e.ColumnIndex == 0)
            {
                string details = GameMoveNames.Details(row.Function.Id);
                e.ToolTipText = (details.Length == 0 ? "" : details + Environment.NewLine)
                    + e.ToolTipText + Environment.NewLine + string.Join(" / ", row.Function.GroupLabels);
            }
            if (e.ColumnIndex >= 0 && e.ColumnIndex < 3)
            {
                string hint = UnifiedBindingUi.NativeEntryHint(workspace, row.Function);
                if (hint.Length > 0) e.ToolTipText += Environment.NewLine + hint;
            }
            if (row.Function.RequiresKeyEnhancement && !workspace.KeyEnhancementEnabled) e.ToolTipText += Environment.NewLine + UnifiedBindingUi.KeyEnhancementHint;
        }
        protected override void Dispose(bool disposing) { if (disposing) { scopeTip.Dispose(); if (codeFont != null) codeFont.Dispose(); } base.Dispose(disposing); }
    }
}
