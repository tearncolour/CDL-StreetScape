using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.66, the surface of a railway, a metro or a tram line (the owner: "支持吸附到铁路和地铁线路"): which pieces of a track composition a post or a wall may stand on (SurfaceSection.StandableTrack).
    /// The pieces' flags of the game's tracks were not seen (NetPieceFlags has no track flag; only roads' pieces were read in the game), so the rule takes the pieces flagged a surface as a road's are,
    /// and where none is flagged the bed. Pure; the reading of the game's edges (SurfaceReader.Around with tracks) is ECS and is not tested here.
    /// </summary>
    internal static class RailSurfaceTests
    {
        public static void RunAll()
        {
            Program.Run("rail surface: pieces flagged a surface are chosen as a road's are", Flagged);
            Program.Run("rail surface: with no piece flagged a surface the bed is chosen", Bed);
            Program.Run("rail surface: nothing for a tunnel, for faces alone, for no pieces", Nothing);
            Program.Run("rail surface: a viaduct's surface puts the wall's line on the deck", Viaduct);
        }

        private static SectionPiece Piece(float x, float width, float top, bool surface = false, bool layer = false, bool other = false, bool blocks = false, string name = "p") =>
            new SectionPiece { X = x, Width = width, Top = top, Surface = surface, Layer = layer, OtherLevel = other, BlocksTraffic = blocks, Name = name };

        /// <summary>The height of the stretch a place across the composition is in (m from its middle; the composition is width wide); NaN where none is.</summary>
        private static float At(List<PaintableStretch> stretches, float width, float x)
        {
            float f = x / width + 0.5f;
            foreach (PaintableStretch s in stretches)
            {
                if (f >= s.F0 - 1e-4f && f <= s.F1 + 1e-4f)
                    return s.Y;
            }

            return float.NaN;
        }

        private static void Flagged()
        {
            // A double track on a ground bed 12 m wide: the ballast 6 m flagged a surface, its two shoulders 3 m each, 0.3 m lower; a face of the cut each side.
            var pieces = new List<SectionPiece>
            {
                Piece(0f, 6f, 0.3f, surface: true, name: "ballast"), Piece(-4.5f, 3f, 0f, surface: true, name: "shoulder"), Piece(4.5f, 3f, 0f, surface: true, name: "shoulder"),
                Piece(-6.1f, 0.2f, -0.5f, surface: true, layer: true, name: "cut face"), Piece(6.1f, 0.2f, -0.5f, surface: true, layer: true, name: "cut face")
            };
            var log = new List<string>();
            List<PaintableStretch> track = SurfaceSection.StandableTrack(pieces, 12.2f, log);
            List<PaintableStretch> road = SurfaceSection.Standable(pieces, 12.2f);
            Program.Check(track.Count == road.Count && track.Count == 3, $"flagged pieces: the same stretches as a road's rule gives ({track.Count} against {road.Count})");
            bool same = track.Count == road.Count;
            for (int i = 0; same && i < track.Count; i++)
                same = track[i].F0 == road[i].F0 && track[i].F1 == road[i].F1 && track[i].Y == road[i].Y;
            Program.Check(same, "to the last figure");
            Program.Near(At(track, 12.2f, 0f), 0.3f, 1e-4f, "the ballast is 0.3 m up");
            Program.Near(At(track, 12.2f, 4.5f), 0f, 1e-4f, "the shoulder is at 0");
            Program.Check(float.IsNaN(At(track, 12.2f, 6.1f)), "the face of the cut is not stood on");
            Program.Check(log.Exists(l => l.Contains("stand")) && !log.Exists(l => l.Contains("bed")), "the log says stand, and not bed: " + string.Join(" | ", log).Substring(0, 60));
        }

        private static void Bed()
        {
            // The same line with no piece flagged a surface (what a track's pieces may be): the sleepers' bed 3.5 m, the shoulders 2 m each at 2 to 4 m, a noise wall 0.3 m wide 1.3 m up at 4.3 m, a face
            // at 4.6 m, and a piece on another level (a drain under the bed) at 0.
            var pieces = new List<SectionPiece>
            {
                Piece(0f, 3.5f, 0.1f, name: "bed"), Piece(-3f, 2f, 0f, name: "shoulder"), Piece(3f, 2f, 0f, name: "shoulder"),
                Piece(4.3f, 0.3f, 1.3f, name: "wall"), Piece(-4.3f, 0.3f, 1.3f, name: "wall"),
                Piece(4.6f, 0.2f, -0.5f, layer: true, name: "face"), Piece(-4.6f, 0.2f, -0.5f, layer: true, name: "face"),
                Piece(0f, 1f, -2f, other: true, name: "drain")
            };
            var log = new List<string>();
            List<PaintableStretch> bed = SurfaceSection.StandableTrack(pieces, 10f, log);
            Program.Check(bed.Count > 0, "there are stretches though no piece is flagged a surface: " + bed.Count);
            Program.Near(At(bed, 10f, 0f), 0.1f, 1e-4f, "the bed is at 0.1 m");
            Program.Near(At(bed, 10f, 3f), 0f, 1e-4f, "the shoulder is at 0");
            Program.Near(At(bed, 10f, -3f), 0f, 1e-4f, "and the other");
            Program.Check(float.IsNaN(At(bed, 10f, 4.3f)) && float.IsNaN(At(bed, 10f, -4.3f)), "a wall 1.3 m up is not stood on");
            Program.Check(float.IsNaN(At(bed, 10f, 4.6f)), "nor a face of the deck");
            Program.Check(!bed.Exists(s => s.Y < -1f), "the piece on another level (the drain, -2 m) is no stretch");
            Program.Check(log.Exists(l => l.Contains("bed (no piece")) && log.Exists(l => l.Contains("a wall")) && log.Exists(l => l.Contains("on another level")), "the log says what became of each piece: " + log.Count + " lines");
            // The road's rule gives nothing for the same pieces: that is what the bed is for.
            Program.Check(SurfaceSection.Standable(pieces, 10f).Count == 0, "the road's rule finds no surface there");
            // A raised track (a bed 0.3 m above its shoulders is fine, 0.6 m is a wall): the limit is MaxStandRise.
            var step = new List<SectionPiece> { Piece(0f, 2f, 0.6f), Piece(2f, 2f, 0f) };
            List<PaintableStretch> stepped = SurfaceSection.StandableTrack(step, 4f);
            Program.Check(float.IsNaN(At(stepped, 4f, 0f)) && !float.IsNaN(At(stepped, 4f, 2f)), $"a piece {SurfaceSection.MaxStandRise + 0.2f:0.0} m above the lowest is left out");
        }

        private static void Nothing()
        {
            // A metro in a tunnel: every piece is on another level.
            var tunnel = new List<SectionPiece> { Piece(0f, 4f, 0f, other: true), Piece(2.5f, 1f, 0f, surface: true, other: true) };
            Program.Check(SurfaceSection.StandableTrack(tunnel, 6f).Count == 0, "a tunnel: no stretch");
            var faces = new List<SectionPiece> { Piece(0f, 4f, 0f, layer: true), Piece(2.5f, 1f, 0f, surface: true, layer: true) };
            Program.Check(SurfaceSection.StandableTrack(faces, 6f).Count == 0, "faces alone: no stretch");
            Program.Check(SurfaceSection.StandableTrack(new List<SectionPiece>(), 6f).Count == 0 && SurfaceSection.StandableTrack(null, 6f).Count == 0, "no pieces, no list: no stretch");
            Program.Check(SurfaceSection.StandableTrack(new List<SectionPiece> { Piece(0f, 4f, 0f) }, 0f).Count == 0, "no width: no stretch");
        }

        private static void Viaduct()
        {
            // A viaduct 12 m up, 10 m wide, with the bed's pieces unflagged; the track's centre line runs along +Z at x = 0 (the left of the line at -5, the right at +5) and the terrain is 2.3 m under it.
            var pieces = new List<SectionPiece> { Piece(0f, 7f, 0.2f, name: "deck"), Piece(-4f, 2f, 0.1f, name: "walk"), Piece(4f, 2f, 0.1f, name: "walk"), Piece(5.1f, 0.2f, -0.6f, layer: true) };
            List<PaintableStretch> stretches = SurfaceSection.StandableTrack(pieces, 10.4f);
            var strips = new List<SurfaceStrip>();
            foreach (PaintableStretch s in stretches)
                strips.AddRange(SurfaceSection.Bands(t => new Vec3(-5.2f, 12f, 200f * t), t => new Vec3(5.2f, 12f, 200f * t), s, 20, "track"));
            Program.Check(strips.Count > 0, "bands of the stretches: " + strips.Count);
            AreaFit Read(float minX, float minZ, float maxX, float maxZ, float originY) => AreaFit.FromStrips(strips, new Vec3(0f, originY, 0f), 0f, minX, maxX, minZ, maxZ);
            var line = new List<Vec3>();
            for (float z = 10f; z <= 150f; z += 2f)
                line.Add(new Vec3(4.6f, 12f, z));
            List<Vec3> stood = new SurfaceHeights().Put(line, "viaduct", line.Count, Read, (x, y, z) => 9.7f);
            float worst = 0f;
            foreach (Vec3 p in stood)
                worst = Math.Max(worst, Math.Abs(p.Y - 12.1f));
            Program.Check(stood.Count == line.Count && worst < 0.01f, $"a wall 4.6 m from the centre line stands on the walkway, 12.1 m ({worst * 1000f:0.0} mm off), not on the ground at 9.7 m");
            // With the surface not read (what 0.12.66's first step had) the terrain 2.3 m down would have been taken.
            List<Vec3> without = new SurfaceHeights().Put(line, "none", line.Count, (a, b, c, d, e) => null, (x, y, z) => 9.7f);
            Program.Check(Math.Abs(without[5].Y - 9.7f) < 0.01f, "with no surface read the wall went down to the ground: " + without[5].Y.ToString("0.0"));
        }
    }
}
