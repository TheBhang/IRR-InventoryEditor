// Gunsmith presets (GunsmithSaveGame): browse, rename, duplicate, delete.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace IrrEditor
{
    public class GunsmithForm : Form
    {
        readonly SaveDoc doc;
        readonly Action<string> checkpoint;
        TreeView tree;
        ListBox parts;
        Label info;
        public bool Changed;

        MapValue Map { get { return doc.G.Root["PresetMap"] as MapValue; } }

        static string Short(string path) { if (string.IsNullOrEmpty(path)) return "?"; return Catalog.Pretty(path.Substring(path.LastIndexOf('.') + 1)); }

        public GunsmithForm(SaveDoc d, Action<string> checkpoint)
        {
            doc = d; this.checkpoint = checkpoint;
            Theme.DpiForm(this);
            Text = L.T("Presety zbraní (Gunsmith)", "Weapon presets (Gunsmith)");
            Width = 900; Height = 640; MinimumSize = new Size(700, 480);
            StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 9.5f); ShowInTaskbar = false; MinimizeBox = false;

            var split = new SplitContainer { Dock = DockStyle.Fill, Padding = new Padding(10) };
            Load += delegate { split.SplitterDistance = (int)(split.Width * 0.38); };
            tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, FullRowSelect = true };
            tree.AfterSelect += delegate { ShowPreset(); };
            parts = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            info = new Label { Dock = DockStyle.Top, Height = 54, ForeColor = Theme.Accent, Font = new Font("Segoe UI Semibold", 11f), Padding = new Padding(4, 6, 0, 0) };
            split.Panel1.Controls.Add(tree);
            split.Panel2.Controls.Add(parts); split.Panel2.Controls.Add(info);

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(10), FlowDirection = FlowDirection.LeftToRight };
            Action<string, EventHandler> btn = (t, h) => { var b = Theme.MakeButton(t, DialogResult.None, false); b.Click += h; bottom.Controls.Add(b); };
            btn(L.T("✎ Přejmenovat", "✎ Rename"), delegate { Rename(); });
            btn(L.T("⧉ Duplikovat", "⧉ Duplicate"), delegate { Duplicate(); });
            btn(L.T("✕ Smazat", "✕ Delete"), delegate { Delete(); });
            var close = Theme.MakeButton(L.T("Zavřít", "Close"), DialogResult.OK, true);
            close.Margin = new Padding(30, 0, 0, 0);
            bottom.Controls.Add(close);
            bottom.Controls.Add(new Label { Text = L.T("Změny se zapíšou tlačítkem 💾 Uložit v hlavním okně.", "Changes are written by 💾 Save in the main window."), AutoSize = true, ForeColor = Theme.TextDim, Margin = new Padding(16, 8, 0, 0) });
            CancelButton = close;

            Controls.Add(split); Controls.Add(bottom);
            Theme.Apply(this);
            Fill(null);
        }

        // node tags: MapEntry for weapons, PropList for presets
        void Fill(PropList select)
        {
            tree.BeginUpdate(); tree.Nodes.Clear();
            TreeNode sel = null;
            var map = Map;
            if (map != null)
                foreach (var e in map.Entries)
                {
                    var tag = BaseData.TagName(e.Key);
                    var arr = (e.Value as PropList) == null ? null : ((PropList)e.Value)["PresetItems"] as ArrayValue;
                    if (arr == null) continue;
                    var wn = tree.Nodes.Add(tag.Split('.').Last() + "  (" + arr.Items.Count + ")");
                    wn.Tag = e;
                    foreach (PropList p in arr.Items)
                    {
                        var pn = wn.Nodes.Add("★ " + Convert.ToString(p["PresetName"]));
                        pn.Tag = p;
                        if (p == select) sel = pn;
                    }
                    wn.Expand();
                }
            tree.EndUpdate();
            if (tree.Nodes.Count == 0) info.Text = L.T("Žádné presety.", "No presets.");
            if (sel != null) tree.SelectedNode = sel;
        }

        PropList SelectedPreset { get { return tree.SelectedNode == null ? null : tree.SelectedNode.Tag as PropList; } }
        ArrayValue ArrayOf(PropList preset)
        {
            foreach (TreeNode wn in tree.Nodes)
                foreach (TreeNode pn in wn.Nodes)
                    if (pn.Tag == preset) return ((PropList)((MapEntry)wn.Tag).Value)["PresetItems"] as ArrayValue;
            return null;
        }

        void ShowPreset()
        {
            parts.Items.Clear();
            var p = SelectedPreset;
            if (p == null) { info.Text = tree.SelectedNode == null ? "" : tree.SelectedNode.Text; return; }
            var item = p["PresetItem"] as PropList;
            var def = item == null ? null : Convert.ToString(item["ItemDefinition"]);
            info.Text = Convert.ToString(p["PresetName"]) + "\n" + Short(def);
            var atts = item == null ? null : item["DefaultAttachments"] as ArrayValue;
            if (atts == null) return;
            var list = atts.Items.OfType<PropList>().ToList();
            // indent by ParentIndex chain
            Func<int, int> depth = null;
            depth = i => { int pi = (int)list[i]["ParentIndex"]; return pi < 0 || pi >= list.Count || pi == i ? 0 : 1 + depth(pi); };
            for (int i = 0; i < list.Count; i++)
            {
                int cnt = list[i]["Count"] is int ? (int)list[i]["Count"] : 1;
                parts.Items.Add(new string(' ', depth(i) * 4) + "• " + Short(Convert.ToString(list[i]["AttachmentDefinition"])) + (cnt > 1 ? "  ×" + cnt : ""));
            }
        }

        void Rename()
        {
            var p = SelectedPreset; if (p == null) return;
            var cur = Convert.ToString(p["PresetName"]);
            var name = DarkInput.Show(this, L.T("Nový název presetu:", "New preset name:"), L.T("Přejmenovat", "Rename"), cur);
            if (string.IsNullOrWhiteSpace(name) || name == cur) return;
            checkpoint(L.T("Přejmenovat preset", "Rename preset"));
            p.Get("PresetName").Value = new FStr(name.Trim());
            doc.Dirty = true; Changed = true;
            Fill(p);
        }

        void Duplicate()
        {
            var p = SelectedPreset; if (p == null) return;
            var arr = ArrayOf(p); if (arr == null) return;
            checkpoint(L.T("Duplikovat preset", "Duplicate preset"));
            var c = p.DeepClone();
            c.Get("PresetName").Value = new FStr(Convert.ToString(p["PresetName"]) + L.T(" (kopie)", " (copy)"));
            arr.Items.Insert(arr.Items.IndexOf(p) + 1, c);
            doc.Dirty = true; Changed = true;
            Fill(c);
        }

        void Delete()
        {
            var p = SelectedPreset; if (p == null) return;
            var arr = ArrayOf(p); if (arr == null) return;
            if (DarkMsg.Show(this, L.T("Smazat preset „" + p["PresetName"] + "“?", "Delete preset “" + p["PresetName"] + "”?"), L.T("Smazat", "Delete"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            checkpoint(L.T("Smazat preset", "Delete preset"));
            arr.Items.Remove(p);
            doc.Dirty = true; Changed = true;
            Fill(null);
        }
    }
}
