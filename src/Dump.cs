// Dev tool: round-trip check + tree dump.  usage: dump.exe roundtrip <files...> | dump.exe tree <file> [maxArrayItems]
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;

namespace IrrEditor
{
    static class DumpProgram
    {
        static int MaxItems = 3;
        static StringBuilder sb = new StringBuilder();

        [STAThread]
        static int Main(string[] args)
        {
            if (args[0] == "roundtrip")
            {
                int bad = 0;
                foreach (var f in args.Skip(1))
                {
                    var orig = File.ReadAllBytes(f);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var g = GvasFile.Parse(orig);
                    var outb = g.ToBytes();
                    bool ok = outb.SequenceEqual(orig);
                    int raws = CountRaw(g.Root);
                    Console.WriteLine("{0,-32} {1} parse+write {2}ms, raw blobs: {3}, trailer {4}b", Path.GetFileName(f), ok ? "OK  " : "DIFF", sw.ElapsedMilliseconds, raws, g.Trailer.Length);
                    if (!ok) bad++;
                }
                return bad;
            }
            if (args[0] == "analyze") { Analyze.Run(args[1]); return 0; }
            if (args[0] == "shots") { Shots.Run(args[1], args[2], args.Length > 3 && args[3] == "en"); return 0; }
            if (args[0] == "features")
            {
                // headless checks of the v1.2 features on a copy of the saves
                string dir = args[1];
                var stash = InvSave.Load(Path.Combine(dir, "StashSaveGame.sav"), "Stash");
                var inv = InvSave.Load(Path.Combine(dir, "InventorySaveGame.sav"), "Inventory");
                var vend = InvSave.Load(Path.Combine(dir, "VendorSaveGame.sav"), "Vendor");
                var gun = SaveDoc.TryLoad(Path.Combine(dir, "GunsmithSaveGame.sav"));
                Catalog.Build(new[] { vend, stash, inv });
                Console.WriteLine("sockets learnt: " + Catalog.Sockets.Count);
                var origStash = stash.G.ToBytes(); var origInv = inv.G.ToBytes();
                var undo = new UndoStack(() => new List<IUndoTarget> { stash, inv }, () => { stash.Rebuild(); inv.Rebuild(); });

                // 1) cross-save move: a stash item into the backpack, then a weapon (with parts) from loadout to stash
                var main = stash.Containers.First(c => c.IsTopLevel && c.HasGrid);
                var small = main.Items.First(i => i.BaseW == 1 && i.BaseH == 1 && stash.ContainerOf(i) == null);
                var pack = inv.GridContainers().Select(k => k.Value).First(c => c.OwnerItem != null && c.OwnerItem.IdName.Contains("Backpack"));
                var spot = inv.FindSpot(pack, 1, 1, null, -1);
                undo.Checkpoint("move1");
                int sBefore = stash.ItemsByUid.Count, iBefore = inv.ItemsByUid.Count;
                var moved = InvSave.Move(small, pack, spot[0], spot[1], spot[2] == 1);
                Console.WriteLine("move stash->backpack: " + moved.DisplayName + " stash " + sBefore + "->" + stash.ItemsByUid.Count + ", inv " + iBefore + "->" + inv.ItemsByUid.Count + ", in pack: " + (moved.Container.UidKey == pack.UidKey));
                var wpn = inv.ItemsByUid.Values.First(i => i.Category.StartsWith("Weapons") && inv.Descendants(i).Count() > 3);
                int parts = inv.Descendants(wpn).Count();
                main = stash.Containers.First(c => c.IsTopLevel && c.HasGrid);
                var ws = stash.FindSpot(main, wpn.BaseW, wpn.BaseH, null, -1);
                undo.Checkpoint("move2");
                var mw = InvSave.Move(wpn, main, ws[0], ws[1], ws[2] == 1);
                Console.WriteLine("move weapon loadout->stash: " + mw.DisplayName + " parts " + parts + " -> " + stash.Descendants(mw).Count() + ", issues " + (stash.Validate().Count + inv.Validate().Count));
                // same-save move between stash pages
                var page = stash.TopUids.Select(k => stash.ByUid[k]).First(c => c != stash.Containers.First(x => x.IsTopLevel && x.HasGrid) && c.HasGrid && c.Elements[0].EnabledCount > 0);
                var it2 = stash.Containers.First(c => c.IsTopLevel && c.HasGrid).Items.First(i => i.BaseW == 1 && i.BaseH == 1);
                var ps = stash.FindSpot(page, 1, 1, null, -1);
                undo.Checkpoint("move3");
                var m3 = InvSave.Move(it2, page, ps[0], ps[1], false);
                Console.WriteLine("move main->" + page.Title + ": " + (m3.Container.UidKey == page.UidKey) + " uid kept " + (m3.UidKey == it2.UidKey));
                // self-containment guard
                { var bp = inv.ItemsByUid.Values.First(i => i.IdName.Contains("Backpack")); var own = inv.ContainerOf(bp); var nestedC = inv.Containers.FirstOrDefault(c => c.OwnerItem != null && c.OwnerItem.Container == own); Console.WriteLine("backpack into own grid refused: " + inv.IsInside(own, bp) + (nestedC != null ? ", into nested container refused: " + inv.IsInside(nestedC, bp) : "") + ", into stash allowed: " + !stash.IsInside(stash.Containers.First(c => c.IsTopLevel && c.HasGrid), bp)); }

                // 2) undo everything -> byte identical
                undo.Undo(); undo.Undo(); undo.Undo();
                Console.WriteLine("after 3x undo stash identical: " + stash.G.ToBytes().SequenceEqual(origStash) + ", inv identical: " + inv.G.ToBytes().SequenceEqual(origInv));
                undo.Redo();
                Console.WriteLine("redo -> stash items " + stash.ItemsByUid.Count + " inv " + inv.ItemsByUid.Count + "; can redo more: " + undo.CanRedo);
                undo.Undo();

                // 3) attach / detach weapon part
                var w2 = inv.ItemsByUid.Values.First(i => i.Category.StartsWith("Weapons"));
                var wmc = inv.ContainerOf(w2);
                var occ = wmc.Items.First(i => i.Category.StartsWith("Attachments") && Catalog.Sockets.ContainsKey(Catalog.SocketKey(w2.Def, i.ContainerIndex)));
                var si = Catalog.Sockets[Catalog.SocketKey(w2.Def, occ.ContainerIndex)];
                var alt = si.Parts.Where(d => d != occ.Def && Catalog.Entries.ContainsKey(d)).Select(d => Catalog.Entries[d]).FirstOrDefault();
                Console.WriteLine("socket " + si.Label + " on " + w2.DisplayName + ": " + si.Parts.Count + " known parts, current " + occ.DisplayName + ", alternative " + (alt == null ? "-" : alt.Name));
                if (alt != null)
                {
                    int ci = occ.ContainerIndex; string wuid = w2.UidKey;
                    inv.RemoveItem(occ); inv.Rebuild();
                    w2 = inv.ItemsByUid[wuid];
                    inv.Insert(alt.Template, inv.ContainerOf(w2), ci, si.SlotIndex, false, 0);
                    w2 = inv.ItemsByUid[wuid];
                    Console.WriteLine("swapped -> " + string.Join(", ", inv.ContainerOf(w2).Items.Where(i => i.ContainerIndex == ci).Select(i => i.DisplayName)) + "; issues " + inv.Validate().Count);
                }
                var bytes = inv.G.ToBytes(); var re = GvasFile.Parse(bytes);
                Console.WriteLine("inventory re-encode stable: " + re.ToBytes().SequenceEqual(bytes));

                // 4) gunsmith presets: duplicate + rename round-trip
                if (gun != null)
                {
                    var map = (MapValue)gun.G.Root["PresetMap"];
                    var arr = (ArrayValue)((PropList)map.Entries[0].Value)["PresetItems"];
                    int before = arr.Items.Count;
                    var c = ((PropList)arr.Items[0]).DeepClone(); c.Get("PresetName").Value = new FStr("Kopie test"); arr.Items.Add(c);
                    var gb = gun.G.ToBytes(); var g2 = GvasFile.Parse(gb);
                    var arr2 = (ArrayValue)((PropList)((MapValue)g2.Root["PresetMap"]).Entries[0].Value)["PresetItems"];
                    Console.WriteLine("gunsmith presets " + before + " -> " + arr2.Items.Count + ", last name '" + ((PropList)arr2.Items.Last())["PresetName"] + "'");
                }
                return 0;
            }
            if (args[0] == "slots")
            {
                // print slot index -> row/col table and items of every top-level grid container
                var s = InvSave.Load(args[1], "Stash");
                foreach (var key in s.TopUids)
                {
                    MainContainer mc;
                    if (!s.ByUid.TryGetValue(key, out mc)) continue;
                    foreach (var ge in mc.Elements.Where(e => e.IsGrid))
                    {
                        Console.WriteLine("== " + mc.Title + " element " + ge.Index + " rows=" + ge.Rows + " cols=" + ge.Cols + " enabled=" + ge.EnabledCount + " tier=" + ge.Tier);
                        var sl = ge.Slots;
                        Console.WriteLine("   first slots: " + string.Join(" ", sl.Take(12).Select(x => x["Index"] + ":" + x["Row"] + "/" + x["Column"] + ((bool)x["bEnabled"] ? "" : "x"))));
                        Console.WriteLine("   last slots:  " + string.Join(" ", sl.Skip(Math.Max(0, sl.Count - 8)).Select(x => x["Index"] + ":" + x["Row"] + "/" + x["Column"] + ((bool)x["bEnabled"] ? "" : "x"))));
                        foreach (var it in mc.Items.Where(i => i.ContainerIndex == ge.Index).OrderBy(i => i.SlotIndex))
                        {
                            var slot = sl.FirstOrDefault(x => (int)x["Index"] == it.SlotIndex);
                            var dim = (PropList)it.P["ItemDimensions"];
                            Func<object, string> v2 = o => { var d = ((RawValue)o).Data; return BitConverter.ToDouble(d, 0) + ".." + BitConverter.ToDouble(d, 8); };
                            Console.WriteLine("   - slot " + it.SlotIndex + (slot != null ? " (r" + slot["Row"] + ",c" + slot["Column"] + ")" : " (no slot)") + " " + it.W + "x" + it.H + (it.Rotated ? " rot" : "")
                                + " X=" + v2(dim["X"]) + " Y=" + v2(dim["Y"]) + " " + it.DisplayName);
                        }
                    }
                }
                foreach (var v in s.Validate()) Console.WriteLine("  ! " + v);
                return 0;
            }
            if (args[0] == "tools")
            {
                string dir = args[1];
                var stash = InvSave.Load(Path.Combine(dir, "StashSaveGame.sav"), "Stash");
                var inv = InvSave.Load(Path.Combine(dir, "InventorySaveGame.sav"), "Inventory");
                var vend = InvSave.Load(Path.Combine(dir, "VendorSaveGame.sav"), "Vendor");
                Catalog.Build(new[] { vend, stash, inv });
                var main = stash.Containers.First(c => c.IsTopLevel && c.HasGrid);
                Console.WriteLine("repair: stash " + Tools.RepairAll(stash) + ", inv " + Tools.RepairAll(inv));
                Console.WriteLine("fill stacks: stash " + Tools.FillStacks(stash) + ", inv " + Tools.FillStacks(inv));
                Console.WriteLine("fill mags: inv " + Tools.FillMagazines(inv) + " (stash " + Tools.FillMagazines(stash) + ")");
                int before = main.Items.Count;
                Console.WriteLine("merge removed " + Tools.MergeStacks(stash, main) + " (items " + before + " -> " + stash.Containers.First(c => c.IsTopLevel && c.HasGrid).Items.Count + ")");
                main = stash.Containers.First(c => c.IsTopLevel && c.HasGrid);
                Console.WriteLine("sort ok: " + Tools.Sort(stash, main));
                Console.WriteLine("unlock pages: " + Tools.UnlockStashPages(stash));
                foreach (var v in stash.Validate().Concat(inv.Validate())) Console.WriteLine("  ! " + v);
                stash.Write(Path.Combine(dir, "StashSaveGame.sav"));
                inv.Write(Path.Combine(dir, "InventorySaveGame.sav"));
                var s2 = InvSave.Load(Path.Combine(dir, "StashSaveGame.sav"), "Stash");
                var m2 = s2.Containers.First(c => c.IsTopLevel && c.HasGrid);
                Console.WriteLine("reloaded stash items=" + m2.Items.Count + " issues=" + s2.Validate().Count);
                var ge = m2.Elements[0];
                var occ = s2.Occupancy(m2, ge, null);
                for (int r = 0; r < Math.Min(ge.Rows, 14); r++)
                {
                    var sb2 = new StringBuilder("   ");
                    for (int c = 0; c < ge.Cols; c++) sb2.Append(occ[r, c] == null ? " ." : " " + occ[r, c].DisplayName.Substring(0, 1));
                    Console.WriteLine(sb2);
                }
                return 0;
            }
            if (args[0] == "model")
            {
                string dir = args[1];
                var stash = InvSave.Load(Path.Combine(dir, "StashSaveGame.sav"), "Stash");
                var inv = InvSave.Load(Path.Combine(dir, "InventorySaveGame.sav"), "Inventory");
                var vend = InvSave.Load(Path.Combine(dir, "VendorSaveGame.sav"), "Vendor");
                Catalog.Build(new[] { vend, stash, inv });
                Console.WriteLine("catalog entries: " + Catalog.Entries.Count);
                foreach (var g in Catalog.Entries.Values.GroupBy(e => e.Category).OrderBy(g => g.Key))
                    Console.WriteLine("  " + g.Key + ": " + g.Count() + "  e.g. " + string.Join(", ", g.Take(3).Select(e => e.Name + " " + e.W + "x" + e.H + (e.AttachmentCount > 0 ? " +" + e.AttachmentCount : ""))));
                foreach (var s in new[] { stash, inv })
                {
                    Console.WriteLine(s.Kind + " top containers:");
                    foreach (var mc in s.Containers.Where(c => c.Label != null)) Console.WriteLine("   " + mc.Title + " items=" + mc.Items.Count);
                    foreach (var v in s.Validate()) Console.WriteLine("   ! " + v);
                }
                // insert test: a weapon from catalog into main stash
                var main = stash.Containers.First(c => c.IsTopLevel && c.HasGrid);
                var wpn = Catalog.Entries.Values.Where(e => e.Category.StartsWith("Weapons")).OrderByDescending(e => e.AttachmentCount).First();
                var spot = stash.FindSpot(main, wpn.W, wpn.H, null, -1);
                Console.WriteLine("insert " + wpn.Name + " (" + wpn.W + "x" + wpn.H + ", +" + wpn.AttachmentCount + ") at " + (spot == null ? "none" : string.Join(",", spot)));
                int before = stash.ItemsByUid.Count;
                var added = stash.Insert(wpn.Template, main, spot[0], spot[1], spot[2] == 1, 0);
                Console.WriteLine("items " + before + " -> " + stash.ItemsByUid.Count + "; descendants " + stash.Descendants(added).Count());
                var bytes = stash.G.ToBytes();
                var re = GvasFile.Parse(bytes);
                Console.WriteLine("re-encode stable: " + re.ToBytes().SequenceEqual(bytes) + ", validate: " + stash.Validate().Count + " issues");
                stash.RemoveItem(added); stash.Rebuild();
                Console.WriteLine("after remove items=" + stash.ItemsByUid.Count + ", bytes equal original: " + stash.G.ToBytes().SequenceEqual(File.ReadAllBytes(stash.Path)));
                return 0;
            }
            if (args[0] == "tree")
            {
                if (args.Length > 2) MaxItems = int.Parse(args[2]);
                var g = GvasFile.Load(args[1]);
                sb.AppendLine(g.SaveClass);
                DumpList(g.Root, 1);
                Console.Out.Write(sb.ToString());
            }
            return 0;
        }

