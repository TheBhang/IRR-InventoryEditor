// Dev tool: render every window off-screen to PNG files (no mouse/keyboard, never touches the desktop).
// usage: dump.exe shots <saveDir> <outDir> [en]
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace IrrEditor
{
    static class Shots
    {
        const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

        static void Pump(int ms)
        {
            var end = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < end) { Application.DoEvents(); System.Threading.Thread.Sleep(15); }
        }

        static void Snap(Control c, string path)
        {
            using (var b = new Bitmap(c.Width, c.Height))
            {
                c.DrawToBitmap(b, new Rectangle(0, 0, c.Width, c.Height));
                b.Save(path);
            }
            Console.WriteLine("shot " + Path.GetFileName(path) + " " + c.Width + "x" + c.Height);
        }

        static T Field<T>(object o, string name) { return (T)o.GetType().GetField(name, NP).GetValue(o); }
        static object Call(object o, string name, params object[] a)
        {
            var m = o.GetType().GetMethods(NP).First(x => x.Name == name && x.GetParameters().Length == a.Length);
            return m.Invoke(o, a);
        }

        static void Off(Form f) { f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-30000, -30000); f.ShowInTaskbar = false; }

        public static void Run(string dir, string outDir, bool en)
        {
            Application.EnableVisualStyles();
            L.En = en;
            Directory.CreateDirectory(outDir);
            string p = en ? "en-" : "cz-";

            var f = new MainForm(dir);
            Off(f);
            f.Show();
            Pump(2500);
            Snap(f, Path.Combine(outDir, p + "1-stash-all.png"));

            var kind = typeof(MainForm).GetNestedType("ViewKind", BindingFlags.NonPublic);
            Call(f, "SetView", Enum.Parse(kind, "Loadout"), null, true);
            Pump(400);
            Snap(f, Path.Combine(outDir, p + "2-loadout.png"));

            // select a weapon in the loadout to fill the details panel, and multi-select two pouch items
            var inv = Field<InvSave>(f, "inv");
            var wpn = inv.ItemsByUid.Values.First(i => i.Category.StartsWith("Weapons"));
            f.SelectItem(wpn, true);
            Pump(300);
            Snap(f, Path.Combine(outDir, p + "3-weapon-details.png"));

            // search highlight
            var sb = Field<ToolStripTextBox>(f, "searchBox");
            sb.Text = "9x19";
            Pump(300);
            Call(f, "SetView", Enum.Parse(kind, "StashAll"), null, true);
            Pump(400);
            Snap(f, Path.Combine(outDir, p + "4-search.png"));
            sb.Text = "";

            // zoomed out stash
            var grid = Field<GridPanel>(f, "grid");
            grid.Zoom = 0.7f;
            Pump(300);
            Snap(f, Path.Combine(outDir, p + "5-zoom70.png"));
            grid.Zoom = 1f;

            // dialogs
            var add = new AddItemForm("Sklad (hlavní)");
            Off(add); add.Show(); Pump(300);
            Field<TextBox>(add, "search").Text = "9x19";
            Pump(200);
            var lv = Field<ListView>(add, "list");
            if (lv.Items.Count > 0) { lv.Items[0].Selected = true; }
            Pump(200);
            Field<DarkSlider>(add, "slider").Value = 25;
            Pump(200);
            Snap(add, Path.Combine(outDir, p + "6-add-item.png"));
            add.Close();

            var parts = new AttachForm(inv, wpn, l => { });
            Off(parts); parts.Show(); Pump(400);
            Snap(parts, Path.Combine(outDir, p + "7-weapon-parts.png"));
            parts.Close();

            var backups = new BackupForm(dir, Path.Combine(dir, "_IRR_Editor_Backups"));
            Off(backups); backups.Show(); Pump(300);
            Snap(backups, Path.Combine(outDir, p + "8-backups.png"));
            backups.Close();

            var gdoc = SaveDoc.TryLoad(Path.Combine(dir, "GunsmithSaveGame.sav"));
            if (gdoc != null)
            {
                var gf = new GunsmithForm(gdoc, l => { });
                Off(gf); gf.Show(); Pump(300);
                var tv = Field<TreeView>(gf, "tree");
                if (tv.Nodes.Count > 0 && tv.Nodes[0].Nodes.Count > 0) tv.SelectedNode = tv.Nodes[0].Nodes[0];
                Pump(300);
                Snap(gf, Path.Combine(outDir, p + "9-presets.png"));
                gf.Close();
            }

            var about = new AboutForm();
            Off(about); about.Show(); Pump(4000);
            Snap(about, Path.Combine(outDir, p + "10-about.png"));
            about.Close();

            Field<bool>(f, "loadingDetails"); // keep reflection helper honest
            f.GetType().GetField("discardOnClose", NP).SetValue(f, true);
            f.Close();
        }
    }
}
