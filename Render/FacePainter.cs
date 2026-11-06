using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using CDLC.TS.Geo;
using UnityEngine;

namespace CDLC.TS.Render
{
    /// <summary>A face's picture, painted on the worker, ready to be made into a texture on the main thread.</summary>
    internal sealed class PaintedFace
    {
        public string Material;
        public int Detail;
        public int Width, Height;
        /// <summary>
        /// The picture as the painter made it: RGBA32, the bottom row first (what a Unity texture takes as it is). Until 0.7.1 it was turned into colours on the worker, a second array as large. Dropped once
        /// the BC1 blocks are known to be read right (Bc1Mode 1): then only the blocks go to the main thread.
        /// </summary>
        public byte[] Raw;

        /// <summary>The picture with its mips as BC1 blocks (0.12.27, BlockCompress.Bc1Chain); null when it is not encoded (BC1 off, or a size that is not whole blocks).</summary>
        public byte[] Blocks;

        /// <summary>The average colour of the picture in linear light (the night glow is tinted with it).</summary>
        public Vector3 Tint;

        public double Ms;
        public string Error;
    }

    /// <summary>
    /// Paints the pictures of the faces on one background thread (0.7.0), and makes them BC1 with their mips on a second (0.12.27: the encoding takes about as long as the painting, which GDI+ keeps to
    /// one thread, so the two run side by side). Until 0.6.1 a picture was painted on the main thread at the moment its tile was built (about 5 ms each with GDI+, and a
    /// tile has dozens): the tile appeared when the last of them was done. Now the tile appears at once with the colour of its boards' ground, and each picture arrives as it is painted, at the size
    /// that fits the tile's distance (FaceDetail). GDI+ is serialised by the painter's own lock, so one thread is all that can work at a time. The queue is by request, the preview's pictures
    /// first; a picture whose face is not wanted any more when its turn comes is not painted. Requests and results are locked; Request and TryTake are the main thread's, the painting is the worker's.
    /// </summary>
    internal static class FacePainter
    {
        private sealed class Job
        {
            public string Material;
            public SignInstance Instance;
            public int Detail;
            public int Epoch;
        }

        private static readonly object s_Lock = new object();
        private static readonly LinkedList<Job> s_Queue = new LinkedList<Job>();
        private static readonly HashSet<string> s_Wanted = new HashSet<string>();
        private static readonly Queue<PaintedFace> s_Done = new Queue<PaintedFace>();
        private static readonly Queue<(PaintedFace Face, int Epoch)> s_ToEncode = new Queue<(PaintedFace, int)>();
        private static Thread s_Thread, s_Encoder;

        /// <summary>
        /// The faces as BC1 (0.12.27): 0 while the first pictures are read back on the main thread and compared (the painter's bytes are kept, for RGBA32 if they are not right), 1 when they were (the
        /// bytes are dropped after encoding), -1 when they were not or the upload failed (nothing is encoded, RGBA32 as before). Written by the main thread only (TsMaterials).
        /// </summary>
        internal static volatile int Bc1Mode;

        /// <summary>
        /// The bytes of the pictures that wait for the encoder or the main thread (0.12.27). The main thread puts on a few a frame, the painter makes many more: without a limit a city's pictures waited
        /// in the managed heap all at once (hundreds of MB of RGBA32 when it loads), and Mono's heap does not give that back. Over the limit the painter waits until some are taken.
        /// </summary>
        private const long kMaxHeldBytes = 64L * 1024 * 1024;
        private static long s_HeldBytes;

        private static long BytesOf(PaintedFace face) => (face.Raw?.LongLength ?? 0L) + (face.Blocks?.LongLength ?? 0L);

        /// <summary>The pictures painted and waiting for the encoder or the main thread.</summary>
        internal static int Pending
        {
            get
            {
                lock (s_Lock)
                    return s_ToEncode.Count + s_Done.Count;
            }
        }

