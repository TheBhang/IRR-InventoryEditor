// Undo / redo by snapshotting the serialized bytes of every editable save.
using System;
using System.Collections.Generic;
using System.Linq;

namespace IrrEditor
{
    public class Snapshot
    {
        public string Label;
        public string Key;                 // for coalescing rapid edits of the same field
        public DateTime At;
        public List<byte[]> Data = new List<byte[]>();
        public List<bool> Dirty = new List<bool>();
    }

    /// <summary>Anything whose state can be captured/restored as a GVAS file.</summary>
    public interface IUndoTarget
    {
        GvasFile Gvas { get; set; }
        bool IsDirty { get; set; }
    }

    public class UndoStack
    {
        const int Limit = 40;
        readonly List<Snapshot> undo = new List<Snapshot>();
        readonly List<Snapshot> redo = new List<Snapshot>();
        readonly Func<List<IUndoTarget>> targets;
        readonly Action restored;

        public UndoStack(Func<List<IUndoTarget>> targets, Action restored) { this.targets = targets; this.restored = restored; }

        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }
        public string UndoLabel { get { return undo.Count > 0 ? undo[undo.Count - 1].Label : null; } }
        public string RedoLabel { get { return redo.Count > 0 ? redo[redo.Count - 1].Label : null; } }

        Snapshot Capture(string label, string key)
        {
            var s = new Snapshot { Label = label, Key = key, At = DateTime.Now };
            foreach (var t in targets()) { s.Data.Add(t.Gvas.ToBytes()); s.Dirty.Add(t.IsDirty); }
            return s;
        }

        /// <summary>Call BEFORE a change. Rapid repeats with the same key (typing a number) collapse into one step.</summary>
        public void Checkpoint(string label, string key = null)
        {
            if (key != null && undo.Count > 0)
            {
                var last = undo[undo.Count - 1];
                if (last.Key == key && (DateTime.Now - last.At).TotalSeconds < 3) { last.At = DateTime.Now; return; }
            }
            undo.Add(Capture(label, key));
            if (undo.Count > Limit) undo.RemoveAt(0);
            redo.Clear();
        }

        /// <summary>Drop the last checkpoint (the action turned out to change nothing).</summary>
        public void Discard() { if (undo.Count > 0) undo.RemoveAt(undo.Count - 1); }

        public void Clear() { undo.Clear(); redo.Clear(); }

        public string Undo() { return Swap(undo, redo); }
        public string Redo() { return Swap(redo, undo); }

        string Swap(List<Snapshot> from, List<Snapshot> to)
        {
            if (from.Count == 0) return null;
            var s = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            var cur = Capture(s.Label, null);
            to.Add(cur);
            var ts = targets();
            for (int i = 0; i < ts.Count && i < s.Data.Count; i++)
            {
                ts[i].Gvas = GvasFile.Parse(s.Data[i]);
                ts[i].IsDirty = s.Dirty[i];
            }
            restored();
            return s.Label;
        }
    }
}
