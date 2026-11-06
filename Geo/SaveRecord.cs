using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>How the road an item stood at differs now from when its save was written (0.12.38, DESIGN-save-data.md 4.5).</summary>
    public enum AnchorChange : byte
    {
        Same = 0,

        /// <summary>No segment ends where it ended: deleted, moved, or split by a new junction.</summary>
        Gone = 1,

        /// <summary>A segment ends where it did but its curve is another.</summary>
        Moved = 2,

        /// <summary>Another road prefab.</summary>
        Replaced = 3,

        /// <summary>Other upgrades (lanes, trees, grass, a median, ...).</summary>
        Upgraded = 4,

        /// <summary>Another number of segments meet at one of its ends.</summary>
        Junction = 5
    }

    /// <summary>
    /// The road segment an item stood at when its save was written, as it was then (0.12.38; the owner: "如果期间修改了道路信息则取消这个道路的匹配"): its prefab's name, its curve, its upgrades (without the
    /// kerb styles' bits, which are not in the save) and how many segments met at each of its ends. Compared field by field with the segment that ends at the same places now. Pure.
    /// </summary>
    public struct RoadAnchor
    {
        /// <summary>How far (m) a point of the curve may be from where it was and count as the same.</summary>
        public const float Tolerance = 0.01f;

        /// <summary>The road prefab's name; empty when it is not known (a record of format 1, written by 0.12.38, has another prefab's name there: SaveRecord.Read).</summary>
        public string Prefab;
        public Vec3 A, B, C, D;
        public uint Left, Right, General;
        public byte StartDegree, EndDegree;

        /// <summary>The key a segment is found by: where its curve starts and ends, to the centimetre.</summary>
        public static long EndsKey(Vec3 a, Vec3 d)
        {
            unchecked
            {
                long h = 17;
                foreach (float v in new[] { a.X, a.Y, a.Z, d.X, d.Y, d.Z })
                    h = h * 1000003L + (long)Math.Round(v * 100.0);
                return h;
            }
        }

        public long Key => EndsKey(A, D);

        public Vec3 Middle => (A + B * 3f + C * 3f + D) * 0.125f;

        public AnchorChange Compare(RoadAnchor now)
        {
            if (!Near(A, now.A) || !Near(D, now.D))
                return AnchorChange.Gone;
            if (!string.IsNullOrEmpty(Prefab) && !string.Equals(Prefab, now.Prefab, StringComparison.Ordinal))
                return AnchorChange.Replaced;
            if (!Near(B, now.B) || !Near(C, now.C))
                return AnchorChange.Moved;
            if (Left != now.Left || Right != now.Right || General != now.General)
                return AnchorChange.Upgraded;
            if (StartDegree != now.StartDegree || EndDegree != now.EndDegree)
                return AnchorChange.Junction;
            return AnchorChange.Same;
        }

        private static bool Near(Vec3 p, Vec3 q) => Math.Abs(p.X - q.X) <= Tolerance && Math.Abs(p.Y - q.Y) <= Tolerance && Math.Abs(p.Z - q.Z) <= Tolerance;
    }

    /// <summary>One placed item in a save's record: the fields of TsItem version 8, the road it stood at (Anchor, -1: none) and, for one put away unmatched, why.</summary>
    public sealed class StoredItem
    {
        public uint TypeHash;
        public float EdgeT;
        public Vec3 Position;
        public float Yaw;
        public byte Flags;
        public uint SupportHash;
        public string Spec = "";
        public float Reach;
        public uint Group;
        public byte Slot;
        public byte Lane;
        public float OffsetX;
        public float Turn;
        public float BaseReach;

        /// <summary>The support's style (TsItem.Style; format 4, 0.12.57).</summary>
        public byte Style;

        /// <summary>The batch that wrote the item (TsItem.BatchId; format 5, 0.12.67); 0 for none.</summary>
        public uint BatchId;

        /// <summary>The rise of a board hung on an arm or a beam (TsItem.Rise; format 6, 0.12.73); 0 for none.</summary>
        public float Rise;

        /// <summary>The clearance of a hung support (TsItem.Clearance; format 7, 0.12.78); 0 for the support's own.</summary>
        public float Clearance;

        public int Anchor = -1;

        /// <summary>Same: placed; otherwise put away, not drawn, until the player places or deletes it (DESIGN-save-data.md 4.5).</summary>
        public AnchorChange Unmatched;
    }

    /// <summary>The kerb styles of a road (KerbStyle values of its two sides), with its anchor; Unmatched as for an item.</summary>
    public sealed class StoredKerb
    {
        public int Anchor;
        public byte Left, Right;
        public AnchorChange Unmatched;
    }

    /// <summary>An extension's data in a record (TsExtension.RegisterSection): the version of its format that wrote it, and its bytes.</summary>
    public sealed class StoredSection
    {
        public int Version;
        public byte[] Data;
    }

    /// <summary>A record written by a newer format than this mod reads: not read, and not written over (DESIGN-save-data.md 4.10).</summary>
    public sealed class NewerRecordException : Exception
    {
        public NewerRecordException(int format) : base("the record's format " + format + " is newer than this mod's " + SaveRecord.Format) { }
    }

    /// <summary>
    /// What one save holds of this mod, kept beside it and not in it (0.12.38, DESIGN-save-data.md 4.2): which save and city it belongs to, the road anchors, the items, the kerb styles and the
    /// extensions' sections. A binary file: the magic, the format, then the parts in order. Pure.
    /// </summary>
    public sealed class SaveRecord
    {
        /// <summary>
        /// 2 (0.12.39): the roads' prefab names are theirs (0.12.38 wrote format 1, read in the Serialize phase after the game had renumbered its prefabs: another prefab's name). 3 (0.12.41): each
        /// extension's section has its version. 4 (0.12.57): each item has its support's style (TsItem.Style). 5 (0.12.67): each item has the batch that wrote it (TsItem.BatchId);
        /// a record is written as 5 only when an item has one, so that a save with no batch item stays readable by the mod of 0.12.66 and before (which refuses a newer format and does not overwrite it).
        /// 6 (0.12.73): each item has the rise of its board on an arm or a beam (TsItem.Rise); written as 6 only when an item has one, so that a save with none stays readable by the mods of 0.12.67 to 0.12.72
        /// (which refuse a newer format and do not overwrite it), as it is by those before.
        /// 7 (0.12.78): each item has the clearance of its hung support (TsItem.Clearance); written as 7 only when an item has one (the same: a save with no clearance set stays readable by the mods before).
        /// </summary>
        public const int Format = 7;

        /// <summary>The format of a record that has a raised board and no clearance.</summary>
        public const int RiseFormat = 6;

        /// <summary>The format of a record that has no batch item, no raised board and no clearance (see Format).</summary>
        public const int PlainFormat = 4;

        /// <summary>The format of a record that has a batch item and no raised board.</summary>
        public const int BatchFormat = 5;
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("CDLTSREC");

        public string ModVersion = "", City = "", SaveId = "", SaveName = "", CityName = "";

        /// <summary>When the save was written (UTC ticks).</summary>
        public long Time;

        public readonly List<RoadAnchor> Anchors = new List<RoadAnchor>();
        public readonly List<StoredItem> Items = new List<StoredItem>();
        public readonly List<StoredKerb> Kerbs = new List<StoredKerb>();

        /// <summary>The extensions' own data (DESIGN-save-data.md 4.9), by extension id.</summary>
        public readonly Dictionary<string, StoredSection> Sections = new Dictionary<string, StoredSection>();

        /// <summary>Read from a record whose body was not read (ReadHeader).</summary>
        public int ItemCount, AnchorCount;

        public void Write(Stream stream) => Write(stream, Items.Exists(i => i.Clearance != 0f) ? Format : Items.Exists(i => i.Rise != 0f) ? RiseFormat : Items.Exists(i => i.BatchId != 0) ? BatchFormat : PlainFormat);

        /// <summary>As above in an older format (2 or more: what this mod wrote before; for the tests of reading it).</summary>
        internal void Write(Stream stream, int format)
        {
            using (var w = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                w.Write(Magic);
                w.Write(format);
                foreach (string s in new[] { ModVersion, City, SaveId, SaveName, CityName })
                    w.Write(s ?? "");
                w.Write(Time);
                w.Write(Items.Count);
                w.Write(Anchors.Count);
                foreach (RoadAnchor a in Anchors)
                {
                    w.Write(a.Prefab ?? "");
                    foreach (Vec3 p in new[] { a.A, a.B, a.C, a.D })
                        Write(w, p);
                    w.Write(a.Left);
                    w.Write(a.Right);
                    w.Write(a.General);
                    w.Write(a.StartDegree);
                    w.Write(a.EndDegree);
                }

                foreach (StoredItem i in Items)
                {
                    w.Write(i.TypeHash);
                    w.Write(i.EdgeT);
                    Write(w, i.Position);
                    w.Write(i.Yaw);
                    w.Write(i.Flags);
                    w.Write(i.SupportHash);
                    w.Write(i.Spec ?? "");
                    w.Write(i.Reach);
                    w.Write(i.Group);
                    w.Write(i.Slot);
                    w.Write(i.Lane);
                    w.Write(i.OffsetX);
                    w.Write(i.Turn);
                    w.Write(i.BaseReach);
                    w.Write(i.Anchor);
                    w.Write((byte)i.Unmatched);
                    if (format >= 4)
                        w.Write(i.Style);
                    if (format >= 5)
                        w.Write(i.BatchId);
                    if (format >= 6)
                        w.Write(i.Rise);
                    if (format >= 7)
                        w.Write(i.Clearance);
                }

                w.Write(Kerbs.Count);
                foreach (StoredKerb k in Kerbs)
                {
                    w.Write(k.Anchor);
                    w.Write(k.Left);
                    w.Write(k.Right);
                    w.Write((byte)k.Unmatched);
                }

                w.Write(Sections.Count);
                foreach (KeyValuePair<string, StoredSection> s in Sections)
                {
                    w.Write(s.Key);
                    if (format >= 3)
                        w.Write(s.Value.Version);
                    w.Write(s.Value.Data.Length);
                    w.Write(s.Value.Data);
                }
            }
        }

        /// <summary>The record in the stream (with its body unless headerOnly). Throws InvalidDataException for what is not one, NewerRecordException for a newer format.</summary>
        public static SaveRecord Read(Stream stream, bool headerOnly = false)
        {
            using (var r = new BinaryReader(stream, Encoding.UTF8, true))
            {
                byte[] magic = r.ReadBytes(Magic.Length);
                if (magic.Length != Magic.Length || !Same(magic, Magic))
                    throw new InvalidDataException("not a record of CDL:StreetScape (CDLC.TS)");
                int format = r.ReadInt32();
                if (format > Format)
                    throw new NewerRecordException(format);
                var record = new SaveRecord { ModVersion = r.ReadString(), City = r.ReadString(), SaveId = r.ReadString(), SaveName = r.ReadString(), CityName = r.ReadString(), Time = r.ReadInt64() };
                record.ItemCount = r.ReadInt32();
                record.AnchorCount = r.ReadInt32();
                if (headerOnly)
                    return record;
                Check(record.AnchorCount, record.ItemCount);
                for (int n = 0; n < record.AnchorCount; n++)
                {
                    string prefab = r.ReadString();
                    var a = new RoadAnchor { Prefab = format >= 2 ? prefab : "", A = Read(r), B = Read(r), C = Read(r), D = Read(r) };
                    a.Left = r.ReadUInt32();
                    a.Right = r.ReadUInt32();
                    a.General = r.ReadUInt32();
                    a.StartDegree = r.ReadByte();
                    a.EndDegree = r.ReadByte();
                    record.Anchors.Add(a);
                }

                for (int n = 0; n < record.ItemCount; n++)
                {
                    record.Items.Add(new StoredItem
                    {
                        TypeHash = r.ReadUInt32(), EdgeT = r.ReadSingle(), Position = Read(r), Yaw = r.ReadSingle(), Flags = r.ReadByte(), SupportHash = r.ReadUInt32(), Spec = r.ReadString(),
                        Reach = r.ReadSingle(), Group = r.ReadUInt32(), Slot = r.ReadByte(), Lane = r.ReadByte(), OffsetX = r.ReadSingle(), Turn = r.ReadSingle(), BaseReach = r.ReadSingle(),
                        Anchor = AnchorIndex(r.ReadInt32(), record.AnchorCount), Unmatched = (AnchorChange)r.ReadByte(), Style = format >= 4 ? r.ReadByte() : (byte)0,
                        BatchId = format >= 5 ? r.ReadUInt32() : 0u, Rise = format >= 6 ? Assembly.ClampRise(r.ReadSingle()) : 0f, Clearance = format >= 7 ? SupportLayouts.ClampClearance(r.ReadSingle()) : 0f
                    });
                }

                int kerbs = r.ReadInt32();
                Check(kerbs, 0);
                for (int n = 0; n < kerbs; n++)
                    record.Kerbs.Add(new StoredKerb { Anchor = AnchorIndex(r.ReadInt32(), record.AnchorCount), Left = r.ReadByte(), Right = r.ReadByte(), Unmatched = (AnchorChange)r.ReadByte() });
                int sections = r.ReadInt32();
                Check(sections, 0);
                for (int n = 0; n < sections; n++)
                {
                    string key = r.ReadString();
                    int version = format >= 3 ? r.ReadInt32() : 0;
                    int length = r.ReadInt32();
                    if (length < 0 || length > stream.Length)
                        throw new InvalidDataException("a section longer than the record");
                    record.Sections[key] = new StoredSection { Version = version, Data = r.ReadBytes(length) };
                }

                return record;
            }
        }

        /// <summary>
        /// How well the record fits the roads as they are (0 to 1): of the anchors its placed items and kerb styles stand on, the share that are the same now (check: an anchor against the road
        /// that ends where it ended). 1 for a record with none.
        /// </summary>
        public float Score(Func<RoadAnchor, AnchorChange> check)
        {
            var used = new HashSet<int>();
            foreach (StoredItem i in Items)
            {
                if (i.Anchor >= 0 && i.Unmatched == AnchorChange.Same)
                    used.Add(i.Anchor);
            }

            foreach (StoredKerb k in Kerbs)
            {
                if (k.Anchor >= 0 && k.Unmatched == AnchorChange.Same)
                    used.Add(k.Anchor);
            }

            if (used.Count == 0)
                return 1f;
            int same = 0;
            foreach (int a in used)
                same += check(Anchors[a]) == AnchorChange.Same ? 1 : 0;
            return same / (float)used.Count;
        }

        private static void Check(int count, int more)
        {
            if (count < 0 || more < 0 || count > 10_000_000 || more > 10_000_000)
                throw new InvalidDataException("a count out of range");
        }

        private static int AnchorIndex(int index, int count)
        {
            if (index < -1 || index >= count)
                throw new InvalidDataException("an anchor out of range");
            return index;
        }

        private static bool Same(byte[] a, byte[] b)
        {
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    return false;
            }

            return true;
        }

        private static void Write(BinaryWriter w, Vec3 p)
        {
            w.Write(p.X);
            w.Write(p.Y);
            w.Write(p.Z);
        }

        private static Vec3 Read(BinaryReader r) => new Vec3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    }

    /// <summary>
    /// The records on disk (0.12.38, DESIGN-save-data.md 4.2): a folder for each city (its id, the game's sessionGuid), a file for each save written with the mod (its own id). A file is written to a
    /// temporary name and renamed over the old one; of the records of one save name the newest four are kept (the one written and the three before it). Pure: plain files.
    /// </summary>
    public static class SaveStore
    {
        public const string Extension = ".tsd";
        public const int KeptPerName = 4;

        public static string FolderOf(string root, string city) => Path.Combine(root, Safe(city));

        public static string PathOf(string root, string city, string saveId) => Path.Combine(FolderOf(root, city), Safe(saveId) + Extension);

        /// <summary>Writes the record and keeps the newest KeptPerName of its save name; returns the files removed.</summary>
        public static List<string> Write(string root, SaveRecord record)
        {
            string folder = FolderOf(root, record.City);
            Directory.CreateDirectory(folder);
            string path = PathOf(root, record.City, record.SaveId), temporary = path + ".tmp";
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write))
                record.Write(file);
            if (File.Exists(path))
                File.Replace(temporary, path, null);
            else
                File.Move(temporary, path);

            var removed = new List<string>();
            var same = new List<(string Path, long Time)>();
            foreach ((string file, SaveRecord header) in Headers(folder))
            {
                if (header.SaveName == record.SaveName)
                    same.Add((file, header.Time));
            }

            same.Sort((a, b) => b.Time.CompareTo(a.Time));
            for (int i = KeptPerName; i < same.Count; i++)
            {
                File.Delete(same[i].Path);
                removed.Add(same[i].Path);
            }

            return removed;
        }

        /// <summary>
        /// How much later than a save's own write time a record may have been made and still be one of its past (s): the game writes the save's file 1 to 2 s after this mod made the record (the
        /// owner's 测试 54, 55 and 56: 1, 1 and 2 s), and the records of two saves made in a row are 10 s apart at the least (测试 39 and 40).
        /// </summary>
        public const long WriteSkewTicks = 5 * TimeSpan.TicksPerSecond;

        /// <summary>
        /// 0.12.102 (the owner, 6 Nov: "如果你另外命名了一个存档 在里面编辑了路牌 在你重载这个存档的其他原本存档时 那个路牌会出现再其他存档里"): the records of a city (newest first) that a save with no record of
        /// its own can be a continuation of, by the time. A save written while the mod was off goes on from a record made before it; the items of a record made AFTER it cannot be in it, and a save
        /// of the same city made long before (an original of the city, from before this store, or one never edited) was given the newest record that fitted its roads (95 %), the signs of the
        /// save made last. savedTicks: the save's write time (UTC ticks), 0 when it is not known: then none can be told apart and none is given. later: how many records were left out for being
        /// later (all of them for an unknown time). The order is kept.
        /// </summary>
        public static List<(string Path, SaveRecord Header)> Ancestors(IList<(string Path, SaveRecord Header)> records, long savedTicks, out int later)
        {
            var kept = new List<(string, SaveRecord)>();
            later = 0;
            foreach ((string path, SaveRecord header) in records)
            {
                if (savedTicks > 0 && header.Time <= savedTicks + WriteSkewTicks)
                    kept.Add((path, header));
                else
                    later++;
            }

            return kept;
        }

        /// <summary>The headers of a city's records, newest first; files that are not records, or of a newer format, are left out (and left alone).</summary>
        public static List<(string Path, SaveRecord Header)> Headers(string folder)
        {
            var found = new List<(string, SaveRecord)>();
            if (!Directory.Exists(folder))
                return found;
            foreach (string file in Directory.GetFiles(folder, "*" + Extension))
            {
                try
                {
                    using (var stream = File.OpenRead(file))
                        found.Add((file, SaveRecord.Read(stream, true)));
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is NewerRecordException || ex is UnauthorizedAccessException)
                {
                }
            }

            found.Sort((a, b) => b.Item2.Time.CompareTo(a.Item2.Time));
            return found;
        }

        public static SaveRecord Load(string path)
        {
            using (var stream = File.OpenRead(path))
                return SaveRecord.Read(stream);
        }

        /// <summary>A name that is a file name: what is not a letter, a digit, '-' or '_' becomes '_'.</summary>
        private static string Safe(string name)
        {
            var safe = new StringBuilder();
            foreach (char c in string.IsNullOrEmpty(name) ? "none" : name)
                safe.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return safe.ToString();
        }
    }
}
