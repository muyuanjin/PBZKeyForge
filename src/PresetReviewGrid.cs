using System;
using System.Drawing;
using System.Windows.Forms;

namespace PBZKeyForge
{
    internal sealed class PresetReviewGrid : DataGridView
    {
        int measuredWidth;

        internal PresetReviewGrid()
        {
            DoubleBuffered = true;
            ReadOnly = true; AllowUserToAddRows = false; AllowUserToDeleteRows = false;
            AllowUserToResizeRows = false; AllowUserToResizeColumns = false;
            RowHeadersVisible = false; MultiSelect = false; TabStop = true;
            BorderStyle = BorderStyle.None; CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            BackgroundColor = EditorTheme.Surface; GridColor = EditorTheme.Line;
            EnableHeadersVisualStyles = false; ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle {
                BackColor = EditorTheme.Floating, ForeColor = EditorTheme.Muted,
                SelectionBackColor = EditorTheme.Floating, SelectionForeColor = EditorTheme.Muted,
                Padding = new Padding(8), WrapMode = DataGridViewTriState.True
            };
            DefaultCellStyle = new DataGridViewCellStyle {
                BackColor = EditorTheme.Surface, ForeColor = EditorTheme.Ink,
                SelectionBackColor = EditorTheme.Hover, SelectionForeColor = EditorTheme.Ink,
                Padding = new Padding(8), WrapMode = DataGridViewTriState.True,
                Alignment = DataGridViewContentAlignment.MiddleLeft
            };
            AlternatingRowsDefaultCellStyle.BackColor = EditorTheme.RowAlternate;
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            ScrollBars = ScrollBars.Vertical;
            Columns.Add(new DataGridViewTextBoxColumn { Name = "kind", HeaderText = "", FillWeight = 12, MinimumWidth = 62 });
            Columns.Add(new DataGridViewTextBoxColumn { Name = "action", HeaderText = I18n.Str("preset.review.action"), FillWeight = 28 });
            Columns.Add(new DataGridViewTextBoxColumn { Name = "before", HeaderText = I18n.Str("preset.review.before"), FillWeight = 30 });
            Columns.Add(new DataGridViewTextBoxColumn { Name = "after", HeaderText = I18n.Str("preset.review.after"), FillWeight = 30 });
            foreach (DataGridViewColumn column in Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
        }

        internal bool CurrentContents
        {
            set
            {
                Columns[0].Visible = Columns[2].Visible = !value;
                Columns[3].HeaderText = I18n.Str(value ? "preset.review.current" : "preset.review.after");
            }
        }

        internal void FitRows()
        {
            if (Rows.Count == 0 || ClientSize.Width <= 0) return;
            AutoResizeRows(DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders);
            measuredWidth = ClientSize.Width;
            ClearSelection();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (IsHandleCreated && ClientSize.Width != measuredWidth) FitRows();
        }
    }
}
