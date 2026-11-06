using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The heights of the ground under a marking or a thing placed with one click (0.12.30; the owner: "减速带、预制的地面区域都不能吸附地面高度": a rubber hump or a toll island on a slope or an uneven road
    /// stood flat at the height of the cursor): a grid of heights over a box of the item's frame (x across, z along), read where it is placed, relative to the item's height. The paint and the solids follow
    /// it, each vertex by the grid's height where it is (MarkingPlan.Bake). Written in the setting Key as "x0,x1,z0,z1,nx,nz,h,h,..." (the box and the heights in centimetres, the heights row by row from
    /// z0, each row from x0), so an item keeps it. Pure.
    /// </summary>
    public sealed class GroundGrid
    {
        public const string Key = "gh";

        /// <summary>The most points across and along, and the step the points are spread at (m) when the box is long enough.</summary>
        public const int MaxAcross = 4, MaxAlong = 8;

        public const float Step = 2.5f;

        public float X0, X1, Z0, Z1;
        public int Nx, Nz;

        /// <summary>The heights (m), Nx a row, Nz rows.</summary>
        public float[] Heights;

        /// <summary>How many points a box of this size takes each way: one every Step or so, two at the least, MaxAcross and MaxAlong at the most.</summary>
        public static void Count(float x0, float x1, float z0, float z1, out int nx, out int nz)
        {
            nx = Math.Max(2, Math.Min(MaxAcross, (int)Math.Ceiling((x1 - x0) / Step - 1e-3f) + 1));
            nz = Math.Max(2, Math.Min(MaxAlong, (int)Math.Ceiling((z1 - z0) / Step - 1e-3f) + 1));
        }

        /// <summary>Where point (i, j) of a grid of nx by nz over the box is.</summary>
        public static void PointOf(float x0, float x1, float z0, float z1, int nx, int nz, int i, int j, out float x, out float z)
        {
            x = x0 + (x1 - x0) * i / (nx - 1);
            z = z0 + (z1 - z0) * j / (nz - 1);
        }

        /// <summary>The height at (x, z) of the frame: bilinear between the points, the nearest edge's beyond the box.</summary>
        public float At(float x, float z)
        {
            float u = X1 > X0 ? (x - X0) / (X1 - X0) * (Nx - 1) : 0f, v = Z1 > Z0 ? (z - Z0) / (Z1 - Z0) * (Nz - 1) : 0f;
            u = Math.Max(0f, Math.Min(Nx - 1, u));
            v = Math.Max(0f, Math.Min(Nz - 1, v));
            int i = Math.Min(Nx - 2, (int)u), j = Math.Min(Nz - 2, (int)v);
            float fu = u - i, fv = v - j;
            float h00 = Heights[j * Nx + i], h10 = Heights[j * Nx + i + 1], h01 = Heights[(j + 1) * Nx + i], h11 = Heights[(j + 1) * Nx + i + 1];
            return (h00 * (1f - fu) + h10 * fu) * (1f - fv) + (h01 * (1f - fu) + h11 * fu) * fv;
        }

        /// <summary>Whether every height is within a centimetre of nothing (a flat ground: the item needs no grid).</summary>
        public bool Flat
        {
            get
            {
                foreach (float h in Heights)
                {
                    if (Math.Abs(h) >= 0.01f)
                        return false;
                }

                return true;
            }
        }

        private static string Cm(float metres) => ((int)Math.Round(metres * 100f)).ToString(CultureInfo.InvariantCulture);

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(Cm(X0)).Append(',').Append(Cm(X1)).Append(',').Append(Cm(Z0)).Append(',').Append(Cm(Z1)).Append(',').Append(Nx).Append(',').Append(Nz);
            foreach (float h in Heights)
                sb.Append(',').Append(Cm(h));
            return sb.ToString();
        }

        /// <summary>The grid of a setting; null when it is empty or not one (a wrong count, a size out of its limits, a height of more than 20 m): the item is then laid flat as before.</summary>
        public static GroundGrid Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;
            string[] parts = text.Split(',');
            if (parts.Length < 6)
                return null;
            var numbers = new List<int>(parts.Length);
            foreach (string part in parts)
            {
                if (!int.TryParse(part, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int n))
                    return null;
                numbers.Add(n);
            }

            int nx = numbers[4], nz = numbers[5];
            if (nx < 2 || nx > MaxAcross || nz < 2 || nz > MaxAlong || parts.Length != 6 + nx * nz || numbers[1] <= numbers[0] || numbers[3] <= numbers[2])
                return null;
            var grid = new GroundGrid { X0 = numbers[0] / 100f, X1 = numbers[1] / 100f, Z0 = numbers[2] / 100f, Z1 = numbers[3] / 100f, Nx = nx, Nz = nz, Heights = new float[nx * nz] };
            for (int k = 0; k < nx * nz; k++)
            {
                if (Math.Abs(numbers[6 + k]) > 2000)
                    return null;
                grid.Heights[k] = numbers[6 + k] / 100f;
            }

            return grid;
        }
    }
}
