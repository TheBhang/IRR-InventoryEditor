// Dark theme for WinForms: palette, recursive styling, toolstrip renderer, dark title bar + scrollbars.
using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace IrrEditor
{
    public static class Theme
    {
        // ---- DPI: the exe is DPI-aware (manifest), so pixel sizes are scaled by hand / AutoScale
        static float scale = 0;
        public static float Scale
        {
            get
            {
                if (scale == 0)
                {
                    try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = Math.Max(1f, g.DpiX / 96f); }
                    catch { scale = 1f; }
                }
                return scale;
            }
        }
        /// <summary>Scale a 96-dpi pixel value to the current display.</summary>
        public static int S(int px) { return (int)Math.Round(px * Scale); }
        /// <summary>Make a form scale its 96-dpi pixel sizes to the display (call first in the constructor).
        /// WinForms' own AutoScale doesn't kick in for forms built in code, so scale explicitly on Load.</summary>
        public static void DpiForm(Form f)
        {
            f.AutoScaleMode = AutoScaleMode.None;
            f.Load += delegate
            {
                if (Math.Abs(Scale - 1f) < 0.01f) return;
                f.Scale(new SizeF(Scale, Scale));
                ScaleExtras(f);
            };
        }

        // things Control.Scale doesn't touch: ListView columns, grid row heights
        static void ScaleExtras(Control c)
        {
            var lv = c as ListView;
            if (lv != null) foreach (ColumnHeader h in lv.Columns) h.Width = (int)(h.Width * Scale);
            foreach (Control ch in c.Controls) ScaleExtras(ch);
        }

        public static readonly Color Bg = Color.FromArgb(24, 26, 29);        // window background
        public static readonly Color Panel = Color.FromArgb(32, 34, 38);     // side panels
        public static readonly Color Surface = Color.FromArgb(42, 45, 50);   // inputs, headers
        public static readonly Color SurfaceHi = Color.FromArgb(56, 60, 67); // hover
        public static readonly Color Border = Color.FromArgb(64, 68, 76);
        public static readonly Color Text = Color.FromArgb(228, 230, 235);
        public static readonly Color TextDim = Color.FromArgb(150, 156, 166);
        public static readonly Color Accent = Color.FromArgb(214, 168, 72);  // gold, matches selection in grid
        public static readonly Color AccentBg = Color.FromArgb(78, 64, 34);
        public static readonly Color Warn = Color.FromArgb(240, 150, 70);
        public static readonly Color Danger = Color.FromArgb(235, 100, 100);

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        public static void DarkTitleBar(Form f)
        {
            f.HandleCreated += delegate
            {
                int on = 1;
                try { if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref on, 4); } catch { }
            };
        }

        /// <summary>Dark scrollbars / native parts for controls that support the Explorer dark theme.</summary>
        public static void DarkNative(Control c)
        {
            Action apply = () => { try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { } };
            if (c.IsHandleCreated) apply(); else c.HandleCreated += delegate { apply(); };
        }

        public static void Apply(Control root)
        {
            Style(root);
            foreach (Control c in root.Controls) Apply(c);
        }

        static void Style(Control c)
        {
            if (c is Form) { c.BackColor = Bg; c.ForeColor = Text; DarkTitleBar((Form)c); return; }
            if (c is ToolStrip)
            {
                var ts = (ToolStrip)c;
                ts.Renderer = new DarkRenderer();
                ts.BackColor = Panel; ts.ForeColor = Text;
                return;
            }
            if (c is SplitContainer) { c.BackColor = Border; return; }
            if (c is SplitterPanel) { c.BackColor = Panel; c.ForeColor = Text; return; }
            if (c is TreeView)
            {
                var t = (TreeView)c; t.BackColor = Panel; t.ForeColor = Text; t.LineColor = Border; t.BorderStyle = BorderStyle.None;
                DarkNative(t); return;
            }
            if (c is ListBox)
            {
                var l = (ListBox)c; l.BackColor = Surface; l.ForeColor = Text; l.BorderStyle = BorderStyle.None; DarkNative(l);
                // gold selection instead of the system blue
                l.DrawMode = DrawMode.OwnerDrawFixed; l.ItemHeight = (int)(l.Font.GetHeight() + S(6));
                l.DrawItem += (s, e) =>
                {
                    if (e.Index < 0) return;
                    bool sel = (e.State & DrawItemState.Selected) != 0;
                    using (var b = new SolidBrush(sel ? AccentBg : l.BackColor)) e.Graphics.FillRectangle(b, e.Bounds);
                    TextRenderer.DrawText(e.Graphics, Convert.ToString(l.Items[e.Index]), l.Font, new Rectangle(e.Bounds.X + S(4), e.Bounds.Y, e.Bounds.Width - S(4), e.Bounds.Height), sel ? Color.White : Text,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping);
                };
                return;
            }
            if (c is ListView) { var l = (ListView)c; l.BackColor = Panel; l.ForeColor = Text; l.BorderStyle = BorderStyle.None; DarkNative(l); return; }
            if (c is TextBox) { var t = (TextBox)c; t.BackColor = Surface; t.ForeColor = Text; t.BorderStyle = BorderStyle.FixedSingle; return; }
            if (c is ComboBox)
            {
                var cb = (ComboBox)c; cb.BackColor = Surface; cb.ForeColor = Text; cb.FlatStyle = FlatStyle.Standard;
                Action ap = () => { try { SetWindowTheme(cb.Handle, "DarkMode_CFD", null); } catch { } };
                if (cb.IsHandleCreated) ap(); else cb.HandleCreated += delegate { ap(); };
                return;
            }
            if (c is Button)
            {
                var b = (Button)c;
                b.FlatStyle = FlatStyle.Flat; b.BackColor = Surface; b.ForeColor = Text; b.UseVisualStyleBackColor = false;
                b.FlatAppearance.BorderColor = Border; b.FlatAppearance.MouseOverBackColor = SurfaceHi; b.FlatAppearance.MouseDownBackColor = AccentBg;
                return;
            }
            if (c is CheckBox) { c.ForeColor = Text; c.BackColor = Color.Transparent; ((CheckBox)c).FlatStyle = FlatStyle.Flat; return; }
            if (c is DataGridView) { StyleGrid((DataGridView)c); return; }
            if (c is Label) { if (c.ForeColor == SystemColors.ControlText) c.ForeColor = Text; if (c.BackColor == SystemColors.Control) c.BackColor = Color.Transparent; return; }
            if (c is GridPanel) return; // draws itself
            if (c.BackColor == SystemColors.Control) c.BackColor = Panel;
            c.ForeColor = Text;
        }

        public static void StyleGrid(DataGridView g)
        {
            g.BackgroundColor = Surface; g.GridColor = Border; g.BorderStyle = BorderStyle.None;
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            g.ColumnHeadersDefaultCellStyle.BackColor = Panel;
            g.ColumnHeadersDefaultCellStyle.ForeColor = TextDim;
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Panel;
            g.ColumnHeadersDefaultCellStyle.SelectionForeColor = TextDim;
            g.DefaultCellStyle.BackColor = Surface;
            g.DefaultCellStyle.ForeColor = Text;
            g.DefaultCellStyle.SelectionBackColor = AccentBg;
            g.DefaultCellStyle.SelectionForeColor = Color.White;
            g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(46, 49, 55);
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            DarkNative(g);
            foreach (Control sb in g.Controls) DarkNative(sb);   // its scrollbars are child windows
        }

        class DarkColors : ProfessionalColorTable
        {
            public override Color ToolStripGradientBegin { get { return Panel; } }
            public override Color ToolStripGradientMiddle { get { return Panel; } }
            public override Color ToolStripGradientEnd { get { return Panel; } }
            public override Color ToolStripBorder { get { return Panel; } }
            public override Color ToolStripDropDownBackground { get { return Surface; } }
            public override Color ImageMarginGradientBegin { get { return Surface; } }
            public override Color ImageMarginGradientMiddle { get { return Surface; } }
            public override Color ImageMarginGradientEnd { get { return Surface; } }
            public override Color MenuBorder { get { return Border; } }
            public override Color MenuItemBorder { get { return Accent; } }
            public override Color MenuItemSelected { get { return SurfaceHi; } }
            public override Color MenuItemSelectedGradientBegin { get { return SurfaceHi; } }
            public override Color MenuItemSelectedGradientEnd { get { return SurfaceHi; } }
            public override Color MenuItemPressedGradientBegin { get { return AccentBg; } }
            public override Color MenuItemPressedGradientEnd { get { return AccentBg; } }
            public override Color ButtonSelectedHighlight { get { return SurfaceHi; } }
            public override Color ButtonSelectedGradientBegin { get { return SurfaceHi; } }
            public override Color ButtonSelectedGradientMiddle { get { return SurfaceHi; } }
            public override Color ButtonSelectedGradientEnd { get { return SurfaceHi; } }
            public override Color ButtonSelectedBorder { get { return Border; } }
            public override Color ButtonPressedGradientBegin { get { return AccentBg; } }
            public override Color ButtonPressedGradientMiddle { get { return AccentBg; } }
            public override Color ButtonPressedGradientEnd { get { return AccentBg; } }
            public override Color ButtonPressedBorder { get { return Accent; } }
            public override Color SeparatorDark { get { return Border; } }
            public override Color SeparatorLight { get { return Panel; } }
            public override Color StatusStripGradientBegin { get { return Panel; } }
            public override Color StatusStripGradientEnd { get { return Panel; } }
        }

        public class DarkRenderer : ToolStripProfessionalRenderer
        {
            public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                if (e.Item.ForeColor != Danger && e.Item.ForeColor != Warn) e.TextColor = e.Item.Enabled ? Text : TextDim;
                base.OnRenderItemText(e);
            }
            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = Text; base.OnRenderArrow(e); }
        }

        /// <summary>Dark headers + rows for a details-view ListView.</summary>
        public static void OwnerDrawListView(ListView lv)
        {
            lv.OwnerDraw = true;
            // stretch the last column so no unpainted (white) header area remains on the right
            EventHandler fit = (s, e) =>
            {
                if (lv.Columns.Count == 0) return;
                int used = 0;
                for (int i = 0; i < lv.Columns.Count - 1; i++) used += lv.Columns[i].Width;
                int w = lv.ClientSize.Width - used;
                if (w > 40) lv.Columns[lv.Columns.Count - 1].Width = w;
            };
            lv.Resize += fit;
            lv.HandleCreated += fit;
            lv.DrawColumnHeader += (s, e) =>
            {
                using (var b = new SolidBrush(Panel)) e.Graphics.FillRectangle(b, e.Bounds);
                using (var p = new Pen(Border)) { e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1); e.Graphics.DrawLine(p, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 4); }
                var r = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, lv.Font, r, TextDim, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.Left);
            };
            lv.DrawItem += (s, e) => { };
            lv.DrawSubItem += (s, e) =>
            {
                bool sel = e.Item.Selected;
                Color bg = sel ? AccentBg : (e.ItemIndex % 2 == 0 ? Panel : Color.FromArgb(37, 40, 44));
                using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.Bounds);
                var r = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, lv.Font, r, e.ColumnIndex == 0 ? Text : TextDim,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            };
        }

        /// <summary>Owner-drawn dark tooltip.</summary>
        public static ToolTip MakeTip()
        {
            var t = new ToolTip { OwnerDraw = true, InitialDelay = 350, ReshowDelay = 100, AutoPopDelay = 8000, BackColor = Surface, ForeColor = Text };
            var font = new Font("Segoe UI", 9f);
            t.Popup += (s, e) =>
            {
                var txt = t.GetToolTip(e.AssociatedControl);
                if (string.IsNullOrEmpty(txt)) txt = t.Tag as string ?? ""; // text passed via Show()
                var sz = TextRenderer.MeasureText(txt, font);
                e.ToolTipSize = new Size(sz.Width + 16, sz.Height + 10);
            };
            t.Draw += (s, e) =>
            {
                using (var b = new SolidBrush(Surface)) e.Graphics.FillRectangle(b, e.Bounds);
                using (var p = new Pen(Border)) e.Graphics.DrawRectangle(p, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
                TextRenderer.DrawText(e.Graphics, e.ToolTipText, font, new Rectangle(8, 5, e.Bounds.Width - 12, e.Bounds.Height - 8), Text, TextFormatFlags.Left | TextFormatFlags.Top);
            };
            return t;
        }

        public static Button MakeButton(string text, DialogResult r, bool primary)
        {
            var b = new Button { Text = text, DialogResult = r, AutoSize = true, MinimumSize = new Size(96, 32), Padding = new Padding(10, 0, 10, 0), Margin = new Padding(6, 0, 0, 0) };
            Style(b);
            if (primary) { b.BackColor = AccentBg; b.FlatAppearance.BorderColor = Accent; }
            return b;
        }

        /// <summary>Dark drop-down list items for a ComboBox.</summary>
        public static void OwnerDrawCombo(ComboBox cb)
        {
            cb.DrawMode = DrawMode.OwnerDrawFixed;
            cb.DrawItem += (s, e) =>
            {
                if (e.Index < 0) return;
                bool sel = (e.State & DrawItemState.Selected) != 0;
                using (var b = new SolidBrush(sel ? AccentBg : Surface)) e.Graphics.FillRectangle(b, e.Bounds);
                TextRenderer.DrawText(e.Graphics, cb.Items[e.Index].ToString(), cb.Font, e.Bounds, Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            };
        }
    }

    /// <summary>Dark integer input (replaces NumericUpDown, whose spinner can't be themed).
    /// Type a number, use the mouse wheel or ↑/↓ (Shift = ×10).</summary>
    public class NumBox : TextBox
    {
        decimal val, min, max = int.MaxValue;
        bool syncing;
        public event EventHandler ValueChanged;
        public bool ThousandsSeparator { get; set; }
        public decimal Minimum { get { return min; } set { min = value; if (val < min) Value = min; } }
        public decimal Maximum { get { return max; } set { max = value; if (val > max) Value = max; } }
        public decimal Value
        {
            get { return val; }
            set
            {
                var v = Math.Max(min, Math.Min(max, value));
                bool changed = v != val;
                val = v;
                syncing = true; Text = Format(v); syncing = false;
                if (changed && ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }
        public NumBox() { TextAlign = HorizontalAlignment.Right; Text = "0"; }
        string Format(decimal v) { return ThousandsSeparator && !Focused ? v.ToString("N0") : v.ToString("0"); }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            if (syncing) return;
            var digits = new string(Text.Where(char.IsDigit).ToArray());
            decimal v;
            if (digits.Length > 0 && digits.Length < 19 && decimal.TryParse(digits, out v))
            {
                v = Math.Max(min, Math.Min(max, v));
                if (v != val) { val = v; if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); }
            }
        }
        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true;
            base.OnKeyPress(e);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            int step = e.Shift ? 10 : 1;
            if (e.KeyCode == Keys.Up) { Value = val + step; SelectionStart = Text.Length; e.Handled = true; }
            else if (e.KeyCode == Keys.Down) { Value = val - step; SelectionStart = Text.Length; e.Handled = true; }
            base.OnKeyDown(e);
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Value = val + (e.Delta > 0 ? 1 : -1) * (ModifierKeys == Keys.Shift ? 10 : 1);
            base.OnMouseWheel(e);
        }
        protected override void OnEnter(EventArgs e) { base.OnEnter(e); syncing = true; Text = Format(val); syncing = false; }
        protected override void OnLeave(EventArgs e) { base.OnLeave(e); syncing = true; Text = Format(val); syncing = false; }
    }

    /// <summary>Dark one-line text prompt.</summary>
    public static class DarkInput
    {
        public static string Show(IWin32Window owner, string prompt, string caption, string value)
        {
            using (var f = new Form())
            {
                Theme.DpiForm(f);
                f.Text = caption; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MinimizeBox = false; f.MaximizeBox = false; f.ShowInTaskbar = false;
                f.StartPosition = FormStartPosition.CenterParent; f.Font = new Font("Segoe UI", 9.5f);
                f.ClientSize = new Size(420, 130);
                var l = new Label { Text = prompt, Left = 16, Top = 14, AutoSize = true };
                var t = new TextBox { Text = value, Left = 16, Top = 40, Width = 388 };
                var ok = Theme.MakeButton("OK", DialogResult.OK, true);
                var cancel = Theme.MakeButton(L.T("Zrušit", "Cancel"), DialogResult.Cancel, false);
                ok.Location = new Point(308, 82); cancel.Location = new Point(204, 82);
                f.Controls.AddRange(new Control[] { l, t, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                Theme.Apply(f);
                ok.BackColor = Theme.AccentBg; ok.FlatAppearance.BorderColor = Theme.Accent;
                return f.ShowDialog(owner) == DialogResult.OK ? t.Text : null;
            }
        }
    }

    /// <summary>Dark horizontal slider (the native TrackBar can't be themed).</summary>
    public class DarkSlider : Control
    {
        int min = 1, max = 100, val = 1;
        bool dragging;
        public event EventHandler ValueChanged;
        public int Minimum { get { return min; } set { min = value; if (max < min) max = min; Value = val; Invalidate(); } }
        public int Maximum { get { return max; } set { max = Math.Max(min, value); Value = val; Invalidate(); } }
        public int Value
        {
            get { return val; }
            set
            {
                int v = Math.Max(min, Math.Min(max, value));
                if (v == val) return;
                val = v; Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }
        public DarkSlider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Height = 28; TabStop = true; Cursor = Cursors.Hand;
        }
        int Pad { get { return Height / 2; } }
        float Frac { get { return max == min ? 0 : (float)(val - min) / (max - min); } }
        void SetFromX(int x)
        {
            float f = Math.Max(0, Math.Min(1, (float)(x - Pad) / Math.Max(1, Width - 2 * Pad)));
            Value = min + (int)Math.Round(f * (max - min));
        }
        protected override void OnMouseDown(MouseEventArgs e) { Focus(); dragging = true; SetFromX(e.X); }
        protected override void OnMouseMove(MouseEventArgs e) { if (dragging) SetFromX(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; }
        protected override void OnMouseWheel(MouseEventArgs e) { Value += e.Delta > 0 ? Math.Max(1, (max - min) / 20) : -Math.Max(1, (max - min) / 20); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left) Value--; else if (e.KeyCode == Keys.Right) Value++;
            else if (e.KeyCode == Keys.Home) Value = min; else if (e.KeyCode == Keys.End) Value = max;
            base.OnKeyDown(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            int y = Height / 2, x0 = Pad, x1 = Width - Pad;
            int tx = x0 + (int)((x1 - x0) * Frac);
            using (var p = new Pen(Theme.SurfaceHi, Math.Max(4, Height / 6))) { p.StartCap = p.EndCap = System.Drawing.Drawing2D.LineCap.Round; g.DrawLine(p, x0, y, x1, y); }
            using (var p = new Pen(Theme.Accent, Math.Max(4, Height / 6))) { p.StartCap = p.EndCap = System.Drawing.Drawing2D.LineCap.Round; if (tx > x0) g.DrawLine(p, x0, y, tx, y); }
            int r = Height / 3;
            using (var b = new SolidBrush(Focused ? Color.White : Theme.Text)) g.FillEllipse(b, tx - r, y - r, 2 * r, 2 * r);
            using (var p = new Pen(Theme.Accent, 2)) g.DrawEllipse(p, tx - r, y - r, 2 * r, 2 * r);
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }

    /// <summary>Drop-in dark replacement for MessageBox.Show.</summary>
    public static class DarkMsg
    {
        public static DialogResult Show(IWin32Window owner, string text, string caption)
        { return Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None); }
        public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons)
        { return Show(owner, text, caption, buttons, MessageBoxIcon.None); }

        public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            using (var f = new Form())
            {
                Theme.DpiForm(f);
                f.Text = caption; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MinimizeBox = false; f.MaximizeBox = false; f.ShowInTaskbar = false;
                f.StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
                f.Font = new Font("Segoe UI", 9.5f); f.AutoSize = true; f.AutoSizeMode = AutoSizeMode.GrowAndShrink; f.Padding = new Padding(18);
                Theme.Apply(f);

                var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill, BackColor = Theme.Bg };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                string glyph = null; Color gc = Theme.Text;
                if (icon == MessageBoxIcon.Warning) { glyph = "⚠"; gc = Theme.Warn; }
                else if (icon == MessageBoxIcon.Error) { glyph = "✖"; gc = Theme.Danger; }
                else if (icon == MessageBoxIcon.Question) { glyph = "?"; gc = Theme.Accent; }
                var ico = new Label { Text = glyph ?? "", AutoSize = true, Font = new Font("Segoe UI Symbol", 20f), ForeColor = gc, Margin = new Padding(0, 0, glyph == null ? 0 : 14, 0) };
                var msg = new Label { Text = text, AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = Theme.Text, Margin = new Padding(0, 6, 0, 18) };
                layout.Controls.Add(ico, 0, 0); layout.Controls.Add(msg, 1, 0);

                var btns = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, BackColor = Theme.Bg, Margin = new Padding(0) };
                Action<string, DialogResult, bool> add = (t, r, p) => { var b = Theme.MakeButton(t, r, p); btns.Controls.Add(b); if (p) f.AcceptButton = b; };
                switch (buttons)
                {
                    case MessageBoxButtons.YesNo: add(L.T("Ne", "No"), DialogResult.No, false); add(L.T("Ano", "Yes"), DialogResult.Yes, true); f.CancelButton = (Button)btns.Controls[0]; break;
                    case MessageBoxButtons.YesNoCancel: add(L.T("Zrušit", "Cancel"), DialogResult.Cancel, false); add(L.T("Ne", "No"), DialogResult.No, false); add(L.T("Ano", "Yes"), DialogResult.Yes, true); f.CancelButton = (Button)btns.Controls[0]; break;
                    case MessageBoxButtons.OKCancel: add(L.T("Zrušit", "Cancel"), DialogResult.Cancel, false); add("OK", DialogResult.OK, true); f.CancelButton = (Button)btns.Controls[0]; break;
                    default: add("OK", DialogResult.OK, true); f.CancelButton = (Button)btns.Controls[0]; break;
                }
                layout.Controls.Add(btns, 0, 1); layout.SetColumnSpan(btns, 2);
                f.Controls.Add(layout);
                var res = owner == null ? f.ShowDialog() : f.ShowDialog(owner);
                if (res == DialogResult.Cancel && buttons == MessageBoxButtons.YesNo) return DialogResult.No;
                return res;
            }
        }
    }
}
