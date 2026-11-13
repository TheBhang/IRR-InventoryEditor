// Smaller windows: weapon parts, backups, about + update check.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace IrrEditor
{
    // ====================================================================== weapon parts
    public class AttachForm : Form
    {
        class SocketRow
        {
            public InvItem Owner; public MainContainer Mc; public int Ci; public SocketInfo Info; public InvItem Occupant; public int Depth;
            public override string ToString()
            {
                string what = Info != null && Info.Label != null ? Info.Label.Split('/').Last() : L.T("slot ", "slot ") + (Ci + 1);
                return new string(' ', Depth * 4) + Owner.DisplayName + " › " + what + ":  " + (Occupant != null ? Occupant.DisplayName : L.T("— prázdné —", "— empty —"));
            }
        }

        readonly InvSave save;
        readonly string rootUid;
        readonly Action<string> checkpoint;
        ListBox sockets, parts;
        Label hint;
        public bool Changed;

        public AttachForm(InvSave s, InvItem root, Action<string> checkpoint)
        {
            save = s; rootUid = root.UidKey; this.checkpoint = checkpoint;
            Theme.DpiForm(this);
            Text = L.T("Díly zbraně – ", "Weapon parts – ") + root.DisplayName;
            Width = 980; Height = 620; MinimumSize = new Size(760, 420);
            StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 9.5f); ShowInTaskbar = false; MinimizeBox = false;

            var split = new SplitContainer { Dock = DockStyle.Fill, Padding = new Padding(10) };
            sockets = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            parts = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            sockets.SelectedIndexChanged += delegate { FillParts(); };
            parts.DoubleClick += delegate { Attach(); };
            split.Panel1.Controls.Add(sockets); split.Panel1.Controls.Add(Caption(L.T("Sloty (díly na zbrani)", "Sockets (parts on the weapon)")));
            split.Panel2.Controls.Add(parts); split.Panel2.Controls.Add(Caption(L.T("Co do slotu pasuje", "What fits this socket")));

            hint = new Label { Dock = DockStyle.Top, Height = 40, ForeColor = Theme.TextDim, Padding = new Padding(12, 8, 12, 0),
                Text = L.T("Kompatibilita se bere z toho, co hra sama na tyto zbraně nasazuje (obchod a tvoje savy). Nasazení do obsazeného slotu původní díl nahradí.",
                           "Compatibility is learnt from what the game itself mounts on these weapons (vendor stock and your saves). Attaching to an occupied socket replaces the part.") };
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(10) };
            var attach = Theme.MakeButton(L.T("⤓ Nasadit vybraný díl", "⤓ Attach selected part"), DialogResult.None, true);
            attach.Click += delegate { Attach(); };
            var remove = Theme.MakeButton(L.T("✕ Odebrat díl ze slotu", "✕ Remove part from socket"), DialogResult.None, false);
            remove.Click += delegate { Remove(); };
            var close = Theme.MakeButton(L.T("Zavřít", "Close"), DialogResult.OK, false);
            close.Margin = new Padding(30, 0, 0, 0);
            bottom.Controls.Add(attach); bottom.Controls.Add(remove); bottom.Controls.Add(close);
            CancelButton = close;
            Controls.Add(split); Controls.Add(hint); Controls.Add(bottom);
            Theme.Apply(this);
            attach.BackColor = Theme.AccentBg; attach.FlatAppearance.BorderColor = Theme.Accent;
            Load += delegate { split.SplitterDistance = split.Width / 2; };
            FillSockets(0);
        }

        static Label Caption(string t) { return new Label { Text = t, Dock = DockStyle.Top, Height = 26, ForeColor = Theme.Accent, Font = new Font("Segoe UI Semibold", 10f) }; }

        InvItem Root { get { InvItem r; return save.ItemsByUid.TryGetValue(rootUid, out r) ? r : null; } }

        void FillSockets(int keepIndex)
        {
            sockets.Items.Clear();
            var root = Root; if (root == null) return;
            Action<InvItem, int> walk = null;
            walk = (it, depth) =>
            {
                var mc = save.ContainerOf(it);
                if (mc == null || mc.HasGrid) return;
                foreach (var ge in mc.Elements)
                {
                    SocketInfo si; Catalog.Sockets.TryGetValue(Catalog.SocketKey(it.Def, ge.Index), out si);
                    var occ = mc.Items.FirstOrDefault(x => x.ContainerIndex == ge.Index);
                    sockets.Items.Add(new SocketRow { Owner = it, Mc = mc, Ci = ge.Index, Info = si, Occupant = occ, Depth = depth });
                    if (occ != null) walk(occ, depth + 1);
                }
            };
            walk(root, 0);
            if (sockets.Items.Count > 0) sockets.SelectedIndex = Math.Min(Math.Max(0, keepIndex), sockets.Items.Count - 1);
            else parts.Items.Clear();
        }

        class PartRow { public CatalogEntry E; public override string ToString() { return E.Name + "   (" + E.Category.Split('/').Last() + ")"; } }

        void FillParts()
        {
            parts.Items.Clear();
            var row = sockets.SelectedItem as SocketRow;
            if (row == null || row.Info == null) { if (row != null) parts.Items.Add(L.T("(pro tento slot nemám žádný známý díl)", "(no known parts for this socket)")); return; }
            foreach (var e in row.Info.Parts.Select(d => { CatalogEntry ce; return Catalog.Entries.TryGetValue(d, out ce) ? ce : null; })
                                            .Where(e => e != null).OrderBy(e => e.Name))
                parts.Items.Add(new PartRow { E = e });
        }

        void Attach()
        {
            var row = sockets.SelectedItem as SocketRow; var part = parts.SelectedItem as PartRow;
            if (row == null || part == null || row.Info == null) return;
            checkpoint(L.T("Nasadit díl", "Attach part"));
            int idx = sockets.SelectedIndex;
            string ownerUid = row.Owner.UidKey; int ci = row.Ci, slot = row.Info.SlotIndex;
            if (row.Occupant != null) { save.RemoveItem(row.Occupant); save.Rebuild(); }
            InvItem owner; if (!save.ItemsByUid.TryGetValue(ownerUid, out owner)) return;
            var mc = save.ContainerOf(owner); if (mc == null) return;
            int count = 0;
            if (part.E.Category.StartsWith("Ammunition"))
            {
                int cap = Tools.MagCapacity(owner);
                count = cap > 0 ? cap : 1;   // magazine: fill it; weapon chamber: one round
            }
            save.Insert(part.E.Template, mc, ci, slot, false, count);
            Changed = true;
            FillSockets(idx);
        }

        void Remove()
        {
            var row = sockets.SelectedItem as SocketRow;
            if (row == null || row.Occupant == null) return;
            checkpoint(L.T("Odebrat díl", "Remove part"));
            int idx = sockets.SelectedIndex;
            save.RemoveItem(row.Occupant); save.Rebuild();
            Changed = true;
            FillSockets(idx);
        }
    }

    // ====================================================================== backups
    public class BackupForm : Form
    {
        readonly string dir, root;
        ListBox list;
        public bool Restored;

        class Entry
        {
            public string Path; public DateTime When; public int Files; public long Bytes;
            public override string ToString()
            {
                return When.ToString(L.En ? "yyyy-MM-dd  HH:mm:ss" : "d. M. yyyy  HH:mm:ss") + "     " + Files + L.T(" souborů", " files") + " · " + (Bytes / 1048576.0).ToString("0.0") + " MB"
                    + (System.IO.Path.GetFileName(Path).Contains("pred-obnovou") ? L.T("   (před obnovou)", "   (before restore)") : "");
            }
        }

        public BackupForm(string saveDir, string backupRoot)
        {
            dir = saveDir; root = backupRoot;
            Theme.DpiForm(this);
            Text = L.T("Zálohy savů", "Save backups");
            Width = 640; Height = 480; MinimumSize = new Size(520, 360);
            StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 9.5f); ShowInTaskbar = false; MinimizeBox = false;
            list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            var top = new Label { Dock = DockStyle.Top, Height = 46, Padding = new Padding(12, 10, 12, 0), ForeColor = Theme.TextDim,
                Text = L.T("Editor zálohuje celou složku savů před každým uložením. Obnovení přepíše současné savy (ty se předtím taky zazálohují).",
                           "The editor backs up the whole save folder before every save. Restoring overwrites the current saves (which are backed up first, too).") };
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(10) };
            var restore = Theme.MakeButton(L.T("⟲ Obnovit vybranou", "⟲ Restore selected"), DialogResult.None, true);
            restore.Click += delegate { Restore(); };
            var open = Theme.MakeButton(L.T("📂 Otevřít složku", "📂 Open folder"), DialogResult.None, false);
            open.Click += delegate { Directory.CreateDirectory(root); Process.Start("explorer.exe", "\"" + root + "\""); };
            var close = Theme.MakeButton(L.T("Zavřít", "Close"), DialogResult.Cancel, false);
            close.Margin = new Padding(30, 0, 0, 0);
            bottom.Controls.Add(restore); bottom.Controls.Add(open); bottom.Controls.Add(close);
            CancelButton = close;
            var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 12, 0) };
            pad.Controls.Add(list);
            Controls.Add(pad); Controls.Add(top); Controls.Add(bottom);
            Theme.Apply(this);
            restore.BackColor = Theme.AccentBg; restore.FlatAppearance.BorderColor = Theme.Accent;
            Fill();
        }

        void Fill()
        {
            list.Items.Clear();
            if (!Directory.Exists(root)) { list.Items.Add(L.T("(zatím žádné zálohy)", "(no backups yet)")); return; }
            foreach (var d in Directory.GetDirectories(root).OrderByDescending(x => x))
            {
                var files = Directory.GetFiles(d, "*.sav");
                if (files.Length == 0) continue;
                list.Items.Add(new Entry { Path = d, When = Directory.GetCreationTime(d), Files = files.Length, Bytes = files.Sum(f => new FileInfo(f).Length) });
            }
            if (list.Items.Count == 0) list.Items.Add(L.T("(zatím žádné zálohy)", "(no backups yet)"));
            else list.SelectedIndex = 0;
        }

        void Restore()
        {
            var e = list.SelectedItem as Entry; if (e == null) return;
            if (MainForm.GameRunning())
            {
                DarkMsg.Show(this, L.T("Hra běží – nejdřív ji zavři.", "The game is running – close it first."), L.T("Obnovení", "Restore"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (DarkMsg.Show(this, L.T("Obnovit savy ze zálohy " + e.When.ToString("d. M. yyyy HH:mm:ss") + "?\nSoučasné savy se nejdřív zazálohují.",
                                       "Restore saves from the backup of " + e.When.ToString("yyyy-MM-dd HH:mm:ss") + "?\nThe current saves are backed up first."),
                             L.T("Obnovení", "Restore"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                var safety = System.IO.Path.Combine(root, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-pred-obnovou");
                Directory.CreateDirectory(safety);
                foreach (var f in Directory.GetFiles(dir, "*.sav")) File.Copy(f, System.IO.Path.Combine(safety, System.IO.Path.GetFileName(f)), true);
                foreach (var f in Directory.GetFiles(e.Path, "*.sav")) File.Copy(f, System.IO.Path.Combine(dir, System.IO.Path.GetFileName(f)), true);
                Restored = true;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                DarkMsg.Show(this, L.T("Obnovení selhalo:\n", "Restore failed:\n") + ex.Message, L.T("Chyba", "Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    // ====================================================================== about + update check
    public class AboutForm : Form
    {
        public const string Repo = "TheBhang/IRR-InventoryEditor";
        Label upd; LinkLabel link;

        public AboutForm()
        {
            Theme.DpiForm(this);
            Text = L.T("O aplikaci", "About");
            FormBorderStyle = FormBorderStyle.FixedDialog; MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 9.5f);
            ClientSize = new Size(560, 300);
            var v = typeof(MainForm).Assembly.GetName().Version;
            var icon = new PictureBox { Left = 20, Top = 20, Width = 64, Height = 64, SizeMode = PictureBoxSizeMode.Zoom };
            try { icon.Image = Icon.ExtractAssociatedIcon(Application.ExecutablePath).ToBitmap(); } catch { }
            var title = new Label { Left = 100, Top = 20, AutoSize = true, Font = new Font("Segoe UI Semibold", 14f), ForeColor = Theme.Accent, Text = "IRR Inventory Editor" };
            var ver = new Label { Left = 102, Top = 56, AutoSize = true, Text = L.T("verze ", "version ") + v.Major + "." + v.Minor + "." + v.Build };
            var body = new Label { Left = 20, Top = 100, Width = 520, Height = 90, Text =
                L.T("Neoficiální editor savů pro Incursion Red River – sklad, výbava, báze a reputace.\n" +
                    "Před každým uložením se savy zálohují. Používej jen se zavřenou hrou.\n" +
                    "Není spojeno s vývojáři hry (Games Farm). Licence GPL-3.0.",
                    "Unofficial save editor for Incursion Red River – stash, loadout, base and reputation.\n" +
                    "Saves are backed up before every write. Only use it while the game is closed.\n" +
                    "Not affiliated with the game's developers (Games Farm). License GPL-3.0.") };
            upd = new Label { Left = 20, Top = 196, Width = 520, Height = 24, Text = L.T("Kontroluji aktualizace…", "Checking for updates…") };
            link = new LinkLabel { Left = 20, Top = 222, AutoSize = true, Text = "github.com/" + Repo, LinkColor = Theme.Accent, ActiveLinkColor = Color.White, VisitedLinkColor = Theme.Accent };
            link.LinkClicked += delegate { Process.Start(link.Tag as string ?? "https://github.com/" + Repo); };
            var ok = Theme.MakeButton("OK", DialogResult.OK, true);
            ok.Location = new Point(440, 250);
            Controls.AddRange(new Control[] { icon, title, ver, body, upd, link, ok });
            AcceptButton = ok; CancelButton = ok;
            Theme.Apply(this);
            upd.ForeColor = Theme.TextDim; title.ForeColor = Theme.Accent; link.LinkColor = Theme.Accent;
            ok.BackColor = Theme.AccentBg; ok.FlatAppearance.BorderColor = Theme.Accent;
            Shown += delegate { CheckAsync(v); };
        }

        void CheckAsync(Version current)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string msg; string url = null; bool newer = false;
                try
                {
                    ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
                    using (var wc = new WebClient())
                    {
                        wc.Headers[HttpRequestHeader.UserAgent] = "IRR-InventoryEditor";
                        wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                        var json = wc.DownloadString("https://api.github.com/repos/" + Repo + "/releases/latest");
                        var tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([0-9.]+)\"").Groups[1].Value;
                        url = Regex.Match(json, "\"html_url\"\\s*:\\s*\"([^\"]+/releases/tag/[^\"]+)\"").Groups[1].Value;
                        Version latest;
                        if (Version.TryParse(tag.Count(c => c == '.') == 1 ? tag + ".0" : tag, out latest))
                        {
                            var cur3 = new Version(current.Major, current.Minor, Math.Max(0, current.Build));
                            newer = latest > cur3;
                            msg = newer ? L.T("✨ Je k dispozici nová verze " + tag + " – klikni na odkaz níže.", "✨ Version " + tag + " is available – click the link below.")
                                        : L.T("✓ Máš nejnovější verzi.", "✓ You have the latest version.");
                        }
                        else msg = L.T("Nepodařilo se zjistit poslední verzi.", "Couldn't determine the latest version.");
                    }
                }
                catch { msg = L.T("Kontrola aktualizací se nepovedla (offline?).", "Update check failed (offline?)."); }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        upd.Text = msg;
                        upd.ForeColor = newer ? Theme.Warn : Theme.TextDim;
                        if (newer && !string.IsNullOrEmpty(url)) { link.Text = url.Replace("https://", ""); link.Tag = url; }
                    }));
                }
                catch { }
            });
        }
    }
}
