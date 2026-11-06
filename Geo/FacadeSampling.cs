using System;
using System.Collections.Generic;
using System.Globalization;

namespace CDLC.TS.Geo
{
    /// <summary>One ray of the facade's sampling: a segment in the world from the eye on through a place on the wall's plane.</summary>
    public readonly struct FacadeRay
    {
        public readonly float FromX, FromY, FromZ, ToX, ToY, ToZ;

        public FacadeRay(float fromX, float fromY, float fromZ, float toX, float toY, float toZ)
        {
            FromX = fromX;
            FromY = fromY;
            FromZ = fromZ;
            ToX = toX;
            ToY = toY;
            ToZ = toZ;
        }
    }

    /// <summary>What one ray found, as the game's raycast says it: whether it hit anything, whether that is the building the board is on, where, and how steep the surface is there.</summary>
    public readonly struct FacadeHit
    {
        public readonly bool Hit, OnBuilding;
        public readonly float X, Y, Z, NormalY;

        public FacadeHit(bool hit, bool onBuilding, float x, float y, float z, float normalY)
        {
            Hit = hit;
            OnBuilding = onBuilding;
            X = x;
            Y = y;
            Z = z;
            NormalY = normalY;
        }
    }

    /// <summary>What to do with the game's answer for rays that were asked: the frame of the ask, the frame after it (the only one in which the answer is there), or later (it is gone).</summary>
    public enum FacadeWait
    {
        Wait,
        Read,
        Late
    }

    /// <summary>Why the facade was not even asked about (for the log: a text board that is on a wall and is not bent says why).</summary>
    public enum FacadeSkip
    {
        None,
        FeatureOff,
        NotText,
        NotWallFlat,
        NoBuilding,
        Broken
    }

    /// <summary>What the rays said: a radius (signed metres; 0 is flat) and what it is made of, for the log and the tests.</summary>
    public sealed class FacadeVerdict
    {
        public float Radius;
        public bool Bent;
        public int Rays, Missed, OtherObject, Roof, TooFar, Used;
        public float Spread, Residual;

        /// <summary>The reason it is flat (empty when it is bent).</summary>
        public string Why = "";

