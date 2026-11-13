// Custom-drawn inventory grid: one or more containers ("sections"), drag & drop between them,
// multi-select (Ctrl+click, rubber band), search highlighting.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace IrrEditor
{
    /// <summary>One block of the view: a whole container (its grids) or a single equipment slot.</summary>
    public class GridSection
    {
        public MainContainer Mc;
        public string Title;     // null = no header (single-container view)
        public bool SlotOnly;    // equipment slot: show the one item it holds
    }

    public class GridPanel : Panel
    {
        class Block
        {
            public MainContainer Mc;
            public GridElement Ge;   // null for a slot block
            public Rectangle R;
            public bool Slot { get { return Ge == null; } }
        }

        public MainForm Owner;
        public InvItem Selected;                                   // primary selection
        public readonly HashSet<InvItem> Multi = new HashSet<InvItem>();  // extra selection (Ctrl+click / band)
        public string Search = "";                                 // lower-case filter, "" = off

        List<GridSection> sections = new List<GridSection>();
        int Cell, Gap, Pad, HeadH, CapH, SlotCell;
        float zoom = 1f;
        /// <summary>Tile size multiplier (Ctrl+wheel, − / + buttons).</summary>
        public float Zoom { get { return zoom; } set { zoom = Math.Max(0.5f, Math.Min(1.6f, value)); ApplyZoom(); DoLayout(); Invalidate(); if (ZoomChanged != null) ZoomChanged(this, EventArgs.Empty); } }
        public event EventHandler ZoomChanged;
        /// <summary>Lay sections out in one row (horizontal scroll) instead of wrapping – used for "all stash pages".</summary>
        public bool NoWrap;
        void ApplyZoom()
        {
            Cell = Theme.S((int)Math.Round(56 * zoom)); Gap = Theme.S((int)Math.Round(28 * zoom)); Pad = Theme.S(16);
            HeadH = Theme.S(26); CapH = Theme.S(18); SlotCell = Theme.S((int)Math.Round(40 * zoom));
        }
        float FontZ(float pt) { return Math.Max(6f, pt * Math.Min(1.25f, Math.Max(0.8f, zoom))); }
        readonly List<Block> blocks = new List<Block>();
        readonly List<KeyValuePair<string, Point>> headers = new List<KeyValuePair<string, Point>>();
        readonly List<KeyValuePair<InvItem, Rectangle>> tiles = new List<KeyValuePair<InvItem, Rectangle>>();
        InvItem drag; int dragOffR, dragOffC; Point mouse; bool dragRot; bool dragging; Point downAt;
        bool banding; Point bandStart;                             // content coordinates
        readonly ToolTip tip = Theme.MakeTip();
        InvItem hover;

        public GridPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            TabStop = true;
            ApplyZoom();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) != 0) { Zoom += e.Delta > 0 ? 0.1f : -0.1f; return; }
            base.OnMouseWheel(e);
        }

        public void Show(MainContainer c) { Show(new List<GridSection> { new GridSection { Mc = c } }); }

        public void Show(List<GridSection> s)
        {
            sections = s; Selected = null; Multi.Clear(); AutoScrollPosition = Point.Empty; DoLayout(); Invalidate();
        }

        /// <summary>Is this container visible in the current view?</summary>
        public bool Shows(MainContainer m) { return m != null && sections.Any(s => s.Mc == m); }
        public IEnumerable<MainContainer> Containers { get { return sections.Select(s => s.Mc); } }

        public bool IsSelected(InvItem it) { return it != null && (it == Selected || Multi.Contains(it)); }

        public static bool Matches(InvItem it, string q)
        {
            if (string.IsNullOrEmpty(q)) return false;
            return it.DisplayName.ToLowerInvariant().Contains(q) || it.Category.ToLowerInvariant().Contains(q);
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); DoLayout(); Invalidate(); }

        void DoLayout()
        {
            blocks.Clear(); headers.Clear();
            int maxW = Math.Max(ClientSize.Width - Pad, Theme.S(300));
            int x = Pad, y = Pad, rowH = 0;
            using (var f = new Font("Segoe UI", 7.5f))
            using (var hf = new Font("Segoe UI Semibold", 10f))
                foreach (var s in sections)
                {
                    var sizes = new List<Size>();
                    var widths = new List<int>();
                    var elems = new List<GridElement>();
                    if (s.SlotOnly)
                    {
                        // equipment slots are drawn smaller than grid cells – they're just "what's equipped"
                        var it = s.Mc.Items.FirstOrDefault();
                        var sz = new Size(Math.Max(3, it == null ? 3 : it.W) * SlotCell, Math.Max(2, it == null ? 2 : it.H) * SlotCell);
                        sizes.Add(sz); widths.Add(sz.Width); elems.Add(null);
                    }
                    else
                        foreach (var ge in s.Mc.Elements.Where(e => e.IsGrid))
                        {
                            var sz = new Size(ge.Cols * Cell, ge.Rows * Cell);
                            sizes.Add(sz);
                            widths.Add(Math.Max(sz.Width, TextRenderer.MeasureText(ElementLabel(s.Mc, ge, 999), f).Width + Theme.S(6)));
                            elems.Add(ge);
                        }
                    int headH = s.Title == null ? 0 : HeadH;
                    int titleW = s.Title == null ? 0 : TextRenderer.MeasureText(s.Title, hf).Width + Theme.S(8);
                    int capH = s.SlotOnly ? 0 : CapH;
                    int contentW = widths.Count == 0 ? 0 : widths.Sum() + Gap * (widths.Count - 1);
                    int secW = Math.Max(titleW, Math.Min(contentW, maxW - Pad));

                    if (!NoWrap && x > Pad && x + secW > maxW) { x = Pad; y += rowH + Gap; rowH = 0; }
                    if (s.Title != null) headers.Add(new KeyValuePair<string, Point>(s.Title, new Point(x, y)));

                    int bx = x, by = y + headH, lineH = 0, right = x + titleW;
                    for (int i = 0; i < sizes.Count; i++)
                    {
                        if (bx > x && bx + widths[i] > x + secW) { bx = x; by += lineH + Gap; lineH = 0; }
                        blocks.Add(new Block { Mc = s.Mc, Ge = elems[i], R = new Rectangle(new Point(bx, by + capH), sizes[i]) });
                        right = Math.Max(right, bx + widths[i]);
                        lineH = Math.Max(lineH, sizes[i].Height + capH);
                        bx += widths[i] + Gap;
                    }
                    int secH = by + lineH - y;
                    x = right + Gap + (s.Title != null ? Theme.S(8) : 0);
                    rowH = Math.Max(rowH, secH);
                }
            AutoScrollMinSize = new Size(blocks.Count == 0 ? 0 : blocks.Max(b => b.R.Right) + Pad, y + rowH + Pad);
        }

        static string ElementLabel(MainContainer mc, GridElement ge, int free)
        {
            return (mc.Elements.Count(x => x.IsGrid) > 1 ? L.T("Část ", "Part ") + (ge.Index + 1) + " · " : "") + ge.Cols + "×" + ge.Rows + L.T(" · volno ", " · free ") + free;
        }

        Point Content(Point p) { return new Point(p.X - AutoScrollPosition.X, p.Y - AutoScrollPosition.Y); }

        Block HitCell(Point p, out int row, out int col)
        {
            p = Content(p);
            foreach (var b in blocks)
                if (b.R.Contains(p)) { col = (p.X - b.R.X) / Cell; row = (p.Y - b.R.Y) / Cell; if (b.Slot) { row = col = 0; } return b; }
            row = col = -1; return null;
        }

        InvItem ItemAt(Point p)
        {
            int r, c;
            var b = HitCell(p, out r, out c);
            if (b == null) return null;
            if (b.Slot) return b.Mc.Items.FirstOrDefault();
            if (r >= b.Ge.Rows || c >= b.Ge.Cols) return null;
            return b.Mc.Save.Occupancy(b.Mc, b.Ge, null)[r, c];
        }

        // ------------------------------------------------------------------ mouse
        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            var it = ItemAt(e.Location);
            if (e.Button == MouseButtons.Right)
            {
                if (it != null)
                {
                    if (!IsSelected(it)) Owner.SelectItem(it, true);
                    Owner.ShowItemMenu(it, PointToScreen(e.Location));
                }
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if (it != null && (ModifierKeys & Keys.Control) != 0) { Owner.ToggleMulti(it); return; }
            if (it == null)
            {
                Owner.SelectItem(null, true);
                banding = true; bandStart = Content(e.Location); mouse = e.Location;
                return;
            }
            Owner.SelectItem(it, true);
            int r, c;
            var b = HitCell(e.Location, out r, out c);
            if (b != null && !b.Slot)
            {
                drag = it; dragOffR = r - it.SlotIndex / b.Ge.Cols; dragOffC = c - it.SlotIndex % b.Ge.Cols; dragRot = it.Rotated;
                downAt = e.Location; dragging = false;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            mouse = e.Location;
            if (banding) { Invalidate(); return; }
            if (drag != null && !dragging && (Math.Abs(e.X - downAt.X) > 4 || Math.Abs(e.Y - downAt.Y) > 4)) dragging = true;
            if (dragging) { Invalidate(); return; }
            var h = ItemAt(e.Location);
            if (h != hover)
            {
                hover = h;
                tip.SetToolTip(this, h == null ? null : h.DisplayName + (h.MaxStack > 1 || h.Count > 1 ? "  ×" + h.Count + "/" + h.MaxStack : "") + "\n" + h.Category
                    + string.Concat(h.Stats.Where(s => s.ShortTag != "Weight").Take(6).Select(s => "\n" + s.ShortTag + ": " + s.Value.ToString("0.##"))));
            }
        }

        Rectangle BandRect()
        {
            var a = bandStart; var b = Content(mouse);
            return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }

        // drop target: any grid block; moving into the item itself (its own backpack) is refused by the owner
        Block DropBlock(Point p, out int r, out int c)
        {
            var b = HitCell(p, out r, out c);
            return b != null && !b.Slot && drag != null ? b : null;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (banding)
            {
                banding = false;
                var band = BandRect();
                if (band.Width > 4 || band.Height > 4)
                    Owner.SelectMany(tiles.Where(t => t.Value.IntersectsWith(band)).Select(t => t.Key).Distinct().ToList());
                Invalidate();
                return;
            }
            if (dragging && drag != null)
            {
                int r, c;
                var b = DropBlock(e.Location, out r, out c);
                if (b != null)
                {
                    bool same = b.Mc == drag.Container;
                    int offR = same && dragRot == drag.Rotated ? dragOffR : 0, offC = same && dragRot == drag.Rotated ? dragOffC : 0;
                    Owner.DropItem(drag, b.Mc, b.Ge, r - offR, c - offC, dragRot);
                }
            }
            drag = null; dragging = false; Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            var it = ItemAt(e.Location);
            if (it != null) Owner.OpenItem(it);
        }

        protected override bool IsInputKey(Keys k) { return k == Keys.R || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (dragging && e.KeyCode == Keys.R) { dragRot = !dragRot; Invalidate(); e.Handled = true; return; }
            base.OnKeyDown(e);
        }

        // ------------------------------------------------------------------ painting
        static Color CatColor(string cat)
        {
            if (cat.StartsWith("Weapons")) return Color.FromArgb(70, 98, 140);
            if (cat.StartsWith("Attachments")) return Color.FromArgb(78, 88, 104);
            if (cat.StartsWith("Ammunition")) return Color.FromArgb(150, 122, 50);
            if (cat.StartsWith("Medical")) return Color.FromArgb(150, 60, 60);
            if (cat.StartsWith("Gear")) return Color.FromArgb(70, 110, 72);
            if (cat.StartsWith("Quest")) return Color.FromArgb(120, 78, 150);
            if (cat.StartsWith("Money")) return Color.FromArgb(160, 140, 40);
            if (cat.StartsWith("Keys")) return Color.FromArgb(40, 120, 120);
            if (cat.StartsWith("Provisions")) return Color.FromArgb(130, 100, 60);
            if (cat.StartsWith("Grenades")) return Color.FromArgb(110, 120, 50);
            if (cat.StartsWith("Barter")) return Color.FromArgb(105, 90, 80);
            return Color.FromArgb(90, 90, 90);
        }

        /// <summary>Word-wrap without breaking words; a word wider than the tile is cut with an ellipsis.</summary>
        static List<string> WrapLabel(Graphics g, string text, Font f, float width, int maxLines)
        {
            var lines = new List<string>();
            if (maxLines <= 0) return lines;
            var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string cur = "";
            Func<string, bool> fits = s => g.MeasureString(s, f, int.MaxValue, StringFormat.GenericTypographic).Width <= width;
            Func<string, string> cut = s =>
            {
                if (fits(s)) return s;
                for (int n = s.Length - 1; n > 0; n--) if (fits(s.Substring(0, n) + "…")) return s.Substring(0, n) + "…";
                return "…";
            };
            int i = 0;
            for (; i < words.Length; i++)
            {
                var w = words[i];
                var cand = cur.Length == 0 ? w : cur + " " + w;
                if (fits(cand)) { cur = cand; continue; }
                if (cur.Length > 0) { lines.Add(cur); cur = ""; if (lines.Count == maxLines) break; }
                if (fits(w)) cur = w; else { lines.Add(cut(w)); if (lines.Count == maxLines) { i++; break; } }
            }
            if (cur.Length > 0 && lines.Count < maxLines) { lines.Add(cur); i = words.Length; }
            if (i < words.Length && lines.Count > 0)
            {
                var last = lines[lines.Count - 1];
                lines[lines.Count - 1] = last.EndsWith("…") ? last : cut(last + "…");
            }
            return lines;
        }

        void DrawTile(Graphics g, InvItem it, Rectangle ir, Font small, Font bold, Brush white)
        {
            tiles.Add(new KeyValuePair<InvItem, Rectangle>(it, ir));
            bool sel = IsSelected(it);
            bool searching = Search.Length > 0;
            bool hit = searching && Matches(it, Search);
            var col = CatColor(it.Category);
            using (var b = new LinearGradientBrush(ir, ControlPaint.Light(col, 0.15f), col, LinearGradientMode.Vertical))
            {
                if (dragging && it == drag) { b.LinearColors = new[] { Color.FromArgb(70, col), Color.FromArgb(70, col) }; }
                g.FillRectangle(b, ir);
            }

            bool hasCount = it.MaxStack > 1 || it.Count > 1;
            var sub = it.Container.Save.ContainerOf(it);
            bool hasBox = sub != null && sub.HasGrid;
            var d = it.GetStat("Durability");
            bool hasBar = d != null && (it.Category.StartsWith("Weapons") || it.Category.StartsWith("Gear"));
            int barH = hasBar ? Theme.S(5) : 0;
            int footH = (hasCount || hasBox) ? Theme.S(14) : 0;

            if (hasBar)
            {
                CatalogEntry ce;
                float max = Catalog.Entries.TryGetValue(it.Def, out ce) && ce.NewDurability > 0 ? ce.NewDurability.Value : 100f;
                float f = Math.Max(0, Math.Min(1, d.Value / max));
                var track = new Rectangle(ir.X + Theme.S(3), ir.Bottom - Theme.S(5), ir.Width - Theme.S(6), Theme.S(3));
                using (var tb = new SolidBrush(Color.FromArgb(90, 0, 0, 0))) g.FillRectangle(tb, track);
                g.FillRectangle(f > 0.6f ? Brushes.LimeGreen : f > 0.3f ? Brushes.Orange : Brushes.Red, new Rectangle(track.X, track.Y, (int)(track.Width * f), track.Height));
            }
            int footTop = ir.Bottom - barH - footH;
            float countW = 0;
            if (hasCount)
            {
                var txt = it.Count >= 100000 ? (it.Count / 1000) + "k" : it.Count >= 10000 ? (Math.Floor(it.Count / 100.0) / 10).ToString("0.#") + "k" : it.Count.ToString();
                var sz = g.MeasureString(txt, bold, int.MaxValue, StringFormat.GenericTypographic);
                countW = sz.Width;
                using (var cb = new SolidBrush(it.Count > it.MaxStack ? Theme.Warn : Color.White))
                    g.DrawString(txt, bold, cb, ir.Right - sz.Width - Theme.S(3), footTop, StringFormat.GenericTypographic);
            }
            if (hasBox && ir.Width - countW > Theme.S(20))
                using (var bb = new SolidBrush(Color.FromArgb(210, 255, 255, 255)))
                    g.DrawString("▣", small, bb, ir.X + Theme.S(2), footTop - 1, StringFormat.GenericTypographic);

            float lineH = small.GetHeight(g);
            int top = ir.Y + Theme.S(3);
            int avail = footTop - top - 1;
            int maxLines = Math.Max(1, (int)(avail / lineH));
            var lines = WrapLabel(g, it.DisplayName, small, ir.Width - Theme.S(7), maxLines);
            var prevClip = g.Clip;
            g.SetClip(new Rectangle(ir.X + 1, ir.Y + 1, ir.Width - 2, Math.Max(lineH > avail ? (int)lineH : avail, 1) + 2));
            for (int k = 0; k < lines.Count; k++)
                g.DrawString(lines[k], small, white, ir.X + Theme.S(3), top + k * lineH, StringFormat.GenericTypographic);
            g.Clip = prevClip;

            // search: dim everything that doesn't match
            if (searching && !hit)
                using (var dim = new SolidBrush(Color.FromArgb(170, 20, 22, 25))) g.FillRectangle(dim, ir);
            var border = sel ? Theme.Accent : hit ? Color.FromArgb(255, 120, 220, 255) : Color.FromArgb(90, 255, 255, 255);
            using (var p = new Pen(border, sel || hit ? Theme.S(2) : 1f))
                g.DrawRectangle(p, sel || hit ? Rectangle.Inflate(ir, -1, -1) : ir);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            tiles.Clear();
            using (var gridPen = new Pen(Color.FromArgb(60, 64, 70)))
            using (var slotPen = new Pen(Color.FromArgb(80, 86, 96)) { DashStyle = DashStyle.Dash })
            using (var cellBg = new SolidBrush(Color.FromArgb(38, 41, 45)))
            using (var hatch = new HatchBrush(HatchStyle.BackwardDiagonal, Color.FromArgb(70, 50, 50), Color.FromArgb(32, 32, 34)))
            using (var small = new Font("Segoe UI", FontZ(7.5f)))
            using (var bold = new Font("Segoe UI Semibold", FontZ(8f)))
            using (var cap = new Font("Segoe UI", 7.5f))
            using (var head = new Font("Segoe UI Semibold", 10f))
            using (var white = new SolidBrush(Color.White))
            using (var label = new SolidBrush(Theme.TextDim))
            using (var headBrush = new SolidBrush(Theme.Accent))
            {
                if (blocks.Count == 0)
                    g.DrawString(L.T("Tento kontejner nemá mřížku (sloty zbraně / výbavy). Obsah je vidět ve stromu vlevo.",
                                     "This container has no grid (weapon / gear slots). Its contents are listed in the tree on the left."), Font, label, Pad, Pad);
                foreach (var h in headers)
                    g.DrawString(h.Key, head, headBrush, h.Value.X, h.Value.Y + 2);
                foreach (var b in blocks)
                {
                    var R = b.R;
                    if (b.Slot)
                    {
                        g.FillRectangle(cellBg, R);
                        var it = b.Mc.Items.FirstOrDefault();
                        if (it == null)
                        {
                            g.DrawRectangle(slotPen, R);
                            g.DrawString(L.T("prázdné", "empty"), small, label, R.X + Theme.S(6), R.Y + Theme.S(6));
                        }
                        else DrawTile(g, it, Rectangle.Inflate(R, -2, -2), small, bold, white);
                        continue;
                    }
                    var ge = b.Ge; var mc = b.Mc;
                    int free = 0; var occ = mc.Save.Occupancy(mc, ge, null);
                    for (int r = 0; r < ge.Rows; r++) for (int c = 0; c < ge.Cols; c++) if (ge.Enabled[r, c] && occ[r, c] == null) free++;
                    g.DrawString(ElementLabel(mc, ge, free), cap, label, R.X, R.Y - CapH + 1);
                    for (int r = 0; r < ge.Rows; r++)
                        for (int c = 0; c < ge.Cols; c++)
                        {
                            var cr = new Rectangle(R.X + c * Cell, R.Y + r * Cell, Cell, Cell);
                            g.FillRectangle(ge.Enabled[r, c] ? (Brush)cellBg : hatch, cr);
                            g.DrawRectangle(gridPen, cr);
                        }
                    foreach (var it in mc.Items.Where(i => i.ContainerIndex == ge.Index && i.SlotIndex >= 0))
                    {
                        int r0 = it.SlotIndex / ge.Cols, c0 = it.SlotIndex % ge.Cols;
                        var ir = new Rectangle(R.X + c0 * Cell + 2, R.Y + r0 * Cell + 2, it.W * Cell - 4, it.H * Cell - 4);
                        DrawTile(g, it, ir, small, bold, white);
                    }
                }
                // drag ghost (green = fits, red = doesn't)
                if (dragging && drag != null)
                {
                    int r, c;
                    var b = DropBlock(mouse, out r, out c);
                    if (b != null)
                    {
                        bool same = b.Mc == drag.Container;
                        int offR = same && dragRot == drag.Rotated ? dragOffR : 0, offC = same && dragRot == drag.Rotated ? dragOffC : 0;
                        int w = dragRot ? drag.BaseH : drag.BaseW, h = dragRot ? drag.BaseW : drag.BaseH;
                        int tr = r - offR, tc = c - offC;
                        bool ok = !b.Mc.Save.IsInside(b.Mc, drag) && b.Mc.Save.Fits(b.Mc, b.Ge, tr, tc, w, h, same ? drag : null);
                        var gr = new Rectangle(b.R.X + tc * Cell + 2, b.R.Y + tr * Cell + 2, w * Cell - 4, h * Cell - 4);
                        using (var gb = new SolidBrush(ok ? Color.FromArgb(110, 80, 200, 120) : Color.FromArgb(110, 220, 60, 60)))
                            g.FillRectangle(gb, gr);
                        g.DrawString(drag.DisplayName + L.T("   (R = otočit)", "   (R = rotate)"), small, white, gr.X + 3, gr.Y + 2);
                    }
                }
                if (banding)
                {
                    var band = BandRect();
                    using (var bb = new SolidBrush(Color.FromArgb(40, Theme.Accent))) g.FillRectangle(bb, band);
                    using (var bp = new Pen(Theme.Accent) { DashStyle = DashStyle.Dash }) g.DrawRectangle(bp, band);
                }
            }
        }
    }
}
