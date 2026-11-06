using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The attached support (0.6.0, 4.8.12 d): boards fixed to a street-lamp post or a signal pole of the game's. The pure part: what is built, what is not, and what it is for.</summary>
    internal static class AttachedTests
    {
        public static void RunAll()
        {
            Program.Run("attached: the support is in the catalogue, appended last, and never chosen by itself", Catalogue);
            Program.Run("attached: only the stack on its clamps is built, nothing of the post and nothing inside the host", Built);
            Program.Run("attached: a band is hollow, closed and faces out and in", Band);
            Program.Run("attached: the host is the nearest post within the radius", Pick);
            Program.Run("attached: the hints say what is wrong in both languages", Hints);
            Program.Run("attached: which boards fit a host", Fits);
            Program.Run("attached: the marker kind and the stand-in of the host", Markers);
        }

        private static SupportDef Attached => SupportCatalog.ById(SupportCatalog.AttachedPole);

        private static void Catalogue()
        {
            Program.Check(Attached != null && Attached.Kind == SupportKind.Attached && SupportKinds.Hosted(SupportKind.Attached), "the attached support exists and is hosted");
            foreach (SupportKind kind in Enum.GetValues(typeof(SupportKind)))
            {
                if (kind != SupportKind.Attached && kind != SupportKind.AttachedArm && kind != SupportKind.OverRoad)
                    Program.Check(((int)kind < (int)SupportKind.Attached || SupportKinds.OnWall(kind) || kind == SupportKind.FCantilever) && !SupportKinds.Hosted(kind), kind + " is before it (or one of the wall supports or the multiple cantilever, appended after it) and is not hosted (a saved kind is a number, so it is appended last)");
            }

            // 0.12.80: the support over the road is appended after the signal arm's; it is not hosted (its structure is read by the tool, and drawn by the game).
            Program.Check(SupportKinds.OverRoad(SupportKind.OverRoad) && !SupportKinds.Hosted(SupportKind.OverRoad) && (int)SupportKind.OverRoad > (int)SupportKind.AttachedArm, "the support over the road is appended last and is not hosted");

            // 0.12.51: the signal arm's support, hosted, appended last (until 0.12.80).
            SupportDef arm = SupportCatalog.ById(SupportCatalog.AttachedArm);
            Program.Check(arm != null && arm.Kind == SupportKind.AttachedArm && SupportKinds.Hosted(arm.Kind) && SupportKinds.OnArm(arm.Kind) && (int)SupportKind.AttachedArm > (int)SupportKind.FCantilever,
                "the signal arm's support exists, is hosted and is appended last");

            // A saved support is the hash of the id: the other supports kept theirs.
            foreach (string id in new[] { SupportCatalog.SinglePole, SupportCatalog.DoublePole, SupportCatalog.TriplePole, SupportCatalog.Cantilever, SupportCatalog.Gantry, SupportCatalog.DoubleCantilever })
                Program.Check(SupportCatalog.ById(id) != null && SupportCatalog.ById(id).Kind != SupportKind.Attached, id + " is still what it was");

            // No sign is put on it by the auto choice: the owner picks it. A lamp-post ad (0.12.49) is what goes on a lamp post.
            foreach (SignDef def in SignCatalog.All)
                Program.Check(SupportKinds.Hosted(SupportPlanner.Auto(def).Kind) == (def.Group == Geo.Signs.LampAds.Group), def.Id + ": the automatic support is the attached one for a lamp-post ad only");
        }

        private static void Built()
        {
            SupportDef attached = Attached, single = SupportCatalog.ById(SupportCatalog.SinglePole);
            foreach (string id in new[] { "gb.prohib.09", "gb.warn.01", "gb.guide9.47b", "gb.guide8.64" })
            {
                SignDef def = SignCatalog.ById(id);
                if (def == null)
                    continue;
                SignParts parts = SignBuilder.Build(def, attached), post = SignBuilder.Build(def, single);
                SupportLayout l = parts.Layout;
                string what = id + " on the attached support";

                Program.Near(l.PoleRadius, SupportLayouts.HostRadius, 1e-6f, what + ": the host has the radius the layout assumes");
                Program.Check(l.PoleX.Length == 1 && l.PoleX[0] == 0f, what + ": one host, in the middle");
                Program.Near(l.BackZ, SupportLayouts.HostRadius + SupportLayouts.PlateDepth + SupportLayouts.RailDepth, 1e-6f, what + ": the board stands clear in front of the host");
                Program.Check(parts.Concrete.VertexCount == 0, what + ": no foundation (the game's post has its own)");
                Program.Check(parts.Metal.VertexCount > 0 && parts.Metal.VertexCount < post.Metal.VertexCount, what + ": less metal than on a post, which has a tube, a cap and a foot: " + parts.Metal.VertexCount + " against " + post.Metal.VertexCount);
                Program.Check(parts.Face.VertexCount == post.Face.VertexCount, what + ": the same face");

                // Nothing is inside the host: the clamps and the channels are round and in front of it, never in its place.
                float closest = float.MaxValue;
                Vec3 nearest = default;
                foreach (Vec3 p in parts.Metal.Positions)
                {
                    float r = (float)Math.Sqrt(p.X * p.X + p.Z * p.Z);
                    if (r < closest)
                    {
                        closest = r;
                        nearest = p;
                    }
                }
                // The plates behind the channels are let 5 mm into the post, as a flat plate is against a round one.
                Program.Check(closest >= SupportLayouts.HostRadius - 0.006f, what + ": no metal inside the host: nearest " + closest + " at " + nearest.X + ", " + nearest.Y + ", " + nearest.Z + " from its axis, host radius " + SupportLayouts.HostRadius);

                // The lowest metal is the board's, not a foot: the stack hangs on the game's post.
                parts.Metal.Bounds(out Vec3 mMin, out Vec3 mMax);
                Program.Check(mMin.Y >= l.BottomClearance - 0.05f, what + ": nothing under the board: lowest " + mMin.Y + ", board from " + l.BottomClearance);
                Program.Check(parts.Picks.Count >= 1 && parts.Picks.Count == post.Picks.Count - 2, what + ": the boards can be pointed at, and neither a post nor a foot that does not exist: " + parts.Picks.Count + " pick(s) against " + post.Picks.Count + " on a post");
            }
        }

        private static void Band()
        {
            const float ri = 0.09f, ro = 0.10f;
            var mesh = new MeshBuilder();
            Solids.Band(mesh, new Vec3(0f, 2f, 0f), new Vec3(0f, 2.07f, 0f), ri, ro, 16);
            Program.Check(mesh.TriangleCount > 0, "a band has triangles");
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (Vec3 p in mesh.Positions)
            {
                float r = (float)Math.Sqrt(p.X * p.X + p.Z * p.Z);
                lo = Math.Min(lo, r);
                hi = Math.Max(hi, r);
            }

            Program.Near(lo, ri, 1e-4f, "nothing is nearer the axis than the inner radius: " + lo);
            Program.Near(hi, ro, 1e-4f, "nothing is further than the outer radius: " + hi);

            // Every triangle faces the way its corners say (the outer wall and the rings out, the inner wall in): the winding and the normals agree.
            int wrong = 0, inward = 0, outward = 0;
            for (int i = 0; i < mesh.Indices.Count; i += 3)
            {
                int a = mesh.Indices[i], b = mesh.Indices[i + 1], c = mesh.Indices[i + 2];
                Vec3 n = MeshBuilder.Cross(mesh.Positions[b] - mesh.Positions[a], mesh.Positions[c] - mesh.Positions[a]);
                Vec3 vertexNormal = mesh.Normals[a] + mesh.Normals[b] + mesh.Normals[c];
                if (Vec3.Dot(n, vertexNormal) <= 0f)
                    wrong++;
                Vec3 centre = (mesh.Positions[a] + mesh.Positions[b] + mesh.Positions[c]) * (1f / 3f);
                float radial = centre.X * n.X + centre.Z * n.Z;
                float rc = (float)Math.Sqrt(centre.X * centre.X + centre.Z * centre.Z);
                // A triangle of a wall (its normal is across the axis): outward on the outer wall, inward on the inner.
                if (Math.Abs(n.Y) < 0.3f * n.Length)
                {
                    if (radial > 0f)
                        outward++;
                    else
                        inward++;
                    Program.Check((rc > (ri + ro) * 0.5f) == (radial > 0f), "a wall triangle faces away from the axis on the outer wall and towards it on the inner wall: radius " + rc);
                }
            }

            Program.Check(wrong == 0, "winding and vertex normals agree: " + wrong + " triangle(s) are the other way");
            Program.Check(outward == 32 && inward == 32, "16 sides: 32 triangles outside and 32 inside: " + outward + " / " + inward);
        }

        private static void Pick()
        {
            var posts = new List<HostCandidate>
            {
                new HostCandidate { X = 10f, Y = 3f, Z = 0f, Kind = HostCandidate.Lamp, Id = 1 },
                new HostCandidate { X = 4f, Y = 0f, Z = 3f, Kind = HostCandidate.Signal, Id = 2 },
                new HostCandidate { X = -3f, Y = 0f, Z = 0f, Kind = HostCandidate.Lamp, Id = 3 }
            };
            Program.Check(HostPick.Nearest(posts, 0f, 0f, HostPick.Radius, out float d) == 2 && Math.Abs(d - 3f) < 1e-4f, "the nearest is the one 3 m away on the left: " + d);
            Program.Check(HostPick.Nearest(posts, 4f, 4f, HostPick.Radius, out d) == 1 && Math.Abs(d - 1f) < 1e-4f, "the signal pole by the cursor: " + d);
            var lone = new List<HostCandidate> { new HostCandidate { X = 0f, Z = 0f } };
            Program.Check(HostPick.Nearest(lone, 0f, 7.9f, HostPick.Radius, out d) == 0 && HostPick.Nearest(lone, 0f, 8.1f, HostPick.Radius, out d) == -1, "7.9 m is inside the radius and 8.1 m is not");
            Program.Check(HostPick.Nearest(posts, 50f, 50f, HostPick.Radius, out d) == -1 && d == float.MaxValue, "nothing within 8 m: -1");
            Program.Check(HostPick.Nearest(posts, 0f, 0f, 2.9f, out d) == -1, "a smaller radius leaves the 3 m post out");
            Program.Check(HostPick.Nearest(posts, 0f, 0f, 3f, out d) == 2, "the radius itself is inside");
            Program.Check(HostPick.Nearest(null, 0f, 0f, 8f, out d) == -1 && HostPick.Nearest(new List<HostCandidate>(), 0f, 0f, 8f, out d) == -1, "no list and no posts are no host");

            // The height does not count: a post on a slope is as near as one on the flat.
            var high = new List<HostCandidate> { new HostCandidate { X = 1f, Y = 500f, Z = 0f, Kind = HostCandidate.Lamp }, new HostCandidate { X = 2f, Y = 0f, Z = 0f, Kind = HostCandidate.Lamp } };
            Program.Check(HostPick.Nearest(high, 0f, 0f, 8f, out d) == 0, "the height is not part of the distance");

            // Two posts equally near: the earlier one.
            var tie = new List<HostCandidate> { new HostCandidate { X = 2f, Z = 0f, Id = 7 }, new HostCandidate { X = -2f, Z = 0f, Id = 8 } };
            Program.Check(HostPick.Nearest(tie, 0f, 0f, 8f, out d) == 0, "a tie goes to the earlier post");
            Program.Check(HostPick.Name(HostCandidate.Lamp) == "lamp" && HostPick.Name(HostCandidate.Signal) == "signal" && HostPick.Name(0) == "", "kind names");
            Program.Check(HostPick.Fixed < HostPick.Radius && HostPick.Fixed > 0.5f, "a placed sign stands at the foot of its post, within a metre and a half");
        }

        private static void Hints()
        {
            foreach (bool zh in new[] { true, false })
            {
                string lamp = HintText.Of(new HintInfo { Kind = "host", Host = "lamp" }, zh), signal = HintText.Of(new HintInfo { Kind = "host", Host = "signal" }, zh), any = HintText.Of(new HintInfo { Kind = "host" }, zh);
                Program.Check(lamp != signal && lamp != any && signal != any, "the three hosts are named apart: " + lamp);
                Program.Check(lamp.Contains(zh ? "路灯杆" : "lamp post") && signal.Contains(zh ? "信号灯杆" : "signal pole"), "the host is named");
                string none = HintText.Of(new HintInfo { Kind = "host-none", Left = HostPick.Radius }, zh);
                Program.Check(none.Contains("8.0 m"), "the radius is in the hint: " + none);
                string large = HintText.Of(new HintInfo { Kind = "host-large" }, zh);
                Program.Check(large.Contains("1.6 m") && large.Contains("2.4 m"), "the size that fits is in the hint: " + large);
                // The owner's 0.12.77 log: a combined auxiliary sign of 1.34 x 1.31 m was refused with a hint that spoke of width and height only; it was the AREA (1.76 m2 over the 1.6 m2 of one post).
                string area = HintText.Of(new HintInfo { Kind = "host-large", Need = 1.76f }, zh);
                Program.Check(area.Contains("1.76") && area.Contains("1.6 m"), "the board's own area is in the hint, with the area a pole carries: " + area);
                Program.Check(area.Contains("m2") || area.Contains("m²"), "the hint names an area, not only a width: " + area);
                Program.Check(!HintText.Of(new HintInfo { Kind = "host-large", Need = 0f }, zh).Contains("0.00"), "no area known: no figure is made up");
                Program.Check(HintText.Of(new HintInfo { Kind = "host-road" }, zh).Length > 10, "the road hint has text");
                Program.Check(!lamp.Contains("{") && !none.Contains("{") && !large.Contains("{"), "nothing is left to fill in");
            }

            string gone = HintText.HostGone(true), goneEn = HintText.HostGone(false);
            Program.Check(gone.Contains("1.5 m") && goneEn.Contains("1.5 m") && gone.Contains("路灯杆") && goneEn.Contains("lamp post") && !gone.Contains("{") && !goneEn.Contains("{"), "the note of a sign that lost its post names the distance and what is missing: " + gone);

            // The keys listed are the keys of the tool: rotate, turn round, change the support.
            List<string[]> keys = HintText.Keys(new HintInfo { Kind = "host", Host = "lamp" }, false);
            Program.Check(keys.Exists(k => k[0] == "[  ]") && keys.Exists(k => k[0] == "\\") && keys.Exists(k => k[0] == ";  '"), "the keys are listed");

            // The size in the text is the size the support planner uses for a single post.
            Program.Check(SupportPlanner.SinglePostMaxWidth == 1.6f && SupportPlanner.SinglePostMaxHeight == 2.4f, "1.6 m and 2.4 m are the limits of a single post");
        }

        private static void Fits()
        {
            int fits = 0, not = 0;
            foreach (SignDef def in SignCatalog.All)
            {
                bool expected = def.Group == Geo.Signs.LampAds.Group || SupportPlanner.Auto(def).Kind == SupportKind.SinglePole;
                Program.Check(SupportPlanner.FitsHost(def) == expected, def.Id + ": fits a host exactly when a single post carries it (or it is a lamp-post ad)");
                fits += expected ? 1 : 0;
                not += expected ? 0 : 1;
            }

            Program.Check(fits > 100 && not > 20, "both happen in the catalogue: " + fits + " fit, " + not + " do not");
            Program.Check(SupportPlanner.FitsHost(SignCatalog.ById("gb.prohib.09")), "a speed-limit circle fits");
            Program.Check(!SupportPlanner.FitsHost(SignCatalog.ById("guide.exit.advance")), "an expressway advance-direction board does not (it is far wider than 1.6 m)");
            Program.Check(!SupportPlanner.FitsHost(SignCatalog.ById("gb.vms.text")), "an overhead screen does not");
            Program.Check(!SupportPlanner.FitsHost(null), "nothing does not");
        }

        private static void Markers()
        {
            Program.Check(TsMarkers.KindOf(SupportKind.Attached) == TsMarkers.KindAttached && TsMarkers.KindAttached == 5, "an attached sign is marker kind 5");
            var kinds = new HashSet<int>();
            foreach (SupportKind kind in Enum.GetValues(typeof(SupportKind)))
                kinds.Add(TsMarkers.KindOf(kind));
            Program.Check(kinds.Contains(TsMarkers.KindPost) && kinds.Contains(TsMarkers.KindAttached) && kinds.Count >= 5, "the kinds stay apart: " + kinds.Count);

            // The stand-in the thumbnail and the pictures draw for the host: a post from the ground to above the board.
            SignParts parts = SignBuilder.Build(SignCatalog.ById("gb.prohib.09"), Attached);
            int before = parts.Metal.VertexCount;
            SignBuilder.AddHostStandIn(parts);
            Program.Check(parts.Metal.VertexCount > before, "the stand-in adds a post");
            parts.Metal.Bounds(out Vec3 min, out Vec3 max);
            Program.Check(min.Y <= 0.001f && max.Y >= parts.Layout.BoardTop + 0.1f, "it stands on the ground and rises over the board: " + min.Y + " to " + max.Y);
        }
    }
}
