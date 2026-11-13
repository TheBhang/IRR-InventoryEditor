// WinForms UI for the Incursion Red River inventory editor.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace IrrEditor
{
    public class MainForm : Form
    {
        string dir;
        InvSave stash, inv, vendor;
        BaseData baseData;
        MainContainer curMC;
        InvItem selItem;
        readonly Stack<MainContainer> history = new Stack<MainContainer>();

        TreeView tree;
        GridPanel grid;
        Label gridTitle;
        Button btnBack;
        // details
        Label lblName, lblInfo;
        TextBox txtDef;
        NumBox numCount, numMax;
        CheckBox chkQuest;
        DataGridView stats;
        ListBox contents;
        Panel details;
        Label emptyHint;
        ToolStripStatusLabel status, warn;
        bool loadingDetails;

        public MainForm(string folder)
        {
            Text = AppTitle;
            Width = 1500; Height = 900;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            KeyPreview = true;
            BuildUi();
            Theme.Apply(this);
            warn.ForeColor = Theme.Warn;
            dir = folder;
            Shown += delegate { LoadAll(); };
            FormClosing += OnClosing;
            KeyDown += OnKey;
        }

        // ================================================================== UI construction
        void BuildUi()
        {
            var ts = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(4), ImageScalingSize = new Size(16, 16), ShowItemToolTips = false };
            var darkTip = Theme.MakeTip();
            // native ToolStrip tooltips can't be themed – show our own dark one
            Action<ToolStripItem, string> tipFor = (item, text) =>
            {
                item.MouseEnter += delegate { if (!(item is ToolStripDropDownItem && ((ToolStripDropDownItem)item).DropDown.Visible)) { darkTip.Tag = text; darkTip.Show(text, ts, item.Bounds.X, item.Bounds.Bottom + 6, 4000); } };
                item.MouseLeave += delegate { darkTip.Hide(ts); };
                item.MouseDown += delegate { darkTip.Hide(ts); };
            };
            Action<string, string, EventHandler> add = (t, tip, h) => { var b = new ToolStripButton(t); tipFor(b, tip); b.Click += h; ts.Items.Add(b); };
            add(L.T("📂 Složka…", "📂 Folder…"), L.T("Vybrat složku SaveGames", "Choose the SaveGames folder"), delegate { PickFolder(); });
            add(L.T("⟳ Znovu načíst", "⟳ Reload"), L.T("Zahodit neuložené změny a načíst znovu", "Discard unsaved changes and reload"), delegate { if (ConfirmDiscard()) LoadAll(); });
            add(L.T("💾 Uložit", "💾 Save"), L.T("Uložit změny (předtím se udělá záloha)", "Save changes (a backup is made first)"), delegate { SaveAll(); });
            ts.Items.Add(new ToolStripSeparator());
            add(L.T("＋ Přidat předmět…", "＋ Add item…"), L.T("Přidat předmět z katalogu do otevřeného kontejneru (Ins)", "Add an item from the catalog to the open container (Ins)"), delegate { AddItemDialog(); });
            add(L.T("⧉ Duplikovat", "⧉ Duplicate"), L.T("Duplikovat vybraný předmět i s obsahem (Ctrl+D)", "Duplicate the selected item with its contents (Ctrl+D)"), delegate { Duplicate(); });
            add(L.T("✕ Smazat", "✕ Delete"), L.T("Smazat vybraný předmět i s obsahem (Del)", "Delete the selected item with its contents (Del)"), delegate { DeleteSel(); });
            add(L.T("↻ Otočit", "↻ Rotate"), L.T("Otočit vybraný předmět (R)", "Rotate the selected item (R)"), delegate { RotateSel(); });
            ts.Items.Add(new ToolStripSeparator());
            var smart = new ToolStripDropDownButton(L.T("✨ Chytré nástroje", "✨ Smart tools"));
            Action<string, EventHandler> sm = (t, h) => { var mi = new ToolStripMenuItem(t); mi.Click += h; smart.DropDownItems.Add(mi); };
            sm(L.T("Opravit vše (výdrž na hodnotu nového kusu)", "Repair all (durability of a brand-new item)"), delegate { Bulk(L.T("Opraveno", "Repaired"), s => Tools.RepairAll(s)); });
            sm(L.T("Doplnit všechny stacky na maximum", "Fill all stacks to maximum"), delegate { Bulk(L.T("Doplněno stacků", "Stacks filled"), s => Tools.FillStacks(s)); });
            sm(L.T("Naplnit zásobníky (podle kapacity v názvu)", "Fill magazines (capacity from the name)"), delegate { Bulk(L.T("Naplněno zásobníků", "Magazines filled"), s => Tools.FillMagazines(s)); });
            smart.DropDownItems.Add(new ToolStripSeparator());
            sm(L.T("Sloučit stacky v otevřeném kontejneru", "Merge stacks in the open container"), delegate { MergeCur(); });
            sm(L.T("Seřadit otevřený kontejner (podle kategorie a velikosti)", "Sort the open container (by category and size)"), delegate { SortCur(); });
            smart.DropDownItems.Add(new ToolStripSeparator());
            sm(L.T("Odemknout zamčené strany skladu (experimentální)", "Unlock locked stash pages (experimental)"), delegate { UnlockPages(); });
            sm(L.T("Zkontrolovat konzistenci (překryvy, mimo mřížku)", "Check consistency (overlaps, out of grid)"), delegate { ShowValidation(true); });
            ts.Items.Add(smart);
            ts.Items.Add(new ToolStripSeparator());
            add(L.T("🏠 Báze && reputace", "🏠 Base && reputation"), L.T("Úrovně zařízení v úkrytu, suroviny, level postavy a loajalita frakcí", "Hideout facility levels, resources, player level and faction loyalty"), delegate { OpenBase(); });
            ts.Items.Add(new ToolStripSeparator());
            add(L.T("🗂 Zálohy", "🗂 Backups"), L.T("Otevřít složku se zálohami", "Open the backups folder"), delegate { var b = BackupRoot(); Directory.CreateDirectory(b); Process.Start("explorer.exe", "\"" + b + "\""); });
            // language switch on the right
            var langBtn = new ToolStripDropDownButton(L.En ? "🌐 English" : "🌐 Čeština") { Alignment = ToolStripItemAlignment.Right };
            var miCz = new ToolStripMenuItem("Čeština") { Checked = !L.En };
            var miEn = new ToolStripMenuItem("English") { Checked = L.En };
            miCz.Click += delegate { SwitchLanguage(false); };
            miEn.Click += delegate { SwitchLanguage(true); };
            langBtn.DropDownItems.Add(miCz); langBtn.DropDownItems.Add(miEn);
            ts.Items.Add(langBtn);
            var about = new ToolStripButton(L.T("ⓘ O aplikaci", "ⓘ About")) { Alignment = ToolStripItemAlignment.Right };
            about.Click += delegate { ShowAbout(); };
            ts.Items.Add(about);

            var ss = new StatusStrip();
            status = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            warn = new ToolStripStatusLabel { ForeColor = Color.DarkRed };
            ss.Items.Add(status); ss.Items.Add(warn);

            tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, FullRowSelect = true, ShowLines = true, Font = new Font("Segoe UI", 9.5f) };
            tree.AfterSelect += OnTreeSelect;

            grid = new GridPanel { Dock = DockStyle.Fill, BackColor = Theme.Bg, AutoScroll = true };
            grid.Owner = this;
            Theme.DarkNative(grid);
            gridTitle = new Label { Dock = DockStyle.Fill, ForeColor = Theme.Text, Font = new Font("Segoe UI Semibold", 11f), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            btnBack = new Button { Text = L.T("← Zpět", "← Back"), Dock = DockStyle.Left, Width = 84 };
            btnBack.Click += delegate { GoBack(); };
            var gridHead = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Theme.Panel, Padding = new Padding(6) };
            gridHead.Controls.Add(gridTitle); gridHead.Controls.Add(btnBack);
            var hint = new Label { Dock = DockStyle.Bottom, Height = 24, ForeColor = Theme.TextDim, BackColor = Theme.Panel, Padding = new Padding(8, 4, 0, 0), AutoEllipsis = true,
                Text = L.T("Táhni myší = přesun · R = otočit · Del = smazat · Ctrl+D = duplikovat · Ins = přidat · dvojklik = otevřít batoh/vestu/zbraň",
                           "Drag = move · R = rotate · Del = delete · Ctrl+D = duplicate · Ins = add · double-click = open backpack/rig/weapon") };
            var center = new Panel { Dock = DockStyle.Fill };
            center.Controls.Add(grid); center.Controls.Add(hint); center.Controls.Add(gridHead);

            details = BuildDetails();

            var splitR = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2 };
            splitR.Panel1.Controls.Add(center);
            emptyHint = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.TextDim,
                Text = L.T("Klikni na předmět v mřížce nebo ve stromu\npro zobrazení a úpravu detailů.", "Click an item in the grid or the tree\nto view and edit its details.") };
            splitR.Panel2.Controls.Add(details);
            splitR.Panel2.Controls.Add(emptyHint);
            var splitL = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
            splitL.Panel1.Controls.Add(tree);
            splitL.Panel2.Controls.Add(splitR);

            Controls.Add(splitL);
            Controls.Add(ts);
            Controls.Add(ss);
            Load += delegate { splitL.SplitterDistance = 300; splitR.SplitterDistance = Math.Max(200, splitR.Width - 380); };
        }

        Panel BuildDetails()
        {
            var p = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8), AutoScroll = true };
            var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            lblName = new Label { AutoSize = true, Font = new Font("Segoe UI Semibold", 12f), MaximumSize = new Size(340, 0) };
            lblInfo = new Label { AutoSize = true, ForeColor = Theme.TextDim, MaximumSize = new Size(340, 0), Margin = new Padding(3, 2, 3, 8) };
            txtDef = new TextBox { ReadOnly = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
            numCount = new NumBox { Maximum = int.MaxValue, Minimum = 0, Dock = DockStyle.Fill, ThousandsSeparator = true };
            numMax = new NumBox { Maximum = int.MaxValue, Minimum = 1, Dock = DockStyle.Fill, ThousandsSeparator = true };
            chkQuest = new CheckBox { Text = L.T("Questový předmět", "Quest item"), AutoSize = true };
            numCount.ValueChanged += delegate { if (!loadingDetails && selItem != null) { selItem.Count = (int)numCount.Value; Changed(selItem); } };
            numMax.ValueChanged += delegate { if (!loadingDetails && selItem != null) { selItem.MaxStack = (int)numMax.Value; Changed(selItem); } };
            chkQuest.CheckedChanged += delegate { if (!loadingDetails && selItem != null) { selItem.Quest = chkQuest.Checked; Changed(selItem); } };

            var btnFill = new Button { Text = "Max", Width = 50 };
            btnFill.Click += delegate { if (selItem != null) numCount.Value = Math.Max(selItem.MaxStack, 1); };
            var btnRepair = new Button { Text = L.T("🔧 Opravit", "🔧 Repair"), AutoSize = true };
            btnRepair.Click += delegate { if (selItem != null) { if (Tools.Repair(selItem)) Changed(selItem); ShowDetails(selItem); } };
            var countRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            numCount.Width = 140; countRow.Controls.Add(numCount); countRow.Controls.Add(btnFill);

            stats = new DataGridView
            {
                Dock = DockStyle.Top, Height = 260, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.CellSelect,
                BackgroundColor = SystemColors.Window, BorderStyle = BorderStyle.FixedSingle
            };
            stats.Columns.Add("stat", L.T("Statistika", "Stat")); stats.Columns.Add("val", L.T("Hodnota", "Value")); stats.Columns.Add("type", L.T("Typ", "Type"));
            stats.Columns[0].ReadOnly = true; stats.Columns[2].ReadOnly = true; stats.Columns[2].FillWeight = 50;
            stats.CellValueChanged += OnStatEdited;

            contents = new ListBox { Dock = DockStyle.Top, Height = 160, IntegralHeight = false };
            contents.DoubleClick += delegate { var it = contents.SelectedItem as ContentEntry; if (it != null) SelectItem(it.Item, true); };

            Func<string, Label> Lbl = s => new Label { Text = s, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 0) };
            t.Controls.Add(lblName, 0, 0); t.SetColumnSpan(lblName, 2);
            t.Controls.Add(lblInfo, 0, 1); t.SetColumnSpan(lblInfo, 2);
            t.Controls.Add(Lbl(L.T("Definice", "Definition")), 0, 2); t.Controls.Add(txtDef, 1, 2);
            t.Controls.Add(Lbl(L.T("Počet", "Count")), 0, 3); t.Controls.Add(countRow, 1, 3);
            t.Controls.Add(Lbl("Max stack"), 0, 4); t.Controls.Add(numMax, 1, 4);
            t.Controls.Add(chkQuest, 1, 5);
            t.Controls.Add(btnRepair, 1, 6);

            var lStats = new Label { Text = L.T("Statistiky (hodnoty jdou editovat)", "Stats (values are editable)"), Dock = DockStyle.Top, Height = 24, TextAlign = ContentAlignment.BottomLeft, Font = new Font("Segoe UI Semibold", 9f) };
            var lCont = new Label { Text = L.T("Obsah / nasazené díly (dvojklik = vybrat)", "Contents / attached parts (double-click = select)"), Dock = DockStyle.Top, Height = 24, TextAlign = ContentAlignment.BottomLeft, Font = new Font("Segoe UI Semibold", 9f) };
            p.Controls.Add(contents); p.Controls.Add(lCont); p.Controls.Add(stats); p.Controls.Add(lStats); p.Controls.Add(t);
            p.Visible = false;
            return p;
        }

        class ContentEntry
        {
            public InvItem Item; public int Depth;
            public override string ToString() { return new string(' ', Depth * 3) + "• " + Item.DisplayName + (Item.MaxStack > 1 || Item.Count > 1 ? "  ×" + Item.Count : ""); }
        }

        // ================================================================== loading / saving
        public static string DefaultFolder()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Test_C\Saved\SaveGames");
        }

        void PickFolder()
        {
            if (!ConfirmDiscard()) return;
            using (var d = new FolderBrowserDialog { Description = L.T("Vyber složku SaveGames (obsahuje StashSaveGame.sav)", "Choose the SaveGames folder (contains StashSaveGame.sav)"), SelectedPath = dir })
                if (d.ShowDialog(this) == DialogResult.OK) { dir = d.SelectedPath; LoadAll(); }
        }

        void LoadAll()
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                string sp = Path.Combine(dir, "StashSaveGame.sav"), ip = Path.Combine(dir, "InventorySaveGame.sav"), vp = Path.Combine(dir, "VendorSaveGame.sav");
                if (!File.Exists(sp) && !File.Exists(ip))
                {
                    status.Text = L.T("Ve složce " + dir + " nejsou savy. Použij „Složka…“.", "No saves found in " + dir + ". Use “Folder…”.");
                    return;
                }
                stash = File.Exists(sp) ? InvSave.Load(sp, "Stash") : null;
                inv = File.Exists(ip) ? InvSave.Load(ip, "Inventory") : null;
                vendor = File.Exists(vp) ? InvSave.Load(vp, "Vendor") : null;
                try { baseData = BaseData.Load(dir); }
                catch (Exception bex) { baseData = null; status.Text = L.T("Báze se nenačetla: ", "Base data failed to load: ") + bex.Message; }
                Catalog.Build(new[] { vendor, stash, inv }.Where(x => x != null));
                history.Clear();
                RebuildTree();
                var main = stash != null ? stash.Containers.FirstOrDefault(c => c.IsTopLevel && c.HasGrid) : null;
                if (main != null) ShowContainer(main, false);
                status.Text = L.T("Načteno: ", "Loaded: ") + dir + "   ·   " + L.T("katalog " + Catalog.Entries.Count + " předmětů", "catalog of " + Catalog.Entries.Count + " items");
                ShowValidation(false);
            }
            catch (Exception ex)
            {
                DarkMsg.Show(this, L.T("Chyba při načítání:\n", "Error while loading:\n") + ex, L.T("Chyba", "Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { Cursor = Cursors.Default; }
        }

        static bool GameRunning()
        {
            return Process.GetProcesses().Any(p =>
            {
                try { var n = p.ProcessName; return n.StartsWith("Test_C", StringComparison.OrdinalIgnoreCase) || n.IndexOf("IncursionRedRiver", StringComparison.OrdinalIgnoreCase) >= 0; }
                catch { return false; }
            });
        }

        string BackupRoot() { return Path.Combine(dir, "_IRR_Editor_Backups"); }

        void SaveAll()
        {
            var dirty = new[] { stash, inv }.Where(s => s != null && s.Dirty).ToList();
            var dirtyDocs = baseData == null ? new List<SaveDoc>() : baseData.Docs.Where(d => d.Dirty).ToList();
            if (dirty.Count == 0 && dirtyDocs.Count == 0) { status.Text = L.T("Žádné změny k uložení.", "Nothing to save."); return; }
            if (GameRunning())
            {
                DarkMsg.Show(this, L.T("Hra právě běží. Ukládej jen když je hra vypnutá – jinak ji hra při ukončení přepíše (nebo se rozbije stav).",
                                       "The game is running. Only save while the game is closed – otherwise the game overwrites the files on exit (or the state breaks)."),
                             L.T("Hra běží", "Game is running"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var problems = dirty.SelectMany(s => s.Validate()).ToList();
            if (problems.Count > 0 && DarkMsg.Show(this, L.T("Nalezené problémy:\n\n", "Problems found:\n\n") + string.Join("\n", problems.Take(15)) + L.T("\n\nPřesto uložit?", "\n\nSave anyway?"),
                                                   L.T("Kontrola", "Check"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                var bdir = Path.Combine(BackupRoot(), DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(bdir);
                foreach (var f in Directory.GetFiles(dir, "*.sav")) File.Copy(f, Path.Combine(bdir, Path.GetFileName(f)), true);
                var writes = dirty.Select(s => new KeyValuePair<string, Action<string>>(s.Path, s.Write))
                    .Concat(dirtyDocs.Select(d => new KeyValuePair<string, Action<string>>(d.Path, d.Write))).ToList();
                foreach (var w in writes)
                {
                    var tmp = w.Key + ".tmp";
                    w.Value(tmp);
                    File.Copy(tmp, w.Key, true);
                    File.Delete(tmp);
                }
                status.Text = L.T("Uloženo (", "Saved (") + string.Join(", ", writes.Select(w => Path.GetFileName(w.Key))) + L.T("). Záloha: ", "). Backup: ") + bdir;
                UpdateTitle();
            }
            catch (Exception ex)
            {
                DarkMsg.Show(this, L.T("Uložení selhalo:\n", "Saving failed:\n") + ex.Message, L.T("Chyba", "Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        bool AnyDirty()
        {
            return new[] { stash, inv }.Any(s => s != null && s.Dirty) || (baseData != null && baseData.Docs.Any(d => d.Dirty));
        }

        void OpenBase()
        {
            if (baseData == null) return;
            using (var f = new BaseForm(baseData))
                if (f.ShowDialog(this) == DialogResult.OK && f.Changed) { status.Text = L.T("Báze / reputace upravena – nezapomeň uložit.", "Base / reputation changed – don't forget to save."); UpdateTitle(); }
        }

        /// <summary>Restart the window in the other language (the UI is built once in the constructor).</summary>
        public bool RestartRequested;
        public string Folder { get { return dir; } }
        void SwitchLanguage(bool en)
        {
            if (en == L.En) return;
            if (AnyDirty() && DarkMsg.Show(this, L.T("Přepnutí jazyka znovu načte savy a neuložené změny se zahodí. Pokračovat?", "Switching language reloads the saves and discards unsaved changes. Continue?"),
                                           L.T("Jazyk", "Language"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            L.Save(en);
            RestartRequested = true;
            discardOnClose = true;
            Close();
        }
        bool discardOnClose;

        void ShowAbout()
        {
            var v = typeof(MainForm).Assembly.GetName().Version;
            DarkMsg.Show(this,
                "IRR Inventory Editor  v" + v.Major + "." + v.Minor + "." + v.Build + "\n\n" +
                L.T("Neoficiální editor savů pro Incursion Red River – sklad, výbava, báze a reputace.\n" +
                    "Před každým uložením se savy zálohují. Používej jen se zavřenou hrou.\n\n" +
                    "Není spojeno s vývojáři hry (Games Farm).",
                    "Unofficial save editor for Incursion Red River – stash, loadout, base and reputation.\n" +
                    "Saves are backed up before every write. Only use it while the game is closed.\n\n" +
                    "Not affiliated with the game's developers (Games Farm)."),
                L.T("O aplikaci", "About"));
        }

        bool ConfirmDiscard()
        {
            if (!AnyDirty()) return true;
            return DarkMsg.Show(this, L.T("Máš neuložené změny. Zahodit je?", "You have unsaved changes. Discard them?"), L.T("Neuložené změny", "Unsaved changes"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (discardOnClose || !AnyDirty()) return;
            var r = DarkMsg.Show(this, L.T("Uložit změny před zavřením?", "Save changes before closing?"), L.T("Neuložené změny", "Unsaved changes"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) e.Cancel = true;
            else if (r == DialogResult.Yes) { SaveAll(); if (AnyDirty()) e.Cancel = true; }
        }

        public const string AppTitle = "Incursion Red River – Inventory Editor";
        void UpdateTitle()
        {
            bool d = AnyDirty();
            Text = AppTitle + (d ? L.T("  •  neuloženo", "  •  unsaved") : "");
        }

        void ShowValidation(bool popup)
        {
            var issues = new[] { stash, inv }.Where(s => s != null).SelectMany(s => s.Validate()).ToList();
            warn.Text = issues.Count == 0 ? "" : L.T("⚠ " + issues.Count + " problém(ů) – Chytré nástroje → Zkontrolovat", "⚠ " + issues.Count + " issue(s) – Smart tools → Check");
            if (popup)
                DarkMsg.Show(this, issues.Count == 0 ? L.T("Vše v pořádku – žádné překryvy ani předměty mimo mřížku.", "All good – no overlaps and nothing outside the grid.") : string.Join("\n", issues.Take(30)),
                             L.T("Kontrola konzistence", "Consistency check"));
        }

        // ================================================================== tree
        void RebuildTree()
        {
            tree.BeginUpdate();
            tree.Nodes.Clear();
            foreach (var s in new[] { stash, inv })
            {
                if (s == null) continue;
                var root = tree.Nodes.Add(s.Kind == "Stash" ? L.T("📦 Sklad", "📦 Stash") : L.T("🎒 Výbava postavy", "🎒 Character loadout"));
                root.Tag = s;
                foreach (var mc in s.Containers.Where(c => c.Label != null && s.TopUids.Contains(c.UidKey)))
                {
                    var n = root.Nodes.Add(mc.Title + (mc.HasGrid ? "  (" + mc.Items.Count + ")" : ""));
                    n.Tag = mc;
                    AddItemNodes(s, n, mc);
                }
                root.Expand();
            }
            tree.EndUpdate();
        }

        void AddItemNodes(InvSave s, TreeNode parent, MainContainer mc)
        {
            foreach (var it in mc.Items.OrderBy(i => i.ContainerIndex).ThenBy(i => i.SlotIndex))
            {
                var n = parent.Nodes.Add(it.ToString());
                n.Tag = it;
                if (it.Count > it.MaxStack && it.MaxStack > 0) n.ForeColor = Theme.Warn;
                var sub = s.ContainerOf(it);
                if (sub != null && sub.Items.Count > 0) AddItemNodes(s, n, sub);
            }
        }

        TreeNode FindNode(TreeNodeCollection nodes, object tag)
        {
            foreach (TreeNode n in nodes)
            {
                if (n.Tag == tag) return n;
                var c = FindNode(n.Nodes, tag);
                if (c != null) return c;
            }
            return null;
        }

        bool syncingTree;
        void OnTreeSelect(object sender, TreeViewEventArgs e)
        {
            if (syncingTree) return;
            var save = e.Node.Tag as InvSave;
            if (save != null)
            {
                if (save.Kind == "Inventory") ShowLoadout(true);
                else { var main = save.Containers.FirstOrDefault(c => c.IsTopLevel && c.HasGrid); if (main != null) ShowContainer(main, true); }
                SelectItem(null, false);
                return;
            }
            var mc = e.Node.Tag as MainContainer;
            var it = e.Node.Tag as InvItem;
            if (mc != null && mc.HasGrid && !(loadoutView && grid.Shows(mc))) { ShowContainer(mc, true); return; }
            if (mc != null && mc.Items.Count > 0) it = mc.Items[0];
            if (mc != null && mc.Items.Count == 0 && mc.Save.Kind == "Inventory") { ShowLoadout(true); SelectItem(null, false); return; }
            if (it != null)
            {
                // character gear that isn't inside a grid (weapons, slots, attachments) → character overview
                if (it.Container.Save.Kind == "Inventory" && GridAncestor(it) == null && !loadoutView) ShowLoadout(true);
                SelectItem(it, false);
            }
        }

        // ================================================================== navigation / selection
        InvSave SaveOf(MainContainer mc) { return mc == null ? null : mc.Save; }
        bool loadoutView; // character overview (all equipment slots + their grids) is shown

        static MainContainer GridAncestor(InvItem it)
        {
            var probe = it == null ? null : it.Container;
            while (probe != null && !probe.HasGrid && probe.OwnerItem != null) probe = probe.OwnerItem.Container;
            return probe != null && probe.HasGrid ? probe : null;
        }

        void PushCurrentView()
        {
            if (loadoutView) history.Push(null);          // null = character overview
            else if (curMC != null) history.Push(curMC);
        }

        void GoBack()
        {
            if (history.Count == 0) return;
            var m = history.Pop();
            if (m == null) ShowLoadout(false); else ShowContainer(m, false);
        }

        public void ShowContainer(MainContainer mc, bool pushHistory)
        {
            if (pushHistory && (loadoutView || curMC != mc)) PushCurrentView();
            loadoutView = false;
            curMC = mc;
            gridTitle.Text = "  " + mc.Title + "   ·   " + (mc.Save.Kind == "Stash" ? L.T("Sklad", "Stash") : L.T("Výbava", "Loadout"));
            btnBack.Enabled = history.Count > 0;
            grid.Show(mc);
        }

        /// <summary>Character overview: every equipment slot, plus the grids of rig / pouch / backpack.</summary>
        public void ShowLoadout(bool pushHistory)
        {
            if (inv == null) return;
            if (pushHistory && !loadoutView) PushCurrentView();
            loadoutView = true;
            curMC = null;
            var secs = new List<GridSection>();
            foreach (var key in inv.TopUids)
            {
                MainContainer mc;
                if (!inv.ByUid.TryGetValue(key, out mc)) continue;
                if (mc.HasGrid) { secs.Add(new GridSection { Mc = mc, Title = mc.Title }); continue; }
                secs.Add(new GridSection { Mc = mc, Title = mc.Title, SlotOnly = true });
                var it = mc.Items.FirstOrDefault();
                var sub = it == null ? null : inv.ContainerOf(it);
                if (sub != null && sub.HasGrid) secs.Add(new GridSection { Mc = sub, Title = "↳ " + it.DisplayName + L.T(" – obsah", " – contents") });
            }
            gridTitle.Text = L.T("  Výbava postavy   ·   přehled", "  Character loadout   ·   overview");
            btnBack.Enabled = history.Count > 0;
            grid.Show(secs);
        }

        public void SelectItem(InvItem it, bool syncTree)
        {
            selItem = it;
            // pick the grid to display: the item's own container if it's a grid, else walk up to the nearest grid
            var probe = GridAncestor(it);
            if (loadoutView)
            {
                // stay in the overview while the item is visible in it (or has no grid at all, e.g. weapon parts)
                bool visible = it == null || grid.Shows(it.Container) || probe == null || grid.Shows(probe);
                if (!visible) ShowContainer(probe, true);
                else curMC = it != null && it.Container.HasGrid ? it.Container : null; // target for Add / Sort
            }
            else if (probe != null && probe != curMC) ShowContainer(probe, true);
            grid.Selected = it;
            grid.Invalidate();
            ShowDetails(it);
            if (it != null)
            {
                var n = FindNode(tree.Nodes, it);
                if (n != null && tree.SelectedNode != n) { syncingTree = true; tree.SelectedNode = n; syncingTree = false; }
            }
        }

        public void OpenItem(InvItem it)
        {
            var sub = it.Container.Save.ContainerOf(it);
            if (sub != null && sub.HasGrid) ShowContainer(sub, true);
            else SelectItem(it, true);
        }

        void ShowDetails(InvItem it)
        {
            loadingDetails = true;
            details.Visible = it != null;
            emptyHint.Visible = it == null;
            if (it == null)
            {
                lblName.Text = L.T("Nic nevybráno", "Nothing selected"); lblInfo.Text = ""; txtDef.Text = ""; stats.Rows.Clear(); contents.Items.Clear();
                loadingDetails = false; return;
            }
            lblName.Text = it.DisplayName;
            CatalogEntry ce; Catalog.Entries.TryGetValue(it.Def, out ce);
            var s = it.Container.Save;
            var desc = s.Descendants(it).ToList();
            float totalW = it.Weight * Math.Max(1, it.CanStack ? it.Count : 1) + desc.Sum(d => d.Weight * Math.Max(1, d.CanStack ? d.Count : 1));
            lblInfo.Text = it.Category + "  ·  " + it.BaseW + "×" + it.BaseH + (it.Rotated ? L.T(" (otočeno)", " (rotated)") : "")
                + "  ·  " + totalW.ToString("0.###") + " kg"
                + (it.State.Length > 2 ? L.T("\nStav: ", "\nState: ") + it.State : "")
                + (ce != null && ce.NewDurability != null ? L.T("\nVýdrž nového kusu: ", "\nDurability when new: ") + ce.NewDurability.Value.ToString("0.##") : "")
                + (it.Count > it.MaxStack ? L.T("\n⚠ Počet je vyšší než Max stack", "\n⚠ Count is higher than Max stack") : "");
            txtDef.Text = it.Def;
            numCount.Value = Math.Max(0, it.Count);
            numMax.Value = Math.Max(1, it.MaxStack);
            chkQuest.Checked = it.Quest;
            stats.Rows.Clear();
            foreach (var st in it.Stats)
            {
                int r = stats.Rows.Add(st.ShortTag, st.Value.ToString("0.#####", CultureInfo.CurrentCulture), st.ModifyType);
                stats.Rows[r].Tag = st;
            }
            contents.Items.Clear();
            AddContents(s, it, 0);
            loadingDetails = false;
        }

        void AddContents(InvSave s, InvItem it, int depth)
        {
            var mc = s.ContainerOf(it);
            if (mc == null) return;
            foreach (var c in mc.Items.OrderBy(x => x.ContainerIndex).ThenBy(x => x.SlotIndex))
            {
                contents.Items.Add(new ContentEntry { Item = c, Depth = depth });
                AddContents(s, c, depth + 1);
            }
        }

        void OnStatEdited(object sender, DataGridViewCellEventArgs e)
        {
            if (loadingDetails || e.RowIndex < 0 || e.ColumnIndex != 1) return;
            var st = stats.Rows[e.RowIndex].Tag as Stat;
            var txt = Convert.ToString(stats.Rows[e.RowIndex].Cells[1].Value);
            float v;
            if (float.TryParse(txt, NumberStyles.Float, CultureInfo.CurrentCulture, out v) || float.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            {
                st.Value = v;
                Changed(selItem);
            }
            else
            {
                loadingDetails = true;
                stats.Rows[e.RowIndex].Cells[1].Value = st.Value.ToString("0.#####", CultureInfo.CurrentCulture);
                loadingDetails = false;
            }
        }

        void Changed(InvItem it)
        {
            it.Container.Save.Dirty = true;
            var n = FindNode(tree.Nodes, it);
            if (n != null) n.Text = it.ToString();
            grid.Invalidate();
            UpdateTitle();
        }

        void StructureChanged(InvItem select)
        {
            foreach (var s in new[] { stash, inv }) if (s != null) s.Rebuild();
            // re-resolve current container by uid (objects are rebuilt)
            if (curMC != null)
            {
                MainContainer m;
                curMC = curMC.Save.ByUid.TryGetValue(curMC.UidKey, out m) ? m : curMC.Save.Containers.FirstOrDefault(c => c.HasGrid);
            }
            // null entries (character overview) survive; containers are re-resolved, vanished ones dropped
            var hist = new List<MainContainer>();
            foreach (var h in history.Reverse())
            {
                MainContainer m;
                if (h == null) hist.Add(null);
                else if (h.Save.ByUid.TryGetValue(h.UidKey, out m)) hist.Add(m);
            }
            history.Clear(); foreach (var h in hist) history.Push(h);
            RebuildTree();
            if (loadoutView) ShowLoadout(false);
            else if (curMC != null) ShowContainer(curMC, false);
            if (select != null)
            {
                InvItem fresh;
                select = select.Container.Save.ItemsByUid.TryGetValue(select.UidKey, out fresh) ? fresh : null;
            }
            SelectItem(select, true);
            UpdateTitle();
            ShowValidation(false);
        }

        // ================================================================== actions
        void OnKey(object sender, KeyEventArgs e)
        {
            if (ActiveControl is TextBox || ActiveControl is NumBox || stats.IsCurrentCellInEditMode || stats.Focused) return;
            if (e.KeyCode == Keys.Delete) { DeleteSel(); e.Handled = true; }
            else if (e.KeyCode == Keys.R && !e.Control) { RotateSel(); e.Handled = true; }
            else if (e.KeyCode == Keys.D && e.Control) { Duplicate(); e.Handled = true; }
            else if (e.KeyCode == Keys.Insert) { AddItemDialog(); e.Handled = true; }
            else if (e.KeyCode == Keys.S && e.Control) { SaveAll(); e.Handled = true; }
            else if (e.KeyCode == Keys.Back && history.Count > 0) { GoBack(); e.Handled = true; }
        }

        void DeleteSel()
        {
            if (selItem == null) return;
            var s = selItem.Container.Save;
            int sub = s.Descendants(selItem).Count();
            if (sub > 0 && DarkMsg.Show(this, L.T("Smazat " + selItem.DisplayName + " včetně " + sub + " vnořených předmětů?", "Delete " + selItem.DisplayName + " including " + sub + " nested items?"), L.T("Smazat", "Delete"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            s.RemoveItem(selItem);
            StructureChanged(null);
        }

        void RotateSel()
        {
            var it = selItem;
            if (it == null || it.SlotIndex < 0) return;
            var ge = it.Container.Elements.FirstOrDefault(e => e.Index == it.ContainerIndex);
            if (ge == null || !ge.IsGrid) return;
            int r = it.SlotIndex / ge.Cols, c = it.SlotIndex % ge.Cols;
            if (it.Container.Save.Fits(it.Container, ge, r, c, it.H, it.W, it)) { it.Rotated = !it.Rotated; Changed(it); }
            else status.Text = L.T("Otočení se nevejde – není tam místo.", "Can't rotate – there's no room.");
        }

        void Duplicate()
        {
            if (selItem == null) return;
            var s = selItem.Container.Save;
            var target = selItem.Container.HasGrid ? selItem.Container : curMC;
            if (target == null || !target.HasGrid || target.Save != s) { status.Text = L.T("Duplikovat jde jen předmět v mřížce.", "Only items in a grid can be duplicated."); return; }
            var t = s.TemplateOf(selItem);
            var spot = s.FindSpot(target, selItem.BaseW, selItem.BaseH, null, -1);
            if (spot == null) { DarkMsg.Show(this, L.T("V kontejneru už není místo.", "The container is full."), L.T("Duplikovat", "Duplicate")); return; }
            var n = s.Insert(t, target, spot[0], spot[1], spot[2] == 1, 0);
            StructureChanged(n);
        }

        void AddItemDialog()
        {
            if (curMC == null || !curMC.HasGrid) { DarkMsg.Show(this, loadoutView
                ? L.T("V přehledu výbavy nejdřív klikni na předmět v batohu, vestě nebo pouzdře – do toho kontejneru se pak bude přidávat.",
                      "In the loadout overview, first click an item in the backpack, rig or pouch – new items go into that container.")
                : L.T("Nejdřív otevři kontejner s mřížkou (sklad, batoh, vestu…).", "Open a container with a grid first (stash, backpack, rig…)."), L.T("Přidat předmět", "Add item")); return; }
            using (var d = new AddItemForm(curMC.Title))
            {
                if (d.ShowDialog(this) != DialogResult.OK || d.Chosen == null) return;
                InvItem last = null;
                int failed = 0;
                for (int k = 0; k < d.Copies; k++)
                {
                    var e = d.Chosen;
                    var spot = curMC.Save.FindSpot(curMC, e.W, e.H, null, -1);
                    if (spot == null) { failed = d.Copies - k; break; }
                    last = curMC.Save.Insert(e.Template, curMC, spot[0], spot[1], spot[2] == 1, d.Count);
                    // Insert rebuilt the save – refresh curMC reference
                    curMC = curMC.Save.ByUid[curMC.UidKey];
                }
                StructureChanged(last);
                if (failed > 0) DarkMsg.Show(this, L.T(failed + "× se nevešlo – kontejner je plný.", failed + "× didn't fit – the container is full."), L.T("Přidat předmět", "Add item"));
            }
        }

        void Bulk(string label, Func<InvSave, int> f)
        {
            int n = 0;
            foreach (var s in new[] { stash, inv }) if (s != null) n += f(s);
            status.Text = label + ": " + n;
            StructureChanged(selItem);
        }

        void MergeCur()
        {
            if (curMC == null) return;
            int removed = Tools.MergeStacks(curMC.Save, curMC);
            status.Text = L.T("Sloučeno – ušetřeno " + removed + " políček/stacků.", "Merged – " + removed + " stacks freed.");
            StructureChanged(null);
        }

        void SortCur()
        {
            if (curMC == null || !curMC.HasGrid) return;
            if (Tools.Sort(curMC.Save, curMC)) status.Text = L.T("Seřazeno.", "Sorted.");
            else status.Text = L.T("Seřazení se nevešlo – nic se nezměnilo.", "Sorting didn't fit – nothing changed.");
            StructureChanged(selItem);
        }

        void UnlockPages()
        {
            if (stash == null) return;
            if (DarkMsg.Show(this, L.T("Odemkne všechna políčka na zamčených stranách skladu.\nHra je může při dalším načtení znovu zamknout podle úrovně úkrytu. Pokračovat?",
                                       "Unlocks every cell on the locked stash pages.\nThe game may lock them again on load, based on the hideout level. Continue?"),
                             L.T("Experimentální", "Experimental"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            int n = Tools.UnlockStashPages(stash);
            status.Text = L.T("Odemčeno stran: ", "Pages unlocked: ") + n;
            StructureChanged(null);
        }

        // called by grid on drop
        public void MoveItem(InvItem it, GridElement ge, int row, int col, bool rotated)
        {
            var s = it.Container.Save;
            int w = rotated ? it.BaseH : it.BaseW, h = rotated ? it.BaseW : it.BaseH;
            if (!s.Fits(it.Container, ge, row, col, w, h, it)) { status.Text = "Sem se to nevejde."; grid.Invalidate(); return; }
            it.ContainerIndex = ge.Index;
            it.SlotIndex = row * ge.Cols + col;
            it.Rotated = rotated;
            Changed(it);
            SelectItem(it, true);
        }
    }

    // ====================================================================== grid control
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
        public InvItem Selected;
        List<GridSection> sections = new List<GridSection>();
        const int Cell = 56, Gap = 28, Pad = 16, HeadH = 24, CapH = 18;
        readonly List<Block> blocks = new List<Block>();
        readonly List<KeyValuePair<string, Point>> headers = new List<KeyValuePair<string, Point>>();
        InvItem drag; int dragOffR, dragOffC; Point mouse; bool dragRot; bool dragging; Point downAt;
        readonly ToolTip tip = Theme.MakeTip();
        InvItem hover;

        public GridPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            TabStop = true;
        }

        public void Show(MainContainer c)
        {
            Show(new List<GridSection> { new GridSection { Mc = c } });
        }

        public void Show(List<GridSection> s)
        {
            sections = s; Selected = null; AutoScrollPosition = Point.Empty; DoLayout(); Invalidate();
        }

        /// <summary>Is this container visible in the current view?</summary>
        public bool Shows(MainContainer m) { return m != null && sections.Any(s => s.Mc == m); }

        protected override void OnResize(EventArgs e) { base.OnResize(e); DoLayout(); Invalidate(); }

        void DoLayout()
        {
            blocks.Clear(); headers.Clear();
            int maxW = Math.Max(ClientSize.Width - Pad, 300);
            int x = Pad, y = Pad, rowH = 0;
            using (var f = new Font("Segoe UI", 7f))
            using (var hf = new Font("Segoe UI Semibold", 9.5f))
                foreach (var s in sections)
                {
                    // measure the section's blocks
                    var sizes = new List<Size>();       // grid/slot rectangle size
                    var widths = new List<int>();       // reserved width (caption may be wider)
                    var elems = new List<GridElement>();
                    if (s.SlotOnly)
                    {
                        var it = s.Mc.Items.FirstOrDefault();
                        // equipment slots are drawn smaller than grid cells – they're just "what's equipped"
                        const int SlotCell = 38;
                        var sz = new Size(Math.Max(3, it == null ? 3 : it.W) * SlotCell, Math.Max(2, it == null ? 2 : it.H) * SlotCell);
                        sizes.Add(sz); widths.Add(sz.Width); elems.Add(null);
                    }
                    else
                        foreach (var ge in s.Mc.Elements.Where(e => e.IsGrid))
                        {
                            var sz = new Size(ge.Cols * Cell, ge.Rows * Cell);
                            sizes.Add(sz);
                            widths.Add(Math.Max(sz.Width, TextRenderer.MeasureText(ElementLabel(s.Mc, ge, 999), f).Width + 6));
                            elems.Add(ge);
                        }
                    int headH = s.Title == null ? 0 : HeadH;
                    int titleW = s.Title == null ? 0 : TextRenderer.MeasureText(s.Title, hf).Width + 8;
                    int capH = s.SlotOnly ? 0 : CapH;
                    int contentW = widths.Count == 0 ? 0 : widths.Sum() + Gap * (widths.Count - 1);
                    int secW = Math.Max(titleW, Math.Min(contentW, maxW - Pad));

                    if (x > Pad && x + secW > maxW) { x = Pad; y += rowH + Gap; rowH = 0; }
                    if (s.Title != null) headers.Add(new KeyValuePair<string, Point>(s.Title, new Point(x, y)));

                    // place blocks, wrapping inside the section when needed
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
                    x = right + Gap + (s.Title != null ? 8 : 0);
                    rowH = Math.Max(rowH, secH);
                }
            AutoScrollMinSize = new Size(blocks.Count == 0 ? 0 : blocks.Max(b => b.R.Right) + Pad, y + rowH + Pad);
        }

        static string ElementLabel(MainContainer mc, GridElement ge, int free)
        {
            return (mc.Elements.Count(x => x.IsGrid) > 1 ? L.T("Část ", "Part ") + (ge.Index + 1) + " · " : "") + ge.Cols + "×" + ge.Rows + L.T(" · volno ", " · free ") + free;
        }

        Point Offset(Point p) { return new Point(p.X - AutoScrollPosition.X, p.Y - AutoScrollPosition.Y); }

        Block HitCell(Point p, out int row, out int col)
        {
            p = Offset(p);
            foreach (var b in blocks)
                if (b.R.Contains(p)) { col = (p.X - b.R.X) / Cell; row = (p.Y - b.R.Y) / Cell; return b; }
            row = col = -1; return null;
        }

        InvItem ItemAt(Point p)
        {
            int r, c;
            var b = HitCell(p, out r, out c);
            if (b == null) return null;
            if (b.Slot) return b.Mc.Items.FirstOrDefault();
            return b.Mc.Save.Occupancy(b.Mc, b.Ge, null)[r, c];
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            var it = ItemAt(e.Location);
            Owner.SelectItem(it, true);
            int r, c;
            var b = HitCell(e.Location, out r, out c);
            if (it != null && b != null && !b.Slot && e.Button == MouseButtons.Left)
            {
                drag = it; dragOffR = r - it.SlotIndex / b.Ge.Cols; dragOffC = c - it.SlotIndex % b.Ge.Cols; dragRot = it.Rotated;
                downAt = e.Location; dragging = false;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            mouse = e.Location;
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

        // drop target is only valid inside the dragged item's own container
        Block DropBlock(Point p, out int r, out int c)
        {
            var b = HitCell(p, out r, out c);
            return b != null && !b.Slot && drag != null && b.Mc == drag.Container ? b : null;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (dragging && drag != null)
            {
                int r, c;
                var b = DropBlock(e.Location, out r, out c);
                if (b != null)
                {
                    int offR = dragRot == drag.Rotated ? dragOffR : 0, offC = dragRot == drag.Rotated ? dragOffC : 0;
                    Owner.MoveItem(drag, b.Ge, r - offR, c - offC, dragRot);
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

        /// <summary>Word-wrap without breaking words; a word wider than the tile is cut with an ellipsis.
        /// Lines that don't fit vertically are dropped and the last visible line gets an ellipsis.</summary>
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
            bool sel = it == Selected;
            var col = CatColor(it.Category);
            using (var b = new LinearGradientBrush(ir, ControlPaint.Light(col, 0.15f), col, LinearGradientMode.Vertical))
            {
                if (dragging && it == drag) { b.LinearColors = new[] { Color.FromArgb(70, col), Color.FromArgb(70, col) }; }
                g.FillRectangle(b, ir);
            }
            using (var p = new Pen(sel ? Theme.Accent : Color.FromArgb(90, 255, 255, 255), sel ? 2f : 1f))
                g.DrawRectangle(p, sel ? Rectangle.Inflate(ir, -1, -1) : ir);

            // footer: [▣]  ............  [count]   + durability bar underneath
            bool hasCount = it.MaxStack > 1 || it.Count > 1;
            var sub = it.Container.Save.ContainerOf(it);
            bool hasBox = sub != null && sub.HasGrid;
            var d = it.GetStat("Durability");
            bool hasBar = d != null && (it.Category.StartsWith("Weapons") || it.Category.StartsWith("Gear"));
            int barH = hasBar ? 5 : 0;
            int footH = (hasCount || hasBox) ? 13 : 0;

            if (hasBar)
            {
                CatalogEntry ce;
                float max = Catalog.Entries.TryGetValue(it.Def, out ce) && ce.NewDurability > 0 ? ce.NewDurability.Value : 100f;
                float f = Math.Max(0, Math.Min(1, d.Value / max));
                var track = new Rectangle(ir.X + 3, ir.Bottom - 5, ir.Width - 6, 3);
                using (var tb = new SolidBrush(Color.FromArgb(90, 0, 0, 0))) g.FillRectangle(tb, track);
                g.FillRectangle(f > 0.6f ? Brushes.LimeGreen : f > 0.3f ? Brushes.Orange : Brushes.Red, new Rectangle(track.X, track.Y, (int)(track.Width * f), 3));
            }
            int footTop = ir.Bottom - barH - footH;
            float countW = 0;
            if (hasCount)
            {
                var txt = it.Count >= 100000 ? (it.Count / 1000) + "k" : it.Count >= 10000 ? (Math.Floor(it.Count / 100.0) / 10).ToString("0.#") + "k" : it.Count.ToString();
                var sz = g.MeasureString(txt, bold, int.MaxValue, StringFormat.GenericTypographic);
                countW = sz.Width;
                using (var cb = new SolidBrush(it.Count > it.MaxStack ? Theme.Warn : Color.White))
                    g.DrawString(txt, bold, cb, ir.Right - sz.Width - 3, footTop, StringFormat.GenericTypographic);
            }
            if (hasBox && ir.Width - countW > 20)
                using (var bb = new SolidBrush(Color.FromArgb(210, 255, 255, 255)))
                    g.DrawString("▣", small, bb, ir.X + 2, footTop - 1, StringFormat.GenericTypographic);

            // name: whatever lines fit above the footer
            float lineH = small.GetHeight(g);
            int top = ir.Y + 3;
            int avail = footTop - top - 1;
            int maxLines = Math.Max(1, (int)(avail / lineH));
            var lines = WrapLabel(g, it.DisplayName, small, ir.Width - 7, maxLines);
            var prevClip = g.Clip;
            g.SetClip(new Rectangle(ir.X + 1, ir.Y + 1, ir.Width - 2, Math.Max(lineH > avail ? (int)lineH : avail, 1) + 2));
            for (int k = 0; k < lines.Count; k++)
                g.DrawString(lines[k], small, white, ir.X + 3, top + k * lineH, StringFormat.GenericTypographic);
            g.Clip = prevClip;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (var gridPen = new Pen(Color.FromArgb(60, 64, 70)))
            using (var slotPen = new Pen(Color.FromArgb(80, 86, 96)) { DashStyle = DashStyle.Dash })
            using (var cellBg = new SolidBrush(Color.FromArgb(38, 41, 45)))
            using (var hatch = new HatchBrush(HatchStyle.BackwardDiagonal, Color.FromArgb(70, 50, 50), Color.FromArgb(32, 32, 34)))
            using (var small = new Font("Segoe UI", 7f))
            using (var bold = new Font("Segoe UI Semibold", 8f))
            using (var head = new Font("Segoe UI Semibold", 9.5f))
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
                            g.DrawString(L.T("prázdné", "empty"), small, label, R.X + 6, R.Y + 6);
                        }
                        else DrawTile(g, it, Rectangle.Inflate(R, -2, -2), small, bold, white);
                        continue;
                    }
                    var ge = b.Ge; var mc = b.Mc;
                    int free = 0; var occ = mc.Save.Occupancy(mc, ge, null);
                    for (int r = 0; r < ge.Rows; r++) for (int c = 0; c < ge.Cols; c++) if (ge.Enabled[r, c] && occ[r, c] == null) free++;
                    g.DrawString(ElementLabel(mc, ge, free), small, label, R.X, R.Y - 16);
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
                // drag ghost
                if (dragging && drag != null)
                {
                    int r, c;
                    var b = DropBlock(mouse, out r, out c);
                    if (b != null)
                    {
                        int offR = dragRot == drag.Rotated ? dragOffR : 0, offC = dragRot == drag.Rotated ? dragOffC : 0;
                        int w = dragRot ? drag.BaseH : drag.BaseW, h = dragRot ? drag.BaseW : drag.BaseH;
                        int tr = r - offR, tc = c - offC;
                        bool ok = b.Mc.Save.Fits(b.Mc, b.Ge, tr, tc, w, h, drag);
                        var gr = new Rectangle(b.R.X + tc * Cell + 2, b.R.Y + tr * Cell + 2, w * Cell - 4, h * Cell - 4);
                        using (var gb = new SolidBrush(ok ? Color.FromArgb(110, 80, 200, 120) : Color.FromArgb(110, 220, 60, 60)))
                            g.FillRectangle(gb, gr);
                        g.DrawString(drag.DisplayName + L.T("   (R = otočit)", "   (R = rotate)"), small, white, gr.X + 3, gr.Y + 2);
                    }
                }
            }
        }
    }

    // ====================================================================== add dialog
    public class AddItemForm : Form
    {
        TextBox search; ComboBox cat; ListView list; NumBox count, copies; Label info;
        public CatalogEntry Chosen; public int Count; public int Copies;

        public AddItemForm(string targetTitle)
        {
            Text = L.T("Přidat předmět do: ", "Add item to: ") + targetTitle;
            Width = 820; Height = 640; StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 9f);
            MinimizeBox = false; ShowInTaskbar = false;
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8, 10, 8, 0), WrapContents = false };
            search = new TextBox { Width = 320, Margin = new Padding(6, 0, 8, 0) };
            cat = new ComboBox { Width = 240, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0) };
            cat.Items.Add(L.T("(všechny kategorie)", "(all categories)"));
            foreach (var c in Catalog.Entries.Values.Select(e => e.Category).Distinct().OrderBy(x => x)) cat.Items.Add(c);
            cat.SelectedIndex = 0;
            top.Controls.Add(new Label { Text = L.T("Hledat:", "Search:"), AutoSize = true, Margin = new Padding(0, 3, 0, 0) });
            top.Controls.Add(search); top.Controls.Add(cat);

            list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
            list.Columns.Add(L.T("Název", "Name"), 300); list.Columns.Add(L.T("Kategorie", "Category"), 170); list.Columns.Add(L.T("Velikost", "Size"), 70);
            list.Columns.Add("Max stack", 80); list.Columns.Add(L.T("Díly", "Parts"), 50); list.Columns.Add(L.T("Zdroj", "Source"), 80);

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(8, 10, 8, 8), FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            count = new NumBox { Minimum = 1, Maximum = int.MaxValue, Width = 110, ThousandsSeparator = true, Margin = new Padding(4, 4, 4, 0) };
            copies = new NumBox { Minimum = 1, Maximum = 100, Width = 60, Value = 1, Margin = new Padding(4, 4, 12, 0) };
            var ok = new Button { Text = L.T("Přidat", "Add"), DialogResult = DialogResult.OK, Width = 100, Height = 30, Margin = new Padding(4, 0, 4, 0) };
            var cancel = new Button { Text = L.T("Zrušit", "Cancel"), DialogResult = DialogResult.Cancel, Width = 90, Height = 30, Margin = new Padding(4, 0, 4, 0) };
            info = new Label { AutoSize = true, Margin = new Padding(12, 7, 0, 0) };
            bottom.Controls.Add(new Label { Text = L.T("Počet ve stacku:", "Stack count:"), AutoSize = true, Margin = new Padding(0, 7, 0, 0) });
            bottom.Controls.Add(count);
            bottom.Controls.Add(new Label { Text = L.T("Kusů:", "Copies:"), AutoSize = true, Margin = new Padding(8, 7, 0, 0) });
            bottom.Controls.Add(copies);
            bottom.Controls.Add(ok); bottom.Controls.Add(cancel); bottom.Controls.Add(info);
            AcceptButton = ok; CancelButton = cancel;

            Controls.Add(list); Controls.Add(top); Controls.Add(bottom);
            search.TextChanged += delegate { Fill(); };
            cat.SelectedIndexChanged += delegate { Fill(); };
            list.SelectedIndexChanged += delegate { OnPick(); };
            list.DoubleClick += delegate { if (list.SelectedItems.Count > 0) { DialogResult = DialogResult.OK; Close(); } };
            FormClosing += delegate
            {
                if (DialogResult != DialogResult.OK) return;
                Chosen = list.SelectedItems.Count > 0 ? (CatalogEntry)list.SelectedItems[0].Tag : null;
                Count = (int)count.Value; Copies = (int)copies.Value;
            };
            Shown += delegate { search.Focus(); };
            Theme.Apply(this);
            Theme.OwnerDrawListView(list);
            Theme.OwnerDrawCombo(cat);
            info.ForeColor = Theme.TextDim;
            ok.BackColor = Theme.AccentBg; ok.FlatAppearance.BorderColor = Theme.Accent;
            Fill();
        }

        void Fill()
        {
            var q = search.Text.Trim().ToLowerInvariant();
            var c = cat.SelectedIndex > 0 ? (string)cat.SelectedItem : null;
            list.BeginUpdate(); list.Items.Clear();
            foreach (var e in Catalog.Entries.Values.Where(e => (c == null || e.Category == c) && (q.Length == 0 || e.Name.ToLowerInvariant().Contains(q) || e.IdName.ToLowerInvariant().Contains(q)))
                                                      .OrderBy(e => e.Category).ThenBy(e => e.Name))
            {
                var li = new ListViewItem(new[] { e.Name, e.Category, e.W + "×" + e.H, e.MaxStack.ToString(), e.AttachmentCount > 0 ? e.AttachmentCount.ToString() : "", e.Template.Source == "Vendor" ? L.T("obchod", "vendor") : L.T("tvoje savy", "your saves") });
                li.Tag = e; list.Items.Add(li);
            }
            list.EndUpdate();
            info.Text = list.Items.Count + L.T(" položek", " items");
        }

        void OnPick()
        {
            if (list.SelectedItems.Count == 0) return;
            var e = (CatalogEntry)list.SelectedItems[0].Tag;
            count.Value = Math.Max(1, e.MaxStack);
            info.Text = e.AttachmentCount > 0 ? L.T("přidá se kompletně sestavené (" + e.AttachmentCount + " dílů)", "added fully assembled (" + e.AttachmentCount + " parts)") : "";
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            L.Init();
            string folder = args.Length > 0 ? args[0] : MainForm.DefaultFolder();
            // the window is rebuilt when the user switches language
            while (true)
            {
                var f = new MainForm(folder);
                Application.Run(f);
                if (!f.RestartRequested) break;
                folder = f.Folder;
            }
        }
    }
}
