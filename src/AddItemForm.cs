// "Add item" dialog: searchable catalog, stack count by typing or slider, number of copies.
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace IrrEditor
{
    public class AddItemForm : Form
    {
        TextBox search; ComboBox cat; ListView list; NumBox count, copies; DarkSlider slider; Label info, sliderMax;
        bool syncing;
        public CatalogEntry Chosen; public int Count; public int Copies;

        public AddItemForm(string targetTitle)
        {
            Theme.DpiForm(this);
            Text = L.T("Přidat předmět do: ", "Add item to: ") + targetTitle;
            Width = 900; Height = 680; MinimumSize = new Size(760, 480);
            StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 9.5f);
            MinimizeBox = false; ShowInTaskbar = false;

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(10, 10, 10, 0), WrapContents = false };
            search = new TextBox { Width = 340, Margin = new Padding(6, 0, 10, 0) };
            cat = new ComboBox { Width = 250, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0) };
            cat.Items.Add(L.T("(všechny kategorie)", "(all categories)"));
            foreach (var c in Catalog.Entries.Values.Select(e => e.Category).Distinct().OrderBy(x => x)) cat.Items.Add(c);
            cat.SelectedIndex = 0;
            top.Controls.Add(new Label { Text = L.T("Hledat:", "Search:"), AutoSize = true, Margin = new Padding(0, 4, 0, 0) });
            top.Controls.Add(search); top.Controls.Add(cat);

            list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
            list.Columns.Add(L.T("Název", "Name"), 320); list.Columns.Add(L.T("Kategorie", "Category"), 180); list.Columns.Add(L.T("Velikost", "Size"), 70);
            list.Columns.Add("Max stack", 80); list.Columns.Add(L.T("Díly", "Parts"), 50); list.Columns.Add(L.T("Zdroj", "Source"), 90);

            // ---- bottom: count (number + slider), copies, buttons
            var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 6, Padding = new Padding(10, 8, 10, 10) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            count = new NumBox { Minimum = 1, Maximum = int.MaxValue, ThousandsSeparator = true, Dock = DockStyle.Fill, Margin = new Padding(6, 4, 6, 4) };
            slider = new DarkSlider { Minimum = 1, Maximum = 1, Dock = DockStyle.Fill, Height = 30, Margin = new Padding(4, 2, 4, 2) };
            sliderMax = new Label { AutoSize = true, Margin = new Padding(0, 8, 14, 0) };
            copies = new NumBox { Minimum = 1, Maximum = 100, Width = 60, Value = 1, Margin = new Padding(6, 4, 0, 4) };
            var copiesRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            copiesRow.Controls.Add(new Label { Text = L.T("Kusů:", "Copies:"), AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
            copiesRow.Controls.Add(copies);
            count.ValueChanged += delegate { if (syncing) return; syncing = true; slider.Value = (int)Math.Min(slider.Maximum, count.Value); syncing = false; };
            slider.ValueChanged += delegate { if (syncing) return; syncing = true; count.Value = slider.Value; syncing = false; };

            bottom.Controls.Add(new Label { Text = L.T("Počet ve stacku:", "Stack count:"), AutoSize = true, Margin = new Padding(0, 8, 0, 0) }, 0, 0);
            bottom.Controls.Add(count, 1, 0);
            bottom.Controls.Add(slider, 2, 0);
            bottom.Controls.Add(sliderMax, 3, 0);
            bottom.Controls.Add(copiesRow, 4, 0);

            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
            var ok = Theme.MakeButton(L.T("Přidat", "Add"), DialogResult.OK, true);
            var cancel = Theme.MakeButton(L.T("Zrušit", "Cancel"), DialogResult.Cancel, false);
            info = new Label { AutoSize = true, Margin = new Padding(0, 8, 16, 0) };
            buttons.Controls.Add(ok); buttons.Controls.Add(cancel); buttons.Controls.Add(info);
            bottom.Controls.Add(buttons, 0, 1); bottom.SetColumnSpan(buttons, 6);
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
            info.ForeColor = Theme.TextDim; sliderMax.ForeColor = Theme.TextDim;
            ok.BackColor = Theme.AccentBg; ok.FlatAppearance.BorderColor = Theme.Accent;
            SetMax(1);
            Fill();
        }

        void SetMax(int max)
        {
            max = Math.Max(1, max);
            syncing = true;
            slider.Maximum = max;
            slider.Enabled = max > 1;
            count.Value = max;          // default: a full stack
            slider.Value = max;
            syncing = false;
            sliderMax.Text = "/ " + max.ToString("N0");
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
            SetMax(e.MaxStack);
            info.Text = e.AttachmentCount > 0 ? L.T("přidá se kompletně sestavené (" + e.AttachmentCount + " dílů)", "added fully assembled (" + e.AttachmentCount + " parts)") : "";
        }
    }
}
