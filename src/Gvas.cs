using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PBZKeyForge
{
    /// <summary>One serialized piece of a GVAS tagged-property list.</summary>
    public class Node
    {
        public string Name;
        public string Type;
        public string[] Meta;
        public int Index;
        public byte[] Guid;
        public byte[] Raw;
        public List<Node> Children = new List<Node>();
        public List<List<Node>> Items = new List<List<Node>>();
        public Node Nested;
        public bool IsTerm;
        public int Kind;           // 0 leaf, 1 struct, 2 array, 3 enum wrapper, 4 terminator
    }

    public class BlobEntry
    {
        public string Name, Key, X, Y;
        public byte Slot;
        public byte[] Pad = new byte[5];
    }

    public class BlobRecord
    {
        public string ClassPath, ObjectName, ProfileId;
        public List<BlobEntry> Entries = new List<BlobEntry>();
    }

    /// <summary>A controller record belongs to one input profile and one device family.</summary>
    public sealed class GamepadProfile
    {
        public string ProfileId, Family;
        public int Index;
        public Node Keys;
        public List<Node> Fields;
    }

    /// <summary>
    /// EnhancedInputUserSettings.sav reader/writer. The format is UE 5.7 GVAS with a
    /// player-mappable key profile block appended after the top level properties.
    /// </summary>
    public class SaveFile
    {
        public InputDefaultSet Defaults { get; internal set; }

        public SaveFile() { Defaults = InputDefaultSet.Legacy; }

        public byte[] Header;
        public List<Node> Props = new List<Node>();
        public int BlobA, BlobB;
        public List<BlobRecord> Records = new List<BlobRecord>();
        public byte[] BlobRest;

        byte[] d;
        int p;

        int I32() { int v = BitConverter.ToInt32(d, p); p += 4; return v; }

        string FStr()
        {
            int n = BitConverter.ToInt32(d, p); p += 4;
            if (n == 0) return "";
            if (n < 0) { string s = Encoding.Unicode.GetString(d, p, -n * 2); p += -n * 2; return s.TrimEnd('\0'); }
            string r = Encoding.UTF8.GetString(d, p, n); p += n; return r.TrimEnd('\0');
        }

        Node ReadTag()
        {
            var t = new Node();
            t.Name = FStr();
            if (t.Name == "None") { t.IsTerm = true; t.Kind = 4; return t; }
            t.Type = FStr();
            if (t.Type == "EnumProperty")
            {
                t.Kind = 3;
                string f1 = I32().ToString(); string en = FStr();
                string f2 = I32().ToString(); string eo = FStr();
                t.Meta = new[] { f1, en, f2, eo };
                t.Nested = ReadTag();
                return t;
            }
            if (t.Type == "ArrayProperty")
            {
                t.Kind = 2;
                string f1 = I32().ToString(); string it = FStr();
                string f2 = I32().ToString(); string sn = FStr();
                string f3 = I32().ToString(); string ou = FStr();
                t.Meta = new[] { f1, it, f2, sn, f3, ou };
            }
            else if (t.Type == "StructProperty")
            {
                t.Kind = 1;
                string f1 = I32().ToString(); string sn = FStr();
                string f2 = I32().ToString(); string ou = FStr();
                t.Meta = new[] { f1, sn, f2, ou };
            }
            t.Index = I32();
            int size = I32();
            bool hasGuid = d[p] != 0; p += 1;
            if (hasGuid) { t.Guid = new byte[16]; Array.Copy(d, p, t.Guid, 0, 16); p += 16; }
            int vstart = p;
            if (t.Kind == 2)
            {
                int cnt = I32();
                for (int i = 0; i < cnt; i++)
                {
                    var elem = new List<Node>();
                    while (true) { var c = ReadTag(); if (c.IsTerm) break; elem.Add(c); }
                    t.Items.Add(elem);
                }
            }
            else if (t.Kind == 1)
            {
                while (true) { var c = ReadTag(); if (c.IsTerm) break; t.Children.Add(c); }
            }
            else
            {
                t.Raw = new byte[size];
                Array.Copy(d, p, t.Raw, 0, size);
                p += size;
            }
            if (p - vstart != size)
                throw new InvalidDataException(string.Format("Size mismatch on {0}: declared {1}, consumed {2}", t.Name, size, p - vstart));
            return t;
        }

        public static SaveFile Load(string path)
        {
            return FromBytes(File.ReadAllBytes(path), GameSchemeSettings.InputDefaultsForSave(path));
        }

        public static SaveFile Load(string path, InputDefaultSet defaults)
        { return FromBytes(File.ReadAllBytes(path), defaults); }

        public SaveFile Clone() { return FromBytes(ToBytes(), Defaults); }

        public static SaveFile FromBytes(byte[] bytes)
        { return FromBytes(bytes, InputDefaultSet.Legacy); }

        public static SaveFile FromBytes(byte[] bytes, InputDefaultSet defaults)
        {
            var s = new SaveFile { Defaults = defaults ?? InputDefaultSet.Legacy };
            s.d = (byte[])bytes.Clone();
            if (s.d.Length < 32 || Encoding.ASCII.GetString(s.d, 0, 4) != "GVAS")
                throw new InvalidDataException("Not a GVAS save file.");
            s.p = 4 + 4 + 4 + 4 + 6 + 4;
            s.FStr();
            s.p += 4;
            int cvc = BitConverter.ToInt32(s.d, s.p); s.p += 4;
            s.p += cvc * 20;
            s.FStr();
            s.p += 1;
            s.Header = new byte[s.p];
            Array.Copy(s.d, 0, s.Header, 0, s.p);

            while (true)
            {
                var t = s.ReadTag();
                s.Props.Add(t);
                if (t.IsTerm) break;
            }
            int topEnd = s.p;
            s.BlobA = BitConverter.ToInt32(s.d, topEnd);
            s.BlobB = BitConverter.ToInt32(s.d, topEnd + 4);
            int count = BitConverter.ToInt32(s.d, topEnd + 8);
            int q = topEnd + 12;
            for (int i = 0; i < count; i++)
            {
                var r = new BlobRecord();
                r.ClassPath = s.ReadStrAt(ref q);
                r.ObjectName = s.ReadStrAt(ref q);
                int n = BitConverter.ToInt32(s.d, q); q += 4;
                for (int j = 0; j < n; j++)
                {
                    var e = new BlobEntry();
                    e.Name = s.ReadStrAt(ref q);
                    e.Key = s.ReadStrAt(ref q);
                    e.X = s.ReadStrAt(ref q);
                    e.Y = s.ReadStrAt(ref q);
                    Array.Copy(s.d, q, e.Pad, 0, 5);
                    e.Slot = s.d[q + 5];
                    q += 6;
                    r.Entries.Add(e);
                }
                r.ProfileId = s.ReadStrAt(ref q);
                s.Records.Add(r);
            }
            s.BlobRest = new byte[s.d.Length - q];
            Array.Copy(s.d, q, s.BlobRest, 0, s.BlobRest.Length);
            return s;
        }

        string ReadStrAt(ref int q)
        {
            int n = BitConverter.ToInt32(d, q); q += 4;
            if (n == 0) return "";
            string r = Encoding.UTF8.GetString(d, q, n - 1);
            q += n;
            return r;
        }

        static void WStr(List<byte> o, string s)
        {
            if (string.IsNullOrEmpty(s)) { o.AddRange(BitConverter.GetBytes(0)); return; }
            var b = Encoding.UTF8.GetBytes(s);
            o.AddRange(BitConverter.GetBytes(b.Length + 1));
            o.AddRange(b); o.Add(0);
        }

        static void WI32(List<byte> o, int v) { o.AddRange(BitConverter.GetBytes(v)); }

        static void WriteTag(List<byte> o, Node t)
        {
            if (t.IsTerm) { WStr(o, "None"); return; }
            WStr(o, t.Name); WStr(o, t.Type);
            if (t.Kind == 3)
            {
                WI32(o, int.Parse(t.Meta[0])); WStr(o, t.Meta[1]);
                WI32(o, int.Parse(t.Meta[2])); WStr(o, t.Meta[3]);
                WriteTag(o, t.Nested);
                return;
            }
            if (t.Kind == 2)
            {
                WI32(o, int.Parse(t.Meta[0])); WStr(o, t.Meta[1]);
                WI32(o, int.Parse(t.Meta[2])); WStr(o, t.Meta[3]);
                WI32(o, int.Parse(t.Meta[4])); WStr(o, t.Meta[5]);
            }
            else if (t.Kind == 1)
            {
                WI32(o, int.Parse(t.Meta[0])); WStr(o, t.Meta[1]);
                WI32(o, int.Parse(t.Meta[2])); WStr(o, t.Meta[3]);
            }
            var body = new List<byte>();
            if (t.Kind == 2)
            {
                WI32(body, t.Items.Count);
                foreach (var el in t.Items)
                {
                    foreach (var c in el) WriteTag(body, c);
                    WStr(body, "None");
                }
            }
            else if (t.Kind == 1)
            {
                foreach (var c in t.Children) WriteTag(body, c);
                WStr(body, "None");
            }
            else body.AddRange(t.Raw);

            WI32(o, t.Index);
            WI32(o, body.Count);
            if (t.Guid != null) { o.Add(1); o.AddRange(t.Guid); } else o.Add(0);
            o.AddRange(body);
        }

        public void Save(string path)
        {
            File.WriteAllBytes(path, ToBytes());
        }

        public byte[] ToBytes()
        {
            var o = new List<byte>();
            o.AddRange(Header);
            foreach (var t in Props) WriteTag(o, t);
            WI32(o, BlobA); WI32(o, BlobB); WI32(o, Records.Count);
            foreach (var r in Records)
            {
                WStr(o, r.ClassPath); WStr(o, r.ObjectName);
                WI32(o, r.Entries.Count);
                foreach (var e in r.Entries)
                {
                    WStr(o, e.Name); WStr(o, e.Key); WStr(o, e.X); WStr(o, e.Y);
                    o.AddRange(e.Pad); o.Add(e.Slot);
                }
                WStr(o, r.ProfileId);
            }
            o.AddRange(BlobRest);
            return o.ToArray();
        }

        public string ActiveProfile
        {
            get
            {
                var t = Props.FirstOrDefault(x => x.Name == "CurrentProfileIdentifierString");
                return t == null ? null : StrOf(t);
            }
        }

        public BlobRecord ActiveRecord
        {
            get { return Records.FirstOrDefault(r => r.ProfileId == ActiveProfile) ?? Records.FirstOrDefault(); }
        }

        public List<Node> GamepadKeys
        {
            get
            {
                var t = Props.FirstOrDefault(x => x.Name == "GamepadFamilyProfiles");
                if (t == null || t.Kind != 2) return new List<Node>();
                return t.Items.Select(el => el.FirstOrDefault(x => x.Name == "Keys")).Where(x => x != null).ToList();
            }
        }

        public List<GamepadProfile> GamepadProfiles
        {
            get
            {
                var result = new List<GamepadProfile>();
                Node profiles = Props.FirstOrDefault(node => node.Name == "GamepadFamilyProfiles");
                if (profiles == null) return result;
                if (profiles.Kind != 2) throw new InvalidDataException("Unsupported gamepad profile list.");
                for (int index = 0; index < profiles.Items.Count; index++)
                {
                    List<Node> fields = profiles.Items[index];
                    string profileId = Field(fields, "ProfileId");
                    Node keys = fields.FirstOrDefault(node => node.Name == "Keys");
                    // Without an explicit profile relationship, the tool cannot safely
                    // infer whether this record is shared or belongs to the selected scheme.
                    if (string.IsNullOrEmpty(profileId) || keys == null || keys.Kind != 2)
                        throw new InvalidDataException("Unsupported gamepad record: an explicit ProfileId and Keys list are required.");
                    result.Add(new GamepadProfile { ProfileId = profileId, Family = Field(fields, "Family"), Index = index, Keys = keys, Fields = fields });
                }
                return result;
            }
        }

        public List<Node> GamepadKeysFor(string profileId)
        {
            return GamepadProfiles.Where(profile => profile.ProfileId == profileId).Select(profile => profile.Keys).ToList();
        }

        public static string StrOf(Node t)
        {
            if (t == null || t.Raw == null) return null;
            int n = BitConverter.ToInt32(t.Raw, 0);
            if (n <= 0) return "";
            return Encoding.UTF8.GetString(t.Raw, 4, n - 1);
        }

        public static void SetStr(Node t, string v)
        {
            var b = Encoding.UTF8.GetBytes(v + "\0");
            var r = new byte[4 + b.Length];
            Array.Copy(BitConverter.GetBytes(b.Length), r, 4);
            Array.Copy(b, 0, r, 4, b.Length);
            t.Raw = r;
        }

        public static string Field(List<Node> elem, string name)
        {
            foreach (var f in elem)
            {
                if (f.IsTerm || f.Name != name) continue;
                if (f.Kind == 3) return StrOf(f.Nested);
                if (f.Kind == 1) return StrOf(f.Children.FirstOrDefault(c => c.Name == "KeyName"));
                return StrOf(f);
            }
            return null;
        }

        public static void SetField(List<Node> elem, string name, string value)
        {
            foreach (var f in elem)
            {
                if (f.IsTerm || f.Name != name) continue;
                if (f.Kind == 3) SaveFile.SetStr(f.Nested, value);
                else if (f.Kind == 1)
                {
                    var k = f.Children.FirstOrDefault(c => c.Name == "KeyName");
                    if (k != null) SaveFile.SetStr(k, value);
                }
                else SaveFile.SetStr(f, value);
                return;
            }
        }

        public static Node CloneNode(Node n)
        {
            var c = new Node
            {
                Name = n.Name,
                Type = n.Type,
                Index = n.Index,
                Kind = n.Kind,
                IsTerm = n.IsTerm,
                Meta = n.Meta == null ? null : (string[])n.Meta.Clone(),
                Raw = n.Raw == null ? null : (byte[])n.Raw.Clone(),
                Guid = n.Guid == null ? null : (byte[])n.Guid.Clone()
            };
            foreach (var x in n.Children) c.Children.Add(CloneNode(x));
            foreach (var el in n.Items) c.Items.Add(el.Select(CloneNode).ToList());
            if (n.Nested != null) c.Nested = CloneNode(n.Nested);
            return c;
        }

        public BlobRecord RecordFor(string profileId)
        {
            return Records.FirstOrDefault(r => r.ProfileId == profileId);
        }

        /// <summary>Write Enhanced Input's profile metadata; the game's selector is in PBZSharedSetting.sav.</summary>
        public bool SetActiveProfile(string profileId)
        {
            if (RecordFor(profileId) == null) return false;
            var t = Props.FirstOrDefault(x => x.Name == "CurrentProfileIdentifierString");
            if (t == null) return false;
            SetStr(t, profileId);
            return true;
        }

        /// <summary>Drop every gamepad override so the file matches the shipped defaults.</summary>
        public void ResetGamepadToDefaults()
        {
            foreach (var keys in GamepadKeys)
                foreach (var el in keys.Items)
                {
                    string def = Field(el, "DefaultKey");
                    if (def != null) SetField(el, "Key", def);
                }
        }

        /// <summary>Remove all keyboard overrides for every profile record.</summary>
        public void ClearKeyboardOverrides()
        {
            foreach (var r in Records) r.Entries = new List<BlobEntry>();
        }
    }
}

