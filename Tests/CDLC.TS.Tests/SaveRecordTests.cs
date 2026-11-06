using System;
using System.Collections.Generic;
using System.IO;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The save-safe records of 0.12.38 (SaveRecord, SaveStore, RoadAnchor; DESIGN-save-data.md): every field back as written, the roads compared, the files written whole and kept.</summary>
    internal static class SaveRecordTests
    {
        public static void RunAll()
        {
            Program.Run("save records: every field back as written", RoundTrip);
            Program.Run("save records: what is not a record, a newer format, a broken one", Refused);
            Program.Run("save records: a road compared with the one there now", Anchors);
            Program.Run("save records: how well a record fits the roads", Score);
            Program.Run("save records: the files on disk, written whole, the newest kept", Store);
            Program.Run("save records: a record of 0.12.38 (format 1) read with its prefab names unknown", FormatOne);
            Program.Run("save records: the support's style kept (format 4, 0.12.57), galvanised from format 3", Styles);
            Program.Run("save records: the batch of an item (format 5, 0.12.67), written only when a batch item is there", Batches);
            Program.Run("save records: a save with no record of its own is given only a record made before it (0.12.102)", Ancestry);
        }

        private static long Utc(int month, int day, int hour, int minute, int second) => new DateTime(2026, month, day, hour, minute, second, DateTimeKind.Utc).Ticks;

        private static (string Path, SaveRecord Header) Header(string name, long time) => ("\\" + name + ".tsd", new SaveRecord { SaveName = name, SaveId = name, Time = time });

        /// <summary>The owner's store (the headers of his city's records, 6 Nov): a few of the 29 with their real times, newest first as SaveStore.Headers gives them.</summary>
        private static List<(string Path, SaveRecord Header)> OwnersStore() => new List<(string, SaveRecord)>
        {
            Header("测试 56", Utc(11, 6, 2, 33, 14)),
            Header("测试 55", Utc(11, 5, 17, 10, 46)),
            Header("测试 54", Utc(11, 5, 16, 19, 52)),
            Header("测试 53", Utc(11, 5, 13, 14, 15)),
            Header("测试 40", Utc(11, 3, 12, 19, 8)),
            Header("测试 39", Utc(11, 3, 12, 18, 58)),
            Header("测试 28", Utc(10, 30, 7, 29, 0)),
            Header("测试 27", Utc(10, 27, 17, 39, 45)),
            Header("SaveTest1024", Utc(10, 27, 17, 6, 33)),
        };

        private static string Names(List<(string Path, SaveRecord Header)> list) => string.Join(", ", list.ConvertAll(r => r.Header.SaveName));

        private static void Ancestry()
        {
            List<(string Path, SaveRecord Header)> store = OwnersStore();
            // His original 测试 20 (written 22 Oct 13:30 local = 05:30 UTC, before the store existed): the records are all later: none is given. It was given the newest that fitted.
            List<(string Path, SaveRecord Header)> none = SaveStore.Ancestors(store, Utc(10, 22, 5, 30, 17), out int later);
            Program.Check(none.Count == 0 && later == store.Count, $"an original from before the store: no record is given, {later} of {store.Count} left out");
            // 测试 20 of 27 Oct, 02:33 UTC: the first record (17:06) is after it too.
            Program.Check(SaveStore.Ancestors(store, Utc(10, 27, 2, 33, 23), out _).Count == 0, "an original of 27 Oct 02:33: none");
            // A save written 31 Oct 12:00 UTC without the mod (no record of its own): the records before it, in the order they were: 测试 28, 测试 27, SaveTest1024.
            List<(string Path, SaveRecord Header)> mid = SaveStore.Ancestors(store, Utc(10, 31, 12, 0, 0), out later);
            Program.Check(Names(mid) == "测试 28, 测试 27, SaveTest1024" && later == 6, $"a save made without the mod on 31 Oct: the records before it, newest first ({Names(mid)}), {later} left out");
            // A save of the newest moment: every record is a past of it.
            List<(string Path, SaveRecord Header)> all = SaveStore.Ancestors(store, Utc(11, 6, 2, 33, 16), out later);
            Program.Check(all.Count == store.Count && later == 0 && Names(all) == Names(store), "a save written after all of them: all of them, in the same order");
            // The skew: the file is written 1 to 2 s after the record; 5 s later is the most a record may be and still count.
            long saved = Utc(11, 3, 12, 19, 0);
            Program.Check(Names(SaveStore.Ancestors(store, saved, out _)).StartsWith("测试 39"), "测试 39 was written at 12:18:58 for a save of 12:19:00: kept; 测试 40 (12:19:08) is later and left out");
            Program.Check(Names(SaveStore.Ancestors(store, saved, out _)).Split(',')[0] == "测试 39", "the first of them is 测试 39, not 测试 40");
            Program.Check(SaveStore.Ancestors(new[] { Header("a", saved + 5 * TimeSpan.TicksPerSecond) }, saved, out _).Count == 1, "a record 5 s after the save: kept (the skew)");
            Program.Check(SaveStore.Ancestors(new[] { Header("a", saved + 5 * TimeSpan.TicksPerSecond + 1) }, saved, out later).Count == 0 && later == 1, "a record 5 s and one tick after: left out");
            Program.Check(SaveStore.Ancestors(new[] { Header("a", saved - 3600L * TimeSpan.TicksPerSecond) }, saved, out _).Count == 1, "a record an hour before: kept");
            Program.Check(SaveStore.WriteSkewTicks == 5 * TimeSpan.TicksPerSecond, "the skew is 5 s");
            // The time of the save is not known: none is given and every one is counted as left out.
            Program.Check(SaveStore.Ancestors(store, 0, out later).Count == 0 && later == store.Count, "a save whose write time is not known: none is given");
            Program.Check(SaveStore.Ancestors(store, -1, out later).Count == 0 && later == store.Count, "a negative time is not known either");
            // Not known is not "before everything": a record whose own time was never set (0) is not given to a save of unknown time either.
            Program.Check(SaveStore.Ancestors(new[] { Header("unset", 0) }, 0, out later).Count == 0 && later == 1 && SaveStore.Ancestors(new[] { Header("unset", 0) }, -5, out _).Count == 0, "a record with no time and a save with none: not given");
            // Nothing to look at, and the list handed in is not changed.
            Program.Check(SaveStore.Ancestors(new List<(string, SaveRecord)>(), Utc(11, 6, 3, 0, 0), out later).Count == 0 && later == 0, "no records: none, none left out");
            Program.Check(store.Count == 9 && store[0].Header.SaveName == "测试 56", "the list handed in is as it was");
            // Written to disk and read back (the way the system gets them): the headers keep their time to the tick, and the order is newest first.
            string folder = Path.Combine(System.AppContext.BaseDirectory, "tmp", "cdlts-ancestry-" + Guid.NewGuid().ToString("N"));
            try
            {
                foreach ((string name, long time) in new[] { ("older", Utc(11, 1, 10, 0, 0)), ("newer", Utc(11, 4, 10, 0, 0)), ("newest", Utc(11, 6, 10, 0, 0)) })
                {
                    var record = new SaveRecord { City = "c1", SaveId = name, SaveName = name, CityName = "城", Time = time, ModVersion = "0.12.102" };
                    SaveStore.Write(folder, record);
                }

                List<(string Path, SaveRecord Header)> onDisk = SaveStore.Headers(SaveStore.FolderOf(folder, "c1"));
                Program.Check(Names(onDisk) == "newest, newer, older", $"the headers on disk, newest first: {Names(onDisk)}");
                List<(string Path, SaveRecord Header)> before = SaveStore.Ancestors(onDisk, Utc(11, 2, 0, 0, 0), out later);
                Program.Check(Names(before) == "older" && later == 2, $"a save of 2 Nov 00:00 is given only the one before it: {Names(before)}, {later} left out");
            }
            finally
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
        }

        private static RoadAnchor Road(float x0, string prefab = "Medium Road") => new RoadAnchor
        {
            Prefab = prefab, A = new Vec3(x0, 10f, 0f), B = new Vec3(x0 + 10f, 10f, 1f), C = new Vec3(x0 + 20f, 10f, 1f), D = new Vec3(x0 + 30f, 10f, 0f), Left = 0x10, Right = 0, General = 0x2,
            StartDegree = 3, EndDegree = 1
        };

        private static SaveRecord Sample()
        {
            var r = new SaveRecord { ModVersion = "0.12.38", City = "8b1a9953-c461-4f8a-9e1c-123456789abc", SaveId = "a1b2c3", SaveName = "我的城市", CityName = "连云港", Time = 638660000000000000L };
            r.Anchors.Add(Road(0f));
            r.Anchors.Add(Road(100f, "Large Road"));
            r.Items.Add(new StoredItem
            {
                TypeHash = 0xDEADBEEF, EdgeT = 0.25f, Position = new Vec3(1.5f, 10.2f, -3f), Yaw = 1.2f, Flags = 9, SupportHash = 77, Spec = "to1=连云港市;dist=500", Reach = 12f, Group = 4242, Slot = 2,
                Lane = 254, OffsetX = -1.25f, Turn = 0.5f, BaseReach = 10f, Anchor = 1
            });
            r.Items.Add(new StoredItem { TypeHash = 5, Position = new Vec3(0f, 0f, 0f), Anchor = -1 });
            r.Items.Add(new StoredItem { TypeHash = 6, Position = new Vec3(2f, 0f, 0f), Anchor = 0, Unmatched = AnchorChange.Upgraded });
            r.Kerbs.Add(new StoredKerb { Anchor = 0, Left = 1, Right = 3 });
            r.Sections["CDLC.TS.RIE"] = new StoredSection { Version = 7, Data = new byte[] { 1, 2, 3, 250 } };
            return r;
        }

        private static SaveRecord Again(SaveRecord r, bool headerOnly = false)
        {
            var stream = new MemoryStream();
            r.Write(stream);
            stream.Position = 0;
            return SaveRecord.Read(stream, headerOnly);
        }

        private static void RoundTrip()
        {
            SaveRecord a = Sample(), b = Again(a);
            Program.Check(b.ModVersion == a.ModVersion && b.City == a.City && b.SaveId == a.SaveId && b.SaveName == a.SaveName && b.CityName == a.CityName && b.Time == a.Time, "the header");
            Program.Check(b.Anchors.Count == 2 && b.Items.Count == 3 && b.Kerbs.Count == 1 && b.Sections.Count == 1, "the counts");
            RoadAnchor x = a.Anchors[1], y = b.Anchors[1];
            Program.Check(y.Prefab == x.Prefab && y.A.Equals(x.A) && y.B.Equals(x.B) && y.C.Equals(x.C) && y.D.Equals(x.D) && y.Left == x.Left && y.Right == x.Right && y.General == x.General
                && y.StartDegree == x.StartDegree && y.EndDegree == x.EndDegree, "an anchor");
            StoredItem i = a.Items[0], j = b.Items[0];
            Program.Check(j.TypeHash == i.TypeHash && j.EdgeT == i.EdgeT && j.Position.Equals(i.Position) && j.Yaw == i.Yaw && j.Flags == i.Flags && j.SupportHash == i.SupportHash && j.Spec == i.Spec
                && j.Reach == i.Reach && j.Group == i.Group && j.Slot == i.Slot && j.Lane == i.Lane && j.OffsetX == i.OffsetX && j.Turn == i.Turn && j.BaseReach == i.BaseReach && j.Anchor == i.Anchor
                && j.Unmatched == i.Unmatched, "an item, every field (Chinese settings too)");
            Program.Check(b.Items[1].Anchor == -1 && b.Items[2].Unmatched == AnchorChange.Upgraded, "an item at no road, an item put away unmatched");
            Program.Check(b.Kerbs[0].Anchor == 0 && b.Kerbs[0].Left == 1 && b.Kerbs[0].Right == 3, "a kerb style");
            Program.Check(b.Sections["CDLC.TS.RIE"].Data.Length == 4 && b.Sections["CDLC.TS.RIE"].Data[3] == 250 && b.Sections["CDLC.TS.RIE"].Version == 7, "an extension's section and its version");
            SaveRecord h = Again(a, true);
            Program.Check(h.SaveName == a.SaveName && h.ItemCount == 3 && h.AnchorCount == 2 && h.Items.Count == 0, "the header alone");
        }

        private static void Refused()
        {
            bool Throws<T>(byte[] bytes) where T : Exception
            {
                try
                {
                    SaveRecord.Read(new MemoryStream(bytes));
                    return false;
                }
                catch (T)
                {
                    return true;
                }
            }

            Program.Check(Throws<InvalidDataException>(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }), "not a record");
            var stream = new MemoryStream();
            Sample().Write(stream);
            byte[] bytes = stream.ToArray();
            byte[] newer = (byte[])bytes.Clone();
            BitConverter.GetBytes(SaveRecord.Format + 1).CopyTo(newer, 8);
            Program.Check(Throws<NewerRecordException>(newer), "a newer format is refused, not misread");
            byte[] cut = new byte[bytes.Length / 2];
            Array.Copy(bytes, cut, cut.Length);
            Program.Check(Throws<EndOfStreamException>(cut), "a record cut short");
            // An item's anchor out of range (the last item's anchor field set to 9).
            SaveRecord bad = Sample();
            bad.Items[2].Anchor = 9;
            var s2 = new MemoryStream();
            bad.Write(s2);
            Program.Check(Throws<InvalidDataException>(s2.ToArray()), "an anchor out of range");
        }

        private static void Anchors()
        {
            RoadAnchor was = Road(0f);
            Program.Check(was.Compare(was) == AnchorChange.Same, "the same road");
            RoadAnchor nudged = was;
            nudged.B = new Vec3(was.B.X + 0.004f, was.B.Y, was.B.Z);
            Program.Check(was.Compare(nudged) == AnchorChange.Same, "within a centimetre: the same");
            RoadAnchor moved = was;
            moved.B = new Vec3(was.B.X, was.B.Y, was.B.Z + 2f);
            Program.Check(was.Compare(moved) == AnchorChange.Moved, "its curve other: moved");
            RoadAnchor gone = was;
            gone.D = new Vec3(15f, 10f, 0f);
            Program.Check(was.Compare(gone) == AnchorChange.Gone, "ends elsewhere (split, moved): gone");
            RoadAnchor replaced = was;
            replaced.Prefab = "Large Road";
            Program.Check(was.Compare(replaced) == AnchorChange.Replaced, "another prefab: replaced");
            RoadAnchor upgraded = was;
            upgraded.Left = 0x11;
            Program.Check(was.Compare(upgraded) == AnchorChange.Upgraded, "other upgrades: upgraded");
            RoadAnchor junction = was;
            junction.StartDegree = 4;
            Program.Check(was.Compare(junction) == AnchorChange.Junction, "a road more at its end: the junction changed");
            Program.Check(was.Key == RoadAnchor.EndsKey(new Vec3(0.001f, 10f, 0f), new Vec3(30f, 10.002f, 0f)) && was.Key != gone.Key, "found by its ends to the centimetre");
            Program.Near(was.Middle.X, 15f, 1e-4f, "its middle");
        }

        private static void Score()
        {
            var r = new SaveRecord();
            for (int n = 0; n < 20; n++)
            {
                r.Anchors.Add(Road(n * 100f));
                r.Items.Add(new StoredItem { Anchor = n });
            }

            r.Items.Add(new StoredItem { Anchor = -1 });
            Program.Near(r.Score(a => AnchorChange.Same), 1f, 1e-6f, "all the same: 1");
            Program.Near(r.Score(a => a.A.X >= 1900f ? AnchorChange.Upgraded : AnchorChange.Same), 0.95f, 1e-6f, "one road of twenty changed: 0.95");
            // Items already put away do not count: their roads were changed before.
            r.Items[0].Unmatched = AnchorChange.Gone;
            Program.Near(r.Score(a => a.A.X < 1f ? AnchorChange.Gone : AnchorChange.Same), 1f, 1e-6f, "an item put away before does not lower it");
            Program.Near(new SaveRecord().Score(a => AnchorChange.Gone), 1f, 1e-6f, "a record of nothing fits");
        }

        /// <summary>0.12.38 wrote another prefab's name for every road (read after the game renumbered its prefabs for the writing): its records compare the roads without them.</summary>
        private static void Styles()
        {
            SaveRecord written = Sample();
            written.Items[0].Style = new SupportStyle { Finish = SupportFinish.Graphite, BlackBack = true }.Packed;
            written.Items[1].Style = new SupportStyle { Finish = SupportFinish.LightGrey }.Packed;
            SaveRecord back = Again(written);
            Program.Check(back.Items[0].Style == written.Items[0].Style && back.Items[1].Style == written.Items[1].Style && back.Items[2].Style == 0, "each item's style read back");
            var stream = new MemoryStream();
            written.Write(stream, 3);
            SaveRecord three = SaveRecord.Read(new MemoryStream(stream.ToArray()));
            Program.Check(three.Items.Count == written.Items.Count && three.Items.TrueForAll(i => i.Style == 0) && three.Items[1].Spec == written.Items[1].Spec && three.Sections.Count == written.Sections.Count,
                "a record of format 3: every item galvanised, nothing else lost");
            SupportStyle s = SupportStyle.Of(written.Items[0].Style);
            Program.Check(s.Finish == SupportFinish.Graphite && s.BlackBack && !s.BlackBacks && s.MetalColour == SolidColour.Graphite, "graphite with black backs: the backs are the steel's black");
            Program.Check(new SupportStyle { BlackBack = true }.BlackBacks && SupportStyle.Of(0).MetalColour == SolidColour.Metal, "galvanised with black backs: the backs apart");
        }

        /// <summary>0.12.67: an item written by a batch keeps its batch id; a record with no batch item is still format 4, so the mod of 0.12.66 reads it (a newer format it refuses).</summary>
        private static void Batches()
        {
            SaveRecord plain = Sample();
            var stream = new MemoryStream();
            plain.Write(stream);
            byte[] bytes = stream.ToArray();
            Program.Check(BitConverter.ToInt32(bytes, 8) == SaveRecord.PlainFormat && SaveRecord.PlainFormat == 4, "a record with no batch item is written as format 4");
            Program.Check(SaveRecord.BatchFormat == 5, "the format of a record with a batch item and no raised board is 5");

            SaveRecord written = Sample();
            written.Items[0].BatchId = 0xCAFEF00D;
            written.Items[2].BatchId = 1;
            stream = new MemoryStream();
            written.Write(stream);
            bytes = stream.ToArray();
            Program.Check(BitConverter.ToInt32(bytes, 8) == 5, "a record with a batch item is written as format 5");
            SaveRecord back = SaveRecord.Read(new MemoryStream(bytes));
            Program.Check(back.Items[0].BatchId == 0xCAFEF00D && back.Items[1].BatchId == 0 && back.Items[2].BatchId == 1, "each item's batch read back");
            Program.Check(back.Items[0].Style == written.Items[0].Style && back.Items[0].Spec == written.Items[0].Spec && back.Items[2].Unmatched == AnchorChange.Upgraded && back.Kerbs.Count == 1 && back.Sections.Count == 1,
                "what follows the batch id is not shifted");

            stream = new MemoryStream();
            written.Write(stream, 4);
            SaveRecord four = SaveRecord.Read(new MemoryStream(stream.ToArray()));
            Program.Check(four.Items.Count == 3 && four.Items.TrueForAll(i => i.BatchId == 0) && four.Items[0].Spec == written.Items[0].Spec && four.Kerbs.Count == 1, "read as format 4: no batch, nothing else lost");
            Program.Check(SaveRecord.Read(new MemoryStream(bytes), true).ItemCount == 3, "the header of a format 5 record reads");
        }

        private static void FormatOne()
        {
            // 0.12.38 to 0.12.40 wrote no section (no extension could register one).
            var stream = new MemoryStream();
            SaveRecord written = Sample();
            written.Sections.Clear();
            written.Write(stream, 2);
            byte[] bytes = stream.ToArray();
            BitConverter.GetBytes(1).CopyTo(bytes, 8);
            SaveRecord old = SaveRecord.Read(new MemoryStream(bytes));
            Program.Check(old.Anchors.TrueForAll(a => a.Prefab == ""), "its prefab names unknown");
            RoadAnchor now = Road(100f, "Large Road");
            Program.Check(old.Anchors[1].Compare(now) == AnchorChange.Same, "a road as it was is the same, whatever its prefab");
            now.Left = 0x11;
            Program.Check(old.Anchors[1].Compare(now) == AnchorChange.Upgraded, "its other fields still compared");
            Program.Check(Again(Sample()).Anchors[1].Prefab == "Large Road", "a record of this format keeps its names");
        }

        private static void Store()
        {
            string root = Path.Combine(System.AppContext.BaseDirectory, "tmp", "cdlts-records-" + Guid.NewGuid().ToString("N"));
            try
            {
                string city = "8b1a9953-c461-4f8a-9e1c-123456789abc";
                for (int n = 0; n < 6; n++)
                {
                    SaveRecord r = Sample();
                    r.City = city;
                    r.SaveId = "save" + n;
                    r.Time = 1000 + n;
                    r.SaveName = n < 5 ? "A" : "B";
                    SaveStore.Write(root, r);
                }

                List<(string Path, SaveRecord Header)> headers = SaveStore.Headers(SaveStore.FolderOf(root, city));
                Program.Check(headers.Count == 5, "of five records of one name the newest four are kept, and the other name's: " + headers.Count);
                Program.Check(headers[0].Header.SaveId == "save5" && headers[headers.Count - 1].Header.SaveId == "save1", "newest first, the oldest of A gone");
                SaveRecord back = SaveStore.Load(SaveStore.PathOf(root, city, "save3"));
                Program.Check(back.Items.Count == 3 && back.Items[0].Spec == "to1=连云港市;dist=500", "a record read back whole");
                // Written again under its id: replaced whole, nothing left behind.
                SaveRecord again = Sample();
                again.City = city;
                again.SaveId = "save3";
                again.Time = 1003;
                again.SaveName = "A";
                again.Items.Clear();
                SaveStore.Write(root, again);
                Program.Check(SaveStore.Load(SaveStore.PathOf(root, city, "save3")).Items.Count == 0, "written over whole");
                Program.Check(Directory.GetFiles(SaveStore.FolderOf(root, city), "*.tmp").Length == 0, "no temporary file left");
                // A file that is not a record, and one of a newer format, are left alone.
                File.WriteAllText(Path.Combine(SaveStore.FolderOf(root, city), "junk" + SaveStore.Extension), "junk");
                Program.Check(SaveStore.Headers(SaveStore.FolderOf(root, city)).Count == 5 && File.Exists(Path.Combine(SaveStore.FolderOf(root, city), "junk" + SaveStore.Extension)), "a file not a record is left alone");
                Program.Check(SaveStore.Headers(Path.Combine(root, "nowhere")).Count == 0, "no folder, no records");
                Program.Check(SaveStore.FolderOf(root, "a/b:c") == Path.Combine(root, "a_b_c"), "ids made file names");
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }
    }
}
