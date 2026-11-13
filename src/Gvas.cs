// Lossless GVAS (Unreal Engine 5.4+ SaveGame) reader/writer.
// Targets C# 5 so it compiles with the csc.exe shipped in Windows (.NET Framework 4.x).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace IrrEditor
{
    public class TypeName
    {
        public string Name;
        public List<TypeName> Inner = new List<TypeName>();
        public TypeName() { }
        public TypeName(string name, params TypeName[] inner) { Name = name; Inner.AddRange(inner); }
        public TypeName Arg(int i) { return i < Inner.Count ? Inner[i] : null; }
        public override string ToString()
        {
            if (Inner.Count == 0) return Name;
            return Name + "(" + string.Join(",", Inner.Select(x => x.ToString())) + ")";
        }
        public TypeName Clone()
        {
            var t = new TypeName(Name);
            foreach (var i in Inner) t.Inner.Add(i.Clone());
            return t;
        }
    }

    /// <summary>Opaque bytes we keep verbatim.</summary>
    public class RawValue
    {
        public byte[] Data;
        public RawValue(byte[] d) { Data = d; }
        public override string ToString() { return "<" + Data.Length + " bytes>"; }
    }

    /// <summary>A struct serialized as tagged properties.</summary>
    public class PropList
    {
        public List<Prop> Props = new List<Prop>();
        public Prop Get(string name) { foreach (var p in Props) if (p.Name == name) return p; return null; }
        public object this[string name]
        {
            get { var p = Get(name); return p == null ? null : p.Value; }
        }
        public PropList DeepClone() { var c = new PropList(); foreach (var p in Props) c.Props.Add(p.DeepClone()); return c; }
    }

    public class ArrayValue
    {
        public List<object> Items = new List<object>();
        public bool ByteArray; // TArray<uint8> stored as a single RawValue
    }

    public class Prop
    {
        public string Name;
        public TypeName Type;
        public byte Flags;
        public int ArrayIndex;
        public byte[] Guid;       // 16 bytes when Flags & 0x02
        public byte[] ExtRaw;     // extension payload when Flags & 0x04
        public object Value;

        public const byte F_ArrayIndex = 0x01, F_Guid = 0x02, F_Ext = 0x04, F_Native = 0x08, F_BoolTrue = 0x10, F_Skipped = 0x20;

        public bool IsNative { get { return (Flags & F_Native) != 0; } }

        public Prop DeepClone()
        {
            var p = (Prop)MemberwiseClone();
            p.Type = Type.Clone();
            p.Value = CloneValue(Value);
            return p;
        }
        public static object CloneValue(object v)
        {
            if (v is PropList) return ((PropList)v).DeepClone();
            if (v is ArrayValue)
            {
                var a = new ArrayValue { ByteArray = ((ArrayValue)v).ByteArray };
                foreach (var i in ((ArrayValue)v).Items) a.Items.Add(CloneValue(i));
                return a;
            }
            if (v is RawValue) return new RawValue((byte[])((RawValue)v).Data.Clone());
            if (v is byte[]) return ((byte[])v).Clone();
            if (v is string[]) return ((string[])v).Clone();
            return v; // immutable primitives / strings
        }
        public override string ToString() { return Name + " : " + Type + " = " + Value; }
    }

    public class GvasFile
    {
        public byte[] Header;          // everything up to and incl. the class name + post-class bytes
        public string SaveClass;
        public PropList Root = new PropList();
        public byte[] Trailer;         // bytes after the root "None"

        // Fixed-size natively serialized structs (UE5 uses doubles for math types).
        static readonly Dictionary<string, int> NativeSizes = new Dictionary<string, int>
        {
            {"Vector2D",16},{"Vector",24},{"Rotator",24},{"Quat",32},{"Guid",16},{"DateTime",8},
            {"Timespan",8},{"LinearColor",16},{"Color",4},{"IntPoint",8},{"IntVector",12},
            {"Vector4",32},{"Box2D",33},{"Transform",80}
        };

        // ------------------------------------------------------------ reading
        public static GvasFile Load(string path) { return Parse(File.ReadAllBytes(path)); }

        public static GvasFile Parse(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
            var g = new GvasFile();
            if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "GVAS") throw new InvalidDataException("Not a GVAS file");
            int saveVer = r.ReadInt32();
            r.ReadInt32(); // UE4 package version
            if (saveVer >= 3) r.ReadInt32(); // UE5 package version
            r.ReadUInt16(); r.ReadUInt16(); r.ReadUInt16(); r.ReadUInt32(); // engine version
            ReadFString(r); // branch
            r.ReadInt32(); // custom version format
            int n = r.ReadInt32();
            r.ReadBytes(n * 20);
            g.SaveClass = ReadFString(r);
            // UE 5.5+: one extra byte follows the class name. Detect it: the next thing must be an FString length.
            long pos = r.BaseStream.Position;
            int peek = r.ReadInt32();
            r.BaseStream.Position = pos;
            if (peek <= 0 || peek > 1024) r.ReadByte();
            int headerLen = (int)r.BaseStream.Position;
            g.Header = new byte[headerLen];
            Array.Copy(data, g.Header, headerLen);
            g.Root = ReadProps(r);
            int rest = (int)(r.BaseStream.Length - r.BaseStream.Position);
            g.Trailer = r.ReadBytes(rest);
            return g;
        }

        public static string ReadFString(BinaryReader r)
        {
            int len = r.ReadInt32();
            if (len == 0) return null; // distinguish "" (len 1) from empty (len 0)
            if (len < 0)
            {
                var b = r.ReadBytes(-len * 2);
                return "\u0001U" + Encoding.Unicode.GetString(b, 0, b.Length - 2); // mark UTF-16
            }
            var bytes = r.ReadBytes(len);
            return Encoding.UTF8.GetString(bytes, 0, len - 1);
        }

        static TypeName ReadTypeName(BinaryReader r)
        {
            var t = new TypeName { Name = ReadFString(r) };
            int inner = r.ReadInt32();
            for (int i = 0; i < inner; i++) t.Inner.Add(ReadTypeName(r));
            return t;
        }

        static PropList ReadProps(BinaryReader r)
        {
            var list = new PropList();
            while (true)
            {
                string name = ReadFString(r);
                if (name == null || name == "None") break;
                var p = new Prop { Name = name };
                p.Type = ReadTypeName(r);
                int size = r.ReadInt32();
                p.Flags = r.ReadByte();
                if ((p.Flags & Prop.F_ArrayIndex) != 0) p.ArrayIndex = r.ReadInt32();
                if ((p.Flags & Prop.F_Guid) != 0) p.Guid = r.ReadBytes(16);
                if ((p.Flags & Prop.F_Ext) != 0)
                {
                    // EPropertyTagExtension byte; 0x02 = OverridableInformation (+ op byte + bool as uint8)
                    byte ext = r.ReadByte();
                    var ms = new MemoryStream(); ms.WriteByte(ext);
                    if ((ext & 0x02) != 0) { ms.WriteByte(r.ReadByte()); ms.WriteByte(r.ReadByte()); }
                    p.ExtRaw = ms.ToArray();
                }
                long start = r.BaseStream.Position;
                byte[] raw = r.ReadBytes(size);
                p.Value = DecodeValue(p.Type, raw, p.IsNative, p.Flags);
                list.Props.Add(p);
            }
            return list;
        }

        // Decode a value; if the decode doesn't consume exactly the bytes, keep them raw (lossless).
        static object DecodeValue(TypeName t, byte[] raw, bool native, byte flags)
        {
            if (t.Name == "BoolProperty") return (flags & Prop.F_BoolTrue) != 0;
            try
            {
                var r = new BinaryReader(new MemoryStream(raw), Encoding.UTF8);
                object v = ReadValue(r, t, native, true);
                if (r.BaseStream.Position != raw.Length) return new RawValue(raw);
                // verify that re-encoding is identical, else keep raw
                var w = new BinaryWriter(new MemoryStream());
                WriteValue(w, t, v, true);
                var re = ((MemoryStream)w.BaseStream).ToArray();
                if (!re.SequenceEqual(raw)) return new RawValue(raw);
                return v;
            }
            catch { return new RawValue(raw); }
        }

        static object ReadValue(BinaryReader r, TypeName t, bool native, bool topLevel)
        {
            switch (t.Name)
            {
                case "IntProperty": return r.ReadInt32();
                case "Int64Property": return r.ReadInt64();
                case "UInt32Property": return r.ReadUInt32();
                case "UInt64Property": return r.ReadUInt64();
                case "Int16Property": return r.ReadInt16();
                case "UInt16Property": return r.ReadUInt16();
                case "Int8Property": return r.ReadSByte();
                case "FloatProperty": return r.ReadSingle();
                case "DoubleProperty": return r.ReadDouble();
                case "BoolProperty": return r.ReadByte() != 0; // only inside containers
                case "NameProperty":
                case "StrProperty":
                case "ObjectProperty":
                case "ClassProperty":
                case "EnumProperty":
                    return new FStr(ReadFString(r));
                case "ByteProperty":
                    if (t.Inner.Count > 0 && t.Inner[0].Name != "None" && topLevel) return new FStr(ReadFString(r));
                    return r.ReadByte();
                case "SoftObjectProperty":
                case "SoftClassProperty":
                    // FSoftObjectPath (5.x): FTopLevelAssetPath(PackageName FName, AssetName FName) + SubPath FString
                    return new FStr[] { new FStr(ReadFString(r)), new FStr(ReadFString(r)), new FStr(ReadFString(r)) };
                case "StructProperty":
                    {
                        string sname = t.Arg(0) != null ? t.Arg(0).Name : "";
                        int fixedSize;
                        if (NativeSizes.TryGetValue(sname, out fixedSize)) return new RawValue(r.ReadBytes(fixedSize));
                        if (sname == "GameplayTagContainer")
                        {
                            int c = r.ReadInt32();
                            var tags = new FStr[c];
                            for (int i = 0; i < c; i++) tags[i] = new FStr(ReadFString(r));
                            return new TagContainer { Tags = tags.ToList() };
                        }
                        if (native && topLevel) throw new NotSupportedException("native struct " + sname);
                        return ReadProps(r);
                    }
                case "ArrayProperty":
                    {
                        var inner = t.Arg(0);
                        int count = r.ReadInt32();
                        var a = new ArrayValue();
                        if (inner.Name == "ByteProperty" && (inner.Inner.Count == 0 || inner.Inner[0].Name == "None"))
                        {
                            a.Items.Add(new RawValue(r.ReadBytes(count)));
                            a.ByteArray = true;
                            return a;
                        }
                        for (int i = 0; i < count; i++) a.Items.Add(ReadValue(r, inner, false, false));
                        return a;
                    }
                case "MapProperty":
                    {
                        // int32 numKeysToRemove + keys, int32 count + (key,value) pairs
                        var m = new MapValue();
                        int rem = r.ReadInt32();
                        for (int i = 0; i < rem; i++) m.Removed.Add(ReadValue(r, t.Arg(0), false, false));
                        int n = r.ReadInt32();
                        for (int i = 0; i < n; i++)
                        {
                            var k = ReadValue(r, t.Arg(0), false, false);
                            var v = ReadValue(r, t.Arg(1), false, false);
                            m.Entries.Add(new MapEntry { Key = k, Value = v });
                        }
                        return m;
                    }
                case "SetProperty":
                    {
                        var s = new SetValue();
                        int rem = r.ReadInt32();
                        for (int i = 0; i < rem; i++) s.Removed.Add(ReadValue(r, t.Arg(0), false, false));
                        int n = r.ReadInt32();
                        for (int i = 0; i < n; i++) s.Items.Add(ReadValue(r, t.Arg(0), false, false));
                        return s;
                    }
                default:
                    throw new NotSupportedException(t.Name);
            }
        }

        // ------------------------------------------------------------ writing
        public byte[] ToBytes()
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(Header);
            WriteProps(w, Root);
            w.Write(Trailer);
            return ms.ToArray();
        }

        public static void WriteFString(BinaryWriter w, string s)
        {
            if (s == null) { w.Write(0); return; }
            if (s.StartsWith("\u0001U"))
            {
                var b = Encoding.Unicode.GetBytes(s.Substring(2));
                w.Write(-(b.Length / 2 + 1)); w.Write(b); w.Write((short)0); return;
            }
            var bytes = Encoding.UTF8.GetBytes(s);
            w.Write(bytes.Length + 1); w.Write(bytes); w.Write((byte)0);
        }

        static void WriteTypeName(BinaryWriter w, TypeName t)
        {
            WriteFString(w, t.Name);
            w.Write(t.Inner.Count);
            foreach (var i in t.Inner) WriteTypeName(w, i);
        }

        static void WriteProps(BinaryWriter w, PropList list)
        {
            foreach (var p in list.Props)
            {
                WriteFString(w, p.Name);
                WriteTypeName(w, p.Type);
                byte[] payload;
                byte flags = p.Flags;
                if (p.Type.Name == "BoolProperty" && p.Value is bool)
                {
                    payload = new byte[0];
                    flags = (byte)((bool)p.Value ? (flags | Prop.F_BoolTrue) : (flags & ~Prop.F_BoolTrue));
                }
                else
                {
                    var tmp = new BinaryWriter(new MemoryStream());
                    WriteValue(tmp, p.Type, p.Value, true);
                    payload = ((MemoryStream)tmp.BaseStream).ToArray();
                }
                w.Write(payload.Length);
                w.Write(flags);
                if ((flags & Prop.F_ArrayIndex) != 0) w.Write(p.ArrayIndex);
                if ((flags & Prop.F_Guid) != 0) w.Write(p.Guid);
                if ((flags & Prop.F_Ext) != 0) w.Write(p.ExtRaw);
                w.Write(payload);
            }
            WriteFString(w, "None");
        }

        static void WriteValue(BinaryWriter w, TypeName t, object v, bool topLevel)
        {
            if (v is RawValue) { w.Write(((RawValue)v).Data); return; }
            switch (t.Name)
            {
                case "IntProperty": w.Write((int)v); return;
                case "Int64Property": w.Write((long)v); return;
                case "UInt32Property": w.Write((uint)v); return;
                case "UInt64Property": w.Write((ulong)v); return;
                case "Int16Property": w.Write((short)v); return;
                case "UInt16Property": w.Write((ushort)v); return;
                case "Int8Property": w.Write((sbyte)v); return;
                case "FloatProperty": w.Write((float)v); return;
                case "DoubleProperty": w.Write((double)v); return;
                case "BoolProperty": w.Write((byte)((bool)v ? 1 : 0)); return;
                case "ByteProperty":
                    if (v is FStr) { WriteFString(w, ((FStr)v).S); return; }
                    w.Write((byte)v); return;
                case "SoftObjectProperty":
                case "SoftClassProperty":
                    foreach (var s in (FStr[])v) WriteFString(w, s.S);
                    return;
                case "StructProperty":
                    if (v is TagContainer)
                    {
                        var tc = (TagContainer)v;
                        w.Write(tc.Tags.Count);
                        foreach (var s in tc.Tags) WriteFString(w, s.S);
                        return;
                    }
                    WriteProps(w, (PropList)v);
                    return;
                case "ArrayProperty":
                    {
                        var a = (ArrayValue)v;
                        if (a.ByteArray)
                        {
                            var d = ((RawValue)a.Items[0]).Data;
                            w.Write(d.Length); w.Write(d); return;
                        }
                        w.Write(a.Items.Count);
                        foreach (var i in a.Items) WriteValue(w, t.Arg(0), i, false);
                        return;
                    }
                case "MapProperty":
                    {
                        var m = (MapValue)v;
                        w.Write(m.Removed.Count);
                        foreach (var k in m.Removed) WriteValue(w, t.Arg(0), k, false);
                        w.Write(m.Entries.Count);
                        foreach (var e in m.Entries) { WriteValue(w, t.Arg(0), e.Key, false); WriteValue(w, t.Arg(1), e.Value, false); }
                        return;
                    }
                case "SetProperty":
                    {
                        var s = (SetValue)v;
                        w.Write(s.Removed.Count);
                        foreach (var k in s.Removed) WriteValue(w, t.Arg(0), k, false);
                        w.Write(s.Items.Count);
                        foreach (var k in s.Items) WriteValue(w, t.Arg(0), k, false);
                        return;
                    }
                default:
                    if (v is FStr) { WriteFString(w, ((FStr)v).S); return; }
                    throw new NotSupportedException(t.Name);
            }
        }
    }

    /// <summary>Wrapper so that null FStrings (length 0) survive round-trips.</summary>
    public class FStr
    {
        public string S;
        public FStr(string s) { S = s; }
        public override string ToString() { return S ?? ""; }
    }

    public class MapEntry { public object Key; public object Value; }

    public class MapValue
    {
        public List<object> Removed = new List<object>();
        public List<MapEntry> Entries = new List<MapEntry>();
    }

    public class SetValue
    {
        public List<object> Removed = new List<object>();
        public List<object> Items = new List<object>();
    }

    public class TagContainer
    {
        public List<FStr> Tags = new List<FStr>();
        public override string ToString() { return "[" + string.Join(", ", Tags.Select(x => x.S)) + "]"; }
    }
}
