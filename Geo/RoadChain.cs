using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>What joins a piece of a chain to the next one.</summary>
    public enum ChainJoint
    {
        /// <summary>Nothing: the chain ends here (a dead end, a junction it does not cross, or the last piece read).</summary>
        End = 0,

        /// <summary>A node where the road only goes on (two roads with car lanes meet there): the lines run on through it.</summary>
        Pass = 1,

        /// <summary>A junction the chain crosses to the road that goes on straightest: the lines are cut there (but an edge line on a side no other road joins).</summary>
        Junction = 2
    }

    /// <summary>One road segment of a chain, as the game has it, and what the chain makes of it.</summary>
    public sealed class ChainPiece
    {
        /// <summary>The caller's number for it (the edge's entity index).</summary>
        public int Id;

        /// <summary>The segment's centre line as the game has it (from the node at its start to the node at its end).</summary>
        public Bezier3 Curve;

        /// <summary>The chain runs against the segment's own direction.</summary>
        public bool Reversed;

        /// <summary>Where along the segment's own curve (m) its car lanes start and end; NaN: the whole curve.</summary>
        public float LaneFrom = float.NaN, LaneTo = float.NaN;

        /// <summary>
        /// 0.12.51: where along its own curve (m) its drawn area starts and ends at its left and right edges (HalfWidth either side of the curve; NaN: not known). A segment meets a junction at a slant,
        /// its two corners at a mouth at different distances (LaneFrom and LaneTo are the nearer ones, where every lane is): a line at a lateral ends where the slant crosses it (LanesS0At, LanesS1At).
        /// </summary>
        public float StartLeft = float.NaN, StartRight = float.NaN, EndLeft = float.NaN, EndRight = float.NaN;

        /// <summary>
        /// 0.12.47: where along its own curve (m) the segment is drawn (its EdgeGeometry; NaN: not known). Its ends are where the segment's area meets a node's: no item of a line crosses them
        /// (RoadChain.Cuts), so that marking a segment or a node again takes away whole items.
        /// </summary>
        public float DrawnFrom = float.NaN, DrawnTo = float.NaN;

        /// <summary>
        /// 0.12.47: the next segment's part of a plain node at an end of a segment marked alone (ChainReader): the lines go on through the node, which is cleared with the segment, to where the next
        /// segment is drawn. Not a segment of the stretch.
        /// </summary>
        public bool Stub;

        /// <summary>Where its car lanes are across it (m right of the segment's centre line, looking along the segment), those that run with the segment and those against it.</summary>
        public readonly List<float> Forward = new List<float>(), Backward = new List<float>();

        /// <summary>The speed limit of its lanes (km/h) and whether it was read.</summary>
        public float Kmh;
        public bool HasSpeed;

        /// <summary>A footway beyond the carriageway (a kerb marks the edge), and a parking lane beside the outermost car lane, on the segment's own left and right.</summary>
        public bool FootwayLeft, FootwayRight, ParkingLeft, ParkingRight;

        /// <summary>
        /// 0.12.2: the cycle lanes (the game's road lanes for bicycles only; not in Forward and Backward) and the bus lanes (car lanes for public transport only; in Forward and Backward too), each with the
        /// segment and against it, in the segment's own frame; and half the road's whole width (m, the game's, footways too): how far a driver sees across it.
        /// </summary>
        public readonly List<float> BikeForward = new List<float>(), BikeBackward = new List<float>(), BusForward = new List<float>(), BusBackward = new List<float>();

        /// <summary>The raised strips between its carriageways (kerbs, medians: lo, hi m right of the curve's direction, left to right; 0.12.22).</summary>
        public readonly List<(float Lo, float Hi)> Raised = new List<(float Lo, float Hi)>();
        public float HalfWidth = 6f;

        /// <summary>0.12.101: how far the game's own surface reaches to the segment's left and right of its curve at its middle (m, both positive; EdgeGeometry's sides), NaN when not read: for the log, against the edge a barrier is given.</summary>
        public float GameLeft = float.NaN, GameRight = float.NaN;

        /// <summary>The width of its cycle lanes (m, the game's; the widest).</summary>
        public float BikeWidth = 1.8f;

        /// <summary>What joins it to the next piece (End for the last).</summary>
        public ChainJoint After;

        /// <summary>
        /// What the reader found at the node after it (0.12.66; After is End where the chain breaks, and the log said End for both): Pass where two roads meet and the road goes on, Junction where more
        /// do, End for the last piece or where it found no way on. JointNote says which node it is, for the log.
        /// </summary>
        public ChainJoint ReaderJoint = ChainJoint.End;
        public string JointNote = "";

        /// <summary>At a junction after the piece: another road joins it on the chain's left, on its right (seen along the chain).</summary>
        public bool JoinLeft, JoinRight;

        /// <summary>
        /// 0.12.67: at a junction after the piece, the roads that join (JunctionMouths) and whether they were all read: a wall is open at their mouths only, not over the whole node. False where they
        /// could not be (a road with no width, a roundabout), and the wall is cut by the lanes as before.
        /// </summary>
        public readonly List<ChainMouth> Mouths = new List<ChainMouth>();
        public bool MouthsKnown;

        // ---- made by the chain

        public ArcTable Arc;

        /// <summary>
        /// How much of the start of the piece (m, along the chain's way) the chain does not use (0.12.65): it starts on the line of the piece before it, that far behind where that one ends, and the
        /// chain goes on from there. 0 for the first piece, and for one that starts where the one before it ends.
        /// </summary>
        public float Skip;

        /// <summary>The chain's distance (m) where the piece starts and ends, and where its car lanes start and end.</summary>
        public float S0, S1, LanesS0, LanesS1;

        /// <summary>
        /// 0.12.51: where its car lanes end (LanesS1At) and start (LanesS0At) along the chain at a lateral (m right of the chain's direction): on its slanted mouth (straight between its corners, held at
        /// a corner beyond it), never short of LanesS1 (before LanesS0) and never beyond the piece; LanesS1 and LanesS0 where the corners are not known.
        /// </summary>
        public float LanesS1At(float lateral) => Math.Max(LanesS1, Math.Min(S1, OnMouth(Reversed ? StartLeft : EndLeft, Reversed ? StartRight : EndRight, lateral, LanesS1)));

        public float LanesS0At(float lateral) => Math.Min(LanesS0, Math.Max(S0, OnMouth(Reversed ? EndLeft : StartLeft, Reversed ? EndRight : StartRight, lateral, LanesS0)));

        /// <summary>The chain's distance of a mouth at a lateral of the chain, from the segment's distances at its left and right corners; fallback when they are not known.</summary>
        private float OnMouth(float atLeft, float atRight, float lateral, float fallback)
        {
            if (float.IsNaN(atLeft) || float.IsNaN(atRight) || !(HalfWidth > 0f))
                return fallback;
            float own = Reversed ? -lateral : lateral;
            float t = Math.Max(0f, Math.Min(1f, (own + HalfWidth) / (2f * HalfWidth)));
            float local = atLeft + (atRight - atLeft) * t;
            return S0 + (Reversed ? S1 - S0 - local : local - Skip);
        }

        /// <summary>Its car lanes across the chain (m right of the chain's direction): those that run the chain's way and those against it.</summary>
        public readonly List<float> With = new List<float>(), Against = new List<float>();

        /// <summary>The footway and the parking on the chain's left and right.</summary>
        public bool KerbLeft, KerbRight, ParkLeft, ParkRight;

        /// <summary>The cycle lanes and the bus lanes across the chain, with it and against it.</summary>
        public readonly List<float> BikeWith = new List<float>(), BikeAgainst = new List<float>(), BusWith = new List<float>(), BusAgainst = new List<float>();

        /// <summary>The raised strips across the chain (lo, hi m right of the chain's direction, left to right).</summary>
        public readonly List<(float Lo, float Hi)> RaisedAcross = new List<(float Lo, float Hi)>();
    }

    /// <summary>A crosswalk across the chain away from a junction (at a node it passes through): from where its near side is to where its far side is (m of the chain's distance), and whether signals control it.</summary>
    public struct ChainCrossing
    {
        public float S0, S1;
        public bool Signals;

        public ChainCrossing(float s0, float s1, bool signals)
        {
            S0 = Math.Min(s0, s1);
            S1 = Math.Max(s0, s1);
            Signals = signals;
        }

        public float Middle => (S0 + S1) * 0.5f;
    }

    /// <summary>A stretch along a chain (m of the chain's distance).</summary>
    public struct ChainSpan
    {
        public float From, To;

        public ChainSpan(float from, float to)
        {
            From = from;
            To = to;
        }

        public float Length => To - From;

        public override string ToString() => $"{From:0.0}..{To:0.0}";
    }

    /// <summary>
    /// Road segments one after the other (0.12.2): the segments of a road that go on through the nodes between them, read from the segment under the cursor both ways, so that a marking runs along the road
    /// whatever its pieces are. The chain's direction is the first segment's way that the reader started from (a piece that runs the other way is Reversed); its distance S is metres from the chain's start;
    /// its lateral is metres right of its direction. The centre line is the segments' curves one after the other (they meet at the nodes' middles), sampled every Step metres, with the right of each sample from
    /// the samples either side of it, so that a line at a lateral turns smoothly at a node. Where a car lane runs (LanesS0 to LanesS1 of each piece) is where the lines are: a line goes on through a node
    /// between two pieces, and is cut at a junction. Pure.
    /// </summary>
    /// <summary>
    /// A place where one piece of a chain goes on into the next without a break (0.12.99): Piece is the piece before it, S the distance along the chain where it ends, Chord the distance along the chain from
    /// the last sample of that piece to the first of the next (the line across it is straight), Jog how far the next piece’s start lies to the right of the line the piece before it was heading on (m, negative:
    /// to the left): two pieces of a road that meet at a plain node a metre or two to the side of each other, the road’s centre line stepping across by that much.
    /// </summary>
    public struct ChainJoin
    {
        public int Piece;
        public float S, Chord, Jog;
    }

    public sealed class RoadChain
    {
        /// <summary>How far apart the samples of the centre line are (m).</summary>
        public const float Step = 1f;

        /// <summary>The longest chain read (m) and the most pieces.</summary>
        public const float MaxLength = 3000f;
        public const int MaxPieces = 80;

        public readonly List<ChainPiece> Pieces;

        /// <summary>The crosswalks across the chain at the nodes it goes on through (the reader fills them, in the order of the chain).</summary>
        public readonly List<ChainCrossing> Crossings = new List<ChainCrossing>();

        /// <summary>Where the pieces are drawn from and to (ChainPiece.DrawnFrom, DrawnTo, in m of the chain's distance), in order: where a segment's area and a node's meet.</summary>
        public readonly List<float> Cuts = new List<float>();

        /// <summary>
        /// 0.12.52: where a piece starts farther than this from where the one before it ends (m), the chain stops there for the lines (the piece before it gets ChainJoint.End) and its centre line
        /// does not join the two: no line crosses the gap, none is projected onto it, the right of the line at each side is its own piece's.
        /// </summary>
        public const float MaxJoinGap = 1f;

        /// <summary>The chain's distance a break takes (m), so that a distance at either side of it is on that side's piece.</summary>
        public const float BreakLength = 0.1f;

        /// <summary>
        /// 0.12.65: how far back from where a piece ends the next one may start and still be the same road (m): two segments of a wide road at a node overlapped by 9.4 and 6.7 m in the log of the owner's
        /// noise walls, the next piece starting on the line of the one before it. More than a node is, it is not one road.
        /// </summary>
        public const float MaxOverlap = 20f;

        /// <summary>
        /// 0.12.66: how far (m) the next piece may start from the end of the one before it at a node where only two roads meet and the reader went on from one to the other (ChainPiece.ReaderJoint =
        /// Pass), and the chain is not broken there. The owner's log of 1 Nov 21:14: a road 12 m wide meeting one 16 m wide, the next starting 2.1 m from the end (2.0 m off the line), and the lines were
        /// cut over 54 m of lanes. Beyond this it is a break as before.
        /// </summary>
        public const float MaxPassGap = 4f;

        /// <summary>
        /// 0.12.97: the farthest a piece may start from the end of the one before it (m) at such a node when it is straight on from it (MaxPassOffset), however wide the road: the two curves end where the node's
        /// own area begins, which for a wide road is farther than 4 m. The owner's log of 5 Nov: two segments of a road 12 m wide ended 4.2 m apart (heading dot 1.00, 0.3 m off the line), and a wall of
        /// 1087 m was cut over 12 m there. The reach is twice the wider half width (the road's width), at most this.
        /// </summary>
        public const float MaxPassReach = 30f;

        /// <summary>0.12.97: how far (m) the next piece's start may be to the side of the line the chain was heading on, beyond MaxPassGap: a road that goes on is on its own line, one that jogs is not.</summary>
        public const float MaxPassOffset = 1.5f;

        /// <summary>Why each break is one (0.12.66), in the order of Breaks: for the log.</summary>
        public readonly List<string> BreakNotes = new List<string>();

        /// <summary>0.12.97: the plain nodes the chain goes across although the two pieces end more than MaxPassGap apart (PassReach), with the distance along the chain and the gap (m): for the log.</summary>
        public readonly List<(float S, float Gap)> Bridges = new List<(float S, float Gap)>();

        /// <summary>The distances where a piece does not start where the one before it ends (MaxJoinGap): the end of the piece before it (the next starts BreakLength on).</summary>
        public readonly List<float> Breaks = new List<float>();

        /// <summary>
        /// 0.12.98: the cosine of the angle (15 degrees) the chord across a join may make with the way the road heads on one side of it, at least: a chord that is off the heading of the piece before it AND
        /// off the heading of the next is a join whose pieces are a metre or two to the side of each other (the plain node of the owner’s log of 5 Nov: 2 m aside and none along), and the right of the
        /// samples either side of it is taken from their own piece’s direction, not from that chord. A bend has a chord along the next piece (it starts where the one before ends): not skewed.
        /// </summary>
        public const float SkewedJoin = 0.966f;

        /// <summary>The samples at which a piece goes on into the next without a break: the last sample of the piece before (the next one is the first of the next piece), and that piece.</summary>
        private readonly List<int> m_Joins = new List<int>(), m_JoinPieces = new List<int>();

        /// <summary>The places where a piece goes on into the next without a break, in the order of the chain (ChainJoin); filled by the constructor.</summary>
        public readonly List<ChainJoin> Joins = new List<ChainJoin>();

        private readonly List<Vec3> m_Points = new List<Vec3>();
        private readonly List<float> m_S = new List<float>();
        private readonly List<Vec3> m_Rights = new List<Vec3>();

        /// <summary>Whether the centre line is broken between sample i and the next (Breaks).</summary>
        private readonly List<bool> m_BreakAfter = new List<bool>();

        /// <summary>The chain's length (m, along the ground).</summary>
        public float Length { get; }

        public RoadChain(IEnumerable<ChainPiece> pieces)
        {
            Pieces = new List<ChainPiece>(pieces);
            float s = 0f;
            for (int p = 0; p < Pieces.Count; p++)
            {
                ChainPiece piece = Pieces[p];
                piece.Arc = new ArcTable(piece.Curve);
                float length = piece.Arc.Length;
                float skip = 0f;
                bool broken = p > 0 && (piece.Curve.Point(piece.Arc.TAt(piece.Reversed ? length : 0f)) - m_Points[m_Points.Count - 1]).Length > MaxJoinGap;
                string why = "";
                if (broken)
                {
                    why = Why(piece, length);
                    if (Overlaps(piece, length, out skip))
                        broken = false;
                    else if (Pieces[p - 1].ReaderJoint == ChainJoint.Pass && Continues(Pieces[p - 1], piece, length))
                    {
                        skip = 0f;
                        broken = false;
                        float across = Vec3.DistanceXZ(piece.Curve.Point(piece.Arc.TAt(piece.Reversed ? length : 0f)), m_Points[m_Points.Count - 1]);
                        if (across > MaxPassGap)
                            Bridges.Add((s, across));
                    }
                    else
                        skip = 0f;
                }
                else
                {
                    skip = 0f;
                }
                piece.Skip = skip;
                float used = length - skip;
                if (broken)
                {
                    Pieces[p - 1].After = ChainJoint.End;
                    Breaks.Add(s);
                    BreakNotes.Add(why);
                    m_BreakAfter[m_BreakAfter.Count - 1] = true;
                    s += BreakLength;
                }

                piece.S0 = s;
                piece.S1 = s + used;
                float a = float.IsNaN(piece.LaneFrom) ? 0f : Math.Max(0f, Math.Min(length, piece.LaneFrom));
                float b = float.IsNaN(piece.LaneTo) ? length : Math.Max(0f, Math.Min(length, piece.LaneTo));
                if (b < a)
                {
                    float swap = a;
                    a = b;
                    b = swap;
                }

                // The distances along the curve are the chain's from where the piece's start is (less what is skipped), and never outside what is used.
                piece.LanesS0 = Math.Max(piece.S0, Math.Min(piece.S1, piece.Reversed ? s + (length - b) - skip : s + a - skip));
                piece.LanesS1 = Math.Max(piece.S0, Math.Min(piece.S1, piece.Reversed ? s + (length - a) - skip : s + b - skip));
                foreach (float drawn in new[] { piece.DrawnFrom, piece.DrawnTo })
                {
                    if (!float.IsNaN(drawn))
                    {
                        float own = Math.Max(0f, Math.Min(length, drawn));
                        Cuts.Add(Math.Max(piece.S0, Math.Min(piece.S1, s + (piece.Reversed ? length - own : own) - skip)));
                    }
                }
                piece.With.Clear();
                piece.Against.Clear();
                foreach (float x in piece.Reversed ? piece.Backward : piece.Forward)
                    piece.With.Add(piece.Reversed ? -x : x);
                foreach (float x in piece.Reversed ? piece.Forward : piece.Backward)
                    piece.Against.Add(piece.Reversed ? -x : x);
                Turn(piece.Reversed ? piece.BikeBackward : piece.BikeForward, piece.BikeWith, piece.Reversed);
                Turn(piece.Reversed ? piece.BikeForward : piece.BikeBackward, piece.BikeAgainst, piece.Reversed);
                Turn(piece.Reversed ? piece.BusBackward : piece.BusForward, piece.BusWith, piece.Reversed);
                Turn(piece.Reversed ? piece.BusForward : piece.BusBackward, piece.BusAgainst, piece.Reversed);
                piece.RaisedAcross.Clear();
                foreach ((float lo, float hi) in piece.Raised)
                    piece.RaisedAcross.Add(piece.Reversed ? (-hi, -lo) : (lo, hi));
                piece.RaisedAcross.Sort((a, b) => a.Lo.CompareTo(b.Lo));
                piece.KerbLeft = piece.Reversed ? piece.FootwayRight : piece.FootwayLeft;
                piece.KerbRight = piece.Reversed ? piece.FootwayLeft : piece.FootwayRight;
                piece.ParkLeft = piece.Reversed ? piece.ParkingRight : piece.ParkingLeft;
                piece.ParkRight = piece.Reversed ? piece.ParkingLeft : piece.ParkingRight;

                int n = Math.Max(1, (int)Math.Ceiling(used / Step));
                if (p > 0 && !broken && m_Points.Count > 0)
                {
                    m_Joins.Add(m_Points.Count - 1);
                    m_JoinPieces.Add(p - 1);
                }
                for (int k = p == 0 || broken ? 0 : 1; k <= n; k++)
                {
                    float d = used * k / n;
                    float onEdge = piece.Reversed ? length - (d + skip) : d + skip;
                    m_Points.Add(piece.Curve.Point(piece.Arc.TAt(onEdge)));
                    m_S.Add(s + d);
                    m_BreakAfter.Add(false);
                }

                s += used;
            }

            Length = s;
            Cuts.Sort();
            for (int i = 0; i < m_Points.Count; i++)
            {
                Vec3 before = m_Points[i > 0 && !m_BreakAfter[i - 1] ? i - 1 : i], after = m_Points[i + 1 < m_Points.Count && !m_BreakAfter[i] ? i + 1 : i];
                m_Rights.Add((after - before).RightXZ());
            }

            // A join whose pieces are side by side (0.12.98): the chord across it is not the way the road heads, and a right taken from it throws a line that is 8 m from the centre back along the road by
            // as much as the chord is turned (4.6 m at 64 degrees: the owner’s hook). The two samples either side of it take their own piece’s direction: the last of the piece before from the chord that comes
            // into it, the first of the next from the chord that goes on from it. A join whose chord is within SkewedJoin of the road’s heading (a bend, a gap straight on) keeps the smooth right.
            for (int k = 0; k < m_Joins.Count; k++)
            {
                int j = m_Joins[k];
                // A join is never after a break, and a piece before one has two samples at least; the next piece may have only one (a metre or less long) or be followed by a break: it then goes on the way the
                // piece before it was heading.
                Vec3 coming = m_Points[j] - m_Points[j - 1], across = m_Points[j + 1] - m_Points[j];
                Vec3 going = j + 2 < m_Points.Count && !m_BreakAfter[j + 1] ? m_Points[j + 2] - m_Points[j + 1] : coming;
                // Where the next piece starts, to the side of the line the piece before it was heading on: its first sample less the way it went from its start to there.
                float chordS = m_S[j + 1] - m_S[j];
                Vec3 start = m_Points[j + 1] - going.HeadingXZ() * chordS;
                Joins.Add(new ChainJoin { Piece = m_JoinPieces[k], S = m_S[j], Chord = chordS, Jog = Vec3.Dot(start - m_Points[j], coming.RightXZ()) });
                Vec3 chord = across.HeadingXZ();
                if (Vec3.Dot(coming.HeadingXZ(), chord) >= SkewedJoin || Vec3.Dot(going.HeadingXZ(), chord) >= SkewedJoin)
                    continue;
                m_Rights[j] = coming.RightXZ();
                m_Rights[j + 1] = going.RightXZ();
            }
        }

        /// <summary>
        /// Whether the piece (length: its curve's) starts on the line of the piece before it, behind where that one ends (0.12.65): the chain's last point is on the piece's curve (a metre at most, at
        /// the same level), up to MaxOverlap from the piece's start, and the piece heads the way the chain does there. skip is that distance. A piece beside the line, going back along it or on another
        /// level is another road.
        /// </summary>
        /// <summary>The next piece starts within MaxPassGap of the end of the chain, at the same level, heading the way the chain does (a bend of 25 degrees at most).</summary>
        private bool Continues(ChainPiece before, ChainPiece piece, float length)
        {
            if (m_Points.Count < 2)
                return false;
            Vec3 last = m_Points[m_Points.Count - 1];
            Vec3 start = piece.Curve.Point(piece.Arc.TAt(piece.Reversed ? length : 0f));
            float gap = Vec3.DistanceXZ(start, last);
            if (gap > PassReach(before, piece) || Math.Abs(start.Y - last.Y) > LevelReach)
                return false;
            Vec3 coming = (last - m_Points[m_Points.Count - 2]).HeadingXZ();
            Vec3 going = piece.Curve.Tangent(piece.Reversed ? 1f : 0f).HeadingXZ() * (piece.Reversed ? -1f : 1f);
            if (Vec3.Dot(coming, going) < 0.9f)
                return false;
            if (gap <= MaxPassGap)
                return true;
            // Beyond MaxPassGap only straight on: ahead of the end of the chain and on the line it was heading on.
            Vec3 across = start - last;
            return Vec3.Dot(across, coming) > 0f && Math.Abs(Vec3.Dot(across, coming.RightXZ())) <= MaxPassOffset;
        }

        /// <summary>How far from the end of a piece the next may start at a plain node and still go on from it (m): MaxPassGap, or the width of the wider road, at most MaxPassReach.</summary>
        public static float PassReach(ChainPiece before, ChainPiece next) => Math.Min(MaxPassReach, Math.Max(MaxPassGap, 2f * Math.Max(before.HalfWidth, next.HalfWidth)));

        /// <summary>What the next piece is to the chain's end, for the log of a break: how far from it, how far behind it and off its line, at what level, heading how.</summary>
        private string Why(ChainPiece piece, float length)
        {
            if (m_Points.Count < 2)
                return "";
            Vec3 last = m_Points[m_Points.Count - 1];
            Vec3 start = piece.Curve.Point(piece.Arc.TAt(piece.Reversed ? length : 0f));
            float t = piece.Curve.ClosestT(last, 64);
            Vec3 on = piece.Curve.Point(t);
            float own = piece.Arc.SAt(t);
            float behind = piece.Reversed ? length - own : own;
            Vec3 coming = (last - m_Points[m_Points.Count - 2]).HeadingXZ();
            Vec3 going = piece.Curve.Tangent(t).HeadingXZ() * (piece.Reversed ? -1f : 1f);
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "#{0} starts {1:0.0} m from the end of the chain, which is {2:0.0} m from its curve ({3:0.0} m along it from its start), {4:+0.0;-0.0} m high, heading dot {5:0.00}; the reader found {6}{7}",
                piece.Id, Vec3.DistanceXZ(start, last), Vec3.DistanceXZ(on, last), behind, start.Y - last.Y, Vec3.Dot(coming, going), Pieces[Pieces.Count > 0 ? Math.Max(0, Pieces.IndexOf(piece) - 1) : 0].ReaderJoint,
                string.IsNullOrEmpty(Pieces[Math.Max(0, Pieces.IndexOf(piece) - 1)].JointNote) ? "" : " at " + Pieces[Math.Max(0, Pieces.IndexOf(piece) - 1)].JointNote);
        }

        private bool Overlaps(ChainPiece piece, float length, out float skip)
        {
            skip = 0f;
            if (m_Points.Count < 2)
                return false;
            Vec3 last = m_Points[m_Points.Count - 1];
            float t = piece.Curve.ClosestT(last, 64);
            Vec3 on = piece.Curve.Point(t);
            if (Vec3.DistanceXZ(on, last) > MaxJoinGap || Math.Abs(on.Y - last.Y) > LevelReach)
                return false;
            float own = piece.Arc.SAt(t);
            skip = piece.Reversed ? length - own : own;
            if (!(skip > 0f) || skip > MaxOverlap || skip >= length)
                return false;
            // The way the chain was heading at its end, and the way the piece heads there (along the chain, so against its curve when it is reversed).
            Vec3 coming = (last - m_Points[m_Points.Count - 2]).HeadingXZ();
            Vec3 going = piece.Curve.Tangent(t).HeadingXZ() * (piece.Reversed ? -1f : 1f);
            return Vec3.Dot(coming, going) >= 0.9f;
        }

        private static void Turn(List<float> from, List<float> into, bool reversed)
        {
            into.Clear();
            foreach (float x in from)
                into.Add(reversed ? -x : x);
        }

        /// <summary>The number of samples of the centre line.</summary>
        public int SampleCount => m_Points.Count;

        /// <summary>The sample at or before a distance, and how far to the next one it is (0 to 1).</summary>
        private int Find(float s, out float fraction)
        {
            fraction = 0f;
            if (m_Points.Count < 2)
                return 0;
            if (s <= 0f)
                return 0;
            if (s >= Length)
            {
                fraction = 1f;
                return m_Points.Count - 2;
            }

            int lo = 0, hi = m_S.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (m_S[mid] <= s)
                    lo = mid;
                else
                    hi = mid;
            }

            float span = m_S[hi] - m_S[lo];
            fraction = span < 1e-6f ? 0f : (s - m_S[lo]) / span;
            return lo;
        }

        /// <summary>The point at a distance along the chain, lateral metres to its right, raised by surface.</summary>
        public Vec3 PointAt(float s, float lateral, float surface = 0f)
        {
            if (m_Points.Count == 0)
                return Vec3.Zero;
            if (m_Points.Count == 1)
                return m_Points[0] + new Vec3(0f, surface, 0f);
            int i = Find(s, out float f);
            Vec3 centre = Vec3.Lerp(m_Points[i], m_Points[i + 1], f);
            Vec3 right = Vec3.Lerp(m_Rights[i], m_Rights[i + 1], f).HeadingXZ();
            Vec3 p = centre + right * lateral;
            return new Vec3(p.X, p.Y + surface, p.Z);
        }

        /// <summary>The way the chain heads at a distance along it (level, length 1).</summary>
        public Vec3 HeadingAt(float s)
        {
            if (m_Points.Count < 2)
                return new Vec3(0f, 0f, 1f);
            int i = Find(s, out _);
            return (m_Points[i + 1] - m_Points[i]).HeadingXZ();
        }

        /// <summary>The grade along the chain at a distance (rise over run).</summary>
        public float GradeAt(float s)
        {
            if (m_Points.Count < 2)
                return 0f;
            int i = Find(s, out _);
            Vec3 d = m_Points[i + 1] - m_Points[i];
            float run = d.LengthXZ;
            return run < 1e-4f ? 0f : d.Y / run;
        }

        /// <summary>The distance along the chain of the point of its centre line nearest to a place (in the ground plane), and how far right of it the place is.</summary>
        public float Project(Vec3 place, out float lateral) => Project(place, out lateral, float.MinValue, float.MaxValue, false);

        /// <summary>
        /// As Project, for the place under the cursor (0.12.52): between s0 and s1 only (the piece clicked; all of the chain when none of it is there), and level with the place: a sample more than
        /// LevelReach above or below it counts as farther by four times that much, so that where the chain passes over or under itself (the loop of an interchange) the end of a line goes on the
        /// road the cursor is on, not on the one across it.
        /// </summary>
        public float ProjectLevel(Vec3 place, out float lateral, float s0 = float.MinValue, float s1 = float.MaxValue) => Project(place, out lateral, s0, s1, true);

        public const float LevelReach = 3f;

        private float Project(Vec3 place, out float lateral, float s0, float s1, bool level)
        {
            lateral = 0f;
            if (m_Points.Count < 2)
                return 0f;
            float best = float.MaxValue, bestS = float.NaN;
            for (int i = 0; i + 1 < m_Points.Count; i++)
            {
                if (m_BreakAfter[i] || m_S[i + 1] < s0 || m_S[i] > s1)
                    continue;
                Vec3 a = m_Points[i], b = m_Points[i + 1];
                float dx = b.X - a.X, dz = b.Z - a.Z, l2 = dx * dx + dz * dz;
                float t = l2 < 1e-9f ? 0f : Math.Max(0f, Math.Min(1f, ((place.X - a.X) * dx + (place.Z - a.Z) * dz) / l2));
                float fx = a.X + dx * t - place.X, fz = a.Z + dz * t - place.Z, d2 = fx * fx + fz * fz;
                if (level)
                {
                    float off = Math.Max(0f, Math.Abs(place.Y - (a.Y + (b.Y - a.Y) * t)) - LevelReach) * 4f;
                    d2 += off * off;
                }

                if (d2 < best)
                {
                    best = d2;
                    bestS = m_S[i] + (m_S[i + 1] - m_S[i]) * t;
                }
            }

            if (float.IsNaN(bestS))
                return s0 > float.MinValue || s1 < float.MaxValue ? Project(place, out lateral, float.MinValue, float.MaxValue, level) : 0f;
            bestS = Math.Max(s0, Math.Min(s1, bestS));
            Vec3 foot = PointAt(bestS, 0f);
            Vec3 right = RightAt(bestS);
            lateral = (place.X - foot.X) * right.X + (place.Z - foot.Z) * right.Z;
            return bestS;
        }

        /// <summary>The right of the chain at a distance (level, length 1).</summary>
        public Vec3 RightAt(float s)
        {
            if (m_Points.Count < 2)
                return new Vec3(1f, 0f, 0f);
            int i = Find(s, out float f);
            return Vec3.Lerp(m_Rights[i], m_Rights[i + 1], f).HeadingXZ();
        }

        /// <summary>
        /// The pieces between two distances, for the log (0.12.52): each one's segment (r: read against its curve), its distances, where its lanes run, its half width, where it starts and ends,
        /// and the breaks.
        /// </summary>
        public string Describe(float s0, float s1)
        {
            float lo = Math.Min(s0, s1), hi = Math.Max(s0, s1);
            var parts = new List<string>();
            for (int i = 0; i < Pieces.Count; i++)
            {
                ChainPiece p = Pieces[i];
                if (p.S1 < lo || p.S0 > hi)
                    continue;
                Vec3 a = p.Curve.Point(p.Reversed ? 1f : 0f), b = p.Curve.Point(p.Reversed ? 0f : 1f);
                // At a junction (0.12.64, after the owner's "岔路节点还是会空"): which sides a road joins and how long the node is where the lanes are not (what a line on a joined side is cut by).
                string join = p.After == ChainJoint.Junction && i + 1 < Pieces.Count
                    ? string.Format(System.Globalization.CultureInfo.InvariantCulture, " [joined {0}{1}; no lanes for {2:0.0} m; {3}]", p.JoinLeft ? "left" : "", p.JoinRight ? (p.JoinLeft ? " and right" : "right") : (p.JoinLeft ? "" : "no side"), Pieces[i + 1].LanesS0 - p.LanesS1,
                        p.MouthsKnown ? "wall open at the mouths of " + string.Join(" + ", p.Mouths.ConvertAll(m => (m.HalfWidth * 2f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " m wide")) : "mouths unknown, cut by the lanes")
                    : "";
                // 0.12.101: the edge a barrier is given on each side (chain's left, right) against how far the game's surface reaches (EdgeGeometry), where that was read.
                string edges = float.IsNaN(p.GameLeft) || float.IsNaN(p.GameRight) ? ""
                    : string.Format(System.Globalization.CultureInfo.InvariantCulture, ", edge L {0:0.00} R {1:0.00} (game surface L {2:0.00} R {3:0.00})", BarrierAuto.EdgeOf(p, -1), BarrierAuto.EdgeOf(p, 1), p.Reversed ? p.GameRight : p.GameLeft, p.Reversed ? p.GameLeft : p.GameRight);
                parts.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "#{0}{1} {2:0.0}-{3:0.0} m (lanes {4:0.0}-{5:0.0}), half {6:0.0}{15}, ({7:0.0}, {8:0.0}, {9:0.0}) to ({10:0.0}, {11:0.0}, {12:0.0}), {13}{14}",
                    p.Id, p.Reversed ? "r" : "", p.S0, p.S1, p.LanesS0, p.LanesS1, p.HalfWidth, a.X, a.Y, a.Z, b.X, b.Y, b.Z, p.After, join + (p.Skip > 0f ? string.Format(System.Globalization.CultureInfo.InvariantCulture, " [its first {0:0.0} m overlap the piece before it and are not used]", p.Skip) : ""), edges));
            }

            string breaks = Breaks.Exists(s => s >= lo && s <= hi) ? "; breaks at " + string.Join(", ", Breaks.FindAll(s => s >= lo && s <= hi).ConvertAll(s => s.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " (" + BreakNotes[Breaks.IndexOf(s)] + ")")) : "";
            string bridged = Bridges.Exists(b => b.S >= lo && b.S <= hi) ? "; went across node gaps at " + string.Join(", ", Bridges.FindAll(b => b.S >= lo && b.S <= hi).ConvertAll(b => b.S.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " (" + b.Gap.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " m)")) : "";
            return string.Join("; ", parts) + breaks + bridged;
        }

        /// <summary>The piece a distance is on (the later one at a joint).</summary>
        public int PieceAt(float s)
        {
            for (int i = 0; i < Pieces.Count; i++)
            {
                if (s < Pieces[i].S1 || i == Pieces.Count - 1)
                    return i;
            }

            return 0;
        }

        /// <summary>
        /// A line along the chain from s0 to s1 (as Line) in parts, cut where a segment's area meets a node's (Cuts) when both sides of a cut are a metre long at least (MarkingRuns.Items(parts)
        /// makes the items of each part apart, the pattern going on over them).
        /// </summary>
        public List<List<Vec3>> LineParts(float s0, float s1, Func<float, float> lateralAt, float surface = 0f)
        {
            var parts = new List<List<Vec3>>();
            float from = s0;
            foreach (float cut in Cuts)
            {
                if (cut - from < MarkingRuns.MinLength || s1 - cut < MarkingRuns.MinLength)
                    continue;
                parts.Add(Line(from, cut, lateralAt, surface));
                from = cut;
            }

            parts.Add(Line(from, s1, lateralAt, surface));
            return parts;
        }

        /// <summary>
        /// The line of a marking along the chain from s0 to s1 (backwards when s1 is the smaller) at a lateral, raised by surface: points at MarkingRuns.Step from s0 and the end, as MarkingRuns.Line makes
        /// them along one segment. Empty when shorter than MarkingRuns.MinLength or longer than MarkingRuns.MaxLength.
        /// </summary>
        public List<Vec3> Line(float s0, float s1, float lateral, float surface) => float.IsNaN(lateral) ? new List<Vec3>() : Line(s0, s1, s => lateral, surface);

        /// <summary>
        /// The line of a barrier (0.12.84, BarrierPath): as Line, with the points closer where the road bends so that the line is within BarrierPath.Tolerance of the curve; the same points as Line where it
        /// is already.
        /// </summary>
        public List<Vec3> LineAdaptive(float s0, float s1, Func<float, float> lateralAt, float surface, bool refineLast = true)
        {
            if (lateralAt == null || float.IsNaN(s0) || float.IsNaN(s1) || float.IsNaN(surface) || float.IsInfinity(s0) || float.IsInfinity(s1) || m_Points.Count < 2)
                return new List<Vec3>();
            s0 = Math.Max(0f, Math.Min(Length, s0));
            s1 = Math.Max(0f, Math.Min(Length, s1));
            Func<float, Vec3> at = s => PointAt(s, lateralAt(s), surface);
            float lo = Math.Min(s0, s1), hi = Math.Max(s0, s1);
            List<float> across = Breaks.FindAll(b => b > lo && b + BreakLength < hi);
            if (across.Count == 0 || hi - lo < MarkingRuns.MinLength || hi - lo > MarkingRuns.MaxLength)
                return BarrierPath.Sample(at, s0, s1, refineLast: refineLast);

            // 0.12.100: a line across a break is made in a stretch for each side and the stretches are joined in the plane (BreakBridge), not by the chain's distance, which is 0.1 m across a break that
            // is metres wide on the ground. The stretches in the order the line runs.
            var stretches = new List<(float From, float To)>();
            float from = lo;
            foreach (float b in across)
            {
                stretches.Add((from, b));
                from = b + BreakLength;
            }

            stretches.Add((from, hi));
            if (s1 < s0)
            {
                stretches.Reverse();
                for (int i = 0; i < stretches.Count; i++)
                    stretches[i] = (stretches[i].To, stretches[i].From);
            }

            List<Vec3> line = null;
            for (int i = 0; i < stretches.Count; i++)
            {
                (float a, float b) = stretches[i];
                List<Vec3> part = Math.Abs(b - a) >= MarkingRuns.MinLength
                    ? BarrierPath.Sample(at, a, b, refineLast: refineLast || i < stretches.Count - 1)
                    : new List<Vec3> { at(a), at(b) };
                if (part.Count == 0 || !part[0].IsFinite || !part[part.Count - 1].IsFinite)
                    return new List<Vec3>();
                line = line == null ? part : BreakBridge.Join(line, part, CutAt(s1 >= s0 ? across[i - 1] : across[across.Count - i], s1 >= s0, lateralAt, at));
            }

            return line;
        }

        /// <summary>
        /// Which stretch of a line across a break is cut back (BreakBridge): the one whose wall is the nearer to its road. The break is at s (the end of the piece before it); forward: the line runs the
        /// way the chain does. A wall less than a quarter of a metre further out on one side than the other, or on the centre line, has either cut.
        /// </summary>
        private BreakBridge.Cut CutAt(float s, bool forward, Func<float, float> lateralAt, Func<float, Vec3> at)
        {
            float lateral = lateralAt(s);
            if (Math.Abs(lateral) < 0.5f)
                return BreakBridge.Cut.Both;
            float outBeyond = Math.Sign(lateral) * Vec3.Dot(at(s + BreakLength) - at(s), RightAt(s));
            if (Math.Abs(outBeyond) < 0.25f)
                return BreakBridge.Cut.Both;
            // The wall of the later piece is nearer the road when it is not as far out as the earlier piece's: the later piece's stretch is the one cut back.
            return (outBeyond < 0f) == forward ? BreakBridge.Cut.After : BreakBridge.Cut.Before;
        }

        /// <summary>Whether the chain's centre line is broken between piece i and the next (Breaks): the next piece starts BreakLength on.</summary>
        public bool BrokenAfter(int i) => i >= 0 && i + 1 < Pieces.Count && Pieces[i + 1].S0 > Pieces[i].S1 + BreakLength * 0.5f;

        /// <summary>As above, with the lateral a function of the distance along the chain (a line that moves across over a transition, 7.1).</summary>
        public List<Vec3> Line(float s0, float s1, Func<float, float> lateralAt, float surface)
        {
            var result = new List<Vec3>();
            if (lateralAt == null || float.IsNaN(s0) || float.IsNaN(s1) || float.IsNaN(surface) || float.IsInfinity(s0) || float.IsInfinity(s1) || m_Points.Count < 2)
                return result;
            s0 = Math.Max(0f, Math.Min(Length, s0));
            s1 = Math.Max(0f, Math.Min(Length, s1));
            float length = Math.Abs(s1 - s0);
            if (length < MarkingRuns.MinLength || length > MarkingRuns.MaxLength)
                return result;
            float sign = s1 >= s0 ? 1f : -1f;
            int whole = (int)Math.Floor(length / MarkingRuns.Step);
            for (int i = 0; i <= whole; i++)
            {
                float d = i * MarkingRuns.Step;
                if (length - d < 0.5f && i > 0)
                    break;
                float s = s0 + sign * d;
                result.Add(PointAt(s, lateralAt(s), surface));
            }

            result.Add(PointAt(s1, lateralAt(s1), surface));
            return result;
        }

        /// <summary>
        /// Where a line at a lateral runs along the chain: from where the first piece's car lanes start to where the last one's end, on through the nodes between pieces, and cut at each junction the chain
        /// crosses (from the end of the lanes before it to the start of those after it) unless the line is an edge line on a side where no other road joins (the far side of a T junction). In the order of
        /// the chain; each at least MarkingRuns.MinLength long.
        /// </summary>
        public List<ChainSpan> Spans(float lateral, bool edgeLine) => Spans(lateral, edgeLine, false);

        /// <summary>The first piece's start, or where its lanes start.</summary>
        private float FirstFrom(float lateral, bool toNodes) => toNodes ? Pieces[0].S0 : Pieces[0].LanesS0At(lateral);

        /// <summary>Where a span ends in a piece: at the piece's end when it is the chain's last and toNodes, else where its lanes end.</summary>
        private static float SpanEnd(ChainPiece piece, bool atNode, float lateral) => atNode ? piece.S1 : piece.LanesS1At(lateral);

        /// <summary>
        /// As above; toNodes (0.12.73; the owner: "弯道墙停在路口前"): the first span starts at the first piece's start and the last ends at the last piece's end, the lane-free part of the node
        /// there included, as a wall may be asked to run on to the junction. Only the chain's two ends: where it breaks (ChainJoint.End between pieces) and at a junction it crosses, a span ends where the lanes do.
        /// </summary>
        public List<ChainSpan> Spans(float lateral, bool edgeLine, bool toNodes)
        {
            var spans = new List<ChainSpan>();
            if (Pieces.Count == 0)
                return spans;
            float from = FirstFrom(lateral, toNodes);
            for (int i = 0; i < Pieces.Count; i++)
            {
                ChainPiece piece = Pieces[i];
                bool last = i == Pieces.Count - 1;
                bool cut = !last && piece.After == ChainJoint.Junction && !(edgeLine && (lateral > 0f ? !piece.JoinRight : !piece.JoinLeft));
                if (last || cut || piece.After == ChainJoint.End)
                {
                    float to = SpanEnd(piece, last && toNodes, lateral);
                    if (to - from >= MarkingRuns.MinLength)
                        spans.Add(new ChainSpan(from, to));
                    if (!last)
                        from = Pieces[i + 1].LanesS0At(lateral);
                }
            }

            return spans;
        }

        /// <summary>
        /// Where a barrier at a lateral runs along the chain by what it does at junctions (0.12.16, BarrierAuto.JunctionMode): Side as an edge line (on through a junction on a side no road joins), Cut at
        /// every junction, Through from the first piece's lanes to the last's.
        /// </summary>
        public List<ChainSpan> Spans(float lateral, JunctionMode mode) => Spans(lateral, mode, false);

        /// <summary>As above; a median barrier (median) in Side goes on through a junction a road joins from one side only and is cut at one joined from both (0.12.17: 0.12.16 took it for a line on the left and cut it wherever a road joined on the left).</summary>
        public List<ChainSpan> Spans(float lateral, JunctionMode mode, bool median) => Spans(lateral, mode, median, null, false);

        /// <summary>
        /// As above (0.12.67): a wall in Side mode is open at a junction only at the mouths of the roads that join on its side, where they are known (JunctionMouths); lateralAt is where the wall
        /// stands at a distance along the chain (a wall that follows the road's edge), else it stands at lateral. A median wall, Cut and Through are as they were.
        /// </summary>
        public List<ChainSpan> Spans(float lateral, JunctionMode mode, bool median, Func<float, float> lateralAt) => Spans(lateral, mode, median, lateralAt, false);

        /// <summary>As above; toNodes: the chain's first and last span run on to the first piece's start and the last piece's end (the other overload of Spans says how).</summary>
        public List<ChainSpan> Spans(float lateral, JunctionMode mode, bool median, Func<float, float> lateralAt, bool toNodes)
        {
            if (mode == JunctionMode.Side && !median)
                return SideSpans(lateral, lateralAt, toNodes);
            if (median && mode == JunctionMode.Side)
            {
                var cut = new List<ChainSpan>();
                if (Pieces.Count == 0)
                    return cut;
                float from = FirstFrom(lateral, toNodes);
                for (int i = 0; i < Pieces.Count; i++)
                {
                    ChainPiece piece = Pieces[i];
                    bool last = i == Pieces.Count - 1;
                    if (last || piece.After == ChainJoint.End || (piece.After == ChainJoint.Junction && piece.JoinLeft && piece.JoinRight))
                    {
                        float to = SpanEnd(piece, last && toNodes, lateral);
                        if (to - from >= MarkingRuns.MinLength)
                            cut.Add(new ChainSpan(from, to));
                        if (!last)
                            from = Pieces[i + 1].LanesS0At(lateral);
                    }
                }

                return cut;
            }

            if (mode != JunctionMode.Through)
                return Spans(lateral, mode == JunctionMode.Side, toNodes);
            var spans = new List<ChainSpan>();
            if (Pieces.Count > 0)
            {
                float from = FirstFrom(lateral, toNodes), to = SpanEnd(Pieces[Pieces.Count - 1], toNodes, lateral);
                if (to - from >= MarkingRuns.MinLength)
                    spans.Add(new ChainSpan(from, to));
            }

            return spans;
        }

        /// <summary>
        /// The stretches of a wall in Side mode (0.12.67): as Spans(lateral, true) cuts a line at a junction a road joins on its side, from the end of the lanes before it to the start of those after
        /// it, except that where the roads that join are known (ChainPiece.MouthsKnown) it is open at their mouths only and runs on along the edge of the node between them.
        /// </summary>
        private List<ChainSpan> SideSpans(float lateral, Func<float, float> lateralAt, bool toNodes)
        {
            var spans = new List<ChainSpan>();
            if (Pieces.Count == 0)
                return spans;
            int side = lateral > 0f ? 1 : -1;
            var openings = new List<ChainSpan>();
            float from = FirstFrom(lateral, toNodes);
            for (int i = 0; i < Pieces.Count; i++)
            {
                ChainPiece piece = Pieces[i];
                bool last = i == Pieces.Count - 1;
                bool cut = !last && piece.After == ChainJoint.Junction && (lateral > 0f ? piece.JoinRight : piece.JoinLeft);
                if (!(last || cut || piece.After == ChainJoint.End))
                    continue;
                if (cut && JunctionMouths.TryOpenings(piece, Pieces[i + 1], lateralAt != null ? lateralAt(piece.S1) : lateral, side, openings))
                {
                    foreach (ChainSpan opening in openings)
                    {
                        if (opening.From - from >= MarkingRuns.MinLength)
                            spans.Add(new ChainSpan(from, opening.From));
                        from = Math.Max(from, opening.To);
                    }

                    continue;
                }

                float to = SpanEnd(piece, last && toNodes, lateral);
                if (to - from >= MarkingRuns.MinLength)
                    spans.Add(new ChainSpan(from, to));
                if (!last)
                    from = Pieces[i + 1].LanesS0At(lateral);
            }

            return spans;
        }

        /// <summary>The span (of Spans) a distance is in; or the nearest end of one when it is in none (inside a junction). NaN when there is no span.</summary>
        public static float Snap(List<ChainSpan> spans, float s, float within)
        {
            float best = float.NaN, distance = float.MaxValue;
            foreach (ChainSpan span in spans)
            {
                if (s >= span.From && s <= span.To)
                {
                    if (s - span.From < within)
                        return span.From;
                    if (span.To - s < within)
                        return span.To;
                    return s;
                }

                foreach (float end in new[] { span.From, span.To })
                {
                    float d = Math.Abs(end - s);
                    if (d < distance)
                    {
                        distance = d;
                        best = end;
                    }
                }
            }

            return best;
        }

        /// <summary>The parts of the stretch from s0 to s1 (either way) that are in the spans, in the order the stretch runs (from s0 to s1), each at least MarkingRuns.MinLength.</summary>
        public static List<ChainSpan> Clip(List<ChainSpan> spans, float s0, float s1)
        {
            var parts = new List<ChainSpan>();
            float lo = Math.Min(s0, s1), hi = Math.Max(s0, s1);
            foreach (ChainSpan span in spans)
            {
                float a = Math.Max(lo, span.From), b = Math.Min(hi, span.To);
                if (b - a >= MarkingRuns.MinLength)
                    parts.Add(s1 >= s0 ? new ChainSpan(a, b) : new ChainSpan(b, a));
            }

            if (s1 < s0)
                parts.Reverse();
            return parts;
        }
    }
}
