using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// One sign as it is drawn: the kind (SignDef) with its settings (the spec of a guide board). A classic sign has none and is the same everywhere; a guide board is laid out from its
    /// spec, so its size, its picture and its parts are those of this instance. The key names the picture: two items with the same kind and spec share one.
    /// </summary>
    public sealed class SignInstance
    {
        /// <summary>The size of the picture's longest side (pixels) and the most pixels to a metre of board: a larger board gets fewer.</summary>
        public const int MaxTexture = 768;
        public const float MaxPixelsPerMetre = 640f;
        public const float MinPixelsPerMetre = 100f;

        /// <summary>The def the geometry is built from: for a guide board a copy with the size of its main board.</summary>
        public SignDef Def;

        /// <summary>The kind's id, then "#" and the written spec for a guide board.</summary>
        public string Key;

        /// <summary>The written spec (canonical), empty for a classic sign.</summary>
        public string Spec;

        /// <summary>The laid out board; null for a classic sign.</summary>
        public GuideBoard Board;

        public GuideSpec Settings;

        /// <summary>The picture's size in pixels, and how many pixels go to a metre of the board (guide boards).</summary>
        public int TexW, TexH;
        public float PixelsPerMetre;

        /// <summary>The box round all parts from the main board's top left corner, y down (guide boards); the picture covers it.</summary>
        public float BoundsX0, BoundsY0, BoundsX1, BoundsY1;

        public bool IsBoard => Board != null;

        /// <summary>An LED screen: a variable-message sign drawn as dots (a board of one part with a dot pitch).</summary>
        public bool IsLed => Board != null && Board.LedPitch > 0f && Board.Parts.Count == 1;
    }

    public static class SignInstances
    {
        /// <summary>At most twice this many boards are kept (TwoGenCache: the ones not used for a whole generation go); until 0.7.0 all were dropped when 1500 were held.</summary>
        private const int kMaxCached = 1500;
        private static readonly TwoGenCache<string, SignInstance> s_Cache = new TwoGenCache<string, SignInstance>(kMaxCached);

        /// <summary>
        /// The instance of a board by the settings exactly as they were written (the kind's id and the string): a settings string that was written by this mod is already canonical, and parsing it to find
        /// out is most of what Of costs (11 microseconds on Mono for a guide board, and the tool asks for the boards of every sign near the cursor every frame it points: until 0.7.1 that was a third of its
        /// time). A string that is not canonical (typed, or from an older version) finds its instance by parsing the first time and by this cache after.
        /// </summary>
        private static readonly TwoGenCache<(string, string), SignInstance> s_Raw = new TwoGenCache<(string, string), SignInstance>(kMaxCached);

        /// <summary>The instance of a sign with a written spec (null or empty: the kind's defaults). Cached; safe from any thread.</summary>
        public static SignInstance Of(SignDef def, string spec)
        {
            if (def.Template == null)
            {
                lock (s_Cache)
                {
                    if (!s_Cache.TryGet(def.Id, out SignInstance classic))
                    {
                        classic = new SignInstance
                        {
                            Def = def,
                            Key = def.Id,
                            Spec = "",
                            TexW = def.TextureWidth,
                            TexH = def.TextureHeight
                        };
                        s_Cache.Set(def.Id, classic);
                    }

                    return classic;
                }
            }

            var rawKey = (def.Id, spec ?? "");
            lock (s_Cache)
            {
                if (s_Raw.TryGet(rawKey, out SignInstance quick))
                    return quick;
            }

            GuideSpec settings = string.IsNullOrEmpty(spec) ? def.Template.Default() : def.Template.Parse(spec);
            string written = settings.ToString();
            string key = def.Id + "#" + written;
            SignInstance found;
            lock (s_Cache)
            {
                s_Cache.TryGet(key, out found);
            }

            if (found == null)
            {
                SignInstance made = Make(def, settings, written, key);
                lock (s_Cache)
                {
                    // Another thread may have made the same one meanwhile: the first stays, so that one board is one object.
                    if (!s_Cache.TryGet(key, out found))
                    {
                        s_Cache.Set(key, made);
                        found = made;
                    }
                }
            }

            lock (s_Cache)
                s_Raw.Set(rawKey, found);
            return found;
        }

        /// <summary>
        /// The instance of a sign with a written spec if it is made already, else null: for the thread that must not wait for the layout (the editor, 0.12.64). A classic sign has its instance at once.
        /// A spec asked for as Of was given it is found; Of makes it (on a worker) and this finds it after.
        /// </summary>
        public static SignInstance Cached(SignDef def, string spec)
        {
            if (def.Template == null)
                return Of(def, spec);
            lock (s_Cache)
            {
                return s_Raw.TryGet((def.Id, spec ?? ""), out SignInstance found) ? found : null;
            }
        }

        /// <summary>Forgets the boards that are kept (a city is left, or a test wants to start from nothing).</summary>
        public static void ClearCaches()
        {
            lock (s_Cache)
            {
                s_Cache.Clear();
                s_Raw.Clear();
            }
        }

        /// <summary>The instance a face material was made for: the key is the kind's id and the spec, so it can be made again after the cache was cleared. Null for a key of no known kind.</summary>
        public static SignInstance FromKey(string key)
        {
            int hash = key.IndexOf('#');
            string id = hash < 0 ? key : key.Substring(0, hash);
            SignDef def = SignCatalog.ById(id);
            return def == null ? null : Of(def, hash < 0 ? "" : key.Substring(hash + 1));
        }

        private static SignInstance Make(SignDef def, GuideSpec settings, string written, string key)
        {
            GuideBoard board = def.Template.Layout(settings);
            GuidePart main = board.Main;
            board.Bounds(out float x0, out float y0, out float x1, out float y1);

            float w = Math.Max(0.01f, x1 - x0), h = Math.Max(0.01f, y1 - y0);
            float ppm = Math.Min(SignInstance.MaxPixelsPerMetre, Math.Min(SignInstance.MaxTexture / w, SignInstance.MaxTexture / h));
            ppm = Math.Max(SignInstance.MinPixelsPerMetre, ppm);
            int tw = Math.Max(16, (int)Math.Ceiling(w * ppm / 4f) * 4);
            int th = Math.Max(16, (int)Math.Ceiling(h * ppm / 4f) * 4);

            SignDef derived = def.Derive();
            derived.Shape = SignCatalog.ShapeOf(main.Shape);
            derived.Width = main.W;
            derived.Height = main.H;
            derived.CornerRadius = main.Radius;
            derived.Side = main.Side;
            derived.ExtraTop = board.ExtraTop;
            if (board.Text != null)
                derived.InkArea = board.Text.InkArea;
            derived.TextureWidth = tw;
            derived.TextureHeight = th;
            if (board.Box != null && board.Box.Clearance > 0f)
                derived.BottomClearance = board.Box.Clearance;
            // A standing light box counts for the whole of its box (its letters are flat on a lit face, not channel letters) and stands at its own height above the ground (0.12.76).
            if (board.Stand != null)
            {
                derived.InkArea = null;
                derived.BottomClearance = board.Stand.Lift;
            }

            // A lamp-post ad's height is its lowest point's (0.12.49): the board's lower edge is above it by what hangs under the parts (a knot, a pendant), and what is above them (a crest, a frame, a
            // finial) counts as reaching above the board.
            if (board.PoleAd != null && board.PoleAd.Clearance > 0f)
            {
                derived.BottomClearance = board.PoleAd.Clearance + board.PoleAd.Below;
                derived.ExtraTop = Math.Max(derived.ExtraTop, board.PoleAd.Above);
            }

            return new SignInstance
            {
                Def = derived,
                Key = key,
                Spec = written,
                Board = board,
                Settings = settings,
                TexW = tw,
                TexH = th,
                PixelsPerMetre = ppm,
                BoundsX0 = x0,
                BoundsY0 = y0,
                BoundsX1 = x1,
                BoundsY1 = y1
            };
        }
    }
}
