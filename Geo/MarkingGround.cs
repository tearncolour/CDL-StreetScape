using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// A marking laid on the ground as it is now (0.12.32; the owner: "修复地面表现悬浮问题"). Until then a marking kept the heights of the moment it was placed: a line along a road ran at the height of the
    /// road's centre line plus the surface where it was first clicked (on a footway or a median that is 20 cm above the carriageway, all of it), a thing put down with one click before 0.12.30 stood flat at
    /// the cursor's height, and nothing followed a road that was raised, lowered or rebuilt later. Here the heights are taken again from what is under the item (onSurface: the caller reads the game's
    /// roads and terrain): a run's path gets the heights of the surface under each of its points, a thing put down with one click a GroundGrid. The item's saved settings are not changed: what is laid is
    /// made again from them and the roads whenever either changes. Pure.
    /// </summary>
    public static class MarkingGround
    {
        /// <summary>What is drawn for a marking: its settings with the heights of the ground, and how far its origin is above or below the item's height (m; a run's path starts at height 0).</summary>
        public sealed class Laid
        {
            public string Spec;
            public float Dy;

            /// <summary>How far the marking was from the ground before, at the worst of its points (m; + above it, - below).</summary>
            public float Worst;

            public int Points;

            /// <summary>The road under the paint of a wide marking that reaches beyond it (RunCut.Needed, 0.12.83), in the laid item's frame: the paint is cut to it when drawn. Null when the paint lies on the road, or the marking is not cut.</summary>
            public AreaFit Cut;
        }

        /// <summary>A point more than this (m) from the ground it was placed on counts as off it in the log.</summary>
        public const float Off = 0.05f;

        /// <summary>How far in front of a kerb line's path (the foot of the kerb's face, toward the road) its heights are read (m).</summary>
        public const float FootProbe = 0.15f;

        /// <summary>
        /// A kerb line's points at the heights of the carriageway just in front of the kerb (0.12.35; kerbOnRight: the kerb is on the right of the way the line runs). Read on the path itself, which is on
        /// the face since the kerb line is put on the road's kerb (0.12.33), the heights were the footway's, and the paint stood a kerb's height over its foot. In front of the face is the strip of the
        /// footway piece at the carriageway's level, read as such since 0.12.37 (SurfaceReader.Pieces).
        /// </summary>
        public static List<Vec3> AtFoot(List<Vec3> line, bool kerbOnRight, Func<List<Vec3>, List<Vec3>> onSurface)
        {
            List<Vec3> on = onSurface(MarkingFills.Offset(line, kerbOnRight ? -FootProbe : FootProbe));
            var laid = new List<Vec3>(line.Count);
            for (int i = 0; i < line.Count; i++)
                laid.Add(new Vec3(line[i].X, on[i].Y, line[i].Z));
            return laid;
        }

        /// <summary>
        /// Whether a marking that was laid from a read that took a road piece as one surface (the game was still loading the piece: SurfaceReader.Pieces) is left as it was instead (0.12.84). A kerb
        /// line's foot is read in front of the kerb's face, which in such a read is the footway's top: the paint would stand on the kerb. The heights it was placed with were read when the piece was
        /// known, and it is laid when the piece becomes known.
        /// </summary>
        public static bool KeepsPlaced(SignDef def) => def != null && MarkingTemplates.IsKerbLine(def.Id);

        /// <summary>What a marking is drawn with after a lay: the lay, or what it had before (null: as placed) when the lay was made from a provisional read and the marking is one that KeepsPlaced.</summary>
        public static Laid Settle(SignDef def, Laid laid, Laid before, bool provisional) => laid != null && provisional && KeepsPlaced(def) ? before : laid;

        /// <summary>The lowest and highest height (m, world) of a laid run's path, for the log; the path starts at originY.</summary>
        public static void PathHeights(GuideTemplate template, string spec, float originY, out float low, out float high)
        {
            low = high = originY;
            foreach (Vec3 p in RunPath.Decode(template.Parse(spec).Get(RunPath.Key)))
            {
                low = Math.Min(low, originY + p.Y);
                high = Math.Max(high, originY + p.Y);
            }
        }

        /// <summary>Whether a marking is laid on the ground: a run (a line, a barrier) or a thing put down with one click that lies on the ground (MarkingTemplates.Drapes); not an area fitted to the road on its own.</summary>
        public static bool Lies(SignDef def) =>
            def != null && def.IsMarking && def.Template != null && !MarkingTemplates.IsFittedArea(def.Id)
            && (MarkingTemplates.IsRun(def.Template) || (MarkingTemplates.Drapes(def.Id) && def.Template.FieldOf(GroundGrid.Key) != null));

        /// <summary>
        /// The marking laid on the ground; null when it has nothing to lay (no path, no paint) or is not one that lies. onSurface gives the world points it is given at the heights of what is under them (its
        /// own height where there is nothing).
        /// </summary>
        public static Laid Lay(SignDef def, string spec, Vec3 position, float yaw, Func<List<Vec3>, List<Vec3>> onSurface)
        {
            if (!Lies(def) || onSurface == null)
                return null;
            GuideTemplate template = def.Template;
            if (MarkingTemplates.IsRun(template))
                return LayRun(template, spec, position, yaw, onSurface);
            MarkingPlan plan = MarkingTemplates.PlanOf(def, spec);
            if (plan == null || plan.MaxX - plan.MinX < 0.01f || plan.MaxZ - plan.MinZ < 0.01f)
                return null;
            GuideSpec parsed = template.Parse(spec);
            GroundGrid before = GroundGrid.Parse(parsed.Get(GroundGrid.Key));
            float grade = (float)parsed.Number("g", 0.0);
            GroundGrid ground = GridUnder(plan, position, yaw, onSurface, (x, z) => before != null ? before.At(x, z) : z * grade);
            float worst = 0f;
            for (int j = 0; j < ground.Nz; j++)
            {
                for (int i = 0; i < ground.Nx; i++)
                {
                    GroundGrid.PointOf(ground.X0, ground.X1, ground.Z0, ground.Z1, ground.Nx, ground.Nz, i, j, out float x, out float z);
                    float was = before != null ? before.At(x, z) : z * grade;
                    worst = Worse(worst, was - ground.Heights[j * ground.Nx + i]);
                }
            }

            // The grid takes the place of the grade (MarkingPlan.Bake); a flat one is written as none, and the item then lies flat at its height.
            string laid = MarkingTemplates.WithGrade(template, MarkingTemplates.WithGround(template, spec, ground), 0f);
            return new Laid { Spec = laid, Dy = 0f, Worst = worst, Points = ground.Heights.Length };
        }

        /// <summary>
        /// The ground under the box of a marking's paint and solids: a point every GroundGrid.Step or so, each at the height onSurface gives it, relative to the item's height. planned gives the height (item
        /// frame) each point is at before, so that the surface nearest it is the one taken where two lie over each other. A bent marking's grid is over its straight box, each point read where the bend
        /// puts it (0.12.49, MarkingPlan.Bake takes the heights before it bends).
        /// </summary>
        public static GroundGrid GridUnder(MarkingPlan plan, Vec3 position, float yaw, Func<List<Vec3>, List<Vec3>> onSurface, Func<float, float, float> planned = null)
        {
            bool bent = plan.Bend != null;
            float x0 = bent ? plan.StraightMinX : plan.MinX, x1 = bent ? plan.StraightMaxX : plan.MaxX, z0 = bent ? plan.StraightMinZ : plan.MinZ, z1 = bent ? plan.StraightMaxZ : plan.MaxZ;
            GroundGrid.Count(x0, x1, z0, z1, out int nx, out int nz);
            var local = new List<Vec3>(nx * nz);
            for (int j = 0; j < nz; j++)
            {
                for (int i = 0; i < nx; i++)
                {
                    GroundGrid.PointOf(x0, x1, z0, z1, nx, nz, i, j, out float x, out float z);
                    Vec3 at = bent ? plan.Bend.Map(x, z) : new Vec3(x, 0f, z);
                    local.Add(new Vec3(at.X, planned != null ? planned(x, z) : 0f, at.Z));
                }
            }

            List<Vec3> on = onSurface(MarkingPolygon.ToWorld(local, position, yaw));
            var grid = new GroundGrid { X0 = x0, X1 = x1, Z0 = z0, Z1 = z1, Nx = nx, Nz = nz, Heights = new float[nx * nz] };
            for (int k = 0; k < grid.Heights.Length; k++)
                grid.Heights[k] = on[k].Y - position.Y;
            return grid;
        }

        private static Laid LayRun(GuideTemplate template, string spec, Vec3 position, float yaw, Func<List<Vec3>, List<Vec3>> onSurface)
        {
            GuideSpec parsed = template.Parse(spec);
            List<Vec3> path = RunPath.Decode(parsed.Get(RunPath.Key));
            if (path.Count < 2)
                return null;
            List<Vec3> world = MarkingPolygon.ToWorld(path, position, yaw);
            List<Vec3> on = MarkingTemplates.IsKerbLine(template.Id) ? AtFoot(world, parsed.Is("side", "r"), onSurface) : onSurface(world);
            float worst = 0f;
            for (int i = 0; i < world.Count; i++)
                worst = Worse(worst, world[i].Y - on[i].Y);
            string laidPath = RunPath.Encode(on, out _);
            if (laidPath.Length == 0)
                return null;
            // The heights are the path's alone: a grade the settings may have (0 for a run placed since 0.12.0) would tilt the laid line again.
            string laid = MarkingTemplates.WithGrade(template, MarkingTemplates.WithPath(template, spec, laidPath), 0f);
            return new Laid { Spec = laid, Dy = on[0].Y - position.Y, Worst = worst, Points = on.Count };
        }

        private static float Worse(float worst, float d) => Math.Abs(d) > Math.Abs(worst) ? d : worst;
    }
}
