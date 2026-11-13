using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace IrrEditor
{
    static class Analyze
    {
        static string G(object raw) { return new Guid(((RawValue)raw).Data).ToString().Substring(0, 8); }
        static string V2(object raw)
        {
            var d = ((RawValue)raw).Data;
            return BitConverter.ToDouble(d, 0) + ".." + BitConverter.ToDouble(d, 8);
        }

        public static void Run(string file)
        {
            var g = GvasFile.Load(file);
            var mcs = (ArrayValue)g.Root["MainContainers"];
            var tops = g.Root["MainContainerUIDs"] as ArrayValue;
            Console.WriteLine("Top UIDs: " + (tops == null ? "-" : string.Join(", ", tops.Items.Select(G))));
            foreach (var p in g.Root.Props) Console.WriteLine("root prop: " + p.Name + " " + p.Type);
            var allItems = new Dictionary<string, PropList>();
            foreach (PropList mc in mcs.Items)
                foreach (PropList it in ((ArrayValue)mc["ContainerItems"]).Items) allItems[G(it["ItemUID"])] = it;
            foreach (PropList mc in mcs.Items)
            {
                string uid = G(mc["MainContainerUID"]);
                PropList owner; allItems.TryGetValue(uid, out owner);
                Console.WriteLine("== MC " + uid + (owner != null ? " (owned by item " + Short(owner) + ")" : ""));
                int ei = 0;
                foreach (PropList el in ((ArrayValue)mc["ContainerElements"]).Items)
                {
                    var slots = ((ArrayValue)el["ContainerSlots"]).Items.Cast<PropList>().ToList();
                    int rows = slots.Count == 0 ? 0 : slots.Max(s => (int)s["Row"]) + 1;
                    int cols = slots.Count == 0 ? 0 : slots.Max(s => (int)s["Column"]) + 1;
                    int dis = slots.Count(s => !(bool)s["bEnabled"]);
                    string extra = string.Join(" ", el.Props.Where(p => p.Name != "ContainerSlots").Select(p => p.Name + "=" + p.Value));
                    Console.WriteLine("   element " + ei++ + ": " + slots.Count + " slots " + rows + "x" + cols + " disabled=" + dis + " " + extra);
                }
                foreach (PropList it in ((ArrayValue)mc["ContainerItems"]).Items)
                {
                    var dim = (PropList)it["ItemDimensions"];
                    Console.WriteLine("   - " + Short(it) + " uid=" + G(it["ItemUID"]) + " mother=" + G(it["MotherUID"]) + " ci=" + it["ContainerIndex"] + " slot=" + it["SlotIndex"]
                        + " n=" + it["Count"] + "/" + it["MaxStack"] + " rot=" + it["bRotated"] + " X=" + V2(dim["X"]) + " Y=" + V2(dim["Y"])
                        + " state=" + it["CurrentState"] + " stats=" + string.Join(";", ((ArrayValue)it["CurrentStats"]).Items.Cast<PropList>().Select(s => ((PropList)s["Tag"])["TagName"].ToString().Replace("Inventory.Stats.", "") + "=" + s["Value"] + "/" + s["MaxValue"]))
                        + " extra=" + string.Join(",", it.Props.Select(p => p.Name).Except(new[] { "ItemDefinition", "ItemUID", "MotherUID", "ContainerIndex", "SlotIndex", "CurrentState", "CurrentStats", "ItemDimensions", "CanStack", "MaxStack", "Count", "bRotated", "bQuestItem" })));
                }
            }
        }
        static string Short(PropList it)
        {
            var s = it["ItemDefinition"].ToString();
            return s.Substring(s.LastIndexOf('.') + 1);
        }
    }
}
