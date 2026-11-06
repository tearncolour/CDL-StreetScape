using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The order a queue of work at places is done in (0.12.27, the owner: "主要还是加载时间优化"): what is within reach of the camera first, nearest first, and the rest after it, as it was. After a city is
    /// loaded every sign of it is queued for its posts' ground; done in the order they were read, the signs round the camera waited for the whole city, and their tiles were built again for every
    /// batch that came. Pure.
    /// </summary>
    public static class NearFirst
    {
        /// <summary>
        /// Orders the entries in place: those whose place is within `within` metres of (cx, cz), nearest first, then the others in the order they had. Returns how many are within. An entry with no place
        /// (null from place) counts as at the camera: it is dropped quickly where it is looked at.
        /// </summary>
        public static int Order<T>(List<T> entries, Func<T, (float X, float Z)?> place, float cx, float cz, float within)
        {
            int n = entries.Count;
            if (n == 0)
                return 0;
            float limit = within * within;
            var nearKeys = new List<float>(Math.Min(n, 4096));
            var near = new List<T>(Math.Min(n, 4096));
            var far = new List<T>();
            foreach (T entry in entries)
            {
                (float X, float Z)? at = place(entry);
                float d = at.HasValue ? (at.Value.X - cx) * (at.Value.X - cx) + (at.Value.Z - cz) * (at.Value.Z - cz) : 0f;
                if (d <= limit)
                {
                    nearKeys.Add(d);
                    near.Add(entry);
                }
                else
                {
                    far.Add(entry);
                }
            }

            T[] nearItems = near.ToArray();
            float[] keys = nearKeys.ToArray();
            Array.Sort(keys, nearItems);
            entries.Clear();
            entries.AddRange(nearItems);
            entries.AddRange(far);
            return nearItems.Length;
        }
    }
}
