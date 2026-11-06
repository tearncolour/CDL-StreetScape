using System;
using System.Collections.Generic;
using UnityEngine;

namespace CDLC.TS.Render
{
    internal static partial class TsMaterials
    {
        private static readonly List<UnityEngine.Object> s_Failed = new List<UnityEngine.Object>();
        private static readonly List<FailedTextureSwap> s_FailedSwaps = new List<FailedTextureSwap>();

        private sealed class FailedTextureSwap
        {
            internal Material Target;
            internal Texture Previous;
            internal Texture2D Created;

            internal void Release()
            {
                if (Created == null) return;
                // Only undo our own attempted slot write; a later owner may already have replaced it.
                if (Target != null && Target.HasProperty("_BaseColorMap") && Target.GetTexture("_BaseColorMap") == Created)
                    Target.SetTexture("_BaseColorMap", Previous);
                UnityEngine.Object.Destroy(Created);
                Created = null;
            }
        }

        private static void ReleaseOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            try
            {
                if (value is Texture texture && HeldByFailedMaterial(texture))
                {
                    s_Failed.Add(value);
                    return;
                }
                UnityEngine.Object.Destroy(value);
            }
            catch
            {
                if (value != null) s_Failed.Add(value);
                throw;
            }
        }

        private static bool HeldByFailedMaterial(Texture texture)
        {
            foreach (UnityEngine.Object value in s_Failed)
                if (value is Material material && material != null && material.HasProperty("_BaseColorMap") && material.GetTexture("_BaseColorMap") == texture)
                    return true;
            return false;
        }

        private static void CleanupFailed(UnityEngine.Object value, Exception allocationError)
        {
            try { ReleaseOwned(value); }
            catch (Exception cleanupError) { throw new AggregateException("Material or texture allocation and cleanup failed.", allocationError, cleanupError); }
        }

        private static void DropOwned(UnityEngine.Object value, List<Exception> failures)
        {
            try { ReleaseOwned(value); }
            catch (Exception error) { failures.Add(error); }
        }

        private static void CleanupFailedSwap(Material target, Texture previous, Texture2D created, Exception allocationError)
        {
            var owned = new FailedTextureSwap { Target = target, Previous = previous, Created = created };
            try { owned.Release(); }
            catch (Exception cleanupError)
            {
                if (owned.Created != null) s_FailedSwaps.Add(owned);
                throw new AggregateException("Face texture binding and rollback failed.", allocationError, cleanupError);
            }
        }

        private static void RetryCleanup()
        {
            List<Exception> failures = null;
            for (int i = s_FailedSwaps.Count - 1; i >= 0; i--)
            {
                FailedTextureSwap owned = s_FailedSwaps[i];
                try { owned.Release(); }
                catch (Exception error) { (failures ?? (failures = new List<Exception>())).Add(error); }
                if (owned.Created == null) s_FailedSwaps.RemoveAt(i);
            }
            // A material whose destruction failed may still reference one of our textures.
            for (int pass = 0; pass < 2; pass++)
            for (int i = s_Failed.Count - 1; i >= 0; i--)
            {
                UnityEngine.Object owned = s_Failed[i];
                if ((owned is Material) != (pass == 0)) continue;
                try
                {
                    if (owned is Texture texture && HeldByFailedMaterial(texture)) continue;
                    if (owned != null) UnityEngine.Object.Destroy(owned);
                    s_Failed.RemoveAt(i);
                }
                catch (Exception error) { (failures ?? (failures = new List<Exception>())).Add(error); }
            }
            if (failures != null) throw new AggregateException("Material or texture cleanup is still pending.", failures);
        }
    }
}
