using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The extensions that joined through TsExtension.Register (the points of API 2, 0.12.93), more than one at a time: a question put to the extensions is answered by all of them, so that a second
    /// extension (AutoRoadLiner) cannot take the place of the first (the Realistic Information Extension, whose own points stay the single fields of API 1 that TsExtension asks first). TEntity is the
    /// game's Entity; it is a parameter so that the rules here are tested without the engine. Main thread; the list is replaced, never changed in place, so a question never meets a half-made one.
    /// </summary>
    internal sealed class ExtensionRegistry<TEntity>
    {
        /// <summary>What an extension answers; every point is optional.</summary>
        internal class Hooks
        {
            /// <summary>The extension and its version, for the log; a name registered again replaces the earlier hooks of that name.</summary>
            public string Name;

            /// <summary>Whether the extension supplies what the screens follow (traffic, accidents, limits, lanes): LiveContent.On is set by those only, since an extension without a source would turn on screens that have nothing to show.</summary>
            public bool ProvidesLiveContent;

            /// <summary>Whether a marking lane of the game is one the extension hides itself: the clearing areas leave its tag on.</summary>
            public Func<TEntity, bool> HidesMarking;

            /// <summary>Whether a road segment or junction is one the extension draws its own lines on: the automatic marking leaves it alone.</summary>
            public Func<TEntity, bool> IsReservedRoad;

            /// <summary>Changes when those roads do (a part of the automatic marking's cache key).</summary>
            public Func<int> ReservedRevision;

            /// <summary>The poles and arms within a radius of (x, z) that boards may be fixed to: added to the lists it is given (they come as they are, with what the extensions before it added).</summary>
            public Action<float, float, float, List<HostCandidate>, List<HostArm>> OtherHosts;
        }

        private Hooks[] m_Hooks = new Hooks[0];

        /// <summary>The names of the extensions registered, in the order they joined.</summary>
        public IReadOnlyList<string> Names
        {
            get
            {
                var names = new string[m_Hooks.Length];
                for (int i = 0; i < names.Length; i++)
                    names[i] = m_Hooks[i].Name;
                return names;
            }
        }

        /// <summary>Whether any extension supplies live content.</summary>
        public bool ProvidesLiveContent
        {
            get
            {
                foreach (Hooks hooks in m_Hooks)
                {
                    if (hooks.ProvidesLiveContent)
                        return true;
                }

                return false;
            }
        }

        /// <summary>Whether any extension gives poles and arms (OtherHosts) to fix boards to.</summary>
        public bool ProvidesHosts
        {
            get
            {
                foreach (Hooks hooks in m_Hooks)
                {
                    if (hooks.OtherHosts != null)
                        return true;
                }

                return false;
            }
        }

        /// <summary>Registers the extension's hooks, or replaces those it registered before under the same name (it keeps its place in the order). The hooks' fields are read when asked, not copied.</summary>
        public void Add(Hooks hooks)
        {
            if (hooks == null || string.IsNullOrEmpty(hooks.Name))
                throw new ArgumentException("an extension needs hooks with a name");
            Hooks[] next;
            int at = Array.FindIndex(m_Hooks, h => h.Name == hooks.Name);
            if (at >= 0)
            {
                next = (Hooks[])m_Hooks.Clone();
                next[at] = hooks;
            }
            else
            {
                next = new Hooks[m_Hooks.Length + 1];
                Array.Copy(m_Hooks, next, m_Hooks.Length);
                next[m_Hooks.Length] = hooks;
            }

            m_Hooks = next;
        }

        /// <summary>True when any extension hides the lane itself.</summary>
        public bool HidesMarking(TEntity lane)
        {
            Hooks[] all = m_Hooks;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].HidesMarking != null && all[i].HidesMarking(lane))
                    return true;
            }

            return false;
        }

        /// <summary>True when any extension draws its own lines on the road.</summary>
        public bool IsReservedRoad(TEntity road)
        {
            Hooks[] all = m_Hooks;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].IsReservedRoad != null && all[i].IsReservedRoad(road))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The revisions of the extensions folded into the one given (the single field of API 1), in order: a number that changes when any of them does (each step is a bijection of the number so far,
        /// and of the revision folded in, so one change cannot cancel itself). Nothing registered gives the seed back, as it was before API 2.
        /// </summary>
        public int FoldRevision(int seed)
        {
            Hooks[] all = m_Hooks;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].ReservedRevision != null)
                    seed = unchecked(seed * 16777619 ^ all[i].ReservedRevision());
            }

            return seed;
        }

        /// <summary>Every extension adds its poles and arms to the same two lists, in the order they joined.</summary>
        public void AddHosts(float x, float z, float radius, List<HostCandidate> posts, List<HostArm> arms)
        {
            Hooks[] all = m_Hooks;
            for (int i = 0; i < all.Length; i++)
                all[i].OtherHosts?.Invoke(x, z, radius, posts, arms);
        }
    }
}
