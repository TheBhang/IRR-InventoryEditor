// Hideout (base) facility levels, resources, player level and faction loyalty – model + editor window.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace IrrEditor
{
    /// <summary>A non-inventory save file edited as a property tree.</summary>
    public class SaveDoc
    {
        public string Path;
        public GvasFile G;
        public bool Dirty;
        public static SaveDoc TryLoad(string path) { return File.Exists(path) ? new SaveDoc { Path = path, G = GvasFile.Load(path) } : null; }
        public void Write(string path)
        {
            var bytes = G.ToBytes();
            GvasFile.Parse(bytes); // must parse back
            File.WriteAllBytes(path, bytes);
            Dirty = false;
        }
    }

    public class HideoutUnit
    {
        public string Tag;
        public MapEntry Level, Tracked;
        public MapValue Requirements; // item tag -> count, may be null
        public string Short { get { return Tag.Substring(Tag.LastIndexOf('.') + 1); } }
        public string Name { get { return L.En ? Short : BaseData.Czech(Short) + "  (" + Short + ")"; } }
        public string RequirementsText
        {
            get
            {
                if (Requirements == null) return "";
                if (Requirements.Entries.Count == 0) return "—";
                return string.Join(", ", Requirements.Entries.Select(e => BaseData.TagName(e.Key).Split('.').Last() + " ×" + e.Value));
            }
        }
    }

    public class ResourceRow { public string Tag; public MapEntry Amount, Max; public string Short { get { return Tag.Split('.').Last(); } } }

    public class LoyaltyRow { public string Faction; public Prop Loyalty; }

    public class BaseData
    {
        public SaveDoc Hideout, Resource, Mission;
        public List<HideoutUnit> Units = new List<HideoutUnit>();
        public List<ResourceRow> Resources = new List<ResourceRow>();
        public List<LoyaltyRow> Loyalty = new List<LoyaltyRow>();
        public Prop PlayerLevel, PlayerXP;

        public IEnumerable<SaveDoc> Docs { get { return new[] { Hideout, Resource, Mission }.Where(d => d != null); } }

        public static string TagName(object structKey)
        {
            var p = structKey as PropList;
            return p == null ? "?" : Convert.ToString(p["TagName"]);
        }

        static readonly Dictionary<string, string> cz = new Dictionary<string, string>
        {
            {"Gunsmith","Zbrojíř"},{"Repair Station","Opravna"},{"Generator","Generátor"},{"Lighting","Osvětlení"},
            {"Shooting Range","Střelnice"},{"Structure","Konstrukce"},{"Intel Center","Zpravodajské centrum"},
            {"Terminal","Terminál"},{"Stash","Sklad"},{"Oil","Ropa"},{"Concrete","Beton"},{"Metal","Kov"},{"Food","Jídlo"}
        };
        public static string Czech(string en) { string c; return cz.TryGetValue(en, out c) ? c : en; }

        static MapValue Map(GvasFile g, string name) { return g == null ? null : g.Root[name] as MapValue; }

        public static BaseData Load(string dir)
        {
            var b = new BaseData
            {
                Hideout = SaveDoc.TryLoad(System.IO.Path.Combine(dir, "SafeHouseSaveGame.sav")),
                Resource = SaveDoc.TryLoad(System.IO.Path.Combine(dir, "ResourceSaveGame.sav")),
                Mission = SaveDoc.TryLoad(System.IO.Path.Combine(dir, "MissionSaveGame.sav")),
            };
            // hideout
            var levels = Map(b.Hideout == null ? null : b.Hideout.G, "HideoutUnitLevels");
            var tracked = Map(b.Hideout == null ? null : b.Hideout.G, "HideoutUnitTracked");
            var req = Map(b.Hideout == null ? null : b.Hideout.G, "RequiredItemsForNextUpgrade");
            if (levels != null)
                foreach (var e in levels.Entries.Where(x => x.Value is int))
                {
                    var tag = TagName(e.Key);
                    var u = new HideoutUnit { Tag = tag, Level = e };
                    if (tracked != null) u.Tracked = tracked.Entries.FirstOrDefault(x => TagName(x.Key) == tag && x.Value is bool);
                    if (req != null)
                    {
                        var r = req.Entries.FirstOrDefault(x => TagName(x.Key) == tag);
                        if (r != null && r.Value is PropList) u.Requirements = ((PropList)r.Value)["Items"] as MapValue;
                    }
                    b.Units.Add(u);
                }
            // resources: union of Amounts and MaxAmounts
            var am = Map(b.Resource == null ? null : b.Resource.G, "Amounts");
            var mx = Map(b.Resource == null ? null : b.Resource.G, "MaxAmounts");
            if (am != null || mx != null)
            {
                var tags = (am == null ? new MapEntry[0] : am.Entries.ToArray()).Concat(mx == null ? new MapEntry[0] : mx.Entries.ToArray())
                           .Select(e => TagName(e.Key)).Distinct();
                foreach (var t in tags)
                    b.Resources.Add(new ResourceRow
                    {
                        Tag = t,
                        Amount = am == null ? null : am.Entries.FirstOrDefault(e => TagName(e.Key) == t && e.Value is int),
                        Max = mx == null ? null : mx.Entries.FirstOrDefault(e => TagName(e.Key) == t && e.Value is int)
                    });
            }
            // player progress
            var prog = b.Mission == null ? null : b.Mission.G.Root["ProgressData"] as PropList;
            if (prog != null)
            {
                b.PlayerLevel = prog.Get("Level"); b.PlayerXP = prog.Get("CurrentXP");
                if (b.PlayerLevel != null && !(b.PlayerLevel.Value is int)) b.PlayerLevel = null;
                if (b.PlayerXP != null && !(b.PlayerXP.Value is float)) b.PlayerXP = null;
                var loy = prog["LoyaltyData"] as ArrayValue;
                if (loy != null)
                    foreach (var f in loy.Items.OfType<PropList>())
                    {
                        var lp = f.Get("Loyalty");
                        if (lp == null || !(lp.Value is float)) continue;
                        b.Loyalty.Add(new LoyaltyRow { Faction = TagName(f["Faction"]).Split('.').Last(), Loyalty = lp });
                    }
            }
            return b;
        }

        /// <summary>Set a resource amount, creating the map entry (cloned from MaxAmounts' key) when it's missing.</summary>
        public void SetAmount(ResourceRow r, int value)
        {
            if (r.Amount == null)
            {
                var am = Map(Resource.G, "Amounts");
                if (am == null || r.Max == null) return;
                r.Amount = new MapEntry { Key = ((PropList)r.Max.Key).DeepClone(), Value = value };
                am.Entries.Add(r.Amount);
            }
            else r.Amount.Value = value;
            Resource.Dirty = true;
        }
    }

    // ====================================================================== editor window
    public class BaseForm : Form
    {
        readonly BaseData data;
        DataGridView units, res, loy;
        NumBox level, xp;
        public bool Changed;

        public BaseForm(BaseData d)
        {
            data = d;
            Text = L.T("Báze, suroviny a reputace", "Base, resources and reputation");
            Width = 1100; Height = 820; MinimumSize = new Size(900, 650);
            StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 9.5f);
            ShowInTaskbar = false; MinimizeBox = false;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(14) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // ---------------- left: hideout units
            var left = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 0) };
            units = MakeGrid();
            units.Columns.Add(TextCol("name", L.T("Zařízení", "Facility"), true, 200));
            units.Columns.Add(TextCol("lvl", L.T("Úroveň", "Level"), false, 55));
            units.Columns.Add(new DataGridViewCheckBoxColumn { Name = "track", HeaderText = L.T("Sledovat", "Track"), FillWeight = 50 });
            units.Columns.Add(TextCol("req", L.T("Potřeba na další úroveň", "Needed for next level"), true, 260));
            units.Columns["req"].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            units.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            foreach (var u in data.Units)
            {
                int r = units.Rows.Add(u.Name, ((int)u.Level.Value).ToString(), u.Tracked != null && (bool)u.Tracked.Value, u.RequirementsText);
                units.Rows[r].Tag = u;
                if (u.Tracked == null) units.Rows[r].Cells["track"].ReadOnly = true;
            }
            var unitBtns = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
            unitBtns.Controls.Add(Btn(L.T("+1 úroveň vybraným", "+1 level to selected"), delegate { BumpSelected(1); }));
            unitBtns.Controls.Add(Btn("−1", delegate { BumpSelected(-1); }));
            unitBtns.Controls.Add(Btn(L.T("Vynulovat požadavky vybraným", "Clear requirements of selected"), delegate { ClearReqs(false); }));
            unitBtns.Controls.Add(Btn(L.T("…všem", "…of all"), delegate { ClearReqs(true); }));
            var unitHint = Hint(L.T("Úroveň −1 = zařízení není postavené. Maximální úrovně v savu nejsou – zvyšuj po jedné a ověř ve hře. " +
                                    "„Potřeba na další úroveň“ je mezipaměť hry; vynulováním bude další upgrade zdarma (experimentální).",
                                    "Level −1 = facility not built. Max levels aren't stored in the save – raise them one at a time and check in game. " +
                                    "“Needed for next level” is a cache kept by the game; clearing it should make the next upgrade free (experimental)."));
            left.Controls.Add(units); left.Controls.Add(unitBtns); left.Controls.Add(unitHint); left.Controls.Add(Header(L.T("🏠  Úkryt – úrovně zařízení", "🏠  Hideout – facility levels")));

            // ---------------- right: resources + progress
            var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(0) };
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 58));

            var resPanel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12) };
            res = MakeGrid();
            res.Columns.Add(TextCol("name", L.T("Surovina", "Resource"), true, 110));
            res.Columns.Add(TextCol("amt", L.T("Množství", "Amount"), false, 80));
            res.Columns.Add(TextCol("max", "Maximum", false, 80));
            foreach (var r in data.Resources)
            {
                int i = res.Rows.Add(L.En ? r.Short : BaseData.Czech(r.Short) + " (" + r.Short + ")", r.Amount == null ? "0" : r.Amount.Value.ToString(), r.Max == null ? "" : r.Max.Value.ToString());
                res.Rows[i].Tag = r;
                if (r.Max == null) res.Rows[i].Cells["max"].ReadOnly = true;
            }
            var resBtns = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
            resBtns.Controls.Add(Btn(L.T("Doplnit vše na maximum", "Fill all to maximum"), delegate { FillResources(); }));
            resPanel.Controls.Add(res); resPanel.Controls.Add(resBtns); resPanel.Controls.Add(Header(L.T("⛏  Suroviny úkrytu", "⛏  Hideout resources")));

            var progPanel = new Panel { Dock = DockStyle.Fill };
            var lv = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Padding = new Padding(0, 4, 0, 8) };
            level = new NumBox { Minimum = 1, Maximum = 999, Width = 70, Value = data.PlayerLevel == null ? 1 : (int)data.PlayerLevel.Value, Enabled = data.PlayerLevel != null };
            xp = new NumBox { Minimum = 0, Maximum = 10000000, Width = 90, Value = data.PlayerXP == null ? 0 : (decimal)Math.Round((float)data.PlayerXP.Value), Enabled = data.PlayerXP != null };
            lv.Controls.Add(new Label { Text = L.T("Level postavy", "Player level"), AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
            lv.Controls.Add(level);
            lv.Controls.Add(new Label { Text = "XP", AutoSize = true, Margin = new Padding(16, 6, 8, 0) });
            lv.Controls.Add(xp);
            loy = MakeGrid();
            loy.Columns.Add(TextCol("f", L.T("Frakce", "Faction"), true, 120));
            loy.Columns.Add(TextCol("v", L.T("Loajalita", "Loyalty"), false, 90));
            foreach (var l in data.Loyalty)
            {
                int i = loy.Rows.Add(l.Faction, ((float)l.Loyalty.Value).ToString("0.##", CultureInfo.CurrentCulture));
                loy.Rows[i].Tag = l;
            }
            var loyBtns = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
            loyBtns.Controls.Add(Btn(L.T("Všem frakcím 100", "Set all factions to 100"), delegate { foreach (DataGridViewRow r in loy.Rows) r.Cells["v"].Value = "100"; }));
            var loyHint = Hint(L.T("V tvém savu mají frakce hodnoty kolem 100 – vyšší hodnoty můžou, ale nemusí, hra respektovat.",
                                   "Factions usually sit around 100 – the game may or may not respect higher values."));
            progPanel.Controls.Add(loy); progPanel.Controls.Add(loyBtns); progPanel.Controls.Add(loyHint); progPanel.Controls.Add(lv); progPanel.Controls.Add(Header(L.T("🎖  Postava a reputace u frakcí", "🎖  Player and faction reputation")));

            right.Controls.Add(resPanel, 0, 0);
            right.Controls.Add(progPanel, 0, 1);

            root.Controls.Add(left, 0, 0);
            root.Controls.Add(right, 1, 0);

            var missing = new List<string>();
            if (data.Hideout == null) missing.Add("SafeHouseSaveGame.sav");
            if (data.Resource == null) missing.Add("ResourceSaveGame.sav");
            if (data.Mission == null) missing.Add("MissionSaveGame.sav");
            if (missing.Count > 0) { var m = Hint(L.T("Chybí: ", "Missing: ") + string.Join(", ", missing)); m.ForeColor = Theme.Warn; root.Controls.Add(m, 0, 1); root.SetColumnSpan(m, 2); }

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 10, 0, 0) };
            var ok = Theme.MakeButton(L.T("Použít", "Apply"), DialogResult.OK, true);
            var cancel = Theme.MakeButton(L.T("Zrušit", "Cancel"), DialogResult.Cancel, false);
            bottom.Controls.Add(ok); bottom.Controls.Add(cancel);
            bottom.Controls.Add(new Label { Text = L.T("Změny se do souborů zapíšou až tlačítkem 💾 Uložit v hlavním okně (se zálohou).",
                                                      "Changes are written to the files only by 💾 Save in the main window (with a backup)."), AutoSize = true, ForeColor = Theme.TextDim, Margin = new Padding(0, 8, 16, 0) });
            root.Controls.Add(bottom, 0, 2); root.SetColumnSpan(bottom, 2);
            AcceptButton = ok; CancelButton = cancel;
            Controls.Add(root);

            Theme.Apply(this);
            foreach (var g in new[] { units, res, loy }) Theme.StyleGrid(g);
            ok.BackColor = Theme.AccentBg; ok.FlatAppearance.BorderColor = Theme.Accent; // primary action
            FormClosing += (s, e) => { if (DialogResult == DialogResult.OK && !Apply()) e.Cancel = true; };
        }

        // ---------------------------------------------------------------- helpers
        static DataGridView MakeGrid()
        {
            return new DataGridView
            {
                Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true, EditMode = DataGridViewEditMode.EditOnEnter,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, ColumnHeadersHeight = 30,
                RowTemplate = { Height = 28 }
            };
        }
        static DataGridViewTextBoxColumn TextCol(string name, string header, bool ro, int weight)
        {
            return new DataGridViewTextBoxColumn { Name = name, HeaderText = header, ReadOnly = ro, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable };
        }
        static Label Header(string t)
        {
            return new Label { Text = t, Dock = DockStyle.Top, Height = 32, Font = new Font("Segoe UI Semibold", 11.5f), ForeColor = Theme.Accent, TextAlign = ContentAlignment.MiddleLeft };
        }
        static Label Hint(string t)
        {
            // height grows with the text so longer (e.g. English) hints are never clipped
            var l = new Label { Text = t, Dock = DockStyle.Bottom, AutoSize = false, Height = 44, ForeColor = Theme.TextDim, Padding = new Padding(0, 6, 0, 0) };
            l.SizeChanged += delegate
            {
                int h = TextRenderer.MeasureText(l.Text, l.Font, new Size(Math.Max(50, l.Width), 0), TextFormatFlags.WordBreak).Height + 12;
                if (l.Width > 50 && Math.Abs(l.Height - h) > 2) l.Height = h;
            };
            return l;
        }
        Button Btn(string t, EventHandler h)
        {
            var b = Theme.MakeButton(t, DialogResult.None, false);
            b.Margin = new Padding(0, 0, 8, 0);
            b.Click += h;
            return b;
        }

        static int ParseInt(object v, int fallback)
        {
            int i; var s = new string(Convert.ToString(v).Where(c => char.IsDigit(c) || c == '-').ToArray());
            return int.TryParse(s, out i) ? i : fallback;
        }
        static float? ParseFloat(object v)
        {
            float f; var s = Convert.ToString(v);
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out f) || float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) return f;
            return null;
        }

        void BumpSelected(int delta)
        {
            units.EndEdit();
            foreach (DataGridViewRow r in units.SelectedRows)
            {
                int v = ParseInt(r.Cells["lvl"].Value, 0) + delta;
                r.Cells["lvl"].Value = Math.Max(-1, v).ToString();
            }
        }

        readonly HashSet<HideoutUnit> clearReq = new HashSet<HideoutUnit>();
        void ClearReqs(bool all)
        {
            foreach (DataGridViewRow r in units.Rows)
            {
                if (!all && !r.Selected) continue;
                var u = (HideoutUnit)r.Tag;
                if (u.Requirements == null || u.Requirements.Entries.Count == 0) continue;
                clearReq.Add(u);
                r.Cells["req"].Value = L.T("— (vynulováno)", "— (cleared)");
            }
        }

        void FillResources()
        {
            res.EndEdit();
            foreach (DataGridViewRow r in res.Rows)
            {
                var max = Convert.ToString(r.Cells["max"].Value);
                if (max.Length > 0) r.Cells["amt"].Value = max;
            }
        }

        /// <summary>Validate the grids and write the values into the property trees.</summary>
        bool Apply()
        {
            foreach (var g in new[] { units, res, loy }) g.EndEdit();
            // hideout
            foreach (DataGridViewRow r in units.Rows)
            {
                var u = (HideoutUnit)r.Tag;
                int lvl = ParseInt(r.Cells["lvl"].Value, int.MinValue);
                if (lvl < -1 || lvl > 50) { DarkMsg.Show(this, L.T("Neplatná úroveň u " + u.Short + " (povoleno −1 až 50).", "Invalid level for " + u.Short + " (allowed −1 to 50)."), L.T("Báze", "Base"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
                if ((int)u.Level.Value != lvl) { u.Level.Value = lvl; data.Hideout.Dirty = true; Changed = true; }
                if (u.Tracked != null)
                {
                    bool t = Convert.ToBoolean(r.Cells["track"].Value);
                    if ((bool)u.Tracked.Value != t) { u.Tracked.Value = t; data.Hideout.Dirty = true; Changed = true; }
                }
                if (clearReq.Contains(u)) { u.Requirements.Entries.Clear(); data.Hideout.Dirty = true; Changed = true; }
            }
            // resources
            foreach (DataGridViewRow r in res.Rows)
            {
                var rr = (ResourceRow)r.Tag;
                int amt = ParseInt(r.Cells["amt"].Value, -1);
                if (amt < 0) { DarkMsg.Show(this, L.T("Neplatné množství u " + rr.Short + ".", "Invalid amount for " + rr.Short + "."), L.T("Suroviny", "Resources"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
                if (rr.Max != null)
                {
                    int mx = ParseInt(r.Cells["max"].Value, -1);
                    if (mx < 0) { DarkMsg.Show(this, L.T("Neplatné maximum u " + rr.Short + ".", "Invalid maximum for " + rr.Short + "."), L.T("Suroviny", "Resources"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
                    if ((int)rr.Max.Value != mx) { rr.Max.Value = mx; data.Resource.Dirty = true; Changed = true; }
                }
                int cur = rr.Amount == null ? 0 : (int)rr.Amount.Value;
                if (cur != amt || (rr.Amount == null && amt > 0)) { data.SetAmount(rr, amt); Changed = true; }
            }
            // progress
            if (data.PlayerLevel != null && (int)data.PlayerLevel.Value != (int)level.Value) { data.PlayerLevel.Value = (int)level.Value; data.Mission.Dirty = true; Changed = true; }
            if (data.PlayerXP != null && Math.Abs((float)data.PlayerXP.Value - (float)xp.Value) >= 0.5f) { data.PlayerXP.Value = (float)xp.Value; data.Mission.Dirty = true; Changed = true; }
            foreach (DataGridViewRow r in loy.Rows)
            {
                var l = (LoyaltyRow)r.Tag;
                var f = ParseFloat(r.Cells["v"].Value);
                if (f == null) { DarkMsg.Show(this, L.T("Neplatná loajalita u " + l.Faction + ".", "Invalid loyalty for " + l.Faction + "."), L.T("Reputace", "Reputation"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
                if (Math.Abs((float)l.Loyalty.Value - f.Value) > 0.0001f) { l.Loyalty.Value = f.Value; data.Mission.Dirty = true; Changed = true; }
            }
            return true;
        }
    }
}