        /// <summary>The bytes of pictures waiting now (for the log).</summary>
        internal static long HeldBytes
        {
            get
            {
                lock (s_Lock)
                    return s_HeldBytes;
            }
        }
        private static int s_Epoch;

        // Counters for the log and the panel (read on the main thread, written on both: a rough figure is all they need).
        internal static int Requested { get; private set; }
        internal static int Painted { get; private set; }
        internal static int Skipped { get; private set; }
        internal static double PaintMs { get; private set; }

        /// <summary>How many pictures are waiting to be painted.</summary>
        internal static int Waiting
        {
            get
            {
                lock (s_Lock)
                    return s_Queue.Count;
            }
        }

        /// <summary>Asks for the picture of a face. Does nothing when it is already asked for. Urgent ones (the preview) go first.</summary>
        internal static void Request(string material, SignInstance instance, int detail, bool urgent)
        {
            if (instance == null)
                return;
            lock (s_Lock)
            {
                if (!s_Wanted.Add(material))
                {
                    if (urgent)
                        MoveToFront(material);
                    return;
                }

                var job = new Job { Material = material, Instance = instance, Detail = detail, Epoch = s_Epoch };
                if (urgent)
                    s_Queue.AddFirst(job);
                else
                    s_Queue.AddLast(job);
                Requested++;
                if (s_Thread == null)
                {
                    s_Thread = new Thread(Run) { IsBackground = true, Name = "CDLC.TS faces", Priority = System.Threading.ThreadPriority.BelowNormal };
                    s_Thread.Start();
                    s_Encoder = new Thread(Encode) { IsBackground = true, Name = "CDLC.TS faces BC1", Priority = System.Threading.ThreadPriority.BelowNormal };
                    s_Encoder.Start();
                }

                // Both workers wait on the lock, each for its own queue.
                Monitor.PulseAll(s_Lock);
            }
        }

        private static void MoveToFront(string material)
        {
            for (LinkedListNode<Job> node = s_Queue.First; node != null; node = node.Next)
            {
                if (node.Value.Material == material)
                {
                    s_Queue.Remove(node);
                    s_Queue.AddFirst(node);
                    return;
                }
            }
        }

        /// <summary>The face is not wanted any more (its tile is gone): a picture that has not been painted yet will not be.</summary>
        internal static void Forget(string material)
        {
            lock (s_Lock)
                s_Wanted.Remove(material);
        }

        /// <summary>A picture that has been painted, if there is one.</summary>
        internal static bool TryTake(out PaintedFace face)
        {
            lock (s_Lock)
            {
                while (s_Done.Count > 0)
                {
                    face = s_Done.Dequeue();
                    s_HeldBytes -= BytesOf(face);
                    Monitor.PulseAll(s_Lock);
                    // A picture whose face was dropped while it was being painted is thrown away.
                    if (s_Wanted.Contains(face.Material))
                        return true;
                    Skipped++;
                }
            }

            face = null;
            return false;
        }

        /// <summary>A city is left: what is waiting is forgotten, and what the worker is painting is thrown away when it is done.</summary>
        internal static void Reset()
        {
            lock (s_Lock)
            {
                s_Epoch++;
                s_Queue.Clear();
                s_Wanted.Clear();
                s_Done.Clear();
                s_ToEncode.Clear();
                s_HeldBytes = 0;
                Monitor.PulseAll(s_Lock);
            }
        }

        private static void Run()
        {
            while (true)
            {
                Job job;
                lock (s_Lock)
                {
                    while (s_Queue.Count == 0 || s_HeldBytes > kMaxHeldBytes)
                        Monitor.Wait(s_Lock);
                    job = s_Queue.First.Value;
                    s_Queue.RemoveFirst();
                    if (job.Epoch != s_Epoch || !s_Wanted.Contains(job.Material))
                    {
                        Skipped++;
                        continue;
                    }
                }

                PaintedFace painted = Paint(job);
                bool encode = painted.Error == null && Bc1Mode >= 0 && BlockCompress.CanEncode(painted.Width, painted.Height);
                lock (s_Lock)
                {
                    if (job.Epoch != s_Epoch || !s_Wanted.Contains(job.Material))
                    {
                        Skipped++;
                    }
                    else
                    {
                        s_HeldBytes += BytesOf(painted);
                        if (encode)
                        {
                            s_ToEncode.Enqueue((painted, job.Epoch));
                            Monitor.PulseAll(s_Lock);
                        }
                        else
                        {
                            s_Done.Enqueue(painted);
                        }
                    }
                }
            }
        }

