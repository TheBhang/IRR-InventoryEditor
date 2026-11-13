// Bulk "smart" operations on top of the model.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace IrrEditor
{
    public static class Tools
    {
        static bool InGrid(InvItem it)
        {
            if (it.SlotIndex < 0) return false;
            var ge = it.Container.Elements.FirstOrDefault(e => e.Index == it.ContainerIndex);
            return ge != null && ge.IsGrid;
        }

        /// <summary>Durability/uses back to the value of a fresh (vendor) copy.</summary>
        public static bool Repair(InvItem it)
        {
            var d = it.GetStat("Durability");
            CatalogEntry e;
            if (d == null || !Catalog.Entries.TryGetValue(it.Def, out e) || e.NewDurability == null) return false;
            if (d.Value >= e.NewDurability.Value) return false;
            d.Value = e.NewDurability.Value;
            return true;
        }

        public static int RepairAll(InvSave s)
        {
            int n = 0;
            foreach (var it in s.ItemsByUid.Values) if (Repair(it)) n++;
            if (n > 0) s.Dirty = true;
            return n;
        }

        /// <summary>Stacks lying in grids get filled to MaxStack (ammo in magazines is left alone).</summary>
        public static int FillStacks(InvSave s)
        {
            int n = 0;
            foreach (var it in s.ItemsByUid.Values)
                if (it.CanStack && it.MaxStack > 1 && it.Count < it.MaxStack && InGrid(it)) { it.Count = it.MaxStack; n++; }
            if (n > 0) s.Dirty = true;
            return n;
        }

        public static int MagCapacity(InvItem mag)
        {
            var m = Regex.Match(mag.IdName, @"(\d+)\s*_?rnd", RegexOptions.IgnoreCase);
            return m.Success ? int.Parse(m.Groups[1].Value) : 0;
        }

        /// <summary>Magazines that already hold some ammo get topped up to their capacity (taken from the name, e.g. "30rnd").</summary>
        public static int FillMagazines(InvSave s)
        {
            int n = 0;
            foreach (var mag in s.ItemsByUid.Values.Where(i => i.Category == "Attachments/Mag"))
            {
                int cap = MagCapacity(mag);
                var mc = s.ContainerOf(mag);
                if (cap <= 0 || mc == null) continue;
                var ammo = mc.Items.FirstOrDefault(i => i.Category.StartsWith("Ammunition"));
                if (ammo != null && ammo.Count < cap) { ammo.Count = cap; n++; }
            }
            if (n > 0) s.Dirty = true;
            return n;
        }

        /// <summary>Merge partial stacks of the same item in one container into as few full stacks as possible.</summary>
        public static int MergeStacks(InvSave s, MainContainer mc)
        {
            int removed = 0;
            var groups = mc.Items.Where(i => i.CanStack && i.MaxStack > 1 && InGrid(i) && !HasContents(s, i))
                                 .GroupBy(i => i.Def).Where(g => g.Count() > 1).ToList();
            foreach (var g in groups)
            {
                var list = g.OrderBy(i => i.ContainerIndex).ThenBy(i => i.SlotIndex).ToList();
                long total = list.Sum(i => (long)i.Count);
                int max = list[0].MaxStack;
                foreach (var it in list)
                {
                    if (total <= 0) { s.RemoveItem(it); removed++; continue; }
                    // keep stacks that are already over the limit as they are (the game allowed them)
                    int c = (int)Math.Min(total, Math.Max(max, it.Count));
                    it.Count = c;
                    total -= c;
                }
            }
            if (removed > 0 || groups.Count > 0) { s.Dirty = true; s.Rebuild(); }
            return removed;
        }

        static bool HasContents(InvSave s, InvItem it)
        {
            var mc = s.ContainerOf(it);
            return mc != null && mc.Items.Count > 0;
        }

        /// <summary>Re-pack items of a grid container: by category, then bigger first. All-or-nothing.</summary>
        public static bool Sort(InvSave s, MainContainer mc)
        {
            var items = mc.Items.Where(InGrid).ToList();
            var saved = items.Select(i => new[] { i.ContainerIndex, i.SlotIndex, i.Rotated ? 1 : 0 }).ToList();
            foreach (var i in items) i.SlotIndex = -1;
            var order = items.OrderBy(i => CatRank(i.Category)).ThenBy(i => i.Category)
                             .ThenByDescending(i => i.BaseW * i.BaseH).ThenBy(i => i.DisplayName).ToList();
            bool ok = true;
            foreach (var it in order)
            {
                it.Rotated = false;
                var spot = s.FindSpot(mc, it.BaseW, it.BaseH, it, -1);
                if (spot == null) { ok = false; break; }
                it.ContainerIndex = spot[0]; it.SlotIndex = spot[1]; it.Rotated = spot[2] == 1;
            }
            if (!ok)
            {
                for (int k = 0; k < items.Count; k++)
                {
                    items[k].ContainerIndex = saved[k][0]; items[k].SlotIndex = saved[k][1]; items[k].Rotated = saved[k][2] == 1;
                }
                return false;
            }
            s.Dirty = true;
            return true;
        }

        static int CatRank(string c)
        {
            string[] order = { "Weapons", "Gear", "Attachments", "Ammunition", "Grenades", "Medical", "Provisions", "Money", "Keys", "Quest", "Barter", "Containers" };
            for (int i = 0; i < order.Length; i++) if (c.StartsWith(order[i])) return i;
            return order.Length;
        }

        public static int UnlockStashPages(InvSave s)
        {
            int n = 0;
            foreach (var key in s.TopUids)
            {
                MainContainer mc;
                if (!s.ByUid.TryGetValue(key, out mc)) continue;
                foreach (var ge in mc.Elements.Where(e => e.IsGrid && e.EnabledCount < e.Rows * e.Cols)) { ge.SetAllEnabled(true); n++; }
            }
            if (n > 0) { s.Dirty = true; s.Rebuild(); }
            return n;
        }
    }
}
