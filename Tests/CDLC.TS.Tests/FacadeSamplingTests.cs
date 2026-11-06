using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The sampling of a building's facade (Geo/FacadeSampling.cs): the rays, what is kept of what they hit, when to ask and to read, the verdict. The game's raycast is stood in for by circles in plan
    /// that a segment meets from outside (a tower) or from inside (a curved front seen from its concave side).
    /// </summary>
    internal static class FacadeSamplingTests
    {
        public static void RunAll()
        {
            Program.Run("facade sampling: the rays across the board", PlanRays);
            Program.Run("facade sampling: when to read, when to ask, why not", Rules);
            Program.Run("facade sampling: what is kept of the hits", Keeping);
            Program.Run("facade sampling: towers of every radius seen from every side", Towers);
            Program.Run("facade sampling: concave fronts, noise, flat walls, corners, neighbours", Others);
            Program.Run("facade sampling: the eye straight out from the wall", StraightIn);
            Program.Run("facade sampling: the log", LogLines);
        }

        // ------------------------------------------------------------------ a stand-in for the game's raycast

        /// <summary>The first point where the segment meets the circle, and the circle's normal there pointing at the viewer (outward for a tower, inward for a concave front).</summary>
        private static bool Cross(FacadeRay ray, double cx, double cz, double radius, bool concave, out double x, out double z, out double nx, out double nz)
        {
            x = z = nx = nz = 0.0;
            double dx = ray.ToX - ray.FromX, dz = ray.ToZ - ray.FromZ;
            double fx = ray.FromX - cx, fz = ray.FromZ - cz;
            double a = dx * dx + dz * dz, b = 2.0 * (fx * dx + fz * dz), c = fx * fx + fz * fz - radius * radius;
            double disc = b * b - 4.0 * a * c;
            if (a < 1e-12 || disc < 0.0)
                return false;
            double root = Math.Sqrt(disc), t1 = (-b - root) / (2.0 * a), t2 = (-b + root) / (2.0 * a);
            double t = t1 >= 0.0 && t1 <= 1.0 ? t1 : t2 >= 0.0 && t2 <= 1.0 ? t2 : double.NaN;
            if (double.IsNaN(t))
                return false;
            x = ray.FromX + t * dx;
            z = ray.FromZ + t * dz;
            double sign = concave ? -1.0 : 1.0;
            nx = sign * (x - cx) / radius;
            nz = sign * (z - cz) / radius;
            return true;
        }

        private sealed class Scene
        {
            public float PointX, PointZ, NormalX, NormalZ;
            public List<FacadeHit> Hits;
            public FacadeVerdict Verdict;
        }

        /// <summary>
        /// A tower (or a concave front) of a radius, the cursor aiming at the wall at an angle round it, an eye at a distance and a side angle from the wall's normal there: the tool's own ray finds the
        /// point and the normal as the game's does, the rays are planned, the circle answers them, the verdict is made. noise: the hits' x and z moved by up to that many metres.
        /// </summary>
        private static Scene Look(double radius, bool concave, double aimDeg, double sideDeg, double distance, float width, double noise = 0.0, int seed = 1)
        {
            double phi = aimDeg * Math.PI / 180.0, side = sideDeg * Math.PI / 180.0;
            double px = radius * Math.Cos(phi), pz = radius * Math.Sin(phi);
            double toward = concave ? -1.0 : 1.0;
            // The viewer is on the outside of a tower and on the inside of a concave front.
            double ex = px + toward * distance * Math.Cos(phi + side), ez = pz + toward * distance * Math.Sin(phi + side), ey = 30.0;
            var aim = new FacadeRay((float)ex, (float)ey, (float)ez, (float)(px * 1.0 + (px - ex) * 0.2), 10f, (float)(pz * 1.0 + (pz - ez) * 0.2));
            if (!Cross(aim, 0.0, 0.0, radius, concave, out double hx, out double hz, out double nx, out double nz))
                return null;
            double nl = Math.Sqrt(nx * nx + nz * nz);
            var scene = new Scene { PointX = (float)hx, PointZ = (float)hz, NormalX = (float)(nx / nl), NormalZ = (float)(nz / nl), Hits = new List<FacadeHit>() };
            List<FacadeRay> rays = FacadeSampling.Plan((float)ex, (float)ey, (float)ez, scene.PointX, 10f, scene.PointZ, scene.NormalX, scene.NormalZ, width);
            var random = new Random(seed);
            foreach (FacadeRay ray in rays)
            {
                if (Cross(ray, 0.0, 0.0, radius, concave, out double x, out double z, out _, out _))
                    scene.Hits.Add(new FacadeHit(true, true, (float)(x + (random.NextDouble() - 0.5) * 2.0 * noise), 10f, (float)(z + (random.NextDouble() - 0.5) * 2.0 * noise), 0f));
                else
                    scene.Hits.Add(new FacadeHit(false, false, 0f, 0f, 0f, 0f));
            }

            scene.Verdict = FacadeSampling.Decide(scene.Hits, scene.PointX, scene.PointZ, scene.NormalX, scene.NormalZ, width);
            return scene;
        }

        // ------------------------------------------------------------------ the rays

        private static void PlanRays()
        {
            // The eye at (0, 20, -40) looks at a wall whose outward normal is -z, the cursor at (0, 10, 0): the tangent is +x, the board 10 m wide: half 6 m.
            List<FacadeRay> rays = FacadeSampling.Plan(0f, 20f, -40f, 0f, 10f, 0f, 0f, -1f, 10f);
            Program.Check(rays.Count == FacadeSampling.Rays && FacadeSampling.Rays == 7, "seven rays: " + rays.Count);
            for (int i = 0; i < rays.Count; i++)
            {
                FacadeRay r = rays[i];
                Program.Check(r.FromX == 0f && r.FromY == 20f && r.FromZ == -40f, "ray " + i + " starts at the eye");
                // Where the segment crosses the wall's plane z = 0.
                float t = (0f - r.FromZ) / (r.ToZ - r.FromZ);
                float x = r.FromX + t * (r.ToX - r.FromX), y = r.FromY + t * (r.ToY - r.FromY);
                Program.Near(x, -6f + 12f * i / 6f, 1e-3f, "ray " + i + " crosses the plane where it was aimed: " + x);
                Program.Near(y, 10f, 1e-3f, "at the height of the cursor: " + y);
                Program.Check(t > 0f && t < 1f, "the plane is on the segment, with a part of it beyond (" + t + ")");
            }

            // It goes on past the plane by 6 m at least, and by half the width of a wide board.
            double beyondSmall = Math.Sqrt(Math.Pow(rays[3].ToX - 0f, 2) + Math.Pow(rays[3].ToY - 10f, 2) + Math.Pow(rays[3].ToZ - 0f, 2));
            Program.Near((float)beyondSmall, 6f, 1e-3f, "6 m past the middle one for a board of 10 m: " + beyondSmall);
            List<FacadeRay> wide = FacadeSampling.Plan(0f, 20f, -40f, 0f, 10f, 0f, 0f, -1f, 80f);
            double beyondWide = Math.Sqrt(Math.Pow(wide[3].ToX - 0f, 2) + Math.Pow(wide[3].ToY - 10f, 2) + Math.Pow(wide[3].ToZ - 0f, 2));
            Program.Near((float)beyondWide, 25f, 1e-3f, "the width is capped: half 25 m, 25 m beyond: " + beyondWide);
            float firstX = wide[0].ToX, lastX = wide[6].ToX;
            Program.Check(lastX - firstX > 49f, "and the rays spread over 50 m: " + (lastX - firstX));
            List<FacadeRay> narrow = FacadeSampling.Plan(0f, 20f, -40f, 0f, 10f, 0f, 0f, -1f, 0.2f);
            float narrowSpread = narrow[6].ToX - narrow[0].ToX;
            Program.Check(narrowSpread > 1.9f && narrowSpread < 2.6f, "a very narrow board is sampled over a metre each side: " + narrowSpread);

            // The tangent turns with the normal: a wall facing +x is sampled along z.
            List<FacadeRay> east = FacadeSampling.Plan(40f, 20f, 0f, 0f, 10f, 0f, 1f, 0f, 10f);
            float tEast = (0f - east[0].FromX) / (east[0].ToX - east[0].FromX);
            float zFirst = east[0].FromZ + tEast * (east[0].ToZ - east[0].FromZ), zLast = east[6].FromZ + (0f - east[6].FromX) / (east[6].ToX - east[6].FromX) * (east[6].ToZ - east[6].FromZ);
            Program.Check(Math.Abs(Math.Abs(zFirst) - 6f) < 1e-3f && Math.Abs(zLast + zFirst) < 1e-3f, "a wall facing +x is sampled along z, from " + zFirst + " to " + zLast);
            Program.Check(zFirst < 0f, "the first ray is on the negative side of the tangent (-n.z, n.x) = (0, 1)");

            // A normal that is not level is made level; none at all, or an eye that is not a number, plans nothing.
            Program.Check(FacadeSampling.Plan(0f, 20f, -40f, 0f, 10f, 0f, 0f, 0f, 10f).Count == 0, "no horizontal normal: no rays");
            Program.Check(FacadeSampling.Plan(float.NaN, 20f, -40f, 0f, 10f, 0f, 0f, -1f, 10f).Count == 0, "an eye that is not a number: no rays");
            Program.Check(FacadeSampling.Plan(0f, 20f, -40f, 0f, 10f, float.PositiveInfinity, 0f, -1f, 10f).Count == 0, "a point that is not finite: no rays");
            Program.Check(FacadeSampling.Plan(0f, 20f, -40f, 0f, 10f, 0f, 0f, -3f, 10f).Count == 7 && Math.Abs(FacadeSampling.Plan(0f, 20f, -40f, 0f, 10f, 0f, 0f, -3f, 10f)[6].ToX - rays[6].ToX) < 1e-3f, "the normal's length does not matter");
        }

        // ------------------------------------------------------------------ the rules

        private static void Rules()
        {
            // Reading: the answer is there in the frame after the ask and in no other (0.12.77 read it two frames on: every time empty, the radius 0, not a line in the log).
            Program.Check(FacadeSampling.Next(0, false) == FacadeWait.Wait && FacadeSampling.Next(0, true) == FacadeWait.Wait, "the frame of the ask: wait, whatever the result holds");
            Program.Check(FacadeSampling.Next(1, true) == FacadeWait.Read, "the next frame, answered: read");
            Program.Check(FacadeSampling.Next(1, false) == FacadeWait.Late, "the next frame, not answered: the game did not answer");
            Program.Check(FacadeSampling.Next(2, true) == FacadeWait.Late && FacadeSampling.Next(2, false) == FacadeWait.Late && FacadeSampling.Next(30, true) == FacadeWait.Late, "two frames or more on: gone, even if the result holds something");
            Program.Check(FacadeSampling.Next(-1, true) == FacadeWait.Wait, "a frame count that went back: wait");

            // Asking.
            Program.Check(!FacadeSampling.ShouldAsk(true, 100, 5f, true, 0), "one is out: no second");
            Program.Check(!FacadeSampling.ShouldAsk(false, 9, 5f, true, 0), "9 frames since the last: too soon");
            Program.Check(FacadeSampling.ShouldAsk(false, 10, 0.6f, false, 0), "10 frames and the place moved 0.6 m: ask");
            Program.Check(!FacadeSampling.ShouldAsk(false, 10, 0.5f, false, 0), "moved exactly half a metre: not yet");
            Program.Check(!FacadeSampling.ShouldAsk(false, 10, 0.4f, false, 0), "moved 0.4 m: no");
            Program.Check(FacadeSampling.ShouldAsk(false, 10, 0f, true, 0), "another building: ask");
            Program.Check(FacadeSampling.ShouldAsk(false, 10, float.MaxValue, false, 0), "never asked before: ask");
            Program.Check(!FacadeSampling.ShouldAsk(false, 29, 5f, false, 2) && FacadeSampling.ShouldAsk(false, 30, 5f, false, 2), "after two unanswered asks it waits 30 frames");
            Program.Check(!FacadeSampling.ShouldAsk(false, 69, 5f, false, 50) && FacadeSampling.ShouldAsk(false, 70, 5f, false, 50), "and never more than 70");
            Program.Check(FacadeSampling.ShouldAsk(false, 10, 5f, false, -3), "a negative count is none");

            // Why not.
            Program.Check(FacadeSampling.Why(true, false, true, true, true) == FacadeSkip.None, "all well: asked");
            Program.Check(FacadeSampling.Why(false, true, false, false, false) == FacadeSkip.FeatureOff, "the feature off comes first");
            Program.Check(FacadeSampling.Why(true, true, false, false, false) == FacadeSkip.Broken, "then a failed read");
            Program.Check(FacadeSampling.Why(true, false, false, true, true) == FacadeSkip.NotText, "then the board");
            Program.Check(FacadeSampling.Why(true, false, true, false, false) == FacadeSkip.NotWallFlat, "then the support");
            Program.Check(FacadeSampling.Why(true, false, true, true, false) == FacadeSkip.NoBuilding, "then the building");
            foreach (FacadeSkip skip in new[] { FacadeSkip.FeatureOff, FacadeSkip.NotText, FacadeSkip.NotWallFlat, FacadeSkip.NoBuilding, FacadeSkip.Broken })
                Program.Check(FacadeSampling.SkipText(skip).Length > 8, skip + " has a text");
            Program.Check(FacadeSampling.SkipText(FacadeSkip.None) == "", "none has none");
        }

        // ------------------------------------------------------------------ what is kept

        private static FacadeHit Wall(float x, float z, float normalY = 0f) => new FacadeHit(true, true, x, 10f, z, normalY);

        private static void Keeping()
        {
            // The wall z = 0 facing -z, the cursor at the origin, a board of 4 m (reach: max(3, 2.4) = 3 m).
            var verdict = new FacadeVerdict();
            var hits = new List<FacadeHit>
            {
                new FacadeHit(false, false, 0f, 0f, 0f, 0f),
                new FacadeHit(true, false, 1f, 10f, 0f, 0f),
                Wall(2f, 0f, 0.9f),
                Wall(3f, 5f),
                Wall(-1f, 0.5f), Wall(0f, 0f), Wall(1f, -0.5f)
            };
            List<(float X, float Z)> used = FacadeSampling.Usable(hits, 0f, 0f, 0f, -1f, 4f, verdict);
            Program.Check(used.Count == 3 && verdict.Used == 3 && verdict.Rays == 7, "three are the wall: " + used.Count + " of " + verdict.Rays);
            Program.Check(verdict.Missed == 1 && verdict.OtherObject == 1 && verdict.Roof == 1 && verdict.TooFar == 1, $"the others are told apart: {verdict.Missed} {verdict.OtherObject} {verdict.Roof} {verdict.TooFar}");
            Program.Check(used.Contains((-1f, 0.5f)) && used.Contains((0f, 0f)) && used.Contains((1f, -0.5f)), "and they are the right ones");

            // The edges of the rules: a normal of 0.7 is a wall, 0.71 a roof, -0.9 (an underside) a roof; a hit exactly at the reach is kept, beyond it not; in front of the wall as well as behind.
            Program.Check(Count(new[] { Wall(0f, 0f, 0.7f) }) == 1 && Count(new[] { Wall(0f, 0f, 0.71f) }) == 0 && Count(new[] { Wall(0f, 0f, -0.9f) }) == 0, "0.7 is a wall, 0.71 and -0.9 are not");
            Program.Check(Count(new[] { Wall(0f, 3f) }) == 1 && Count(new[] { Wall(0f, 3.01f) }) == 0, "3 m behind the plane is kept, a hair more is not");
            Program.Check(Count(new[] { Wall(0f, -3f) }) == 1 && Count(new[] { Wall(0f, -3.01f) }) == 0, "3 m in front of it too");
            Program.Check(Count(new[] { Wall(float.NaN, 0f) }) == 0 && Count(new[] { Wall(0f, float.NaN) }) == 0, "a hit that is not a number is not kept");
            // A wide board reaches farther from the plane: 20 m wide, half 12 m.
            var wideVerdict = new FacadeVerdict();
            Program.Check(FacadeSampling.Usable(new[] { Wall(0f, 10f) }, 0f, 0f, 0f, -1f, 20f, wideVerdict).Count == 1 && FacadeSampling.Usable(new[] { Wall(0f, 12.5f) }, 0f, 0f, 0f, -1f, 20f, new FacadeVerdict()).Count == 0, "a wide board allows more depth: half its width");
            // The normal need not be a unit vector.
            Program.Check(FacadeSampling.Usable(new[] { Wall(0f, 2.9f) }, 0f, 0f, 0f, -5f, 4f, new FacadeVerdict()).Count == 1, "the normal's length does not matter");
            Program.Check(FacadeSampling.Usable(null, 0f, 0f, 0f, -1f, 4f, new FacadeVerdict()).Count == 0 && FacadeSampling.Usable(hits, 0f, 0f, 0f, 0f, 4f, new FacadeVerdict()).Count == 0, "nothing, or no normal: nothing");
        }

        private static int Count(IReadOnlyList<FacadeHit> hits) => FacadeSampling.Usable(hits, 0f, 0f, 0f, -1f, 4f, new FacadeVerdict()).Count;

        // ------------------------------------------------------------------ the verdict

        private static void Towers()
        {
            int tried = 0, bent = 0;
            foreach (double radius in new[] { 5.0, 15.0, 40.0, 120.0 })
            {
                foreach (double side in new[] { 0.0, 25.0, 50.0 })
                {
                    foreach (double distance in new[] { 30.0, 80.0 })
                    {
                        foreach (double aim in new[] { 0.0, 100.0, 215.0 })
                        {
                            float width = radius <= 5.0 ? 6f : 8f;
                            Scene scene = Look(radius, false, aim, side, distance, width);
                            if (scene == null)
                                continue;
                            tried++;
                            string where = $"R {radius}, side {side}, distance {distance}, aim {aim}";
                            // R = 120 m under a board of 8 m is 67 mm off a plane in its middle: well over the 10 mm the fit asks for.
                            if (scene.Verdict.Bent)
                            {
                                bent++;
                                Program.Near(scene.Verdict.Radius, (float)radius, (float)radius * 0.03f, where + ": the radius of a tower is positive and right: " + scene.Verdict.Radius);
                            }
                            else
                            {
                                Program.Check(side >= 50.0 && radius <= 5.0, where + ": flat only where the rays cannot all reach a tower this tight from a side: " + scene.Verdict.Text());
                            }
                        }
                    }
                }
            }

            Program.Check(tried >= 60 && bent >= tried * 9 / 10, $"most of the views fit: {bent} of {tried}");
        }

        private static void Others()
        {
            // A concave front, seen from inside: a negative radius.
            foreach (double radius in new[] { 8.0, 25.0, 60.0 })
            {
                Scene scene = Look(radius, true, 40.0, 10.0, 20.0, 8f);
                Program.Check(scene != null && scene.Verdict.Bent, "a concave front of " + radius + " m is fitted: " + scene?.Verdict.Text());
                if (scene != null && scene.Verdict.Bent)
                    Program.Near(scene.Verdict.Radius, -(float)radius, (float)radius * 0.03f, "its radius is negative: " + scene.Verdict.Radius);
            }

            // Noise of a centimetre on every hit: the radius is within 10 % and the residual says how noisy it was.
            Scene noisy = Look(15.0, false, 0.0, 20.0, 40.0, 10f, 0.01, 7);
            Program.Check(noisy.Verdict.Bent && Math.Abs(noisy.Verdict.Radius - 15f) < 1.5f && noisy.Verdict.Residual > 0f && noisy.Verdict.Residual < 0.03f, "a centimetre of noise: " + noisy.Verdict.Text());
            // Noise of 20 cm: not a wall of a circle any more.
            Scene rough = Look(15.0, false, 0.0, 20.0, 40.0, 10f, 0.2, 3);
            Program.Check(!rough.Verdict.Bent || rough.Verdict.Residual <= ArcFit.MaxResidual, "20 cm of noise is flat or within the limit: " + rough.Verdict.Text());

            // A flat wall: seven hits on a line.
            var flat = new List<FacadeHit>();
            for (int i = 0; i < 7; i++)
                flat.Add(Wall(-3f + i, 0f));
            FacadeVerdict flatVerdict = FacadeSampling.Decide(flat, 0f, 0f, 0f, -1f, 6f);
            Program.Check(!flatVerdict.Bent && flatVerdict.Radius == 0f && flatVerdict.Why.Length > 0 && flatVerdict.Used == 7, "a flat wall is flat, with the reason: " + flatVerdict.Text());

            // A corner of two walls under the middle ray: flat or a circle that is within the limit, never a circle that does not fit.
            var corner = new List<FacadeHit> { Wall(-3f, 0f), Wall(-2f, 0f), Wall(-1f, 0f), Wall(0f, 0f), Wall(0f, 1f), Wall(0f, 2f), Wall(0f, 3f) };
            FacadeVerdict cornerVerdict = FacadeSampling.Decide(corner, 0f, 0f, -1f, -1f, 6f);
            Program.Check(!cornerVerdict.Bent || cornerVerdict.Residual <= ArcFit.MaxResidual, "a corner is flat or fits: " + cornerVerdict.Text());

            // Some of the rays on a neighbour: the others are enough; most of them: flat, saying so.
            Scene tower = Look(20.0, false, 30.0, 10.0, 50.0, 8f);
            var mixed = new List<FacadeHit>(tower.Hits);
            for (int i = 0; i < 3; i++)
                mixed[i] = new FacadeHit(true, false, mixed[i].X, mixed[i].Y, mixed[i].Z, 0f);
            FacadeVerdict mixedVerdict = FacadeSampling.Decide(mixed, tower.PointX, tower.PointZ, tower.NormalX, tower.NormalZ, 8f);
            Program.Check(mixedVerdict.Bent && mixedVerdict.OtherObject == 3 && Math.Abs(mixedVerdict.Radius - 20f) < 0.6f, "three of seven on a neighbour: the four left fit: " + mixedVerdict.Text());
            for (int i = 0; i < 5; i++)
                mixed[i] = new FacadeHit(true, false, mixed[i].X, mixed[i].Y, mixed[i].Z, 0f);
            FacadeVerdict few = FacadeSampling.Decide(mixed, tower.PointX, tower.PointZ, tower.NormalX, tower.NormalZ, 8f);
            Program.Check(!few.Bent && few.OtherObject == 5 && few.Why.StartsWith("fewer than 3"), "five of seven on a neighbour: flat, and it says why: " + few.Text());

            // Rays that all hit a roof: flat.
            var roofs = tower.Hits.Select(h => new FacadeHit(h.Hit, h.OnBuilding, h.X, h.Y, h.Z, 0.95f)).ToList();
            FacadeVerdict roofVerdict = FacadeSampling.Decide(roofs, tower.PointX, tower.PointZ, tower.NormalX, tower.NormalZ, 8f);
            Program.Check(!roofVerdict.Bent && roofVerdict.Roof == roofs.Count(h => h.Hit), "roofs are not walls: " + roofVerdict.Text());

            // Nothing hit at all, nothing given.
            FacadeVerdict none = FacadeSampling.Decide(new List<FacadeHit>(), 0f, 0f, 0f, -1f, 6f);
            Program.Check(!none.Bent && none.Rays == 0 && none.Why.Length > 0, "no hits: flat");
            Program.Check(!FacadeSampling.Decide(null, 0f, 0f, 0f, -1f, 6f).Bent, "no list: flat");

            // The text of a verdict.
            Scene shown = Look(15.0, false, 0.0, 0.0, 40.0, 8f);
            string text = shown.Verdict.Text();
            Program.Check(text.Contains("a wall of radius 15.0 m") && text.Contains("rays on the building's wall") && text.Contains("rms 0 mm"), "a fit says its radius and its counts: " + text);
            Program.Check(shown.Verdict.Spread > 9f && shown.Verdict.Spread < 10.5f, "and its spread: " + shown.Verdict.Spread);
            Program.Check(flatVerdict.Text().Contains("flat (") && flatVerdict.Text().StartsWith("7 of 7 rays"), "a flat one says it is flat and why: " + flatVerdict.Text());
        }

        private static void StraightIn()
        {
            (float x, float y, float z) = FacadeSampling.Eye(10f, 5f, 20f, 0f, -1f);
            Program.Check(Math.Abs(x - 10f) < 1e-4f && y == 5f && Math.Abs(z - (20f - FacadeSampling.Standoff)) < 1e-4f, "out along the normal at the point's height: " + x + ", " + y + ", " + z);
            (float x2, _, float z2) = FacadeSampling.Eye(0f, 0f, 0f, 3f, 4f);
            Program.Check(Math.Abs(x2 - 0.6f * FacadeSampling.Standoff) < 1e-3f && Math.Abs(z2 - 0.8f * FacadeSampling.Standoff) < 1e-3f, "the normal's length does not matter: " + x2 + ", " + z2);
            (float x3, float y3, float z3) = FacadeSampling.Eye(1f, 2f, 3f, 0f, 0f);
            Program.Check(x3 == 1f && y3 == 2f && z3 == 3f, "no normal: the point itself");

            // Seen from out in front, every tower and every concave front is read, from every side of it: the cursor's own aim does not matter.
            int tried = 0, fitted = 0;
            foreach (double radius in new[] { 3.0, 5.0, 15.0, 40.0, 120.0 })
            {
                foreach (bool concave in new[] { false, true })
                {
                    foreach (double aim in new[] { 0.0, 70.0, 140.0, 250.0, 330.0 })
                    {
                        if (concave && radius < 6.0)
                            continue;
                        double phi = aim * Math.PI / 180.0;
                        float px = (float)(radius * Math.Cos(phi)), pz = (float)(radius * Math.Sin(phi));
                        float sign = concave ? -1f : 1f;
                        float nx = sign * (float)Math.Cos(phi), nz = sign * (float)Math.Sin(phi);
                        (float ex, float ey, float ez) = FacadeSampling.Eye(px, 10f, pz, nx, nz);
                        var hits = new List<FacadeHit>();
                        foreach (FacadeRay ray in FacadeSampling.Plan(ex, ey, ez, px, 10f, pz, nx, nz, 6f))
                        {
                            if (Cross(ray, 0.0, 0.0, radius, concave, out double hx, out double hz, out _, out _))
                                hits.Add(new FacadeHit(true, true, (float)hx, 10f, (float)hz, 0f));
                            else
                                hits.Add(new FacadeHit(false, false, 0f, 0f, 0f, 0f));
                        }

                        FacadeVerdict verdict = FacadeSampling.Decide(hits, px, pz, nx, nz, 6f);
                        tried++;
                        string where = "R " + radius + (concave ? " concave" : "") + ", aim " + aim;
                        if (verdict.Bent)
                        {
                            fitted++;
                            Program.Near(verdict.Radius, (float)(concave ? -radius : radius), (float)radius * 0.02f, where + ": " + verdict.Text());
                        }
                        else
                        {
                            // A board 6 m wide on R = 3 m is more than the half circle: the plane's tangent points miss the wall.
                            Program.Check(radius <= 5.0, where + ": flat only for a wall tighter than the board is wide: " + verdict.Text());
                        }
                    }
                }
            }

            Program.Check(fitted >= tried - 5 && fitted > 25, "nearly all of them fit: " + fitted + " of " + tried);
        }

        private static void LogLines()
        {
            var log = new FacadeSampling.Log();
            int logged = 0;
            for (int i = 0; i < 100; i++)
            {
                if (log.Attempt())
                    logged++;
            }

            Program.Check(logged == 30, "the first thirty attempts are written: " + logged);
            Program.Check(log.Skipped(FacadeSkip.NoBuilding) && !log.Skipped(FacadeSkip.NoBuilding), "a reason for not asking is written once");
            Program.Check(log.Skipped(FacadeSkip.FeatureOff) && log.Skipped(FacadeSkip.Broken) && log.Skipped(FacadeSkip.NotText), "each reason has its own once");
            Program.Check(!log.Skipped(FacadeSkip.None), "asking is not a reason");
        }
    }
}
