using System;

namespace CDLC.TS.Geo
{
    /// <summary>A place on a building: the world x, z of the board's origin and the way it faces (yaw: +Z of the sign is (sin yaw, 0, cos yaw)).</summary>
    public readonly struct FacadePlace
    {
        public readonly float X, Z, Yaw;

        public FacadePlace(float x, float z, float yaw)
        {
            X = x;
            Z = z;
            Yaw = yaw;
        }
    }

    /// <summary>
    /// The arrow keys on a building (0.12.76; the owner: "按方向上下左右可以调整在建筑立面上的位置"): left and right move a board along the facade as the screen shows (along the arc of a curved one), up and
    /// down move its lower edge, Shift takes the larger step; on a roof the keys move it across the roof as seen from the camera, and a board may overhang the roof's edge by a small tolerance at most
    /// (RoofFit). Pure: the tool reads the keys and the camera and writes the board.
    /// </summary>
    public static class FacadeMove
    {
        /// <summary>One press (m), with Shift; the farthest a board is moved from where the cursor put it (m); the height limits come from WallMount.</summary>
        public const float Step = 0.1f, CoarseStep = 0.5f, MaxShift = 30f;

        /// <summary>A press of an arrow key as a distance (m): 0.1, or 0.5 with Shift.</summary>
        public static float StepOf(bool shift) => shift ? CoarseStep : Step;

        /// <summary>Which way along a wall's tangent (the tangent being WallMount.Along of its outward normal) the screen's right is: +1 when the camera's right points along it, else -1 (a wall seen from behind is not moved the way the arrow points).</summary>
        public static int SenseOf(float tangentX, float tangentZ, float cameraRightX, float cameraRightZ) => tangentX * cameraRightX + tangentZ * cameraRightZ >= 0f ? 1 : -1;

        /// <summary>
        /// A board moved distance d (m, positive along the wall's tangent (-nz, nx), which is WallMount.Along) from p on a wall whose outward unit normal is (nx, nz): along a straight line on a flat wall
        /// (radius 0), along the arc of a curved one (radius &gt; 0 bulging toward the viewer, &lt; 0 curving round him) so that it stays on the surface and turns with it.
        /// </summary>
        public static FacadePlace Along(float px, float pz, float nx, float nz, float radius, float distance)
        {
            float length = (float)Math.Sqrt(nx * nx + nz * nz);
            if (length < 1e-6f)
                return new FacadePlace(px, pz, 0f);
            nx /= length;
            nz /= length;
            float tx = -nz, tz = nx;
            if (radius == 0f || float.IsNaN(radius))
                return new FacadePlace(px + tx * distance, pz + tz * distance, (float)Math.Atan2(nx, nz));
            // The centre is behind the wall for a bulge (radius > 0) and in front for a hollow: p - n R either way.
            double cx = px - (double)nx * radius, cz = pz - (double)nz * radius;
            double theta = distance / (double)radius;
            double c = Math.Cos(theta), s = Math.Sin(theta);
            double mx = nx * c + tx * s, mz = nz * c + tz * s;
            return new FacadePlace((float)(cx + mx * radius), (float)(cz + mz * radius), (float)Math.Atan2(mx, mz));
        }

        /// <summary>The height of a lower edge after a step up (+) or down (-): to a tenth of a metre, never below minHeight or above maxHeight.</summary>
        public static float Height(float lower, float delta, float minHeight, float maxHeight)
        {
            float h = (float)Math.Round((lower + delta) * 10f) / 10f;
            return Math.Max(minHeight, Math.Min(maxHeight, h));
        }

        /// <summary>The offset kept for a nudge made of presses: the sum of the steps, none beyond MaxShift either way.</summary>
        public static float Accumulate(float offset, int direction, bool shift) => Math.Max(-MaxShift, Math.Min(MaxShift, (float)Math.Round((offset + direction * StepOf(shift)) * 100f) / 100f));

        /// <summary>
        /// A step across a roof: the arrows moved by what the camera sees. right (x, z) and forward (x, z) are the camera's axes on the ground (unit); arrowX +1 is right, forward is +1 for the up key.
        /// </summary>
        public static (float X, float Z) AcrossRoof(float x, float z, float rightX, float rightZ, float forwardX, float forwardZ, int arrowX, int forward, bool shift)
        {
            float step = StepOf(shift);
            return (x + (rightX * arrowX + forwardX * forward) * step, z + (rightZ * arrowX + forwardZ * forward) * step);
        }
    }

    /// <summary>Whether a board standing on a roof is on it (RoofFit.Overhang): the building's footprint as its prefab's bounds, in its own frame.</summary>
    public static class RoofFit
    {
        /// <summary>How far a board may reach past the roof's edge (m): the bounds are the building's box, not its roof's outline.</summary>
        public const float Tolerance = 0.3f;

        /// <summary>
        /// How far the board's footprint reaches outside the building's bounds (m; 0 when it is inside). The footprint is the rectangle halfWidth to each side of the board's middle, front before its
        /// back plane and back behind it, turned by yaw about (px, pz); the bounds are minX..maxX and minZ..maxZ in the building's frame (its yaw about (bx, bz)). The farthest corner decides.
        /// </summary>
        public static float Overhang(float bx, float bz, float buildingYaw, float minX, float maxX, float minZ, float maxZ, float px, float pz, float yaw, float halfWidth, float front, float back)
        {
            double sy = Math.Sin(yaw), cy = Math.Cos(yaw);
            // The board's +Z is (sin yaw, cos yaw) and its +X is (cos yaw, -sin yaw): a corner at (x, z) in the board's frame.
            double sb = Math.Sin(buildingYaw), cb = Math.Cos(buildingYaw);
            double worst = 0.0;
            foreach (double lx in new[] { -(double)halfWidth, halfWidth })
            {
                foreach (double lz in new[] { -(double)back, front })
                {
                    double wx = px + lx * cy + lz * sy, wz = pz - lx * sy + lz * cy;
                    // Into the building's frame (the inverse of its turn).
                    double dx = wx - bx, dz = wz - bz;
                    double ux = dx * cb - dz * sb, uz = dx * sb + dz * cb;
                    double over = Math.Max(Math.Max(minX - ux, ux - maxX), Math.Max(minZ - uz, uz - maxZ));
                    worst = Math.Max(worst, over);
                }
            }

            return (float)worst;
        }
    }
}