        /// <summary>The second worker: the painted pictures as BC1 with their mips; a picture that is not wanted any more is not encoded. A picture that fails to encode goes on as it was painted.</summary>
        private static void Encode()
        {
            while (true)
            {
                PaintedFace painted;
                int encodingEpoch;
                lock (s_Lock)
                {
                    while (s_ToEncode.Count == 0)
                        Monitor.Wait(s_Lock);
                    (PaintedFace face, int epoch) = s_ToEncode.Dequeue();
                    if (epoch != s_Epoch || !s_Wanted.Contains(face.Material))
                    {
                        if (epoch == s_Epoch)
                            s_HeldBytes -= BytesOf(face);
                        Skipped++;
                        Monitor.PulseAll(s_Lock);
                        continue;
                    }

                    painted = face;
                    encodingEpoch = epoch;
                }

                long heldBefore = BytesOf(painted);

                var clock = Stopwatch.StartNew();
                if (Bc1Mode >= 0)
                {
                    try
                    {
                        painted.Blocks = BlockCompress.Bc1Chain(painted.Raw, painted.Width, painted.Height);
                        if (Bc1Mode == 1)
                            painted.Raw = null;
                    }
                    catch (Exception ex)
                    {
                        painted.Blocks = null;
                        EncodeError = ex.GetType().Name + ": " + ex.Message;
                    }
                }

                EncodeMs += clock.Elapsed.TotalMilliseconds;
                Encoded++;
                lock (s_Lock)
                {
                    // A city left meanwhile counted from 0 again: this picture is not in that count, and is not wanted.
                    if (encodingEpoch != s_Epoch)
                    {
                        Skipped++;
                    }
                    else if (s_Wanted.Contains(painted.Material))
                    {
                        s_HeldBytes += BytesOf(painted) - heldBefore;
                        s_Done.Enqueue(painted);
                    }
                    else
                    {
                        s_HeldBytes -= heldBefore;
                        Skipped++;
                    }

                    Monitor.PulseAll(s_Lock);
                }
            }
        }

        /// <summary>How many pictures were encoded and how long that took on the second worker (ms), and the last error of an encoding ("" for none).</summary>
        internal static int Encoded { get; private set; }
        internal static double EncodeMs { get; private set; }
        internal static string EncodeError { get; private set; } = "";

        private static PaintedFace Paint(Job job)
        {
            var clock = Stopwatch.StartNew();
            var painted = new PaintedFace { Material = job.Material, Detail = job.Detail };
            try
            {
                // An LED screen's picture is its dots (at the size of its own grid); any other board is painted at the size of the detail.
                SignInstance sized = job.Instance.IsLed ? job.Instance : FaceDetail.Sized(job.Instance, FaceDetail.MaxSide(job.Detail));
                byte[] raw = LedScreens.PaintFace(sized, out int w, out int h);
                if (raw.Length != w * h * 4)
                    throw new InvalidOperationException($"the painter returned {raw.Length} bytes for {w}x{h}");

                ImageOps.AverageLinear(raw, w, h, out float tr, out float tg, out float tb);
                painted.Width = w;
                painted.Height = h;
                painted.Raw = raw;
                painted.Tint = new Vector3(tr, tg, tb);
            }
            catch (Exception ex)
            {
                painted.Error = ex.ToString();
            }

            painted.Ms = clock.Elapsed.TotalMilliseconds;
            Painted++;
            PaintMs += painted.Ms;
            return painted;
        }
    }
}