        public string Text()
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            string counts = string.Format(c, "{0} of {1} rays on the building's wall (missed {2}, another object {3}, a roof {4}, too far from the wall {5})", Used, Rays, Missed, OtherObject, Roof, TooFar);
            return Bent
                ? string.Format(c, "{0}: a wall of radius {1:0.0} m (rms {2:0} mm over {3:0.0} m)", counts, Radius, Residual * 1000f, Spread)
                : counts + ": flat (" + Why + ")";
        }
    }

    /// <summary>
    /// The sampling of a building's facade for a text board that is to follow its curve (0.12.77): the rays across the board, what to keep of what they hit, when to ask and when to read, and the
    /// verdict (ArcFit). The game's RaycastSystem keeps the answer to a context for ONE update: the rays asked in one frame are answered in the next frame's tool update, and are gone from the frame after
    /// that (0.12.77 read them two frames on: GetResult was empty every time, the radius stayed 0 and not one line said so: the owner, 2 Nov, "文字还是不能逐字按建筑曲率放置"). Pure.
    /// </summary>
    public static class FacadeSampling
    {
        public const int Rays = 7;

        /// <summary>The fewest frames between two asks, and how much longer a run of unanswered asks makes it (a frame each).</summary>
        public const int AskEvery = 10;

        /// <summary>How far the cursor's place on the wall may move before the facade is asked about again (m).</summary>
        public const float AskMove = 0.5f;

        /// <summary>A surface this steep (the y of its normal) is a roof, not a wall.</summary>
        public const float RoofNormalY = 0.7f;

        /// <summary>Half the width the rays are spread over: a little over half the board, at least a metre, at most 25 m.</summary>
        public static float Half(float boardWidth) => Math.Min(Math.Max(boardWidth * 0.6f, 1f), 25f);

        /// <summary>How far out from the wall the rays start (m), and the least: an eye nearer than that is moved out to it.</summary>
        public const float Standoff = 10f;

        /// <summary>
        /// Where the rays start: out from the wall's point along its outward normal, at the point's height, not at the camera. A ray from the camera that meets the wall at a grazing angle slides along it,
        /// or meets a neighbour first; one that comes straight in meets it where it is (the wall's own curve is what the points show). The normal is level (WallMount.TryNormal). The same eye serves
        /// the editor, which has no cursor and a camera that may be behind the wall.
        /// </summary>
        public static (float X, float Y, float Z) Eye(float pointX, float pointY, float pointZ, float normalX, float normalZ)
        {
            double length = Math.Sqrt((double)normalX * normalX + (double)normalZ * normalZ);
            if (length < 1e-6)
                return (pointX, pointY, pointZ);
            return ((float)(pointX + normalX / length * Standoff), pointY, (float)(pointZ + normalZ / length * Standoff));
        }

        /// <summary>
        /// The rays across the board: from the eye through seven places on the wall's tangent plane at the height of the cursor (the board's middle), and on past them by half the width or 6 m, so that a
        /// wall that curves away from the plane (a tower) is still reached; one that curves toward the eye is reached before it. Empty when the normal has no horizontal part.
        /// </summary>
        public static List<FacadeRay> Plan(float eyeX, float eyeY, float eyeZ, float pointX, float pointY, float pointZ, float normalX, float normalZ, float boardWidth)
        {
            var rays = new List<FacadeRay>(Rays);
            double length = Math.Sqrt((double)normalX * normalX + (double)normalZ * normalZ);
            if (length < 1e-6 || float.IsNaN(eyeX + eyeY + eyeZ + pointX + pointY + pointZ + boardWidth) || float.IsInfinity(eyeX + eyeY + eyeZ + pointX + pointY + pointZ))
                return rays;
            float tx = (float)(-normalZ / length), tz = (float)(normalX / length);
            float half = Half(boardWidth), beyond = Math.Max(6f, half);
            for (int i = 0; i < Rays; i++)
            {
                float s = -half + 2f * half * i / (Rays - 1);
                float x = pointX + tx * s, z = pointZ + tz * s;
                double dx = x - eyeX, dy = pointY - eyeY, dz = z - eyeZ;
                double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (d < 1e-4)
                    continue;
                rays.Add(new FacadeRay(eyeX, eyeY, eyeZ, (float)(x + dx / d * beyond), (float)(pointY + dy / d * beyond), (float)(z + dz / d * beyond)));
            }

            return rays;
        }

        /// <summary>
        /// The answer of a context lives one frame: the game's RaycastSystem updates before the tools, so rays added by the tool in frame N are made in frame N+1 before the tool runs, read in that
        /// frame, and cleared by the update of frame N+2. framesSinceAsk counts the game's frames (Time.frameCount), not the tool's calls. In the frame of the ask there is nothing to read yet; in the next
        /// one the answer is there (or the game did not answer: late); a frame later it is gone whatever the result holds.
        /// </summary>
        public static FacadeWait Next(int framesSinceAsk, bool answerThere)
        {
            if (framesSinceAsk < 1)
                return FacadeWait.Wait;
            return framesSinceAsk == 1 && answerThere ? FacadeWait.Read : FacadeWait.Late;
        }

        /// <summary>
        /// Whether to ask now: nothing is out, enough frames have passed since the last ask (longer after asks nobody answered: the game may not answer, and the frame is not to be spent on it), and the
        /// cursor's place on the wall has moved or is on another building. moved: how far the place is from where the last ask was made (float.MaxValue when none was).
        /// </summary>
        public static bool ShouldAsk(bool outstanding, int framesSinceLast, float moved, bool otherBuilding, int unanswered)
        {
            if (outstanding || framesSinceLast < AskEvery + Math.Min(Math.Max(unanswered, 0), 6) * AskEvery)
                return false;
            return moved > AskMove || otherBuilding;
        }

        /// <summary>Why a text board that is on a wall is not asked about; None when it is.</summary>
        public static FacadeSkip Why(bool enabled, bool broken, bool text, bool wallFlat, bool building)
        {
            if (!enabled)
                return FacadeSkip.FeatureOff;
            if (broken)
                return FacadeSkip.Broken;
            if (!text)
                return FacadeSkip.NotText;
            if (!wallFlat)
                return FacadeSkip.NotWallFlat;
            return building ? FacadeSkip.None : FacadeSkip.NoBuilding;
        }

        public static string SkipText(FacadeSkip skip)
        {
            switch (skip)
            {
                case FacadeSkip.FeatureOff: return "the feature is off (features.textSupports)";
                case FacadeSkip.NotText: return "the board is not a text light box";
                case FacadeSkip.NotWallFlat: return "the support is not a flat wall support";
                case FacadeSkip.NoBuilding: return "no building under the cursor";
                case FacadeSkip.Broken: return "an earlier read failed";
                default: return "";
            }
        }

        /// <summary>
        /// What of the hits is the wall: the ones on the building the board is on, not a roof, and within reach of the wall's plane (a convex wall is behind it by at most the board's half width, a concave
        /// one in front of it). x, z of each, in metres seen from above, and how the others were left out. point and normal: where the cursor hit the wall and its outward normal.
        /// </summary>
        public static List<(float X, float Z)> Usable(IReadOnlyList<FacadeHit> hits, float pointX, float pointZ, float normalX, float normalZ, float boardWidth, FacadeVerdict counts)
        {
            var used = new List<(float X, float Z)>(hits == null ? 0 : hits.Count);
            double length = Math.Sqrt((double)normalX * normalX + (double)normalZ * normalZ);
            if (hits == null || length < 1e-6)
                return used;
            double nx = normalX / length, nz = normalZ / length;
            float reach = Math.Max(3f, Half(boardWidth));
            counts.Rays = hits.Count;
            foreach (FacadeHit hit in hits)
            {
                if (!hit.Hit)
                {
                    counts.Missed++;
                    continue;
                }

                if (!hit.OnBuilding)
                {
                    counts.OtherObject++;
                    continue;
                }

                if (Math.Abs(hit.NormalY) > RoofNormalY)
                {
                    counts.Roof++;
                    continue;
                }

                double depth = (hit.X - pointX) * nx + (hit.Z - pointZ) * nz;
                if (Math.Abs(depth) > reach || float.IsNaN(hit.X + hit.Z))
                {
                    counts.TooFar++;
                    continue;
                }

                used.Add((hit.X, hit.Z));
            }

            counts.Used = used.Count;
            return used;
        }

        /// <summary>The verdict for what the rays hit: a radius when the wall is a circle in plan (ArcFit), else flat with the reason.</summary>
        public static FacadeVerdict Decide(IReadOnlyList<FacadeHit> hits, float pointX, float pointZ, float normalX, float normalZ, float boardWidth)
        {
            var verdict = new FacadeVerdict();
            List<(float X, float Z)> used = Usable(hits, pointX, pointZ, normalX, normalZ, boardWidth, verdict);
            if (used.Count < ArcFit.MinPoints)
            {
                verdict.Why = "fewer than " + ArcFit.MinPoints + " usable hits";
                return verdict;
            }

            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach ((float X, float Z) p in used)
            {
                minX = Math.Min(minX, p.X);
                maxX = Math.Max(maxX, p.X);
                minZ = Math.Min(minZ, p.Z);
                maxZ = Math.Max(maxZ, p.Z);
            }

            verdict.Spread = (float)Math.Sqrt((maxX - minX) * (maxX - minX) + (maxZ - minZ) * (maxZ - minZ));
            ArcFitResult fit = ArcFit.Fit(used, normalX, normalZ, boardWidth);
            verdict.Residual = fit.Residual;
            if (!fit.Ok)
            {
                verdict.Why = fit.Why;
                return verdict;
            }

            verdict.Bent = true;
            verdict.Radius = fit.Radius;
            return verdict;
        }

        /// <summary>How many lines of the facade the log gets: every fit's answer for the first Attempts, each reason for not asking once.</summary>
        public sealed class Log
        {
            public const int Attempts = 30;

            private int m_Attempts;
            private readonly HashSet<FacadeSkip> m_Skips = new HashSet<FacadeSkip>();

            public bool Attempt() => m_Attempts++ < Attempts;

            public bool Skipped(FacadeSkip skip) => skip != FacadeSkip.None && m_Skips.Add(skip);
        }
    }
}
