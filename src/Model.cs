// Domain model over the generic GVAS tree: containers, grids, items, catalog, cloning.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace IrrEditor
{
    public static class Uid
    {
        public static string Key(object raw) { return BitConverter.ToString(((RawValue)raw).Data); }
        public static bool IsZero(object raw) { return ((RawValue)raw).Data.All(b => b == 0); }
        public static RawValue New() { return new RawValue(Guid.NewGuid().ToByteArray()); }
    }

    public class Stat
    {
        public PropList P;
        public string Tag { get { return ((PropList)P["Tag"])["TagName"].ToString(); } }
        public string ShortTag { get { return Tag.Replace("Inventory.Stats.", ""); } }
        public float Value { get { return (float)P["Value"]; } set { P.Get("Value").Value = value; } }
        public float MaxValue { get { return (float)P["MaxValue"]; } }
        public string ModifyType { get { var v = P["StatModifyType"]; return v == null ? "" : v.ToString().Replace("EStatItemModifyType::", ""); } }
    }

    public class InvItem
    {
        public PropList P;
        public MainContainer Container; // container whose ContainerItems holds this item

        public string Def { get { return P["ItemDefinition"].ToString(); } }
        public string IdName { get { var d = Def; return d.Substring(d.LastIndexOf('.') + 1); } }
        public string DisplayName { get { return Catalog.Pretty(IdName); } }
        public string Category { get { return Catalog.CategoryOf(Def); } }
        public string UidKey { get { return Uid.Key(P["ItemUID"]); } }
        public string MotherKey { get { return Uid.Key(P["MotherUID"]); } }
        public int ContainerIndex { get { return (int)P["ContainerIndex"]; } set { P.Get("ContainerIndex").Value = value; } }
        public int SlotIndex { get { return (int)P["SlotIndex"]; } set { P.Get("SlotIndex").Value = value; } }
        public int Count { get { return (int)P["Count"]; } set { P.Get("Count").Value = value; } }
        public int MaxStack { get { return (int)P["MaxStack"]; } set { P.Get("MaxStack").Value = value; } }
        public bool CanStack { get { var v = P["CanStack"]; return v is bool && (bool)v; } }
        public bool Rotated { get { var v = P["bRotated"]; return v is bool && (bool)v; } set { var p = P.Get("bRotated"); if (p != null) p.Value = value; } }
        public bool Quest { get { var v = P["bQuestItem"]; return v is bool && (bool)v; } set { var p = P.Get("bQuestItem"); if (p != null) p.Value = value; } }
        public string State { get { var v = P["CurrentState"]; return v == null ? "" : v.ToString(); } }

        public List<Stat> Stats
        {
            get
            {
                var a = P["CurrentStats"] as ArrayValue;
                if (a == null) return new List<Stat>();
                return a.Items.OfType<PropList>().Select(x => new Stat { P = x }).ToList();
            }
        }
        public Stat GetStat(string shortTag) { return Stats.FirstOrDefault(s => s.ShortTag == shortTag); }

        static double[] V2(object raw)
        {
            var d = raw as RawValue;
            if (d == null || d.Data.Length < 16) return new double[] { -0.5, 0.5 };
            return new[] { BitConverter.ToDouble(d.Data, 0), BitConverter.ToDouble(d.Data, 8) };
        }
        /// <summary>Unrotated footprint in grid cells. The game truncates fractional extents
        /// (a weapon stored as 5.5 × 2.5 occupies 5 × 2 cells in game), so round down.</summary>
        public int BaseW { get { var dim = P["ItemDimensions"] as PropList; if (dim == null) return 1; var x = V2(dim["X"]); return Cells(x[1] - x[0]); } }
        public int BaseH { get { var dim = P["ItemDimensions"] as PropList; if (dim == null) return 1; var y = V2(dim["Y"]); return Cells(y[1] - y[0]); } }
        static int Cells(double extent) { return Math.Max(1, (int)Math.Floor(extent + 0.001)); }
        public int W { get { return Rotated ? BaseH : BaseW; } }
        public int H { get { return Rotated ? BaseW : BaseH; } }

        public float Weight
        {
            get { var s = GetStat("Weight"); return s == null ? 0 : s.Value; }
        }
        public override string ToString() { return DisplayName + (Count > 1 || MaxStack > 1 ? " x" + Count : ""); }
    }

    public class GridElement
    {
        public PropList P;
        public int Index;
        public int Rows, Cols;
        public bool[,] Enabled;
        public int Tier { get { var v = P["ContainerTier"]; return v is int ? (int)v : -1; } }
        public bool IsGrid { get { return Rows > 0 && Cols > 0; } }
        public List<PropList> Slots { get { return ((ArrayValue)P["ContainerSlots"]).Items.Cast<PropList>().ToList(); } }

        public void SetAllEnabled(bool on)
        {
            foreach (var s in Slots) s.Get("bEnabled").Value = on;
            Load();
        }
        public void Load()
        {
            var slots = Slots;
            Rows = 0; Cols = 0;
            foreach (var s in slots)
            {
                Rows = Math.Max(Rows, (int)s["Row"] + 1);
                Cols = Math.Max(Cols, (int)s["Column"] + 1);
            }
            Enabled = new bool[Math.Max(Rows, 0), Math.Max(Cols, 0)];
            foreach (var s in slots)
            {
                int r = (int)s["Row"], c = (int)s["Column"];
                if (r >= 0 && c >= 0) Enabled[r, c] = (bool)s["bEnabled"];
            }
        }
        public int EnabledCount { get { int n = 0; if (Enabled != null) foreach (var b in Enabled) if (b) n++; return n; } }
    }

    public class MainContainer
    {
        public PropList P;
        public InvSave Save;
        public string UidKey { get { return Uid.Key(P["MainContainerUID"]); } }
        public List<GridElement> Elements = new List<GridElement>();
        public ArrayValue ItemsArray { get { return (ArrayValue)P["ContainerItems"]; } }
        public List<InvItem> Items = new List<InvItem>();
        public InvItem OwnerItem;   // item this container belongs to (null for top-level)
        public bool IsTopLevel;
        public string Label;        // friendly name for top-level containers

        public bool HasGrid { get { return Elements.Any(e => e.IsGrid); } }
        public string Title
        {
            get
            {
                if (Label != null) return Label;
                if (OwnerItem != null) return OwnerItem.DisplayName;
                return "Container " + UidKey.Substring(0, 11);
            }
        }
    }

    /// <summary>One inventory-like save file (StashSaveGame / InventorySaveGame / VendorSaveGame).</summary>
    public class InvSave : IUndoTarget
    {
        public GvasFile Gvas { get { return G; } set { G = value; } }
        public bool IsDirty { get { return Dirty; } set { Dirty = value; } }
        public string Path;
        public string Kind;          // "Stash", "Inventory", "Vendor"
        public GvasFile G;
        public bool Dirty;
        public List<MainContainer> Containers = new List<MainContainer>();
        public Dictionary<string, MainContainer> ByUid = new Dictionary<string, MainContainer>();
        public Dictionary<string, InvItem> ItemsByUid = new Dictionary<string, InvItem>();
        public List<string> TopUids = new List<string>();
        string containersProp;

        public static InvSave Load(string path, string kind)
        {
            var s = new InvSave { Path = path, Kind = kind, G = GvasFile.Load(path) };
            s.containersProp = s.G.Root.Get("VendorMainContainers") != null ? "VendorMainContainers" : "MainContainers";
            s.Rebuild();
            return s;
        }

        public ArrayValue ContainersArray { get { return (ArrayValue)G.Root[containersProp]; } }

        /// <summary>(Re)derive the object model from the property tree.</summary>
        public void Rebuild()
        {
            Containers.Clear(); ByUid.Clear(); ItemsByUid.Clear(); TopUids.Clear();
            var tops = (G.Root["MainContainerUIDs"] ?? G.Root["VendorMainContainerUIDs"]) as ArrayValue;
            if (tops != null) foreach (var t in tops.Items) TopUids.Add(Uid.Key(t));
            foreach (PropList mcp in ContainersArray.Items)
            {
                var mc = new MainContainer { P = mcp, Save = this };
                int i = 0;
                foreach (PropList el in ((ArrayValue)mcp["ContainerElements"]).Items)
                {
                    var ge = new GridElement { P = el, Index = i++ };
                    ge.Load();
                    mc.Elements.Add(ge);
                }
                foreach (PropList ip in mc.ItemsArray.Items)
                {
                    var it = new InvItem { P = ip, Container = mc };
                    mc.Items.Add(it);
                    ItemsByUid[it.UidKey] = it;
                }
                Containers.Add(mc);
                ByUid[mc.UidKey] = mc;
            }
            foreach (var mc in Containers)
            {
                InvItem owner;
                if (ItemsByUid.TryGetValue(mc.UidKey, out owner)) mc.OwnerItem = owner;
                mc.IsTopLevel = TopUids.Contains(mc.UidKey) || mc.OwnerItem == null;
            }
            LabelTopLevel();
        }

        void LabelTopLevel()
        {
            if (Kind == "Stash")
            {
                int page = 0;
                foreach (var key in TopUids)
                {
                    MainContainer mc;
                    if (!ByUid.TryGetValue(key, out mc)) continue;
                    page++;
                    var g = mc.Elements.FirstOrDefault(e => e.IsGrid);
                    bool locked = g != null && g.EnabledCount == 0;
                    mc.Label = page == 1 ? L.T("Sklad (hlavní)", "Stash (main)") : L.T("Sklad – strana ", "Stash – page ") + page + (locked ? L.T(" [zamčeno]", " [locked]") : "");
                }
            }
            else if (Kind == "Inventory")
            {
                foreach (var key in TopUids)
                {
                    MainContainer mc;
                    if (!ByUid.TryGetValue(key, out mc)) continue;
                    var it = mc.Items.FirstOrDefault();
                    if (mc.HasGrid) mc.Label = L.T("Pouzdro / kapsy", "Pouch / pockets");
                    else if (it != null) mc.Label = Catalog.SlotGuess(it) + ": " + it.DisplayName;
                    else mc.Label = L.T("(prázdný slot)", "(empty slot)");
                }
            }
        }

        public MainContainer ContainerOf(InvItem it)
        {
            MainContainer mc; return ByUid.TryGetValue(it.UidKey, out mc) ? mc : null;
        }

        /// <summary>All items inside this item, recursively (attachments, ammo, backpack contents).</summary>
        public IEnumerable<InvItem> Descendants(InvItem it)
        {
            var mc = ContainerOf(it);
            if (mc == null) yield break;
            foreach (var c in mc.Items)
            {
                yield return c;
                foreach (var d in Descendants(c)) yield return d;
            }
        }

        // ---------------------------------------------------------------- grid helpers
        public InvItem[,] Occupancy(MainContainer mc, GridElement ge, InvItem ignore)
        {
            var occ = new InvItem[ge.Rows, ge.Cols];
            foreach (var it in mc.Items)
            {
                if (it == ignore || it.ContainerIndex != ge.Index || it.SlotIndex < 0) continue;
                int r0 = it.SlotIndex / ge.Cols, c0 = it.SlotIndex % ge.Cols;
                for (int r = r0; r < r0 + it.H && r < ge.Rows; r++)
                    for (int c = c0; c < c0 + it.W && c < ge.Cols; c++)
                        if (occ[r, c] == null) occ[r, c] = it;
            }
            return occ;
        }

        public bool Fits(MainContainer mc, GridElement ge, int row, int col, int w, int h, InvItem ignore)
        {
            if (row < 0 || col < 0 || row + h > ge.Rows || col + w > ge.Cols) return false;
            var occ = Occupancy(mc, ge, ignore);
            for (int r = row; r < row + h; r++)
                for (int c = col; c < col + w; c++)
                    if (!ge.Enabled[r, c] || occ[r, c] != null) return false;
            return true;
        }

        /// <summary>First free spot scanning row-major, preferring the unrotated orientation anywhere
        /// before trying rotated; returns (element, slot, rotated) or null.</summary>
        public int[] FindSpot(MainContainer mc, int w, int h, InvItem ignore, int onlyElement)
        {
            for (int pass = 0; pass < (w != h ? 2 : 1); pass++)
            {
                int ww = pass == 0 ? w : h, hh = pass == 0 ? h : w;
                foreach (var ge in mc.Elements)
                {
                    if (!ge.IsGrid || (onlyElement >= 0 && ge.Index != onlyElement)) continue;
                    for (int r = 0; r < ge.Rows; r++)
                        for (int c = 0; c < ge.Cols; c++)
                            if (Fits(mc, ge, r, c, ww, hh, ignore)) return new[] { ge.Index, r * ge.Cols + c, pass };
                }
            }
            return null;
        }

        /// <summary>Problems worth showing: overlaps, out-of-bounds, overstacked.</summary>
        public List<string> Validate()
        {
            var res = new List<string>();
            foreach (var mc in Containers)
                foreach (var ge in mc.Elements.Where(e => e.IsGrid))
                {
                    var seen = new InvItem[ge.Rows, ge.Cols];
                    foreach (var it in mc.Items.Where(i => i.ContainerIndex == ge.Index && i.SlotIndex >= 0))
                    {
                        int r0 = it.SlotIndex / ge.Cols, c0 = it.SlotIndex % ge.Cols;
                        if (r0 + it.H > ge.Rows || c0 + it.W > ge.Cols)
                            res.Add(mc.Title + ": " + it.DisplayName + L.T(" je mimo mřížku", " is out of grid bounds"));
                        for (int r = r0; r < Math.Min(ge.Rows, r0 + it.H); r++)
                            for (int c = c0; c < Math.Min(ge.Cols, c0 + it.W); c++)
                            {
                                if (seen[r, c] != null) { res.Add(mc.Title + ": " + it.DisplayName + L.T(" se překrývá s ", " overlaps ") + seen[r, c].DisplayName); r = 9999; break; }
                                seen[r, c] = it;
                            }
                    }
                }
            return res.Distinct().ToList();
        }

        // ---------------------------------------------------------------- mutation
        public void RemoveItem(InvItem it)
        {
            // remove sub-containers first
            var mc = ContainerOf(it);
            if (mc != null)
            {
                foreach (var c in mc.Items.ToList()) RemoveItem(c);
                ContainersArray.Items.Remove(mc.P);
                var tops = G.Root["MainContainerUIDs"] as ArrayValue;
                if (tops != null) tops.Items.RemoveAll(x => Uid.Key(x) == mc.UidKey);
            }
            it.Container.ItemsArray.Items.Remove(it.P);
            // clear quick-slot references
            var qs = G.Root["QuickSlots"] as ArrayValue;
            if (qs != null)
                foreach (PropList q in qs.Items)
                    if (q.Get("ItemUID") != null && Uid.Key(q["ItemUID"]) == it.UidKey) q.Get("ItemUID").Value = new RawValue(new byte[16]);
            Dirty = true;
        }

        /// <summary>Insert a cloned template (item + its sub-container tree) into target container.</summary>
        public InvItem Insert(ItemTemplate t, MainContainer target, int elementIndex, int slot, bool rotated, int count)
        {
            var map = new Dictionary<string, RawValue>();
            Func<object, RawValue> remap = raw =>
            {
                string k = Uid.Key(raw);
                RawValue n;
                if (!map.TryGetValue(k, out n)) { n = Uid.New(); map[k] = n; }
                return n;
            };
            var root = t.Item.DeepClone();
            root.Get("ItemUID").Value = remap(root["ItemUID"]);
            root.Get("MotherUID").Value = new RawValue((byte[])((RawValue)target.P["MainContainerUID"]).Data.Clone());
            root.Get("ContainerIndex").Value = elementIndex;
            root.Get("SlotIndex").Value = slot;
            if (root.Get("bRotated") != null) root.Get("bRotated").Value = rotated;
            if (count > 0) root.Get("Count").Value = count;
            target.ItemsArray.Items.Add(root);
            foreach (var sub in t.SubContainers)
            {
                var c = sub.DeepClone();
                c.Get("MainContainerUID").Value = remap(c["MainContainerUID"]);
                foreach (PropList ip in ((ArrayValue)c["ContainerItems"]).Items)
                {
                    ip.Get("ItemUID").Value = remap(ip["ItemUID"]);
                    ip.Get("MotherUID").Value = remap(ip["MotherUID"]);
                }
                ContainersArray.Items.Add(c);
            }
            Dirty = true;
            Rebuild();
            return ItemsByUid[Uid.Key(root["ItemUID"])];
        }

        /// <summary>Is <paramref name="mc"/> the item's own container or nested anywhere inside it?</summary>
        public bool IsInside(MainContainer mc, InvItem it)
        {
            var probe = mc;
            while (probe != null)
            {
                if (probe.OwnerItem == null) return false;
                if (probe.OwnerItem == it) return true;
                probe = probe.OwnerItem.Container;
            }
            return false;
        }

        /// <summary>Move an item (with everything inside it) to another container of the same save.</summary>
        public void MoveWithin(InvItem it, MainContainer target, int elementIndex, int slot, bool rotated)
        {
            if (target == it.Container) { it.ContainerIndex = elementIndex; it.SlotIndex = slot; it.Rotated = rotated; Dirty = true; return; }
            if (IsInside(target, it)) throw new InvalidOperationException("Cannot move an item into itself");
            it.Container.ItemsArray.Items.Remove(it.P);
            it.P.Get("MotherUID").Value = new RawValue((byte[])((RawValue)target.P["MainContainerUID"]).Data.Clone());
            it.ContainerIndex = elementIndex; it.SlotIndex = slot; it.Rotated = rotated;
            target.ItemsArray.Items.Add(it.P);
            Dirty = true;
            Rebuild();
        }

        /// <summary>Move an item to a container in any save (stash ↔ loadout). Returns the item as it exists afterwards.</summary>
        public static InvItem Move(InvItem it, MainContainer target, int elementIndex, int slot, bool rotated)
        {
            var src = it.Container.Save; var dst = target.Save;
            if (src == dst)
            {
                string uid = it.UidKey;
                src.MoveWithin(it, target, elementIndex, slot, rotated);
                return src.ItemsByUid[uid];
            }
            var t = src.TemplateOf(it);
            var n = dst.Insert(t, target, elementIndex, slot, rotated, 0);
            src.RemoveItem(it);
            src.Rebuild();
            return n;
        }

        /// <summary>Every container with a grid in this save, with a readable path ("Stash (main)", "Backpack › Item Case"…).</summary>
        public List<KeyValuePair<string, MainContainer>> GridContainers()
        {
            var res = new List<KeyValuePair<string, MainContainer>>();
            foreach (var mc in Containers.Where(c => c.HasGrid))
            {
                var parts = new List<string>();
                var probe = mc;
                int guard = 0;
                while (probe != null && guard++ < 10)
                {
                    parts.Insert(0, probe.Title);
                    probe = probe.OwnerItem == null ? null : probe.OwnerItem.Container;
                    if (probe != null && probe.Label != null && !probe.HasGrid) { probe = null; } // equipment slot – its title is the item itself
                }
                res.Add(new KeyValuePair<string, MainContainer>(string.Join(" › ", parts.Distinct()), mc));
            }
            return res;
        }

        public ItemTemplate TemplateOf(InvItem it)
        {
            var t = new ItemTemplate { Item = it.P.DeepClone(), Source = Kind };
            var stack = new Stack<InvItem>(); stack.Push(it);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                var mc = ContainerOf(cur);
                if (mc == null) continue;
                t.SubContainers.Add(mc.P.DeepClone());
                foreach (var c in mc.Items) stack.Push(c);
            }
            return t;
        }

        public void Write(string path)
        {
            var bytes = G.ToBytes();
            // verify that what we write parses back
            GvasFile.Parse(bytes);
            File.WriteAllBytes(path, bytes);
            Dirty = false;
        }
    }

    public class ItemTemplate
    {
        public PropList Item;
        public List<PropList> SubContainers = new List<PropList>();
        public string Source;
        public InvItem View { get { return new InvItem { P = Item }; } }
    }

    public class CatalogEntry
    {
        public string Def;
        public string IdName;
        public string Name;
        public string Category;
        public ItemTemplate Template;  // preferred: a fresh vendor copy
        public float? NewDurability;   // durability of a fresh copy (for "Repair")
        public int MaxStack;
        public int W, H;
        public int AttachmentCount;
    }

    /// <summary>What has been seen attached where: (parent item definition, socket index) → part definitions.</summary>
    public class SocketInfo
    {
        public string Label;                                   // e.g. "Attachments/Barrel"
        public int SlotIndex = -1;                             // SlotIndex used by parts in this socket
        public HashSet<string> Parts = new HashSet<string>();
    }

    public static class Catalog
    {
        public static Dictionary<string, CatalogEntry> Entries = new Dictionary<string, CatalogEntry>();
        public static Dictionary<string, SocketInfo> Sockets = new Dictionary<string, SocketInfo>();
        public static string SocketKey(string parentDef, int ci) { return parentDef + "#" + ci; }

        public static void Build(IEnumerable<InvSave> saves)
        {
            Entries.Clear();
            Sockets.Clear();
            // compatibility learnt from every assembled item in every save (vendor stock has hundreds)
            foreach (var s in saves)
                foreach (var mc in s.Containers.Where(c => c.OwnerItem != null && !c.HasGrid))
                    foreach (var part in mc.Items)
                    {
                        var key = SocketKey(mc.OwnerItem.Def, part.ContainerIndex);
                        SocketInfo si;
                        if (!Sockets.TryGetValue(key, out si)) { si = new SocketInfo { SlotIndex = part.SlotIndex }; Sockets[key] = si; }
                        si.Parts.Add(part.Def);
                        if (si.Label == null) si.Label = part.Category;
                    }
            // vendor first: freshest, full-durability, fully-assembled
            foreach (var s in saves.OrderBy(x => x.Kind == "Vendor" ? 0 : 1))
                foreach (var mc in s.Containers)
                    foreach (var it in mc.Items)
                    {
                        CatalogEntry e;
                        if (!Entries.TryGetValue(it.Def, out e))
                        {
                            var t = s.TemplateOf(it);
                            e = new CatalogEntry
                            {
                                Def = it.Def, IdName = it.IdName, Name = it.DisplayName, Category = it.Category,
                                Template = t, MaxStack = it.MaxStack, W = it.BaseW, H = it.BaseH,
                                AttachmentCount = s.Descendants(it).Count()
                            };
                            Entries[it.Def] = e;
                        }
                        var d = it.GetStat("Durability");
                        if (d != null && (s.Kind == "Vendor" || e.NewDurability == null))
                            e.NewDurability = Math.Max(e.NewDurability ?? 0, d.Value);
                    }
        }

        public static string Pretty(string idName)
        {
            var s = idName.StartsWith("ID_") ? idName.Substring(3) : idName;
            return s.Replace('_', ' ');
        }

        public static string CategoryOf(string def)
        {
            const string marker = "/Items/";
            int i = def.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return "Other";
            var rest = def.Substring(i + marker.Length).Split('/');
            if (rest.Length >= 3 && (rest[0] == "Weapons" || rest[0] == "Ammunition" || rest[0] == "Attachments" || rest[0] == "Gear" || rest[0] == "Equipment"))
                return rest[0] + "/" + rest[1];
            return rest[0];
        }

        public static string SlotGuess(InvItem it)
        {
            var c = it.Category.ToLowerInvariant();
            var n = it.IdName.ToLowerInvariant();
            if (n.Contains("backpack") || c.Contains("backpack")) return L.T("Batoh", "Backpack");
            if (c.Contains("rig") || c.Contains("platecarrier")) return L.T("Vesta", "Rig");
            if (c.Contains("helmet")) return L.T("Helma", "Helmet");
            if (c.StartsWith("weapons")) return L.T("Zbraň", "Weapon");
            if (c.StartsWith("melee")) return L.T("Nůž", "Melee");
            return L.T("Výbava", "Gear");
        }
    }
}
