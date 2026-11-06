using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The gate in front of the synchronous mesh read of the game's road pieces (0.12.78: the owner's loading screen that never ended). The game's GeometryAsset cannot be made here; what is tested is the
    /// decision from its state (every combination, and the states the game's own loader and a synchronous read leave: the contract is written at GeometryState), the cache rule of what was read (a busy
    /// piece is not kept), the wait for busy pieces and the redo of what was made without them, composed the way AreaFitSystem and KerbSystem use them.
    /// </summary>
    internal static class GeometryGateTests
    {
        public static void RunAll()
        {
            Program.Run("geometry gate: a read is made only of an idle asset or one whose meshes someone holds (all 64 states)", EveryState);
            Program.Run("geometry gate: the states the game's loader and our own read leave", NamedStates);
            Program.Run("geometry gate: the state says what it is in the log", Describe);
            Program.Run("face book: a piece that was read, or has nothing to read, is kept; a busy one is not", Keeping);
            Program.Run("face book: a busy piece becomes known once, and the epoch moves once", Epochs);
            Program.Run("face book: Poll tries each busy piece once and leaves the ones still busy", Polling);
            Program.Run("kerb hold: reads wait while pieces are busy, for a time, not for ever", Holding);
            Program.Run("provisional ledger: what was made without a piece is handed back once when one becomes known", Ledger);
            Program.Run("kerb reads: items made while a piece was busy are made again, the others are not; the wait ends when the pieces are known", Composed);
        }

        private static GeometryState State(bool scheduled = false, bool started = false, bool done = false, bool header = false, bool data = false, bool meshes = false) =>
            new GeometryState(scheduled, started, done, header, data, meshes);

        private static void EveryState()
        {
            int safe = 0;
            for (int bits = 0; bits < 64; bits++)
            {
                GeometryState s = State((bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0, (bits & 8) != 0, (bits & 16) != 0, (bits & 32) != 0);
                bool idle = bits % 32 == 0;
                Program.Check(s.Idle == idle, "idle is no flag but the meshes: " + s);
                bool expected = idle || (bits & 32) != 0;
                Program.Check(GeometryGate.CanRead(s) == expected, "a read is made when the asset is idle or its meshes are held: " + s + " -> " + GeometryGate.CanRead(s));
                if (GeometryGate.CanRead(s))
                    safe++;
            }

            // The 32 states without held meshes: only one is idle; the 32 with them are all readable (Obtain then only counts).
            Program.Check(safe == 33, "33 of the 64 states are readable: " + safe);
        }

        private static void NamedStates()
        {
            Program.Check(GeometryGate.CanRead(State()), "an asset the game has not loaded, or has unloaded (UnloadPartial), is read");
            // BatchMeshSystem asked for it, the loader has not started it: ReleaseMeshes would reset Scheduled and the loader's completion would throw 'not scheduled'.
            Program.Check(!GeometryGate.CanRead(State(scheduled: true)), "requested, queued, not started: not read");
            // The loader started it and the header read is under way: ObtainMeshes would take and dispose the read handle and then throw 'required less data'.
            Program.Check(!GeometryGate.CanRead(State(scheduled: true, started: true)), "the header read is under way: not read");
            Program.Check(!GeometryGate.CanRead(State(scheduled: true, started: true, header: true, data: true)), "the body read is under way: not read");
            // What those two broke, which nothing will complete: still not read, so as not to make it worse.
            Program.Check(!GeometryGate.CanRead(State(scheduled: true, started: true, header: true)), "left by a read that took the loader's handle: not read");
            Program.Check(!GeometryGate.CanRead(State(started: true, header: true)), "left by a release that reset Scheduled: not read");
            // Finished, the data held until the game's unload: ReleaseMeshes would dispose what BatchMeshSystem has not taken yet.
            Program.Check(!GeometryGate.CanRead(State(done: true, header: true, data: true)), "done, the data held for the game: not read");
            Program.Check(!GeometryGate.CanRead(State(header: true)) && !GeometryGate.CanRead(State(data: true)), "a header or data that someone loaded: not read");
            // Someone holds the meshes (the ref count is above zero): Obtain returns them and Release counts them down.
            Program.Check(GeometryGate.CanRead(State(meshes: true)), "meshes held by someone: read");
            Program.Check(GeometryGate.CanRead(State(scheduled: true, started: true, meshes: true)), "meshes held while a load is under way: Obtain does not touch the load");
        }

        private static void Describe()
        {
            Program.Check(State().ToString() == "idle", "idle: " + State());
            Program.Check(State(scheduled: true, started: true).ToString() == "scheduled started", "the loader's: " + State(scheduled: true, started: true));
            Program.Check(State(started: true, header: true, data: true, done: true, meshes: true).ToString() == "started header data done meshes", "every flag but scheduled, in the order of the load: " + State(started: true, header: true, data: true, done: true, meshes: true));
        }

        private static (PieceRead, int) Read(PieceRead kind, int faces = 0) => (kind, faces);

        private static void Keeping()
        {
            var book = new FaceBook<string, int>();
            int reads = 0;
            Func<string, (PieceRead, int)> ready = k => { reads++; return Read(PieceRead.Ready, 7); };
            Program.Check(book.TryResolve("a", ready, out int faces) && faces == 7 && reads == 1, "a readable piece is read");
            Program.Check(book.TryResolve("a", ready, out faces) && faces == 7 && reads == 1, "and kept: not read again");
            Program.Check(book.TryGetKnown("a", out faces) && faces == 7, "and found without a read");

            Func<string, (PieceRead, int)> none = k => { reads++; return Read(PieceRead.NoData); };
            Program.Check(book.TryResolve("b", none, out faces) && faces == 0 && reads == 2, "a piece with nothing to read is a known piece");
            Program.Check(book.TryResolve("b", ready, out faces) && faces == 0 && reads == 2, "and its answer is kept: the old defect kept a failed read for the session, and this keeps only a real 'no data'");

            Func<string, (PieceRead, int)> busy = k => { reads++; return Read(PieceRead.Busy); };
            Program.Check(!book.TryResolve("c", busy, out faces) && faces == 0 && reads == 3, "a busy piece is not known");
            Program.Check(!book.TryGetKnown("c", out _) && book.Count == 2, "and is not kept");
            Program.Check(!book.TryResolve("c", busy, out _) && reads == 4, "it is asked again the next time (nothing was kept)");
            Program.Check(book.BusyNow == 1 && book.Deferred == 1, "once on the list, however often it is asked: " + book.BusyNow + ", " + book.Deferred);
            Program.Check(book.Epoch == 0, "nothing was out of date: a piece that was never known before is not news");

            // A city that is left while a piece is busy forgets it as well: the next city has its own.
            book.Clear();
            Program.Check(book.BusyNow == 0 && book.Deferred == 0 && book.Count == 0, "a new city starts with nothing kept, busy or counted: " + book.BusyNow + ", " + book.Deferred + ", " + book.Count);
        }

        private static void Epochs()
        {
            var book = new FaceBook<string, int>();
            bool loaded = false;
            Func<string, (PieceRead, int)> read = k => loaded ? Read(PieceRead.Ready, 3) : Read(PieceRead.Busy);
            Program.Check(!book.TryResolve("a", read, out _) && !book.TryResolve("b", read, out _), "two pieces are busy");
            Program.Check(book.BusyNow == 2 && book.Deferred == 2, "both listed");
            loaded = true;
            Program.Check(book.TryResolve("a", read, out int faces) && faces == 3, "one is read once the game is done");
            Program.Check(book.Epoch == 1 && book.BusyNow == 1 && book.Deferred == 2, "the epoch moved, one is left, and the count of those that were busy stays: " + book.Epoch + ", " + book.BusyNow + ", " + book.Deferred);
            Program.Check(book.TryResolve("a", read, out _) && book.Epoch == 1, "known again: no news");
            Program.Check(book.TryResolve("b", read, out _) && book.Epoch == 2 && book.BusyNow == 0, "the other: the epoch moves again");

            // A piece that turns out to have nothing to read is news as well: what was made without it took it as one surface, as it is.
            var other = new FaceBook<string, int>();
            bool empty = false;
            other.TryResolve("x", k => empty ? Read(PieceRead.NoData) : Read(PieceRead.Busy), out _);
            empty = true;
            Program.Check(other.TryResolve("x", k => Read(PieceRead.NoData), out _) && other.Epoch == 1, "a busy piece that has no data is known too, and moves the epoch");

            // A piece known at its first look moves nothing.
            var first = new FaceBook<string, int>();
            first.TryResolve("y", k => Read(PieceRead.Ready, 1), out _);
            Program.Check(first.Epoch == 0 && first.Deferred == 0, "known at the first look: no epoch, never deferred");

            book.Clear();
            Program.Check(book.Count == 0 && book.BusyNow == 0 && book.Deferred == 0 && book.Epoch == 2, "a new city forgets what was kept and listed, not the epoch");
        }

        private static void Polling()
        {
            var book = new FaceBook<string, int>();
            var ready = new HashSet<string>();
            int reads = 0;
            Func<string, (PieceRead, int)> read = k => { reads++; return ready.Contains(k) ? Read(PieceRead.Ready, 1) : Read(PieceRead.Busy); };
            Program.Check(book.Poll(read) == 0 && reads == 0, "nothing busy: nothing is read");
            foreach (string k in new[] { "a", "b", "c" })
                book.TryResolve(k, read, out _);
            Program.Check(reads == 3 && book.BusyNow == 3, "three busy");
            reads = 0;
            Program.Check(book.Poll(read) == 0 && reads == 3 && book.BusyNow == 3 && book.Epoch == 0, "each is tried once and stays busy");
            ready.Add("b");
            reads = 0;
            Program.Check(book.Poll(read) == 1 && reads == 3 && book.BusyNow == 2 && book.Epoch == 1 && book.TryGetKnown("b", out _) && !book.TryGetKnown("a", out _), "one became known: it is kept, the others are not, and the epoch moved");
            ready.Add("a");
            ready.Add("c");
            Program.Check(book.Poll(read) == 2 && book.BusyNow == 0 && book.Epoch == 3, "the rest");
            reads = 0;
            Program.Check(book.Poll(read) == 0 && reads == 0, "and a book with nothing busy costs nothing");
        }

        private static void Holding()
        {
            var hold = new KerbHold(20.0);
            Program.Check(!hold.Holds(0, 0.0), "no piece waits: no hold");
            Program.Check(hold.Holds(3, 10.0), "pieces wait: held");
            Program.Check(hold.Holds(2, 29.9), "still within 20 s of the first busy frame");
            Program.Check(!hold.Holds(2, 30.0) && hold.Expired, "20 s of it: the reads go on");
            Program.Check(hold.TakeExpiry() && !hold.TakeExpiry(), "the expiry is reported once");
            Program.Check(!hold.Holds(1, 31.0) && hold.Expired, "and stays let through while pieces are busy");
            Program.Check(!hold.Holds(0, 32.0) && !hold.Expired, "the pieces are known: the hold is over");
            Program.Check(hold.Holds(1, 100.0) && !hold.Holds(1, 120.0) && hold.TakeExpiry(), "a new run holds again for its own 20 s, and its expiry is reported again");
            hold.Reset();
            Program.Check(!hold.Expired && hold.Holds(1, 200.0) && hold.Holds(1, 219.0), "Reset starts over");
            // A run is of time since the first busy frame, not of calls: the loading screen draws a frame in about 90 ms.
            var slow = new KerbHold(20.0);
            Program.Check(slow.Holds(1, 0.0) && slow.Holds(1, 0.09) && slow.Holds(1, 0.18) && slow.Holds(1, 19.0), "frames are no measure");
        }

        private static void Ledger()
        {
            var ledger = new ProvisionalLedger<(int, int)>();
            var redo = new List<(int, int)>();
            Program.Check(!ledger.TakeRedo(0, redo) && redo.Count == 0, "nothing noted: nothing to do");
            ledger.Note((0, 1));
            ledger.Note((1, 2));
            ledger.Note((0, 1));
            Program.Check(ledger.Count == 2, "an item is noted once: " + ledger.Count);
            Program.Check(!ledger.TakeRedo(0, redo) && ledger.Count == 2, "no piece became known: they stay noted");
            Program.Check(ledger.TakeRedo(1, redo) && redo.Count == 2 && redo.Contains((0, 1)) && redo.Contains((1, 2)) && ledger.Count == 0, "a piece became known: both are handed back and out of the ledger");
            redo.Clear();
            Program.Check(!ledger.TakeRedo(1, redo) && !ledger.TakeRedo(2, redo) && redo.Count == 0, "handed back once; a later epoch has nothing");
            // What is noted after that waits for the next epoch: the ledger remembers the epoch it was last handed back at.
            ledger.Note((7, 7));
            Program.Check(!ledger.TakeRedo(2, redo) && ledger.Count == 1 && redo.Count == 0, "an item noted at epoch 2 is not handed back at epoch 2");
            Program.Check(ledger.TakeRedo(3, redo) && redo.Count == 1, "but at the next one");
            redo.Clear();
            ledger.Note((2, 3));
            ledger.Clear(5);
            Program.Check(ledger.Count == 0, "a new city empties it");
            // The first thing asked after Clear: a ledger that kept the old epoch would take this item for news.
            ledger.Note((9, 9));
            Program.Check(!ledger.TakeRedo(5, redo) && ledger.Count == 1 && redo.Count == 0, "an item noted in the new city waits for a piece of that city: its epoch is the one it started at");
            ledger.Note((4, 4));
            Program.Check(ledger.TakeRedo(6, redo) && redo.Count == 2, "and works from there");
        }

        /// <summary>The way AreaFitSystem.WatchKerbs and KerbSystem use them: a read meets pieces, items are made, the pieces are polled, the reads wait, the noted items are made again.</summary>
        private static void Composed()
        {
            var book = new FaceBook<string, int>();
            var hold = new KerbHold(20.0);
            var ledger = new ProvisionalLedger<string>();
            var loaded = new HashSet<string>();
            Func<string, (PieceRead, int)> read = k => loaded.Contains(k) ? Read(PieceRead.Ready, 1) : Read(PieceRead.Busy);
            var made = new List<string>();
            int provisionalReads = 0;

            // An item is made from the pieces it needs; one that is not known takes the read as provisional (SurfaceReader.ProvisionalReads moves) and the item is noted.
            Action<string, string[]> make = (item, pieces) =>
            {
                int before = provisionalReads;
                foreach (string piece in pieces)
                {
                    if (!book.TryResolve(piece, read, out _))
                        provisionalReads++;
                }

                made.Add(item);
                if (provisionalReads != before)
                    ledger.Note(item);
            };

            // The loading screen: the game is still loading both pieces. The first item meets them; the hold starts at the next frame.
            make("first", new[] { "kerbA", "kerbB" });
            Program.Check(book.BusyNow == 2 && ledger.Count == 1, "the first item found two busy pieces and is provisional");
            Program.Check(hold.Holds(book.BusyNow, 1.0), "from the next frame the reads wait");
            Program.Check(!ledger.TakeRedo(book.Epoch, new List<string>()), "nothing is made again while no piece is known");

            // The game finishes one piece: the epoch moves and the item would be made again, but the reads still wait for the other.
            loaded.Add("kerbA");
            book.Poll(read);
            Program.Check(book.BusyNow == 1 && hold.Holds(book.BusyNow, 2.0), "one piece is known, one is not: the reads wait");
            var redo = new List<string>();
            Program.Check(ledger.TakeRedo(book.Epoch, redo) && redo.SequenceEqual(new[] { "first" }), "the item is queued again");

            // It is made again at once only if the hold lets it: it does not, so it is not made (the system skips the queue), and the second piece comes.
            loaded.Add("kerbB");
            book.Poll(read);
            Program.Check(book.BusyNow == 0 && !hold.Holds(book.BusyNow, 3.0), "all known: the reads go on");
            made.Clear();
            make("first", new[] { "kerbA", "kerbB" });
            make("second", new[] { "kerbA" });
            Program.Check(made.SequenceEqual(new[] { "first", "second" }) && ledger.Count == 0, "made again from known pieces: not provisional, and nothing else is");
            Program.Check(!ledger.TakeRedo(book.Epoch, redo), "so nothing is made a third time");

            // A piece the game never finishes: the reads go on after the hold, the item is provisional, and it is made again when the piece is finally known.
            var late = new FaceBook<string, int>();
            var lateHold = new KerbHold(20.0);
            var lateLedger = new ProvisionalLedger<string>();
            bool arrived = false;
            Func<string, (PieceRead, int)> lateRead = k => arrived ? Read(PieceRead.Ready, 1) : Read(PieceRead.Busy);
            late.TryResolve("kerbC", lateRead, out _);
            Program.Check(lateHold.Holds(late.BusyNow, 0.0) && lateHold.Holds(late.BusyNow, 19.0) && !lateHold.Holds(late.BusyNow, 20.5), "held for 20 s, then let through");
            bool ok = late.TryResolve("kerbC", lateRead, out _);
            if (!ok)
                lateLedger.Note("item");
            Program.Check(!ok && lateLedger.Count == 1, "made provisional after the hold");
            arrived = true;
            late.Poll(lateRead);
            var again = new List<string>();
            Program.Check(lateLedger.TakeRedo(late.Epoch, again) && again.SequenceEqual(new[] { "item" }), "and made again when the piece is known, however late");
        }
    }
}