        static int CountRaw(object v)
        {
            if (v is RawValue) return 1;
            if (v is PropList) return ((PropList)v).Props.Sum(p => CountRaw(p.Value));
            if (v is ArrayValue && !((ArrayValue)v).ByteArray) return ((ArrayValue)v).Items.Sum(i => CountRaw(i));
            return 0;
        }

        static void DumpList(PropList l, int d)
        {
            foreach (var p in l.Props) DumpVal(p.Name + " [" + p.Type + (p.Flags != 0 ? " f=" + p.Flags : "") + "]", p.Value, d);
        }

        static void DumpVal(string label, object v, int d)
        {
            string ind = new string(' ', d * 2);
            if (v is PropList) { sb.AppendLine(ind + label); DumpList((PropList)v, d + 1); }
            else if (v is ArrayValue && !((ArrayValue)v).ByteArray)
            {
                var a = (ArrayValue)v;
                sb.AppendLine(ind + label + " x" + a.Items.Count);
                for (int i = 0; i < a.Items.Count && i < MaxItems; i++) DumpVal("[" + i + "]", a.Items[i], d + 1);
            }
            else if (v is MapValue)
            {
                var m = (MapValue)v;
                sb.AppendLine(ind + label + " map x" + m.Entries.Count + (m.Removed.Count > 0 ? " removed " + m.Removed.Count : ""));
                foreach (var e in m.Entries.Take(MaxItems)) { DumpVal("key", e.Key, d + 1); DumpVal("val", e.Value, d + 2); }
            }
            else if (v is SetValue) sb.AppendLine(ind + label + " set x" + ((SetValue)v).Items.Count);
            else if (v is RawValue)
            {
                var b = ((RawValue)v).Data;
                sb.AppendLine(ind + label + " = RAW " + BitConverter.ToString(b.Take(40).ToArray()));
            }
            else if (v is FStr[]) sb.AppendLine(ind + label + " = " + string.Join(" | ", ((FStr[])v).Select(x => x.S)));
            else sb.AppendLine(ind + label + " = " + v);
        }
    }
}

