// Main window of the Incursion Red River inventory editor.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace IrrEditor
{
    public class MainForm : Form
    {
        // ------------------------------------------------------------------ state
        string dir;
        InvSave stash, inv, vendor;
        BaseData baseData;
        SaveDoc gunsmith;
        MainContainer curMC;                 // active grid container: target for Add / Sort / Merge
        InvItem selItem;                     // primary selection (multi-selection lives in grid.Multi)

        enum ViewKind { Container, Loadout, StashAll }
        class ViewRef { public ViewKind Kind; public MainContainer Mc; }
        ViewKind view = ViewKind.StashAll;
        MainContainer viewMc;
        readonly Stack<ViewRef> history = new Stack<ViewRef>();
        UndoStack undo;
        Dictionary<string, DateTime> stamps = new Dictionary<string, DateTime>();

        // ------------------------------------------------------------------ controls
        TreeView tree;
        GridPanel grid;
        Label gridTitle, gridStats;
        Button btnBack;
        Label lblName, lblInfo;
        TextBox txtDef;
        NumBox numCount, numMax;
        CheckBox chkQuest;
        DataGridView stats;
        ListBox contents;
        Button btnDetach, btnParts;
        Panel details;
        Label emptyHint;
        ToolStripStatusLabel status, warn;
        ToolStripButton btnUndo, btnRedo;
        ToolStripTextBox searchBox;
        bool loadingDetails;

        public const string AppTitle = "Incursion Red River – Inventory Editor";
        public bool RestartRequested;
        public string Folder { get { return dir; } }
        bool discardOnClose;

        public MainForm(string folder)
        {
            Theme.DpiForm(this);
            Text = AppTitle;
            Width = 1560; Height = 940;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5f);
            KeyPreview = true;
            BuildUi();
            Theme.Apply(this);
            warn.ForeColor = Theme.Warn;
            searchBox.BackColor = Theme.Surface; searchBox.ForeColor = Theme.Text;
            dir = folder;
            undo = new UndoStack(UndoTargets, AfterRestore);
            Shown += delegate { LoadAll(); };
            Activated += delegate { CheckDiskChanges(); };
            FormClosing += OnClosing;
            KeyDown += OnKey;
        }

        // ================================================================== UI construction
        void BuildUi()
        {
            var ts = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(6, 4, 6, 4), ShowItemToolTips = false };
            var darkTip = Theme.MakeTip();
            // native ToolStrip tooltips can't be themed – show our own dark one
            Action<ToolStripItem, Func<string>> tipFor = (item, text) =>
            {
                item.MouseEnter += delegate
                {
                    if (item is ToolStripDropDownItem && ((ToolStripDropDownItem)item).DropDown.Visible) return;
                    var t = text(); darkTip.Tag = t; darkTip.Show(t, ts, item.Bounds.X, item.Bounds.Bottom + Theme.S(6), 4000);
                };
                item.MouseLeave += delegate { darkTip.Hide(ts); };
                item.MouseDown += delegate { darkTip.Hide(ts); };
            };
            Func<string, string, EventHandler, ToolStripButton> add = (t, tip, h) => { var b = new ToolStripButton(t); tipFor(b, () => tip); b.Click += h; ts.Items.Add(b); return b; };
            add(L.T("📂 Složka…", "📂 Folder…"), L.T("Vybrat složku SaveGames", "Choose the SaveGames folder"), delegate { PickFolder(); });
            add(L.T("⟳ Znovu načíst", "⟳ Reload"), L.T("Zahodit neuložené změny a načíst znovu", "Discard unsaved changes and reload"), delegate { if (ConfirmDiscard()) LoadAll(); });
            add(L.T("💾 Uložit", "💾 Save"), L.T("Uložit změny (Ctrl+S, předtím se udělá záloha)", "Save changes (Ctrl+S, a backup is made first)"), delegate { SaveAll(); });
            ts.Items.Add(new ToolStripSeparator());
            btnUndo = new ToolStripButton(L.T("↶ Zpět", "↶ Undo")) { Enabled = false };
            btnUndo.Click += delegate { DoUndo(); };
            tipFor(btnUndo, () => L.T("Vrátit: ", "Undo: ") + (undo.UndoLabel ?? "—") + " (Ctrl+Z)");
            btnRedo = new ToolStripButton(L.T("↷ Znovu", "↷ Redo")) { Enabled = false };
            btnRedo.Click += delegate { DoRedo(); };
            tipFor(btnRedo, () => L.T("Znovu: ", "Redo: ") + (undo.RedoLabel ?? "—") + " (Ctrl+Y)");
            ts.Items.Add(btnUndo); ts.Items.Add(btnRedo);
            ts.Items.Add(new ToolStripSeparator());
            add(L.T("＋ Přidat…", "＋ Add…"), L.T("Přidat předmět z katalogu do otevřeného kontejneru (Ins)", "Add an item from the catalog to the open container (Ins)"), delegate { AddItemDialog(); });
            add(L.T("⧉ Duplikovat", "⧉ Duplicate"), L.T("Duplikovat vybraný předmět i s obsahem (Ctrl+D)", "Duplicate the selected item with its contents (Ctrl+D)"), delegate { Duplicate(); });
            add(L.T("✕ Smazat", "✕ Delete"), L.T("Smazat vybrané předměty i s obsahem (Del)", "Delete the selected items with their contents (Del)"), delegate { DeleteSel(); });
            add(L.T("↻ Otočit", "↻ Rotate"), L.T("Otočit vybraný předmět (R)", "Rotate the selected item (R)"), delegate { RotateSel(); });
            ts.Items.Add(new ToolStripSeparator());
            var smart = new ToolStripDropDownButton(L.T("✨ Nástroje", "✨ Tools"));
            Action<string, EventHandler> sm = (t, h) => { var mi = new ToolStripMenuItem(t); mi.Click += h; smart.DropDownItems.Add(mi); };
            sm(L.T("Opravit vše (výdrž na hodnotu nového kusu)", "Repair all (durability of a brand-new item)"), delegate { Bulk(L.T("Opravit vše", "Repair all"), L.T("Opraveno", "Repaired"), s => Tools.RepairAll(s)); });
            sm(L.T("Doplnit všechny stacky na maximum", "Fill all stacks to maximum"), delegate { Bulk(L.T("Doplnit stacky", "Fill stacks"), L.T("Doplněno stacků", "Stacks filled"), s => Tools.FillStacks(s)); });
            sm(L.T("Naplnit zásobníky (podle kapacity v názvu)", "Fill magazines (capacity from the name)"), delegate { Bulk(L.T("Naplnit zásobníky", "Fill magazines"), L.T("Naplněno zásobníků", "Magazines filled"), s => Tools.FillMagazines(s)); });
            smart.DropDownItems.Add(new ToolStripSeparator());
            sm(L.T("Sloučit stacky v otevřeném kontejneru", "Merge stacks in the open container"), delegate { MergeCur(); });
            sm(L.T("Seřadit otevřený kontejner (podle kategorie a velikosti)", "Sort the open container (by category and size)"), delegate { SortCur(); });
            smart.DropDownItems.Add(new ToolStripSeparator());
            sm(L.T("Odemknout zamčená políčka skladu (experimentální)", "Unlock locked stash cells (experimental)"), delegate { UnlockPages(); });
            sm(L.T("Zkontrolovat konzistenci (překryvy, mimo mřížku)", "Check consistency (overlaps, out of grid)"), delegate { ShowValidation(true); });
            ts.Items.Add(smart);
            ts.Items.Add(new ToolStripSeparator());
            add(L.T("🏠 Báze && reputace", "🏠 Base && reputation"), L.T("Úrovně zařízení v úkrytu, suroviny, level postavy a loajalita frakcí", "Hideout facility levels, resources, player level and faction loyalty"), delegate { OpenBase(); });
            add(L.T("🔧 Presety zbraní", "🔧 Weapon presets"), L.T("Presety z Gunsmithu – prohlížení, přejmenování, duplikace, mazání", "Gunsmith presets – browse, rename, duplicate, delete"), delegate { OpenGunsmith(); });
            add(L.T("🗂 Zálohy", "🗂 Backups"), L.T("Seznam záloh a obnovení savů", "List backups and restore saves"), delegate { OpenBackups(); });

            // right side: language, about, search
            var langBtn = new ToolStripDropDownButton(L.En ? "🌐 English" : "🌐 Čeština") { Alignment = ToolStripItemAlignment.Right };
            var miCz = new ToolStripMenuItem("Čeština") { Checked = !L.En };
            var miEn = new ToolStripMenuItem("English") { Checked = L.En };
            miCz.Click += delegate { SwitchLanguage(false); };
            miEn.Click += delegate { SwitchLanguage(true); };
            langBtn.DropDownItems.Add(miCz); langBtn.DropDownItems.Add(miEn);
            ts.Items.Add(langBtn);
            var about = new ToolStripButton(L.T("ⓘ O aplikaci", "ⓘ About")) { Alignment = ToolStripItemAlignment.Right };
            about.Click += delegate { using (var f = new AboutForm()) f.ShowDialog(this); };
            ts.Items.Add(about);
            searchBox = new ToolStripTextBox { Alignment = ToolStripItemAlignment.Right, BorderStyle = BorderStyle.FixedSingle, AutoSize = false, Width = Theme.S(190) };
            searchBox.TextChanged += delegate { ApplySearch(); };
            searchBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { JumpToMatch(); e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Escape) { searchBox.Text = ""; grid.Focus(); e.Handled = e.SuppressKeyPress = true; }
            };
            tipFor(searchBox, () => L.T("Hledat předmět (Ctrl+F). Enter = další výsledek, Esc = zrušit.", "Find an item (Ctrl+F). Enter = next match, Esc = clear."));
            ts.Items.Add(searchBox);
            ts.Items.Add(new ToolStripLabel("🔍") { Alignment = ToolStripItemAlignment.Right });

            var ss = new StatusStrip { SizingGrip = false };
            status = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Text };
            warn = new ToolStripStatusLabel();
            ss.Items.Add(status); ss.Items.Add(warn);

            tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, FullRowSelect = true, ShowLines = true, Font = new Font("Segoe UI", 10f), ItemHeight = Theme.S(26) };
            tree.AfterSelect += OnTreeSelect;

            grid = new GridPanel { Dock = DockStyle.Fill, BackColor = Theme.Bg, AutoScroll = true };
            grid.Owner = this;
            Theme.DarkNative(grid);
            gridTitle = new Label { Dock = DockStyle.Fill, ForeColor = Theme.Text, Font = new Font("Segoe UI Semibold", 12f), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            gridStats = new Label { Dock = DockStyle.Right, Width = 330, ForeColor = Theme.TextDim, TextAlign = ContentAlignment.MiddleRight, AutoEllipsis = true };
            btnBack = new Button { Text = L.T("← Zpět", "← Back"), Dock = DockStyle.Left, Width = 90 };
            btnBack.Click += delegate { GoBack(); };
            var gridHead = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Theme.Panel, Padding = new Padding(8, 7, 12, 7) };
            var zoomOut = new Button { Text = "−", Dock = DockStyle.Right, Width = 40 };
            var zoomIn = new Button { Text = "+", Dock = DockStyle.Right, Width = 40 };
            var zoomLbl = new Label { Dock = DockStyle.Right, Width = 64, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.TextDim, Text = "100 %" };
            zoomOut.Click += delegate { grid.Zoom -= 0.1f; };
            zoomIn.Click += delegate { grid.Zoom += 0.1f; };
            grid.ZoomChanged += delegate { zoomLbl.Text = (int)Math.Round(grid.Zoom * 100) + " %"; };
            gridHead.Controls.Add(gridTitle); gridHead.Controls.Add(gridStats); gridHead.Controls.Add(zoomOut); gridHead.Controls.Add(zoomLbl); gridHead.Controls.Add(zoomIn); gridHead.Controls.Add(btnBack);
            var hint = new Label { Dock = DockStyle.Bottom, Height = 28, ForeColor = Theme.TextDim, BackColor = Theme.Panel, Padding = new Padding(10, 6, 0, 0), AutoEllipsis = true,
                Text = L.T("Táhni = přesun (i do jiného kontejneru) · Ctrl+klik / tažení v prázdnu = výběr více · pravé tlačítko = menu · R = otočit · Ctrl+Z = zpět",
                           "Drag = move (also into another container) · Ctrl+click / drag on empty space = multi-select · right-click = menu · R = rotate · Ctrl+Z = undo") };
            var center = new Panel { Dock = DockStyle.Fill };
            center.Controls.Add(grid); center.Controls.Add(hint); center.Controls.Add(gridHead);

            details = BuildDetails();

            var splitR = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2, SplitterWidth = 6 };
            splitR.Panel1.Controls.Add(center);
            emptyHint = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.TextDim,
                Text = L.T("Klikni na předmět v mřížce nebo ve stromu\npro zobrazení a úpravu detailů.", "Click an item in the grid or the tree\nto view and edit its details.") };
            splitR.Panel2.Controls.Add(details);
            splitR.Panel2.Controls.Add(emptyHint);
            var splitL = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 6 };
            splitL.Panel1.Controls.Add(tree);
            splitL.Panel2.Controls.Add(splitR);

            Controls.Add(splitL);
            Controls.Add(ts);
            Controls.Add(ss);
            Load += delegate { splitL.SplitterDistance = Theme.S(320); splitR.SplitterDistance = Math.Max(Theme.S(200), splitR.Width - Theme.S(400)); };
        }

        Panel BuildDetails()
        {
            var p = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10), AutoScroll = true };
            var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            lblName = new Label { AutoSize = true, Font = new Font("Segoe UI Semibold", 13f), MaximumSize = new Size(360, 0) };
            lblInfo = new Label { AutoSize = true, ForeColor = Theme.TextDim, MaximumSize = new Size(360, 0), Margin = new Padding(3, 2, 3, 8) };
            txtDef = new TextBox { ReadOnly = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
            numCount = new NumBox { Maximum = int.MaxValue, Minimum = 0, Dock = DockStyle.Fill, ThousandsSeparator = true };
            numMax = new NumBox { Maximum = int.MaxValue, Minimum = 1, Dock = DockStyle.Fill, ThousandsSeparator = true };
            chkQuest = new CheckBox { Text = L.T("Questový předmět", "Quest item"), AutoSize = true };
            numCount.ValueChanged += delegate { if (!loadingDetails && selItem != null) { Checkpoint(L.T("Změna počtu", "Change count"), "count:" + selItem.UidKey); selItem.Count = (int)numCount.Value; Changed(selItem); } };
            numMax.ValueChanged += delegate { if (!loadingDetails && selItem != null) { Checkpoint(L.T("Změna max stacku", "Change max stack"), "max:" + selItem.UidKey); selItem.MaxStack = (int)numMax.Value; Changed(selItem); } };
            chkQuest.CheckedChanged += delegate { if (!loadingDetails && selItem != null) { Checkpoint(L.T("Questový příznak", "Quest flag")); selItem.Quest = chkQuest.Checked; Changed(selItem); } };

            var btnFill = new Button { Text = "Max", Width = 56 };
            btnFill.Click += delegate { if (selItem != null) numCount.Value = Math.Max(selItem.MaxStack, 1); };
            var btnRepair = new Button { Text = L.T("🔧 Opravit", "🔧 Repair"), AutoSize = true };
            btnRepair.Click += delegate { RepairSel(); };
            var countRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            numCount.Width = 150; countRow.Controls.Add(numCount); countRow.Controls.Add(btnFill);

            stats = new DataGridView
            {
                Dock = DockStyle.Top, Height = 270, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AllowUserToResizeRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.CellSelect,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, ColumnHeadersHeight = Theme.S(30)
            };
            stats.RowTemplate.Height = Theme.S(26);
            stats.Columns.Add("stat", L.T("Statistika", "Stat")); stats.Columns.Add("val", L.T("Hodnota", "Value")); stats.Columns.Add("type", L.T("Typ", "Type"));
            stats.Columns[0].ReadOnly = true; stats.Columns[2].ReadOnly = true; stats.Columns[2].FillWeight = 50;
            stats.CellValueChanged += OnStatEdited;

            contents = new ListBox { Dock = DockStyle.Top, Height = 170, IntegralHeight = false };
            contents.DoubleClick += delegate { var it = contents.SelectedItem as ContentEntry; if (it != null) SelectItem(it.Item, true); };
            var partBtns = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
            btnParts = new Button { Text = L.T("🔩 Díly zbraně…", "🔩 Weapon parts…"), AutoSize = true };
            btnParts.Click += delegate { OpenParts(); };
            btnDetach = new Button { Text = L.T("⤒ Sundat vybraný díl do skladu", "⤒ Detach selected part to stash"), AutoSize = true };
            btnDetach.Click += delegate { DetachPart(); };
            partBtns.Controls.Add(btnParts); partBtns.Controls.Add(btnDetach);

            Func<string, Label> Lbl = s => new Label { Text = s, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 0) };
            t.Controls.Add(lblName, 0, 0); t.SetColumnSpan(lblName, 2);
            t.Controls.Add(lblInfo, 0, 1); t.SetColumnSpan(lblInfo, 2);
            t.Controls.Add(Lbl(L.T("Definice", "Definition")), 0, 2); t.Controls.Add(txtDef, 1, 2);
            t.Controls.Add(Lbl(L.T("Počet", "Count")), 0, 3); t.Controls.Add(countRow, 1, 3);
            t.Controls.Add(Lbl("Max stack"), 0, 4); t.Controls.Add(numMax, 1, 4);
            t.Controls.Add(chkQuest, 1, 5);
            t.Controls.Add(btnRepair, 1, 6);

            Func<string, Label> Head = s => new Label { Text = s, Dock = DockStyle.Top, Height = 30, TextAlign = ContentAlignment.BottomLeft, Font = new Font("Segoe UI Semibold", 9.5f), ForeColor = Theme.Accent };
            p.Controls.Add(partBtns); p.Controls.Add(contents); p.Controls.Add(Head(L.T("Obsah / nasazené díly (dvojklik = vybrat)", "Contents / attached parts (double-click = select)")));
            p.Controls.Add(stats); p.Controls.Add(Head(L.T("Statistiky (hodnoty jdou editovat)", "Stats (values are editable)"))); p.Controls.Add(t);
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
                try { gunsmith = SaveDoc.TryLoad(Path.Combine(dir, "GunsmithSaveGame.sav")); } catch { gunsmith = null; }
                Catalog.Build(new[] { vendor, stash, inv }.Where(x => x != null));
                history.Clear();
                undo.Clear(); UpdateUndoButtons();
                RefreshStamps();
                RebuildTree();
                selItem = null;
                if (stash != null) SetView(ViewKind.StashAll, null, false);
                else if (inv != null) SetView(ViewKind.Loadout, null, false);
                ShowDetails(null);
                status.Text = L.T("Načteno: ", "Loaded: ") + dir + "   ·   " + L.T("katalog " + Catalog.Entries.Count + " předmětů", "catalog of " + Catalog.Entries.Count + " items");
                ShowValidation(false);
                UpdateTitle();
            }
            catch (Exception ex)
            {
                DarkMsg.Show(this, L.T("Chyba při načítání:\n", "Error while loading:\n") + ex, L.T("Chyba", "Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { Cursor = Cursors.Default; }
        }

        public static bool GameRunning()
        {
            return Process.GetProcesses().Any(p =>
            {
                try { var n = p.ProcessName; return n.StartsWith("Test_C", StringComparison.OrdinalIgnoreCase) || n.IndexOf("IncursionRedRiver", StringComparison.OrdinalIgnoreCase) >= 0; }
                catch { return false; }
            });
        }

        string BackupRoot() { return Path.Combine(dir, "_IRR_Editor_Backups"); }

        // ---- change detection: did the game write the saves while the editor was open?
        void RefreshStamps()
        {
            stamps.Clear();
            try { foreach (var f in Directory.GetFiles(dir, "*.sav")) stamps[f] = File.GetLastWriteTimeUtc(f); } catch { }
        }

        List<string> ChangedOnDisk()
        {
            var res = new List<string>();
            try
            {
                foreach (var f in Directory.GetFiles(dir, "*.sav"))
                {
                    DateTime t;
                    if (!stamps.TryGetValue(f, out t) || File.GetLastWriteTimeUtc(f) != t) res.Add(Path.GetFileName(f));
                }
            }
            catch { }
            return res;
        }

        bool checkingDisk;
        void CheckDiskChanges()
        {
            if (dir == null || stamps.Count == 0 || checkingDisk) return;
            var ch = ChangedOnDisk();
            if (ch.Count == 0) return;
            checkingDisk = true;
            try
            {
                if (!AnyDirty())
                {
                    LoadAll();
                    status.Text = L.T("Savy se mezitím změnily na disku (hra?) – načteno znovu.", "The saves changed on disk (the game?) – reloaded.");
                }
                else warn.Text = L.T("⚠ Savy se změnily na disku – při uložení se zeptám", "⚠ Saves changed on disk – you'll be asked on save");
            }
            finally { checkingDisk = false; }
        }

        IEnumerable<SaveDoc> Docs
        {
            get
            {
                if (baseData != null) foreach (var d in baseData.Docs) yield return d;
                if (gunsmith != null) yield return gunsmith;
            }
        }

        void SaveAll()
        {
            var dirty = new[] { stash, inv }.Where(s => s != null && s.Dirty).ToList();
            var dirtyDocs = Docs.Where(d => d.Dirty).ToList();
            if (dirty.Count == 0 && dirtyDocs.Count == 0) { status.Text = L.T("Žádné změny k uložení.", "Nothing to save."); return; }
            if (GameRunning())
            {
                DarkMsg.Show(this, L.T("Hra právě běží. Ukládej jen když je hra vypnutá – jinak ji hra při ukončení přepíše (nebo se rozbije stav).",
                                       "The game is running. Only save while the game is closed – otherwise the game overwrites the files on exit (or the state breaks)."),
                             L.T("Hra běží", "Game is running"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var changed = ChangedOnDisk();
            if (changed.Count > 0)
            {
                var r = DarkMsg.Show(this, L.T("Tyto savy se od načtení změnily na disku (nejspíš je uložila hra):\n" + string.Join(", ", changed) +
                                               "\n\nAno = načíst je znovu (tvoje neuložené změny se zahodí)\nNe = přesto uložit (přepíše novější stav ze hry)",
                                               "These saves changed on disk since loading (probably written by the game):\n" + string.Join(", ", changed) +
                                               "\n\nYes = reload them (your unsaved changes are discarded)\nNo = save anyway (overwrites the newer state from the game)"),
                                     L.T("Savy se změnily", "Saves changed"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (r == DialogResult.Yes) { LoadAll(); return; }
                if (r != DialogResult.No) return;
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
                RefreshStamps();
                warn.Text = "";
                status.Text = L.T("Uloženo (", "Saved (") + string.Join(", ", writes.Select(w => Path.GetFileName(w.Key))) + L.T("). Záloha: ", "). Backup: ") + bdir;
                UpdateTitle();
                ShowValidation(false);
            }
            catch (Exception ex)
            {
                DarkMsg.Show(this, L.T("Uložení selhalo:\n", "Saving failed:\n") + ex.Message, L.T("Chyba", "Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        bool AnyDirty()
        {
            return new[] { stash, inv }.Any(s => s != null && s.Dirty) || Docs.Any(d => d.Dirty);
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

        void UpdateTitle()
        {
            Text = AppTitle + (AnyDirty() ? L.T("  •  neuloženo", "  •  unsaved") : "");
        }

        void ShowValidation(bool popup)
        {
            var issues = new[] { stash, inv }.Where(s => s != null).SelectMany(s => s.Validate()).ToList();
            if (!warn.Text.StartsWith("⚠ " + L.T("Savy", "Saves")))
                warn.Text = issues.Count == 0 ? "" : L.T("⚠ " + issues.Count + " problém(ů) – Nástroje → Zkontrolovat", "⚠ " + issues.Count + " issue(s) – Tools → Check");
            if (popup)
                DarkMsg.Show(this, issues.Count == 0 ? L.T("Vše v pořádku – žádné překryvy ani předměty mimo mřížku.", "All good – no overlaps and nothing outside the grid.") : string.Join("\n", issues.Take(30)),
                             L.T("Kontrola konzistence", "Consistency check"));
        }

        // ================================================================== undo / redo
        List<IUndoTarget> UndoTargets()
        {
            var l = new List<IUndoTarget>();
            if (stash != null) l.Add(stash);
            if (inv != null) l.Add(inv);
            foreach (var d in Docs) l.Add(d);
            return l;
        }

        string pendingSelect;
        void AfterRestore()
        {
            if (stash != null) stash.Rebuild();
            if (inv != null) inv.Rebuild();
            if (baseData != null) baseData = BaseData.Build(baseData.Hideout, baseData.Resource, baseData.Mission);
            StructureChanged(pendingSelect);
        }

        void Checkpoint(string label, string key = null)
        {
            undo.Checkpoint(label, key);
            UpdateUndoButtons();
        }

        void UpdateUndoButtons()
        {
            btnUndo.Enabled = undo.CanUndo;
            btnRedo.Enabled = undo.CanRedo;
        }

        void DoUndo()
        {
            if (!undo.CanUndo) return;
            pendingSelect = selItem == null ? null : selItem.UidKey;
            var l = undo.Undo();
            status.Text = L.T("Vráceno: ", "Undone: ") + l;
            UpdateUndoButtons(); UpdateTitle();
        }

        void DoRedo()
        {
            if (!undo.CanRedo) return;
            pendingSelect = selItem == null ? null : selItem.UidKey;
            var l = undo.Redo();
            status.Text = L.T("Znovu provedeno: ", "Redone: ") + l;
            UpdateUndoButtons(); UpdateTitle();
        }

        // ================================================================== tree
        void RebuildTree()
        {
            tree.BeginUpdate();
            tree.Nodes.Clear();
            foreach (var s in new[] { stash, inv })
            {
                if (s == null) continue;
                var root = tree.Nodes.Add(s.Kind == "Stash" ? L.T("📦 Sklad (všechny strany)", "📦 Stash (all pages)") : L.T("🎒 Výbava postavy", "🎒 Character loadout"));
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
            HighlightTree();
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
                SetView(save.Kind == "Inventory" ? ViewKind.Loadout : ViewKind.StashAll, null, true);
                SelectItem(null, false);
                return;
            }
            var mc = e.Node.Tag as MainContainer;
            var it = e.Node.Tag as InvItem;
            if (mc != null && mc.HasGrid) { SetView(ViewKind.Container, mc, true); return; }
            if (mc != null && mc.Items.Count > 0) it = mc.Items[0];
            if (mc != null && mc.Items.Count == 0 && mc.Save.Kind == "Inventory") { SetView(ViewKind.Loadout, null, true); SelectItem(null, false); return; }
            if (it != null) SelectItem(it, false);
        }

        // ================================================================== views / navigation
        static MainContainer GridAncestor(InvItem it)
        {
            var probe = it == null ? null : it.Container;
            while (probe != null && !probe.HasGrid && probe.OwnerItem != null) probe = probe.OwnerItem.Container;
            return probe != null && probe.HasGrid ? probe : null;
        }

        MainContainer MainStash { get { return stash == null ? null : stash.Containers.FirstOrDefault(c => c.IsTopLevel && c.HasGrid); } }

        void SetView(ViewKind kind, MainContainer mc, bool push)
        {
            if (push && (kind != view || mc != viewMc) && (view != ViewKind.Container || viewMc != null))
                history.Push(new ViewRef { Kind = view, Mc = viewMc });
            view = kind; viewMc = mc;
            var secs = new List<GridSection>();
            string title;
            if (kind == ViewKind.Container && mc != null)
            {
                secs.Add(new GridSection { Mc = mc });
                title = mc.Title + "   ·   " + (mc.Save.Kind == "Stash" ? L.T("Sklad", "Stash") : L.T("Výbava", "Loadout"));
                curMC = mc;
            }
            else if (kind == ViewKind.StashAll && stash != null)
            {
                foreach (var key in stash.TopUids)
                {
                    MainContainer m;
                    if (stash.ByUid.TryGetValue(key, out m) && m.HasGrid) secs.Add(new GridSection { Mc = m, Title = m.Title });
                }
                title = L.T("Sklad   ·   všechny strany", "Stash   ·   all pages");
                curMC = MainStash;
            }
            else
            {
                view = ViewKind.Loadout;
                if (inv != null)
                    foreach (var key in inv.TopUids)
                    {
                        MainContainer m;
                        if (!inv.ByUid.TryGetValue(key, out m)) continue;
                        if (m.HasGrid) { secs.Add(new GridSection { Mc = m, Title = m.Title }); continue; }
                        secs.Add(new GridSection { Mc = m, Title = m.Title, SlotOnly = true });
                        var it = m.Items.FirstOrDefault();
                        var sub = it == null ? null : inv.ContainerOf(it);
                        if (sub != null && sub.HasGrid) secs.Add(new GridSection { Mc = sub, Title = "↳ " + it.DisplayName + L.T(" – obsah", " – contents") });
                    }
                title = L.T("Výbava postavy   ·   přehled", "Loadout   ·   overview");
                curMC = null;
            }
            gridTitle.Text = "  " + title;
            btnBack.Enabled = history.Count > 0;
            grid.NoWrap = kind == ViewKind.StashAll;
            grid.Show(secs);
            grid.Search = SearchQuery;
            UpdateGridStats();
        }

        void UpdateGridStats()
        {
            int n = 0; float kg = 0;
            foreach (var mc in grid.Containers.Distinct())
                foreach (var it in mc.Items)
                {
                    n++;
                    kg += it.Weight * Math.Max(1, it.CanStack ? it.Count : 1);
                    foreach (var d in it.Container.Save.Descendants(it)) kg += d.Weight * Math.Max(1, d.CanStack ? d.Count : 1);
                }
            gridStats.Text = L.T(n + " předmětů  ·  " + kg.ToString("0.#") + " kg", n + " items  ·  " + kg.ToString("0.#") + " kg");
        }

        void GoBack()
        {
            if (history.Count == 0) return;
            var v = history.Pop();
            SetView(v.Kind, v.Mc, false);
        }

        public void ShowContainer(MainContainer mc, bool push) { SetView(ViewKind.Container, mc, push); }

        bool VisibleInGrid(InvItem it)
        {
            if (it == null) return true;
            if (grid.Shows(it.Container)) return true;
            var probe = GridAncestor(it);
            if (probe == null) return view == ViewKind.Loadout && it.Container.Save == inv;
            return grid.Shows(probe);
        }

        /// <summary>Bring the item on screen (switching views if needed) and make it the selection.</summary>
        public void SelectItem(InvItem it, bool syncTree)
        {
            selItem = it;
            if (it != null && !VisibleInGrid(it))
            {
                var probe = GridAncestor(it);
                if (probe == null) SetView(ViewKind.Loadout, null, true);
                else if (probe.Save == stash && stash.TopUids.Contains(probe.UidKey)) SetView(ViewKind.StashAll, null, true);
                else SetView(ViewKind.Container, probe, true);
            }
            if (view != ViewKind.Container && it != null && it.Container.HasGrid) curMC = it.Container; // target for Add / Sort
            grid.Selected = it;
            grid.Multi.Clear();
            grid.Invalidate();
            ShowDetails(it);
            if (it != null)
            {
                var n = FindNode(tree.Nodes, it);
                if (n != null && tree.SelectedNode != n) { syncingTree = true; tree.SelectedNode = n; syncingTree = false; }
            }
        }

        public void ToggleMulti(InvItem it)
        {
            if (selItem == null) { SelectItem(it, true); return; }
            if (it == selItem) return;
            if (!grid.Multi.Remove(it)) grid.Multi.Add(it);
            grid.Invalidate();
            ShowDetails(selItem);
        }

        public void SelectMany(List<InvItem> items)
        {
            if (items.Count == 0) { SelectItem(null, true); return; }
            SelectItem(items[0], true);
            foreach (var it in items.Skip(1)) grid.Multi.Add(it);
            grid.Invalidate();
            ShowDetails(selItem);
        }

        List<InvItem> Selection()
        {
            var l = new List<InvItem>();
            if (selItem != null) l.Add(selItem);
            l.AddRange(grid.Multi.Where(x => x != selItem));
            return l;
        }

        public void OpenItem(InvItem it)
        {
            var sub = it.Container.Save.ContainerOf(it);
            if (sub != null && sub.HasGrid) SetView(ViewKind.Container, sub, true);
            else if (sub != null && sub.Elements.Count > 0 && Catalog.Sockets.Keys.Any(k => k.StartsWith(it.Def + "#"))) { SelectItem(it, true); OpenParts(); }
            else SelectItem(it, true);
        }

        void ShowDetails(InvItem it)
        {
            loadingDetails = true;
            details.Visible = it != null;
            emptyHint.Visible = it == null;
            if (it == null)
            {
                lblName.Text = ""; lblInfo.Text = ""; txtDef.Text = ""; stats.Rows.Clear(); contents.Items.Clear();
                loadingDetails = false; return;
            }
            int extra = grid.Multi.Count(x => x != it);
            lblName.Text = it.DisplayName + (extra > 0 ? L.T("   (+" + extra + " dalších)", "   (+" + extra + " more)") : "");
            CatalogEntry ce; Catalog.Entries.TryGetValue(it.Def, out ce);
            var s = it.Container.Save;
            var desc = s.Descendants(it).ToList();
            float totalW = it.Weight * Math.Max(1, it.CanStack ? it.Count : 1) + desc.Sum(d => d.Weight * Math.Max(1, d.CanStack ? d.Count : 1));
            lblInfo.Text = it.Category + "  ·  " + it.BaseW + "×" + it.BaseH + (it.Rotated ? L.T(" (otočeno)", " (rotated)") : "")
                + "  ·  " + totalW.ToString("0.###") + " kg"
                + (it.State.Length > 2 ? L.T("\nStav: ", "\nState: ") + it.State : "")
                + (ce != null && ce.NewDurability != null ? L.T("\nVýdrž nového kusu: ", "\nDurability when new: ") + ce.NewDurability.Value.ToString("0.##") : "")
                + (it.Count > it.MaxStack ? L.T("\n⚠ Počet je vyšší než Max stack", "\n⚠ Count is higher than Max stack") : "")
                + (extra > 0 ? L.T("\nVybráno " + (extra + 1) + " předmětů – Smazat, Opravit a Přesunout platí pro všechny.", "\n" + (extra + 1) + " items selected – Delete, Repair and Move apply to all.") : "");
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
            var own = s.ContainerOf(it);
            btnParts.Visible = own != null && !own.HasGrid && own.Elements.Count > 0;
            btnDetach.Visible = contents.Items.Count > 0;
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
                Checkpoint(L.T("Úprava statistiky ", "Edit stat ") + st.ShortTag);
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

        InvItem FindItem(string uid)
        {
            if (uid == null) return null;
            InvItem it;
            if (stash != null && stash.ItemsByUid.TryGetValue(uid, out it)) return it;
            if (inv != null && inv.ItemsByUid.TryGetValue(uid, out it)) return it;
            return null;
        }

        void StructureChanged(InvItem select) { StructureChanged(select == null ? null : select.UidKey); }

        /// <summary>Everything was rebuilt: re-resolve view/history by UID, refresh tree, grid and selection.</summary>
        void StructureChanged(string selectUid)
        {
            foreach (var s in new[] { stash, inv }) if (s != null) s.Rebuild();
            Func<MainContainer, MainContainer> resolve = m =>
            {
                if (m == null) return null;
                MainContainer r;
                return m.Save.ByUid.TryGetValue(m.UidKey, out r) ? r : null;
            };
            var hist = history.Reverse().Select(h => new ViewRef { Kind = h.Kind, Mc = resolve(h.Mc) })
                              .Where(h => h.Kind != ViewKind.Container || h.Mc != null).ToList();
            history.Clear(); foreach (var h in hist) history.Push(h);
            var vm = resolve(viewMc);
            if (view == ViewKind.Container && vm == null) { view = ViewKind.StashAll; }
            var keepCur = resolve(curMC);
            RebuildTree();
            SetView(view, vm, false);
            if (keepCur != null && view != ViewKind.Container) curMC = keepCur;
            SelectItem(FindItem(selectUid), true);
            UpdateTitle();
            ShowValidation(false);
            UpdateUndoButtons();
        }

        // ================================================================== search
        string SearchQuery { get { return searchBox == null ? "" : searchBox.Text.Trim().ToLowerInvariant(); } }

        IEnumerable<InvItem> AllItems()
        {
            foreach (var s in new[] { stash, inv })
                if (s != null) foreach (var it in s.ItemsByUid.Values) yield return it;
        }

        bool searchStatus;   // status bar currently shows search results
        void ApplySearch()
        {
            grid.Search = SearchQuery;
            grid.Invalidate();
            HighlightTree();
            var q = SearchQuery;
            if (q.Length == 0) { if (searchStatus) status.Text = ""; searchStatus = false; return; }
            searchStatus = true;
            int a = stash == null ? 0 : stash.ItemsByUid.Values.Count(i => GridPanel.Matches(i, q));
            int b = inv == null ? 0 : inv.ItemsByUid.Values.Count(i => GridPanel.Matches(i, q));
            status.Text = L.T("Nalezeno: " + (a + b) + "  (sklad " + a + ", výbava " + b + ")  ·  Enter = další", "Found: " + (a + b) + "  (stash " + a + ", loadout " + b + ")  ·  Enter = next");
        }

        void HighlightTree()
        {
            var q = SearchQuery;
            Action<TreeNodeCollection> walk = null;
            walk = nodes =>
            {
                foreach (TreeNode n in nodes)
                {
                    var it = n.Tag as InvItem;
                    bool hit = it != null && GridPanel.Matches(it, q);
                    n.BackColor = hit ? Theme.AccentBg : Color.Empty;
                    if (hit && n.Parent != null) n.Parent.Expand();
                    walk(n.Nodes);
                }
            };
            tree.BeginUpdate(); walk(tree.Nodes); tree.EndUpdate();
        }

        void JumpToMatch()
        {
            var q = SearchQuery; if (q.Length == 0) return;
            var list = AllItems().Where(i => GridPanel.Matches(i, q)).ToList();
            if (list.Count == 0) return;
            int idx = selItem == null ? -1 : list.IndexOf(selItem);
            SelectItem(list[(idx + 1) % list.Count], true);
            status.Text = L.T("Výsledek ", "Match ") + ((idx + 1) % list.Count + 1) + " / " + list.Count;
        }

        // ================================================================== actions
        void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.F) { searchBox.Focus(); searchBox.SelectAll(); e.Handled = true; return; }
            if (searchBox.Focused) return;
            if (e.Control && e.KeyCode == Keys.S) { SaveAll(); e.Handled = true; return; }
            if (ActiveControl is TextBox || stats.IsCurrentCellInEditMode || stats.Focused) return;
            if (e.Control && e.KeyCode == Keys.Z && !e.Shift) { DoUndo(); e.Handled = true; }
            else if (e.Control && (e.KeyCode == Keys.Y || (e.KeyCode == Keys.Z && e.Shift))) { DoRedo(); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete) { DeleteSel(); e.Handled = true; }
            else if (e.KeyCode == Keys.R && !e.Control) { RotateSel(); e.Handled = true; }
            else if (e.KeyCode == Keys.D && e.Control) { Duplicate(); e.Handled = true; }
            else if (e.KeyCode == Keys.Insert) { AddItemDialog(); e.Handled = true; }
            else if (e.KeyCode == Keys.Escape) { SelectItem(null, true); e.Handled = true; }
            else if (e.KeyCode == Keys.Back && history.Count > 0) { GoBack(); e.Handled = true; }
        }

        /// <summary>Right-click menu on a grid item.</summary>
        public void ShowItemMenu(InvItem it, Point screen)
        {
            var sel = Selection();
            var m = new ContextMenuStrip { Renderer = new Theme.DarkRenderer(), BackColor = Theme.Surface, ForeColor = Theme.Text, ShowImageMargin = false, Font = Font };
            Action<string, EventHandler, bool> mi = (t, h, en) => { var x = new ToolStripMenuItem(t) { Enabled = en }; x.Click += h; m.Items.Add(x); };
            string n = sel.Count > 1 ? " (" + sel.Count + ")" : "";
            var move = new ToolStripMenuItem(L.T("⇄ Přesunout do", "⇄ Move to") + n);
            foreach (var s in new[] { stash, inv })
            {
                if (s == null) continue;
                foreach (var kv in s.GridContainers())
                {
                    var target = kv.Value;
                    if (sel.Any(x => x.Container == target || s.IsInside(target, x))) continue;
                    var x2 = new ToolStripMenuItem((s.Kind == "Stash" ? L.T("Sklad: ", "Stash: ") : L.T("Výbava: ", "Loadout: ")) + kv.Key);
                    x2.Click += delegate { MoveSelectionTo(target); };
                    move.DropDownItems.Add(x2);
                }
            }
            if (move.DropDownItems.Count == 0) move.Enabled = false;
            foreach (ToolStripItem d in move.DropDownItems) d.ForeColor = Theme.Text;
            ((ToolStripDropDownMenu)move.DropDown).ShowImageMargin = false;
            move.DropDown.BackColor = Theme.Surface;
            m.Items.Add(move);
            var sub = it.Container.Save.ContainerOf(it);
            if (sub != null && sub.HasGrid) mi(L.T("▣ Otevřít", "▣ Open"), delegate { SetView(ViewKind.Container, sub, true); }, true);
            if (sub != null && !sub.HasGrid && sub.Elements.Count > 0) mi(L.T("🔩 Díly zbraně…", "🔩 Weapon parts…"), delegate { OpenParts(); }, sel.Count == 1);
            mi(L.T("↻ Otočit (R)", "↻ Rotate (R)"), delegate { RotateSel(); }, sel.Count == 1);
            mi(L.T("⧉ Duplikovat (Ctrl+D)", "⧉ Duplicate (Ctrl+D)"), delegate { Duplicate(); }, sel.Count == 1);
            mi(L.T("🔧 Opravit", "🔧 Repair") + n, delegate { RepairSel(); }, true);
            m.Items.Add(new ToolStripSeparator());
            mi(L.T("✕ Smazat (Del)", "✕ Delete (Del)") + n, delegate { DeleteSel(); }, true);
            m.Closed += delegate { BeginInvoke((Action)(() => m.Dispose())); };
            m.Show(screen);
        }

        /// <summary>Drop from the grid: same container = reposition, other container (even other save) = move.</summary>
        public void DropItem(InvItem it, MainContainer target, GridElement ge, int row, int col, bool rotated)
        {
            var s = target.Save;
            int w = rotated ? it.BaseH : it.BaseW, h = rotated ? it.BaseW : it.BaseH;
            bool same = target == it.Container;
            if (s.IsInside(target, it)) { status.Text = L.T("Předmět nejde vložit sám do sebe.", "An item can't go inside itself."); grid.Invalidate(); return; }
            if (!s.Fits(target, ge, row, col, w, h, same ? it : null)) { status.Text = L.T("Sem se to nevejde.", "It doesn't fit here."); grid.Invalidate(); return; }
            Checkpoint(L.T("Přesun ", "Move ") + it.DisplayName);
            if (same)
            {
                it.ContainerIndex = ge.Index; it.SlotIndex = row * ge.Cols + col; it.Rotated = rotated;
                Changed(it);
                SelectItem(it, true);
                UpdateGridStats();
                return;
            }
            var moved = InvSave.Move(it, target, ge.Index, row * ge.Cols + col, rotated);
            status.Text = L.T("Přesunuto do: ", "Moved to: ") + target.Title;
            StructureChanged(moved);
        }

        void MoveSelectionTo(MainContainer target)
        {
            var uids = Selection().Select(x => x.UidKey).ToList();
            if (uids.Count == 0) return;
            string targetUid = target.UidKey; var ts = target.Save;
            Checkpoint(L.T("Přesun do ", "Move to ") + target.Title);
            int ok = 0, failed = 0; InvItem last = null;
            foreach (var uid in uids)
            {
                var it = FindItem(uid);
                MainContainer tgt;
                if (it == null || !ts.ByUid.TryGetValue(targetUid, out tgt)) continue;
                if (it.Container == tgt || ts.IsInside(tgt, it)) continue;
                var spot = ts.FindSpot(tgt, it.BaseW, it.BaseH, null, -1);
                if (spot == null) { failed++; continue; }
                last = InvSave.Move(it, tgt, spot[0], spot[1], spot[2] == 1);
                ok++;
            }
            if (ok == 0) undo.Discard();
            status.Text = L.T("Přesunuto: " + ok + (failed > 0 ? ", nevešlo se: " + failed : ""), "Moved: " + ok + (failed > 0 ? ", didn't fit: " + failed : ""));
            StructureChanged(last);
        }

        void DeleteSel()
        {
            var sel = Selection();
            if (sel.Count == 0) return;
            int nested = sel.Sum(x => x.Container.Save.Descendants(x).Count());
            if ((sel.Count > 1 || nested > 0) && DarkMsg.Show(this,
                    L.T("Smazat " + (sel.Count == 1 ? sel[0].DisplayName : sel.Count + " předmětů") + (nested > 0 ? " včetně " + nested + " vnořených" : "") + "?\n(Jde vrátit přes Zpět.)",
                        "Delete " + (sel.Count == 1 ? sel[0].DisplayName : sel.Count + " items") + (nested > 0 ? " including " + nested + " nested" : "") + "?\n(Can be undone.)"),
                    L.T("Smazat", "Delete"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Checkpoint(L.T("Smazat ", "Delete ") + (sel.Count == 1 ? sel[0].DisplayName : sel.Count + L.T(" předmětů", " items")));
            foreach (var uid in sel.Select(x => x.UidKey).ToList())
            {
                var it = FindItem(uid);
                if (it == null) continue;
                var s = it.Container.Save;
                s.RemoveItem(it); s.Rebuild();
            }
            StructureChanged((string)null);
        }

        void RepairSel()
        {
            var sel = Selection();
            if (sel.Count == 0) return;
            Checkpoint(L.T("Opravit", "Repair"));
            int n = 0;
            foreach (var it in sel) { if (Tools.Repair(it)) { n++; it.Container.Save.Dirty = true; } }
            if (n == 0) { undo.Discard(); UpdateUndoButtons(); status.Text = L.T("Není co opravovat.", "Nothing to repair."); return; }
            status.Text = L.T("Opraveno: ", "Repaired: ") + n;
            grid.Invalidate(); ShowDetails(selItem); UpdateTitle();
        }

        void RotateSel()
        {
            var it = selItem;
            if (it == null || it.SlotIndex < 0) return;
            var ge = it.Container.Elements.FirstOrDefault(e => e.Index == it.ContainerIndex);
            if (ge == null || !ge.IsGrid) return;
            int r = it.SlotIndex / ge.Cols, c = it.SlotIndex % ge.Cols;
            if (it.Container.Save.Fits(it.Container, ge, r, c, it.H, it.W, it)) { Checkpoint(L.T("Otočit", "Rotate")); it.Rotated = !it.Rotated; Changed(it); }
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
            Checkpoint(L.T("Duplikovat ", "Duplicate ") + selItem.DisplayName);
            var n = s.Insert(t, target, spot[0], spot[1], spot[2] == 1, 0);
            StructureChanged(n);
        }

        void AddItemDialog()
        {
            if (curMC == null || !curMC.HasGrid)
            {
                DarkMsg.Show(this, view == ViewKind.Loadout
                    ? L.T("V přehledu výbavy nejdřív klikni na předmět v batohu, vestě nebo pouzdře – do toho kontejneru se pak bude přidávat.",
                          "In the loadout overview, first click an item in the backpack, rig or pouch – new items go into that container.")
                    : L.T("Nejdřív otevři kontejner s mřížkou (sklad, batoh, vestu…).", "Open a container with a grid first (stash, backpack, rig…)."), L.T("Přidat předmět", "Add item"));
                return;
            }
            using (var d = new AddItemForm(curMC.Title))
            {
                if (d.ShowDialog(this) != DialogResult.OK || d.Chosen == null) return;
                Checkpoint(L.T("Přidat ", "Add ") + d.Chosen.Name);
                InvItem last = null;
                int failed = 0;
                string uid = curMC.UidKey; var s = curMC.Save;
                for (int k = 0; k < d.Copies; k++)
                {
                    var target = s.ByUid[uid];
                    var spot = s.FindSpot(target, d.Chosen.W, d.Chosen.H, null, -1);
                    if (spot == null) { failed = d.Copies - k; break; }
                    last = s.Insert(d.Chosen.Template, target, spot[0], spot[1], spot[2] == 1, d.Count);
                }
                if (last == null) undo.Discard();
                StructureChanged(last);
                if (failed > 0) DarkMsg.Show(this, L.T(failed + "× se nevešlo – kontejner je plný.", failed + "× didn't fit – the container is full."), L.T("Přidat předmět", "Add item"));
            }
        }

        void Bulk(string undoLabel, string label, Func<InvSave, int> f)
        {
            Checkpoint(undoLabel);
            int n = 0;
            foreach (var s in new[] { stash, inv }) if (s != null) n += f(s);
            if (n == 0) undo.Discard();
            status.Text = label + ": " + n;
            StructureChanged(selItem);
        }

        void MergeCur()
        {
            if (curMC == null) return;
            Checkpoint(L.T("Sloučit stacky", "Merge stacks"));
            int removed = Tools.MergeStacks(curMC.Save, curMC);
            status.Text = L.T("Sloučeno – ušetřeno " + removed + " políček/stacků.", "Merged – " + removed + " stacks freed.");
            StructureChanged((string)null);
        }

        void SortCur()
        {
            if (curMC == null || !curMC.HasGrid) return;
            Checkpoint(L.T("Seřadit ", "Sort ") + curMC.Title);
            if (Tools.Sort(curMC.Save, curMC)) status.Text = L.T("Seřazeno: ", "Sorted: ") + curMC.Title;
            else { undo.Discard(); status.Text = L.T("Seřazení se nevešlo – nic se nezměnilo.", "Sorting didn't fit – nothing changed."); }
            StructureChanged(selItem);
        }

        void UnlockPages()
        {
            if (stash == null) return;
            if (DarkMsg.Show(this, L.T("Odemkne všechna zamčená políčka na stranách skladu.\nHra je může při dalším načtení znovu zamknout podle úrovně úkrytu. Pokračovat?",
                                       "Unlocks every locked cell on the stash pages.\nThe game may lock them again on load, based on the hideout level. Continue?"),
                             L.T("Experimentální", "Experimental"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            Checkpoint(L.T("Odemknout sklad", "Unlock stash"));
            int n = Tools.UnlockStashPages(stash);
            if (n == 0) undo.Discard();
            status.Text = L.T("Odemčeno stran: ", "Pages unlocked: ") + n;
            StructureChanged((string)null);
        }

        // ---- weapon parts
        void OpenParts()
        {
            if (selItem == null) return;
            var s = selItem.Container.Save; string uid = selItem.UidKey;
            using (var f = new AttachForm(s, selItem, l => Checkpoint(l)))
            {
                f.ShowDialog(this);
                if (f.Changed) { status.Text = L.T("Díly zbraně upraveny.", "Weapon parts changed."); StructureChanged(uid); }
            }
        }

        void DetachPart()
        {
            var e = contents.SelectedItem as ContentEntry;
            if (e == null) { status.Text = L.T("Nejdřív vyber díl v seznamu obsahu.", "Select a part in the contents list first."); return; }
            var part = e.Item; string keep = selItem == null ? null : selItem.UidKey;
            // first grid with room: the open container, the main stash, the other stash pages, then the loadout
            var candidates = new List<MainContainer>();
            if (curMC != null && curMC.HasGrid) candidates.Add(curMC);
            if (MainStash != null) candidates.Add(MainStash);
            foreach (var s in new[] { stash, inv }) if (s != null) candidates.AddRange(s.GridContainers().Select(k => k.Value));
            foreach (var target in candidates.Distinct())
            {
                if (target.Save.IsInside(target, part)) continue;
                var spot = target.Save.FindSpot(target, part.BaseW, part.BaseH, null, -1);
                if (spot == null) continue;
                Checkpoint(L.T("Sundat ", "Detach ") + part.DisplayName);
                InvSave.Move(part, target, spot[0], spot[1], spot[2] == 1);
                status.Text = L.T("Díl přesunut do: ", "Part moved to: ") + target.Title;
                StructureChanged(keep);
                return;
            }
            status.Text = L.T("Nikde není místo pro tento díl.", "There's no room for this part anywhere.");
        }

        // ---- windows
        void OpenBase()
        {
            if (baseData == null) return;
            Checkpoint(L.T("Báze a reputace", "Base and reputation"));
            using (var f = new BaseForm(baseData))
            {
                if (f.ShowDialog(this) == DialogResult.OK && f.Changed) { status.Text = L.T("Báze / reputace upravena – nezapomeň uložit.", "Base / reputation changed – don't forget to save."); UpdateTitle(); }
                else undo.Discard();
            }
            UpdateUndoButtons();
        }

        void OpenGunsmith()
        {
            if (gunsmith == null || !(gunsmith.G.Root["PresetMap"] is MapValue))
            {
                DarkMsg.Show(this, L.T("GunsmithSaveGame.sav nebyl nalezen nebo nemá presety.", "GunsmithSaveGame.sav was not found or has no presets."), L.T("Presety zbraní", "Weapon presets"));
                return;
            }
            using (var f = new GunsmithForm(gunsmith, l => Checkpoint(l)))
            {
                f.ShowDialog(this);
                if (f.Changed) status.Text = L.T("Presety upraveny – nezapomeň uložit.", "Presets changed – don't forget to save.");
            }
            UpdateTitle(); UpdateUndoButtons();
        }

        void OpenBackups()
        {
            if (dir == null) return;
            if (AnyDirty() && !ConfirmDiscard()) return;
            using (var f = new BackupForm(dir, BackupRoot()))
                if (f.ShowDialog(this) == DialogResult.OK && f.Restored)
                {
                    LoadAll();
                    status.Text = L.T("Savy obnoveny ze zálohy.", "Saves restored from backup.");
                }
        }

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
