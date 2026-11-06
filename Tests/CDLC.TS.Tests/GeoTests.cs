using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>Offline checks of the sign geometry: meshes, winding, picture coordinates, catalogue and chunking.</summary>
    internal static class GeoTests
    {
        public static void RunAll()
        {
            Program.Run("catalogue: ids, hashes and fields", Catalogue);
            Program.Run("outline: shapes, orientation and size", Outlines);
            Program.Run("mesh: every sign is finite, closed in index range and has unit normals", MeshSanity);
            Program.Run("mesh: front-facing winding agrees with the normals", Winding);
            Program.Run("mesh: the face shows its picture and faces +Z", FaceFacing);
            Program.Run("supports: every sign on every support fits together", Assembly);
            Program.Run("supports: the catalogue and the layouts", SupportRules);
            Program.Run("supports: the cantilever's arm points to the road", ArmDirection);
            Program.Run("supports: items keep their support in a tile", SupportInChunks);
            Program.Run("picking: the parts of a sign and not the air round them", PickingRules);
            Program.Run("mesh: Append moves and turns", AppendTransform);
            Program.Run("tiles: floor division also below zero", TileKeys);
            Program.Run("chunks: items of one tile become metal and face meshes", ChunkContent);
            Program.Run("chunks: meshes are cut before 16-bit indices run out", ChunkSplit);
            Program.Run("chunks: unknown types and bad numbers are skipped", ChunkSkips);
            Program.Run("spacing: the nearest sign of the same type", SpacingRules);
            Program.Run("tile diff: what a resync has to rebuild", TileDiffRules);
        }

        private static void Catalogue()
        {
            var ids = new HashSet<string>();
            var hashes = new HashSet<uint>();
            foreach (SignDef def in SignCatalog.All)
            {
                Program.Check(ids.Add(def.Id), $"duplicate id {def.Id}");
                Program.Check(hashes.Add(def.Hash), $"hash collision at {def.Id}");
                Program.Check(!string.IsNullOrEmpty(def.Zh) && !string.IsNullOrEmpty(def.En) && !string.IsNullOrEmpty(def.Code), $"{def.Id} has names");
                Program.Check(def.Width > 0f && def.Height > 0f && SupportPlanner.Auto(def) != null, $"{def.Id} has a size and the planner gives it a support");
                if (def.Template == null)
                {
                    Program.Check(def.TextureWidth * def.Height == def.TextureHeight * def.Width, $"{def.Id}: the picture has the proportions of the board");
                }
                else
                {
                    // A guide board's picture covers the box round all its parts (to within the pixels it is rounded up to).
                    SignInstance inst = SignInstances.Of(def, null);
                    float boxW = inst.BoundsX1 - inst.BoundsX0, boxH = inst.BoundsY1 - inst.BoundsY0;
                    Program.Near((float)inst.TexW / inst.TexH, boxW / boxH, 0.02f * (boxW / boxH) + 0.02f, $"{def.Id}: the picture has the proportions of the board and its plates");
                    Program.Check(inst.TexW <= SignInstance.MaxTexture + 4 && inst.TexH <= SignInstance.MaxTexture + 4, $"{def.Id}: the picture is not larger than {SignInstance.MaxTexture} px");
                }
                Program.Check(ReferenceEquals(SignCatalog.ById(def.Id), def) && ReferenceEquals(SignCatalog.ByHash(def.Hash), def), $"{def.Id} is found by id and hash");
            }

            Program.Check(SignCatalog.ById("nothing") == null && SignCatalog.ByHash(7u) == null, "unknown ids give null");
            Program.Check(SignCatalog.HashOf("gb.stop") == SignCatalog.HashOf("gb.stop"), "the hash is stable");
            // Saved with every item: if this changes, old saves lose their items. 0x... computed from the FNV-1a of the id as written.
            Program.Check(SignCatalog.HashOf("") == 2166136261u, "the hash of nothing is the FNV offset basis");
        }

        private static void Outlines()
        {
            foreach (SignDef def in SignCatalog.All)
            {
                List<Point2> outline = SignCatalog.Outline(def);
                Program.Check(outline.Count >= 4, $"{def.Id} has an outline");
                float area2 = 0f;
                float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                for (int i = 0; i < outline.Count; i++)
                {
                    Point2 a = outline[i];
                    Point2 b = outline[(i + 1) % outline.Count];
                    area2 += a.X * b.Y - b.X * a.Y;
                    minX = Math.Min(minX, a.X);
                    maxX = Math.Max(maxX, a.X);
                    minY = Math.Min(minY, a.Y);
                    maxY = Math.Max(maxY, a.Y);
                }

                Program.Check(area2 > 0f, $"{def.Id} runs counter-clockwise");
                Program.Near(maxX - minX, def.Width, def.Shape == ShapeKind.Octagon ? 0.05f : 0.005f, $"{def.Id} width");
                Program.Near(maxY - minY, def.Height, def.Shape == ShapeKind.Octagon ? 0.05f : 0.005f, $"{def.Id} height");
            }

            SignDef octagon = SignCatalog.ById("gb.stop");
            List<Point2> o = SignCatalog.Outline(octagon);
            Program.Check(o.Count == 8, "an octagon has eight corners");
            // The sides are flat on the axes: the right-most points share one x, the distance between opposite sides is the width.
            float maxXo = float.MinValue;
            foreach (Point2 p in o)
                maxXo = Math.Max(maxXo, p.X);
            Program.Near(maxXo, octagon.Width * 0.5f, 1e-4f, "octagon: flat side at half the width");
            Program.Check(SignCatalog.Outline(SignCatalog.ById("gb.speed60")).Count == 48, "a circle is 48 corners");
        }

        /// <summary>Every sign on every support style: what a player can place.</summary>
        internal static IEnumerable<(SignDef sign, SupportDef support)> Combos()
        {
            foreach (SignDef def in SignCatalog.All)
            {
                // A lamp-post ad hangs beside its post, not on its face (0.12.49): LampAdTests check it.
                if (def.Group == CDLC.TS.Geo.Signs.LampAds.Group)
                    continue;
                // The letters of a text light box are solids with no plate behind them (0.12.75): TextBoxTests check it.
                if (def.Id == CDLC.TS.Geo.Signs.TextBoxes.TemplateId)
                    continue;
                // A standing light box is a housing with a lit panel and flat letters, with a frame of its own on a post (0.12.76): StandBoxTests check it.
                if (def.Id == CDLC.TS.Geo.Signs.StandBoxes.TemplateId)
                    continue;
                foreach (SupportDef support in SupportCatalog.All)
                {
                    // Two posts on a board narrower than 0.7 m (the countdown board) make no sense: the posts are 0.6 m apart at the least.
                    if (support.Kind == SupportKind.DoublePole && def.Width < 0.7f)
                        continue;
                    // Three posts on a board narrower than 3 m likewise.
                    if (support.Kind == SupportKind.TriplePole && def.Width < 3.0f)
                        continue;
                    yield return (def, support);
                }
            }
        }

        private static IEnumerable<SignParts> AllParts()
        {
            foreach ((SignDef sign, SupportDef support) in Combos())
                yield return SignBuilder.Build(sign, support);
        }

        /// <summary>The meshes of a support that are checked: its steel, the first face, the concrete and the painted solids it has (an electric box's body, 0.12.23).</summary>
        private static List<MeshBuilder> Meshes(SignParts parts)
        {
            var meshes = new List<MeshBuilder> { parts.Metal, parts.Face, parts.Concrete };
            foreach (MeshBuilder solid in parts.Solids)
            {
                if (solid != null)
                    meshes.Add(solid);
            }

            return meshes;
        }

        private static void MeshSanity()
        {
            foreach (SignParts parts in AllParts())
            {
                foreach (MeshBuilder mesh in Meshes(parts))
                {
                    if (mesh == parts.Concrete && mesh.VertexCount == 0)
                        continue;
                    Program.Check(mesh.VertexCount > 0 && mesh.TriangleCount > 0, "a mesh is not empty");
                    Program.Check(mesh.Normals.Count == mesh.VertexCount && mesh.U.Count == mesh.VertexCount && mesh.V.Count == mesh.VertexCount, "parallel arrays agree");
                    Program.Check(mesh.Indices.Count % 3 == 0, "whole triangles");
                    foreach (int index in mesh.Indices)
                    {
                        if (index < 0 || index >= mesh.VertexCount)
                        {
                            Program.Check(false, "an index points outside the vertices");
                            break;
                        }
                    }

                    for (int i = 0; i < mesh.VertexCount; i++)
                    {
                        if (!mesh.Positions[i].IsFinite || !mesh.Normals[i].IsFinite)
                        {
                            Program.Check(false, "a vertex is not finite");
                            break;
                        }

                        float length = mesh.Normals[i].Length;
                        if (Math.Abs(length - 1f) > 1e-3f)
                        {
                            Program.Check(false, $"a normal has length {length}");
                            break;
                        }
                    }
                }
            }
        }

        private static void Winding()
        {
            foreach (SignParts parts in AllParts())
            {
                foreach (MeshBuilder mesh in Meshes(parts))
                {
                    int bad = 0;
                    int degenerate = 0;
                    for (int t = 0; t < mesh.TriangleCount; t++)
                    {
                        int a = mesh.Indices[t * 3], b = mesh.Indices[t * 3 + 1], c = mesh.Indices[t * 3 + 2];
                        Vec3 n = MeshBuilder.Cross(mesh.Positions[b] - mesh.Positions[a], mesh.Positions[c] - mesh.Positions[a]);
                        if (n.Length < 1e-9f)
                        {
                            degenerate++;
                            continue;
                        }

                        Vec3 vertexNormal = mesh.Normals[a] + mesh.Normals[b] + mesh.Normals[c];
                        if (Vec3.Dot(n, vertexNormal) <= 0f)
                            bad++;
                    }

                    Program.Check(bad == 0, $"{bad} triangles face against their normals");
                    Program.Check(degenerate == 0, $"{degenerate} degenerate triangles");
                }
            }
        }

        private static void FaceFacing()
        {
            foreach ((SignDef def, SupportDef support) in Combos())
            {
                SignParts parts = SignBuilder.Build(def, support);
                // A plate on a street-name post's head is printed on both faces round the post's axis (PlatePostTests).
                if (parts.Layout.Head != null)
                    continue;
                MeshBuilder face = parts.Face;
                for (int i = 0; i < face.VertexCount; i++)
                {
                    Program.Check(face.Normals[i].Z > 0.999f, $"{def.Id}: face normal points to +Z");
                    Program.Near(face.Positions[i].Z, SignBuilder.FacePlane(parts.Layout, SignInstances.Of(def, null)), 1e-5f, $"{def.Id} on {support.Id}: face plane");
                    if (face.U[i] < -1e-4f || face.U[i] > 1f + 1e-4f || face.V[i] < -1e-4f || face.V[i] > 1f + 1e-4f)
                        Program.Check(false, $"{def.Id}: picture coordinate {face.U[i]}, {face.V[i]} is outside the picture");
                }

                // The picture is not mirrored. A viewer in front of the face looks along -normal; in Unity's left-handed frame the right of a viewer
                // is cross(up, forward). The picture's U must grow towards that right-hand side (0.1.1 had it growing with +X and every picture was
                // mirrored on screen although the pictures themselves, drawn offline, were right).
                Vec3 forward = new Vec3(0f, 0f, -1f);
                Vec3 viewerRight = MeshBuilder.Cross(new Vec3(0f, 1f, 0f), forward);
                Program.Near(viewerRight.X, -1f, 1e-6f, "the right of a viewer who looks along -Z is -X");
                int left = 0, right = 0;
                for (int i = 1; i < face.VertexCount; i++)
                {
                    if (Vec3.Dot(viewerRight, face.Positions[i]) < Vec3.Dot(viewerRight, face.Positions[left])) left = i;
                    if (Vec3.Dot(viewerRight, face.Positions[i]) > Vec3.Dot(viewerRight, face.Positions[right])) right = i;
                }

                Program.Check(face.U[right] > face.U[left], $"{def.Id}: U grows towards the viewer's right");
                Program.Check(face.U[right] > 0.9f && face.U[left] < 0.1f, $"{def.Id}: the board spans the whole picture sideways");
                // The same for every pair of vertices, not only the two ends.
                bool consistent = true;
                for (int i = 0; i < face.VertexCount && consistent; i++)
                {
                    for (int j = 0; j < face.VertexCount; j++)
                    {
                        float dx = Vec3.Dot(viewerRight, face.Positions[i] - face.Positions[j]);
                        float du = face.U[i] - face.U[j];
                        if (Math.Abs(dx) > 1e-3f && Math.Sign(dx) != Math.Sign(du))
                        {
                            consistent = false;
                            break;
                        }
                    }
                }

                Program.Check(consistent, $"{def.Id}: U grows with the viewer's right for every pair of vertices");
                // The back faces the other way: seen from behind, the right of the viewer is +X. An electric box's back is its body's.
                BoxBody box = SignInstances.Of(def, null).Board?.Box;
                MeshBuilder back = box != null ? parts.Solids[(int)box.Colour] : parts.Layout.Framed ? parts.Solids[(int)SolidColour.Graphite] : parts.Metal;
                int backLeft = -1, backRight = -1;
                for (int i = 0; i < back.VertexCount; i++)
                {
                    // Within the board's width: an electric box's rain hood has its back in that plane too, and reaches past the box's sides.
                    if (back.Normals[i].Z > -0.999f || Math.Abs(back.Positions[i].Z - parts.Layout.BackZ) > 1e-5f || Math.Abs(back.Positions[i].X - parts.Layout.BoardCentreX) > def.Width * 0.5f + 1e-4f)
                        continue;
                    if (backLeft < 0 || back.Positions[i].X < back.Positions[backLeft].X) backLeft = i;
                    if (backRight < 0 || back.Positions[i].X > back.Positions[backRight].X) backRight = i;
                }

                Program.Check(backLeft >= 0 && back.U[backRight] > back.U[backLeft], $"{def.Id}: on the back U grows with +X");
                int low = 0, high = 0;
                for (int i = 1; i < face.VertexCount; i++)
                {
                    if (face.Positions[i].Y < face.Positions[low].Y) low = i;
                    if (face.Positions[i].Y > face.Positions[high].Y) high = i;
                }

                Program.Check(face.V[high] > face.V[low], $"{def.Id}: V grows with Y");
            }
        }

        private static void Assembly()
        {
            foreach ((SignDef def, SupportDef support) in Combos())
            {
                string what = $"{def.Id} on {support.Id}";
                SignParts parts = SignBuilder.Build(def, support);
                SupportLayout layout = parts.Layout;
                parts.Face.Bounds(out Vec3 fMin, out Vec3 fMax);

                Program.Check(support.Kind == layout.Kind, what + ": the layout is of the support's kind");
                Program.Near(fMin.Y, layout.BottomClearance, 1e-3f, what + ": the lower edge of the board is at the clearance");
                SignInstance drawn = SignInstances.Of(def, null);
                float drawnHeight = drawn.IsBoard ? drawn.BoundsY1 - drawn.BoundsY0 : def.Height;
                float drawnWidth = drawn.IsBoard ? drawn.BoundsX1 - drawn.BoundsX0 : def.Width;
                Program.Near(fMax.Y - fMin.Y, drawnHeight, 0.05f, what + ": board height");
                // A pointed board (路7 a) has its tip rounded: its mesh is a little short of its box.
                bool pointed = drawn.IsBoard && (drawn.Board.Main.Shape == GuidePartShape.PointRight || drawn.Board.Main.Shape == GuidePartShape.PointLeft);
                Program.Near(fMax.X - fMin.X, drawnWidth, def.Shape == ShapeKind.Octagon ? 0.05f : pointed ? 0.03f : 0.005f, what + ": board width");
                Program.Near((fMin.X + fMax.X) * 0.5f, layout.BoardCentreX, 0.01f, what + ": the board is centred where the layout says");
                // A plate has the layout's front plane and is as thick as the folded edge; an LED screen is a cabinet and its face is further forward (SignBuilder.FacePlane, Led.CabinetDepth).
                float facePlane = SignBuilder.FacePlane(layout, drawn);
                if (layout.Head != null)
                {
                    // On a street-name post's head (0.12.57): round the post's axis, PlateDepth thick, printed on both faces.
                    Program.Near(fMax.Z, layout.Head.PlateDepth * 0.5f, 1e-5f, what + ": the plate's face half its depth before the post's axis");
                    Program.Near(fMin.Z, -layout.Head.PlateDepth * 0.5f, 1e-5f, what + ": its back face half its depth behind");
                }
                else
                {
                    Program.Near(fMin.Z, facePlane, 1e-5f, what + ": the face is one plane");
                    Program.Near(fMax.Z, facePlane, 1e-5f, what + ": the face is one plane (top)");
                }
                Program.Near(layout.FrontZ - layout.BackZ, SupportLayouts.BoardThickness, 1e-6f, what + ": the layout's plate is as thick as the edge says");
                float thickness = drawn.Board?.Box != null ? drawn.Board.Box.Depth : drawn.IsLed ? Led.CabinetDepth(drawn.Board.Main.W, drawn.Board.Main.H) : SupportLayouts.BoardThickness;
                Program.Near(facePlane - layout.BackZ, thickness, 1e-5f, what + ": the board is as thick as it is built (an electric box, a cabinet or the edge)");

                // The ground: the lowest metal is the flange / the pad, never below the foundation and never above the ground by more than the pad shows. An attached support (a hosted one) has no
                // foundation and no post of its own: it is only the channels and the clamps round the game's post, and its lowest part is the lowest channel.
                bool hosted = SupportKinds.Hosted(support.Kind);
                // On a wall (0.12.2): no foundation and no post; a bracket's wall plate reaches a little below the root of its arm, which may be below a small board's lower edge.
                // A board flat on the face of a structure over the road (0.12.80) is built as one flat on a wall: no post, no foundation, nothing above the board; it is no wall (its host is no building).
                bool overRoad = SupportKinds.OverRoad(support.Kind), flatOnFace = support.Kind == SupportKind.WallFlat || overRoad && !support.Hanger;
                bool wall = SupportKinds.OnWall(support.Kind) || overRoad, onArm = SupportKinds.OnArm(support.Kind);
                if (wall)
                {
                    Program.Check(parts.Concrete.VertexCount == 0, what + ": a wall support has no foundation");
                    Program.Check(parts.Min.Y >= layout.BottomClearance - 0.25f, what + ": nothing of it is far below the board: lowest " + parts.Min.Y + ", board from " + layout.BottomClearance);
                }
                else if (support.Integrated || layout.Head != null)
                {
                    // The Shanghai pole and the street-name plate posts (0.12.57): their flanges under the paving, the post going down into it.
                    Program.Check(parts.Concrete.VertexCount == 0 && parts.Min.Y < 0f && parts.Min.Y > -1f, what + ": the pole goes into the ground, its foundation not drawn");
                }
                else if (!hosted)
                {
                    Program.Check(parts.Min.Y < 0f && parts.Min.Y > -1f, what + ": the foundation is under the ground");
                    Program.Check(parts.Concrete.VertexCount > 0, what + ": there is a foundation");
                    parts.Concrete.Bounds(out Vec3 cMin, out Vec3 cMax);
                    Program.Check(cMax.Y > 0f && cMax.Y < 0.02f, what + ": the pedestal is level with the ground, 1 cm over it (22MR601 9.2.2): " + cMax.Y);
                }
                else
                {
                    Program.Check(parts.Concrete.VertexCount == 0, what + ": an attached support has no foundation");
                    Program.Check(parts.Min.Y >= layout.BottomClearance - 0.05f, what + ": nothing of it is below the board (the host is the game's): lowest " + parts.Min.Y + ", board from " + layout.BottomClearance);
                }

                // The posts rise above the board (the cantilever's post above the arm), and no post goes through the face.
                parts.Metal.Bounds(out Vec3 mMin, out Vec3 mMax);
                // A post rises above its board and plate; an arm or a beam passes behind the board and the plate on it stands above (GB 5768.2 Fig. 7): the posts then rise to the arm or the beam.
                bool overheadForm = SupportKinds.Hung(support.Kind);
                if (flatOnFace)
                    Program.Check(mMax.Y <= fMax.Y + 1e-3f, what + ": flat on a wall nothing stands above the board");
                else if (support.Hanger)
                    Program.Check(mMax.Y >= layout.HangerTop && mMax.Y <= layout.HangerTop + SupportLayouts.PlateInto + 1e-3f, what + ": hung, the rods reach the soffit and nothing stands over it: " + mMax.Y + " against " + layout.HangerTop);
                else if (support.Kind == SupportKind.WallBracket)
                    Program.Check(mMax.Y >= layout.ArmY + SupportLayouts.BracketArmRadius - 1e-3f && layout.ArmY > fMax.Y, what + ": the bracket's arm is above the board, its tie above the arm");
                else if (!hosted)
                    Program.Check(overheadForm ? (layout.HeightLimit ? parts.Max.Y : mMax.Y) >= layout.ArmY : mMax.Y >= layout.PoleTop - 1e-3f, what + ": the support reaches its post's top");
                else
                    Program.Check(mMax.Y >= fMax.Y - 0.2f && mMax.Y <= fMax.Y + 0.05f, what + ": an attached support is no taller than its boards (a clamp may stand a few centimetres over): " + mMax.Y + " against " + fMax.Y);
                if (layout.Head != null)
                    Program.Check(layout.PoleTop < fMin.Y && layout.PoleTop > fMin.Y - layout.Head.Height, what + ": the post ends in the head under the plate");
                else if (!wall && !onArm)
                    Program.Check(overheadForm ? layout.PoleTop >= layout.ArmY - layout.CrankRise : layout.PoleTop <= layout.BoardTop - 0.03f + 1e-4f && layout.PoleTop > layout.BoardCentreY,
                        what + (overheadForm ? ": the post rises over its arm's root (a cranked arm rises beyond it)" : ": the post ends behind the board, in its upper half (22MR601 pp.74, 76)"));
                // A post stands behind the board (its radius is less than the back plane's distance); the overhead forms' posts are beside the road, away from the board. A wall has no post.
                bool overhead = SupportKinds.Hung(support.Kind);
                if (!overhead && support.Kind != SupportKind.WallBracket)
                    Program.Check(layout.PoleRadius < layout.BackZ, what + ": the post is behind the board's back (a post of " + layout.PoleRadius + " m, the back at " + layout.BackZ + ")");
                Program.Check(layout.PoleX.Length == (wall || onArm ? 0 : support.Kind == SupportKind.DoublePole || support.Kind == SupportKind.Gantry ? 2 : support.Kind == SupportKind.TriplePole ? 3 : 1), what + ": the number of posts");
                Program.Check(mMin.Z <= -layout.PoleRadius + 1e-4f, what + ": the post reaches behind the origin plane by its radius");

                // The clearance rules (provisional, from the summaries of GB 51038 4.3.6 and the draft of GB 5768.2 3.12): a roadside post 1.5 to 2.5 m,
                // not below 1.2 m anywhere; overhead more than 5 m.
                if (overhead)
                    Program.Check(layout.BottomClearance >= (layout.HeightLimit ? support.Clearance : 5.0f), what + ": an overhead board is above the clearance of the road");
                else if (onArm)
                    Program.Check(layout.BottomClearance < layout.HangY && layout.BoardTop > layout.HangY, what + ": on a signal arm the board is across the arm's axis, the origin");
                else if (support.Hanger)
                    Program.Check(layout.BottomClearance >= OverRoad.MinFree - 1e-4f, what + ": hung, the free height under the board is kept");
                else if (Geo.Signs.PlaceNameSigns.IsStone(def.Id))
                    Program.Near(layout.BottomClearance, Geo.Signs.PlaceNameSigns.StoneClearance, 1e-5f, what + ": a name stone (GB 17733 5.1.2) stands low");
                else
                    Program.Check(layout.BottomClearance >= 1.5f && layout.BottomClearance <= 2.5f, what + ": a roadside board is 1.5 to 2.5 m up");

                // Nothing flies: every connected piece of the support touches another. The simple form of that: no metal vertex is further than a metre from the
                // axis of the nearest post or from the arm / board, checked by the layout numbers below for each kind.
                if (layout.Framed)
                {
                    // The framed cantilever (0.12.57): one arm over the frames, past the boards' outer edge (FramedTests).
                    float far = layout.ArmSide > 0f ? fMax.X : -fMin.X, reach = layout.ArmSide > 0f ? mMax.X : -mMin.X;
                    Program.Check(layout.ArmYs.Length == 1 && layout.ArmY > fMax.Y && reach >= layout.ArmLength - 1e-3f && far < layout.ArmLength, what + ": the framed board hangs under its arm, inside its reach");
                }
                else if (SupportKinds.Arm(support.Kind))
                {
                    // A double cantilever has a second arm of the same length on the other side of the post (one board is on the left arm; the right one is bare).
                    if (support.Kind == SupportKind.DoubleCantilever)
                        Program.Check(mMin.X <= -layout.ArmLength + 1e-3f && mMin.X >= -layout.ArmLength - 0.05f, what + ": the second arm reaches " + mMin.X + " for a length of " + layout.ArmLength);
                    // Along the arm, which reaches to -X on the mirrored cantilever (ArmSide).
                    float reach = layout.ArmSide > 0f ? mMax.X : -mMin.X, near = layout.ArmSide > 0f ? fMin.X : -fMax.X, far = layout.ArmSide > 0f ? fMax.X : -fMin.X;
                    Program.Check(reach >= layout.ArmLength - 1e-3f && reach <= layout.ArmLength + Geo.Assembly.ArmTipInside + 0.01f, what + ": the arm reaches " + reach + " for a length of " + layout.ArmLength);
                    Program.Check(far >= layout.ArmLength - 1e-3f && far <= layout.ArmLength + Geo.Assembly.ArmTipInside + 1e-3f, what + ": the arm ends behind the board, at most 5 cm short of its far edge (22MR601 p.80): " + far);
                    Program.Check(near >= layout.BoardFrom - 1e-3f, what + ": the board keeps its distance from the post");
                    Program.Check(layout.ArmYs.Length == SupportLayouts.ArmCount(drawn.Def.Width * (drawn.Def.Height + drawn.Def.ExtraTop), drawn.Def.Height, support.Kind == SupportKind.FCantilever, out _),
                        what + ": one to three arms by the board's area");
                    bool lowF = support.Kind == SupportKind.FCantilever && drawn.Def.Height < SupportLayouts.MinArmSpacing + 0.3f;
                    Program.Check(lowF || Array.TrueForAll(layout.ArmYs, y => y > layout.BottomClearance && y < layout.BoardTop), what + ": the arms pass behind the board, between its lower and upper edges");
                    if (layout.ArmYs.Length == 1)
                        Program.Near(layout.ArmY, layout.BoardTop - SupportLayouts.ArmInset(drawn.Def.Height), 1e-4f, what + ": one arm at its middle, or the upper part of a tall board");
                }
                else if (onArm)
                {
                    // 0.12.51: on another mod's arm (the line z = 0, y = 0 along X): the board in front of it, the channels and the clamps behind the board, round the arm.
                    Program.Check(layout.BackZ >= layout.HangRadius + 0.02f, what + ": the board stands clear in front of the arm");
                    Program.Check(mMin.X >= fMin.X - 0.01f && mMax.X <= fMax.X + 0.01f, what + ": nothing sticks out beside the board: metal " + mMin.X + ".." + mMax.X + ", face " + fMin.X + ".." + fMax.X);
                    Program.Check(mMin.Z <= -layout.HangRadius + 0.01f && (drawn.IsLed || mMax.Z <= fMin.Z + 1e-3f), what + ": the clamps go round the back of the arm and stay behind the face (an LED screen's cabinet reaches it)");
                    Program.Check(mMin.Y >= fMin.Y - 0.05f && mMax.Y <= fMax.Y + 0.05f, what + ": the channels are within the board's height");
                }
                else if (support.Hanger)
                {
                    // Hung (0.12.81): the tube's axis is the plane z = 0, the board in front of it clamped to it, the rods in the tube's plane and the braces behind it (OverHungTests has the rest).
                    Program.Check(layout.BackZ >= layout.HangRadius + 0.02f, what + ": the board stands clear in front of the tube");
                    Program.Check(mMin.Z <= -layout.HangRadius + 0.01f && (drawn.IsLed || mMax.Z <= fMin.Z + 1e-3f), what + ": the clamps go round the back of the tube and stay behind the face (an LED screen's cabinet reaches it)");
                    Program.Check(mMin.Y >= fMin.Y - 0.05f && layout.ArmY > fMin.Y && layout.ArmY < fMax.Y, what + ": nothing of it is under the board and the tube passes behind it");
                }
                else if (flatOnFace)
                {
                    // On the wall (the plane z = 0): nothing goes into it, the board stands off it by the spacers.
                    Program.Check(mMin.Z >= -1e-4f, what + ": nothing goes into the wall: " + mMin.Z);
                    Program.Near(layout.BackZ, support.Roof ? SupportLayouts.RoofBackZ : SupportLayouts.WallGap, 1e-6f, what + ": the board's back is the spacers off the wall (a roof's frame behind it)");
                    Program.Check(mMin.X >= fMin.X - 0.01f && mMax.X <= fMax.X + 0.01f, what + ": nothing sticks out beside the board: metal " + mMin.X + ".." + mMax.X + ", face " + fMin.X + ".." + fMax.X);
                }
                else if (support.Kind == SupportKind.WallBracket)
                {
                    // The wall is the plane x = 0: the bracket goes out of it to its side, the board under the arm, clear of the wall and of the arm's end.
                    float side = support.Side < 0 ? -1f : 1f;
                    Program.Check(side > 0f ? mMin.X >= -1e-4f : mMax.X <= 1e-4f, what + ": nothing goes into the wall");
                    float near = side > 0f ? fMin.X : -fMax.X, far = side > 0f ? fMax.X : -fMin.X;
                    Program.Check(near >= SupportLayouts.BracketWallGap - 0.01f && far <= layout.ArmLength + 1e-3f, what + ": the board hangs clear of the wall and under the arm: " + near + " to " + far + " of " + layout.ArmLength);
                    Program.Check(fMax.Y <= layout.ArmY - SupportLayouts.BracketArmRadius + 1e-3f, what + ": the board hangs under the arm");
                    Program.Check(Math.Abs(fMin.Z - fMax.Z) < 1e-5f && Math.Abs(layout.BackZ + SupportLayouts.BoardThickness * 0.5f) < 1e-6f, what + ": the board's plane is the arm's");
                }
                else if (support.Kind == SupportKind.Gantry)
                {
                    Program.Near(layout.ArmLength, layout.PoleX[1] - layout.PoleX[0], 1e-4f, what + ": the span is the distance between the posts");
                    Program.Check(layout.ArmLength >= def.Width + 2f * Geo.Assembly.GantryClear - 1e-3f, what + ": the board keeps clear of the posts: span " + layout.ArmLength);
                    Program.Check(fMin.X > layout.PoleX[0] + layout.PoleRadius && fMax.X < layout.PoleX[1] - layout.PoleRadius, what + ": the board hangs between the posts");
                    // The truss and the board overlap: a board that is not taller than the truss and its chords has its lower edge at the clearance, a taller one covers it with its top level with the
                    // upper chord's top.
                    float truss0 = layout.HangY, truss1 = layout.HangY + layout.BeamDepth;
                    float overlap = Math.Min(layout.BoardTop, truss1) - Math.Max(layout.BottomClearance, truss0);
                    Program.Check(overlap >= Math.Min(drawn.Def.Height, layout.BeamDepth) - layout.ChordRadius - 1e-3f, what + ": the board is fixed on the truss, in front of it: they overlap by " + overlap);
                    float cover = layout.BeamDepth + 2f * layout.ChordRadius;
                    if (layout.HeightLimit)
                    {
                        // A height-limit frame (0.12.78): the beam's underside is the clearance, whatever the board's height, which hangs from it.
                        Program.Near(layout.BottomClearance, support.Clearance, 1e-3f, what + ": the board's lower edge is at the clearance");
                        Program.Near(layout.HangY - layout.ChordRadius, support.Clearance, 1e-3f, what + ": the beam's underside is at the clearance");
                    }
                    else
                    {
                        if (drawn.Def.Height <= cover)
                            Program.Near(layout.BottomClearance, 5.5f, 1e-3f, what + ": a board no taller than the truss has its lower edge at the clearance");
                        else
                            Program.Near(layout.BoardTop, layout.HangY + layout.BeamDepth + layout.ChordRadius, 1e-3f, what + ": a tall board's top is level with the top of the upper chord");
                        Program.Check(layout.HangY - layout.ChordRadius >= 5.5f - 1e-3f, what + ": the truss is above the clearance of the road");
                        Program.Check(layout.BottomClearance >= 5.5f - 1e-3f, what + ": the board's lower edge is above the clearance of the road");
                    }
                    Program.Near(layout.ArmY - layout.BeamDepth * 0.5f, layout.HangY, 1e-4f, what + ": the lower chord is what the board hangs on");
                    Program.Check(layout.PoleTop > layout.ArmY + layout.BeamDepth * 0.5f, what + ": the posts rise above the beam");
                    Program.Near(mMax.X - mMin.X, layout.ArmLength + 2f * (layout.PoleRadius * 1.25f + 0.0f), Math.Max(2f, layout.ArmLength * 0.2f), what + ": nothing sticks out beyond the posts (width " + (mMax.X - mMin.X) + ")");
                }
                else
                {
                    float postEdge = layout.PoleX[layout.PoleX.Length - 1] + layout.PoleRadius;
                    // A board fixed to a host that is not ours may be narrower than the host (a ten-centimetre plate on an eighteen-centimetre pole); then it stands clear in front of it.
                    if (!hosted)
                        Program.Check(postEdge <= fMax.X, what + ": the posts are inside the width of the board");
                    else
                        Program.Check(layout.PoleX.Length == 1 && layout.BackZ >= layout.PoleRadius + 0.02f, what + ": an attached board is on one host and stands clear in front of it");
                    Program.Check(mMax.X <= Math.Max(fMax.X, layout.PoleX[layout.PoleX.Length - 1] + 0.30f) + 0.01f, what + ": nothing sticks out beside the board: metal " + mMin.X + ".." + mMax.X + ", face " + fMin.X + ".." + fMax.X);
                }
            }
        }

        private static void SupportRules()
        {
            var ids = new HashSet<string>();
            var hashes = new HashSet<uint>();
            foreach (SupportDef support in SupportCatalog.All)
            {
                Program.Check(ids.Add(support.Id), "duplicate support id " + support.Id);
                Program.Check(hashes.Add(support.Hash), "support hash collision at " + support.Id);
                Program.Check(support.Hash != 0u, support.Id + " has a hash that is not the 'sign's own' marker 0");
                Program.Check(!string.IsNullOrEmpty(support.Zh) && !string.IsNullOrEmpty(support.En), support.Id + " has names");
                Program.Check(ReferenceEquals(SupportCatalog.ById(support.Id), support) && ReferenceEquals(SupportCatalog.ByHash(support.Hash), support), support.Id + " is found by id and hash");
            }

            Program.Check(SupportCatalog.ByHash(0u) == null && SupportCatalog.ByHash(12345u) == null && SupportCatalog.ById("nothing") == null, "hash 0, an unknown hash and an unknown id are no support");
            Program.Check(SupportCatalog.All.Count >= 4, "the single post, the double post, the triple post and the cantilever");
            Program.Check(SupportCatalog.ById(SupportCatalog.SinglePole).Kind == SupportKind.SinglePole && SupportCatalog.ById(SupportCatalog.DoublePole).Kind == SupportKind.DoublePole && SupportCatalog.ById(SupportCatalog.Cantilever).Kind == SupportKind.Cantilever, "the kinds");

            // A saved item with hash 0 (a 0.1.x save) or with the hash of a support that is gone is drawn on the sign's own default.
            SignDef stop = SignCatalog.ById("gb.stop");
            SignDef exit = SignCatalog.ById("test.exit");
            Program.Check(SupportCatalog.For(stop, 0u).Id == SupportCatalog.SinglePole, "the stop sign's default is the single post");
            Program.Check(SupportCatalog.For(exit, 0u).Id == SupportCatalog.DoublePole, "the long board's default is the double post");
            Program.Check(SupportCatalog.For(stop, 999u).Id == SupportCatalog.SinglePole, "a support that is gone gives the sign's default");
            Program.Check(SupportCatalog.For(stop, SupportCatalog.ById(SupportCatalog.Cantilever).Hash).Id == SupportCatalog.Cantilever, "a chosen support wins over the default");

            // The post that a board gets (REPORT.md roadside 2.1, 2.2): φ76 to 0.7 m wide, φ89 to 1.2 m², φ114 above on one post; on two by the area each carries.
            Program.Near(SupportLayouts.PoleRadiusFor(0.7f, 0.7f, SupportKind.SinglePole), 0.038f, 1e-6f, "a 70 cm triangle: 76 mm tube");
            Program.Near(SupportLayouts.PoleRadiusFor(0.8f, 0.8f, SupportKind.SinglePole), 0.0445f, 1e-6f, "an 80 cm board: 89 mm tube");
            Program.Near(SupportLayouts.PoleRadiusFor(1.0f, 1.2f, SupportKind.SinglePole), 0.0445f, 1e-6f, "1.0 x 1.2 m: 89 mm (S8)");
            Program.Near(SupportLayouts.PoleRadiusFor(1.2f, 1.2f, SupportKind.SinglePole), 0.057f, 1e-6f, "1.44 m²: 114 mm");
            Program.Near(SupportLayouts.PoleRadiusFor(1.8f, 0.8f, SupportKind.DoublePole), 0.0445f, 1e-6f, "1.8 x 0.8 m on two posts: 89 mm (S7)");
            Program.Near(SupportLayouts.PoleRadiusFor(5.0f, 3.0f, SupportKind.DoublePole), 0.1365f, 1e-6f, "5 x 3 m on two: 273 mm (S10)");

            // Double post: the posts stand 0.22 of the width in from the edges, symmetric.
            SupportLayout two = SupportLayouts.Of(exit, SupportCatalog.ById(SupportCatalog.DoublePole));
            Program.Near(two.PoleX[0], -two.PoleX[1], 1e-6f, "the posts are symmetric");
            Program.Near(two.PoleX[1], Math.Max(exit.Width * (0.5f - SupportLayouts.DoublePostInset), 0.3f), 1e-6f, "0.22 of the width in from the edge");
            var wide = new SignDef { Id = "t", Width = 6f, Height = 1f, BottomClearance = 2.2f, Shape = ShapeKind.Rect };
            Program.Near(SupportLayouts.Of(wide, SupportCatalog.ById(SupportCatalog.DoublePole)).PoleX[1], 6f * (0.5f - SupportLayouts.DoublePostInset), 1e-5f, "a 6 m board: 1.32 m in from each edge");
            var narrow = new SignDef { Id = "t", Width = 0.4f, Height = 0.4f, BottomClearance = 2.2f, Shape = ShapeKind.Rect };
            Program.Near(SupportLayouts.Of(narrow, SupportCatalog.ById(SupportCatalog.DoublePole)).PoleX[1], 0.3f, 1e-6f, "never closer than 0.6 m");

            // The cantilever's arm grows to hold a wide board; the board stays clear of the post and of the free end.
            SupportDef cantilever = SupportCatalog.ById(SupportCatalog.Cantilever);
            SupportLayout one = SupportLayouts.Of(stop, cantilever);
            Program.Near(one.ArmLength, cantilever.ArmLength, 1e-6f, "the arm keeps its length for a small board");
            var huge = new SignDef { Id = "t", Width = 6f, Height = 2f, BottomClearance = 2.2f, Shape = ShapeKind.Rect };
            SupportLayout big = SupportLayouts.Of(huge, cantilever);
            Program.Check(big.ArmLength >= Geo.Assembly.BoardFromPost + huge.Width - Geo.Assembly.ArmTipInside - 1e-4f, "the arm is long enough for a 6 m board");
            Program.Check(big.BoardCentreX - huge.Width * 0.5f >= Geo.Assembly.BoardFromPost - 1e-4f && big.BoardCentreX + huge.Width * 0.5f <= big.ArmLength + Geo.Assembly.ArmTipInside + 1e-4f, "the big board is clear of the post, the arm's end behind it");
            // The support's own clearance wins over the sign's for the overhead style.
            Program.Near(one.BottomClearance, 5.5f, 1e-6f, "the cantilever's board is at 5.5 m");
            Program.Near(SupportLayouts.Of(stop, SupportCatalog.ById(SupportCatalog.SinglePole)).BottomClearance, stop.BottomClearance, 1e-6f, "a post takes the sign's own clearance");
        }

        private static void ArmDirection()
        {
            // A sign faces the traffic that comes to it, and the arm lies along +X of the sign's frame. From the snapping rule (EdgeSnap.Solve) +X must point to the
            // road for a sign on either side, or the arm would reach away from the road. The frame turns by the yaw of the facing: local +Z goes to
            // (sin yaw, cos yaw), local +X to (cos yaw, -sin yaw).
            var curve = new ArcTable(Bezier3.Line(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 100f)));
            foreach (float cursorX in new[] { 9f, -9f })
            {
                EdgeSnapResult snap = EdgeSnap.Solve(curve, 8f, new Vec3(cursorX, 0f, 40f), EdgeSnapOptions.Default);
                var localX = new Vec3((float)Math.Cos(snap.Yaw), 0f, -(float)Math.Sin(snap.Yaw));
                Vec3 toRoad = new Vec3(0f - snap.Position.X, 0f, 0f);
                Program.Check(Vec3.Dot(localX, toRoad) > 0f, $"the arm of a sign on the side x={cursorX} points to the road (local +X {localX}, to the road {toRoad})");
            }

            // And with the chunk transform: a built cantilever turned by that yaw really has its arm tip on the road side.
            SignDef stop = SignCatalog.ById("gb.stop");
            SignParts parts = SignBuilder.Build(stop, SupportCatalog.ById(SupportCatalog.Cantilever));
            EdgeSnapResult right = EdgeSnap.Solve(curve, 8f, new Vec3(9f, 0f, 40f), EdgeSnapOptions.Default);
            var world = new MeshBuilder();
            world.Append(parts.Metal, right.Position, right.Yaw);
            world.Bounds(out Vec3 min, out Vec3 max);
            Program.Check(min.X < right.Position.X - 3f, "the arm of a sign on the right of the road reaches towards -X, over the road: " + min.X + " from " + right.Position.X);
        }

        private static bool Picked(SignParts parts, Vec3 at, float yaw, Vec3 from, Vec3 to) => Picking.Pick(parts.Picks, at, yaw, from, to, out _);

        private static void PickingRules()
        {
            // A ray from the viewer, 20 m in front of the sign (it faces +Z), straight at a point of its frame.
            Vec3 In(float x, float y) => new Vec3(x, y, 0.0f);
            bool Hit(SignParts parts, Vec3 at, float yaw, float x, float y)
            {
                float s = (float)Math.Sin(yaw), c = (float)Math.Cos(yaw);
                // The point (x, y, z=0.1) of the sign's frame and a viewer 20 m in front of it, in world space.
                Vec3 Local(float lx, float ly, float lz) => new Vec3(at.X + lx * c + lz * s, at.Y + ly, at.Z - lx * s + lz * c);
                return Picking.Pick(parts.Picks, at, yaw, Local(x, y, 20f), Local(x, y, -5f), out _);
            }

            var origin = new Vec3(100f, 5f, -40f);
            foreach (float yaw in new[] { 0f, 0.7f, -2.1f })
            {
                SignDef stop = SignCatalog.ById("gb.stop");
                SignParts single = SignBuilder.Build(stop, SupportCatalog.ById(SupportCatalog.SinglePole));
                SupportLayout l = single.Layout;
                string tag = $"yaw {yaw:0.0}: ";

                Program.Check(Hit(single, origin, yaw, l.BoardCentreX, l.BoardCentreY), tag + "the middle of the board is picked");
                Program.Check(Hit(single, origin, yaw, 0f, 1.0f), tag + "the post below the board is picked");
                Program.Check(Hit(single, origin, yaw, 0f, 0.03f), tag + "the foot is picked");
                Program.Check(!Hit(single, origin, yaw, 1.0f, l.BoardCentreY), tag + "the air beside the board is not picked");
                Program.Check(!Hit(single, origin, yaw, 0.5f, 1.0f), tag + "the air beside the post is not picked");
                Program.Check(!Hit(single, origin, yaw, 0f, l.PoleTop + 0.5f), tag + "the air above the post is not picked");
                // The octagon's corner of its bounding square is air (the corner is cut off).
                Program.Check(!Hit(single, origin, yaw, 0.39f, l.BoardCentreY + 0.39f), tag + "the cut corner of the octagon is not picked");
                Program.Check(Hit(single, origin, yaw, 0.30f, l.BoardCentreY + 0.30f), tag + "inside the octagon's corner is picked");

                SignParts speed = SignBuilder.Build(SignCatalog.ById("gb.speed60"), SupportCatalog.ById(SupportCatalog.SinglePole));
                Program.Check(Hit(speed, origin, yaw, 0.25f, speed.Layout.BoardCentreY + 0.25f), tag + "inside the circle");
                Program.Check(!Hit(speed, origin, yaw, 0.35f, speed.Layout.BoardCentreY + 0.35f), tag + "the corner of the circle's bounding square is not picked");

                // Two posts: the gap between them, below the board, is air; the posts are not.
                SignDef exit = SignCatalog.ById("test.exit");
                SignParts two = SignBuilder.Build(exit, SupportCatalog.ById(SupportCatalog.DoublePole));
                Program.Check(Hit(two, origin, yaw, two.Layout.PoleX[0], 1.0f) && Hit(two, origin, yaw, two.Layout.PoleX[1], 1.0f), tag + "both posts are picked");
                Program.Check(!Hit(two, origin, yaw, 0f, 1.0f), tag + "the gap between the posts under the board is not picked");
                Program.Check(Hit(two, origin, yaw, 0f, two.Layout.BoardCentreY), tag + "the board between the posts is picked");

                // The cantilever: the arm, the post, the board; not the air under the arm beside the board, not the air between the post and the board.
                SignParts hung = SignBuilder.Build(stop, SupportCatalog.ById(SupportCatalog.Cantilever));
                SupportLayout h = hung.Layout;
                Program.Check(Hit(hung, origin, yaw, h.ArmLength * 0.5f, h.ArmY), tag + "the arm is picked");
                Program.Check(Hit(hung, origin, yaw, 0f, 3f), tag + "the post is picked");
                Program.Check(Hit(hung, origin, yaw, h.BoardCentreX, h.BoardCentreY), tag + "the hung board is picked");
                Program.Check(!Hit(hung, origin, yaw, 2.0f, 3.0f), tag + "the air between the post and the board is not picked");
                Program.Check(!Hit(hung, origin, yaw, h.ArmLength * 0.5f, h.ArmY - 1.5f), tag + "the air under the arm is not picked");
                Program.Check(!Hit(hung, origin, yaw, h.ArmLength + 1f, h.ArmY), tag + "the air beyond the arm's end is not picked");
            }

            // The ray is a segment: a hit beyond its end is no hit; the distance is along the segment.
            SignParts p = SignBuilder.Build(SignCatalog.ById("gb.stop"));
            Vec3 at = new Vec3(0f, 0f, 0f);
            float y = p.Layout.BoardCentreY;
            Program.Check(!Picked(p, at, 0f, new Vec3(0f, y, 20f), new Vec3(0f, y, 10f)), "a segment that stops 10 m short of the sign picks nothing");
            Program.Check(Picking.Pick(p.Picks, at, 0f, new Vec3(0f, y, 20f), new Vec3(0f, y, -5f), out float distance) && Math.Abs(distance - (20f - p.Layout.FrontZ)) < 0.07f, "the distance is along the ray to the near face: " + distance);
            Program.Check(!Picked(p, at, 0f, new Vec3(0f, y, 20f), new Vec3(0f, y, 20f)), "a ray of no length picks nothing");
            Program.Check(Picked(p, at, 0f, new Vec3(0f, y, -20f), new Vec3(0f, y, 5f)), "the board is picked from behind too");
            Program.Check(p.Picks.Count >= 3, "a post sign has the board, the post and the foot as shapes");

            // Geometry helpers.
            var square = new[] { new Point2(-1f, -1f), new Point2(1f, -1f), new Point2(1f, 1f), new Point2(-1f, 1f) };
            Program.Check(Picking.Inside(square, 0f, 0f) && !Picking.Inside(square, 1.5f, 0f), "point in polygon");
            Program.Near(Picking.DistanceToOutline(square, 2f, 0f), 1f, 1e-5f, "distance to the outline");
            Program.Near(Picking.DistanceToOutline(square, 0f, 0f), 1f, 1e-5f, "distance from the middle to the outline");
            Program.Check(Picking.RayBox(new Vec3(0f, 0f, -5f), new Vec3(0f, 0f, 10f), new Vec3(-1f, -1f, -1f), new Vec3(1f, 1f, 1f), 0f, out float t) && Math.Abs(t - 0.4f) < 1e-5f, "ray into a box");
            Program.Check(!Picking.RayBox(new Vec3(3f, 0f, -5f), new Vec3(0f, 0f, 10f), new Vec3(-1f, -1f, -1f), new Vec3(1f, 1f, 1f), 0.5f, out _), "a ray past a box misses it");
            Program.Check(Picking.RayBox(new Vec3(1.4f, 0f, -5f), new Vec3(0f, 0f, 10f), new Vec3(-1f, -1f, -1f), new Vec3(1f, 1f, 1f), 0.5f, out _), "the tolerance grows the box");
        }

        private static void SupportInChunks()
        {
            SignDef stop = SignCatalog.ById("gb.stop");
            uint stopHash = stop.Hash;
            uint cantilever = SupportCatalog.ById(SupportCatalog.Cantilever).Hash;
            var plain = new List<ItemRecord> { new ItemRecord(stopHash, new Vec3(10f, 0f, 10f), 0f) };
            var hung = new List<ItemRecord> { new ItemRecord(stopHash, new Vec3(10f, 0f, 10f), 0f, cantilever) };

            int Total(List<ItemRecord> items)
            {
                int sum = 0;
                foreach (ChunkPart part in ChunkBuilder.Build(items, Vec3.Zero, out _))
                    sum += part.Mesh.VertexCount;
                return sum;
            }

            Program.Check(Total(plain) != Total(hung), "a sign on a cantilever is another mesh than on its default post");
            Program.Check(Total(plain) == SignBuilder.Build(stop).VertexCount, "hash 0 draws the sign on its default support");
            Program.Check(Total(hung) == SignBuilder.Build(stop, SupportCatalog.ById(SupportCatalog.Cantilever)).VertexCount, "the support hash picks the support");

            var gone = new List<ItemRecord> { new ItemRecord(stopHash, new Vec3(10f, 0f, 10f), 0f, 4242u) };
            Program.Check(Total(gone) == Total(plain), "a support that is gone is drawn on the sign's default");

            // The concrete is its own mesh so that it can have its own material.
            var materials = new HashSet<string>();
            foreach (ChunkPart part in ChunkBuilder.Build(plain, Vec3.Zero, out _))
                materials.Add(part.Material);
            Program.Check(materials.Contains("concrete") && materials.Contains("metal") && HasFace(materials, "gb.prohib.01"), "metal, concrete and the face: " + string.Join(",", materials));

            // A change of support is a change of the item: the tile is rebuilt.
            var before = new Dictionary<int, ItemRecord> { [1] = plain[0] };
            var after = new Dictionary<int, ItemRecord> { [1] = hung[0] };
            Program.Check(TileDiff.Dirty(before, after).Count == 1, "changing only the support dirties the tile");
        }

        private static void AppendTransform()
        {
            var source = new MeshBuilder();
            int a = source.Vertex(new Vec3(1f, 2f, 0f), new Vec3(1f, 0f, 0f));
            int b = source.Vertex(new Vec3(0f, 2f, 1f), new Vec3(0f, 0f, 1f));
            int c = source.Vertex(new Vec3(0f, 3f, 0f), new Vec3(0f, 1f, 0f));
            source.Tri(a, b, c);

            var target = new MeshBuilder();
            target.Vertex(Vec3.Zero, new Vec3(0f, 1f, 0f));
            target.Append(source, new Vec3(10f, 0f, 20f), (float)(Math.PI / 2));
            Program.Check(target.VertexCount == 4 && target.Indices.Count == 3, "appended after the existing vertices");
            Program.Check(target.Indices[0] == 1 && target.Indices[1] == 2 && target.Indices[2] == 3, "indices are shifted");
            // Yaw 90 degrees: local +Z goes to world +X, local +X goes to world -Z.
            Vec3 p1 = target.Positions[1];
            Program.Near(p1.X, 10f, 1e-4f, "local +X, world x");
            Program.Near(p1.Z, 19f, 1e-4f, "local +X goes to -Z");
            Vec3 p2 = target.Positions[2];
            Program.Near(p2.X, 11f, 1e-4f, "local +Z goes to +X");
            Program.Near(p2.Z, 20f, 1e-4f, "local +Z, world z");
            Program.Near(target.Normals[2].X, 1f, 1e-4f, "the normal turns too");
            Program.Near(target.Positions[3].Y, 3f, 1e-5f, "height is kept");
        }

        private static void TileKeys()
        {
            Program.Check(Tiles.Of(0f, 0f).Equals(new TileKey(0, 0)), "origin");
            Program.Check(Tiles.Of(191.9f, 0f).X == 0 && Tiles.Of(192f, 0f).X == 1, "the edge belongs to the next tile");
            Program.Check(Tiles.Of(-0.1f, -0.1f).Equals(new TileKey(-1, -1)), "just below zero is tile -1");
            Program.Check(Tiles.Of(-192f, 0f).X == -1 && Tiles.Of(-192.1f, 0f).X == -2, "floor below zero");
            Vec3 centre = Tiles.Centre(new TileKey(-1, 2));
            Program.Near(centre.X, -96f, 1e-4f, "tile centre x");
            Program.Near(centre.Z, 480f, 1e-4f, "tile centre z");
            Program.Check(Tiles.Of(centre.X, centre.Z).Equals(new TileKey(-1, 2)), "the centre is inside its tile");
        }

        private static List<ItemRecord> Items(int count, string id)
        {
            uint hash = SignCatalog.ById(id).Hash;
            var list = new List<ItemRecord>();
            for (int i = 0; i < count; i++)
                list.Add(new ItemRecord(hash, new Vec3(100f + i * 0.5f, 3f, 50f), 0.3f * i));
            return list;
        }

        /// <summary>The face material of a sign is "face:&lt;id&gt;#&lt;spec&gt;".</summary>
        private static bool HasFace(IEnumerable<string> materials, string id)
        {
            foreach (string m in materials)
            {
                if (m.StartsWith("face:" + id + "#", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static void ChunkContent()
        {
            var items = Items(3, "gb.stop");
            items.AddRange(Items(2, "gb.speed60"));
            Vec3 origin = Tiles.Centre(new TileKey(0, 0));
            List<ChunkPart> parts = ChunkBuilder.Build(items, origin, out int skipped);
            Program.Check(skipped == 0, "nothing skipped");
            var materials = new HashSet<string>();
            foreach (ChunkPart part in parts)
                materials.Add(part.Material);
            Program.Check(materials.Count == 4 && materials.Contains("metal") && materials.Contains("concrete") && HasFace(materials, "gb.prohib.01") && HasFace(materials, "gb.prohib.39"), "metal, concrete and one face per type: " + string.Join(",", materials));

            SignParts stop = SignBuilder.Build(SignCatalog.ById("gb.stop"));
            SignParts speed = SignBuilder.Build(SignCatalog.ById("gb.speed60"));
            int metal = 0, stopFace = 0;
            foreach (ChunkPart part in parts)
            {
                if (part.Material == "metal") metal += part.Mesh.VertexCount;
                if (part.Material.StartsWith("face:gb.prohib.01#", StringComparison.Ordinal)) stopFace += part.Mesh.VertexCount;
            }

            Program.Check(metal == 3 * stop.Metal.VertexCount + 2 * speed.Metal.VertexCount, "all metal is in the metal mesh");
            Program.Check(stopFace == 3 * stop.Face.VertexCount, "the faces of a type are together");

            // The mesh is in the space of the tile centre.
            foreach (ChunkPart part in parts)
            {
                part.Mesh.Bounds(out Vec3 min, out Vec3 max);
                Program.Check(min.X > 100f - origin.X - 2f && max.X < 102f - origin.X + 2f, "x is relative to the tile centre");
            }
        }

        private static void ChunkSplit()
        {
            SignParts parts = SignBuilder.Build(SignCatalog.ById("gb.stop"));
            int perSign = parts.Metal.VertexCount;
            int limit = perSign * 10 + 5;
            List<ChunkPart> chunk = ChunkBuilder.Build(Items(35, "gb.stop"), Vec3.Zero, out int skipped, limit);
            int metalMeshes = 0, metalVertices = 0;
            foreach (ChunkPart part in chunk)
            {
                Program.Check(part.Mesh.VertexCount <= limit, "a mesh stays under the limit");
                if (part.Material == "metal")
                {
                    metalMeshes++;
                    metalVertices += part.Mesh.VertexCount;
                }
            }

            Program.Check(metalMeshes == 4, $"35 signs at 10 per mesh are 4 meshes, got {metalMeshes}");
            Program.Check(metalVertices == 35 * perSign, "no sign is lost in the cut");
            Program.Check(skipped == 0, "none skipped");
            // The real limit: 16-bit indices.
            Program.Check(ChunkBuilder.MaxVertices < 65535, "the limit is below the 16-bit range");
        }

        private static void SpacingRules()
        {
            uint stop = SignCatalog.ById("gb.stop").Hash, speed = SignCatalog.ById("gb.speed60").Hash;
            var items = new List<ItemRecord>
            {
                new ItemRecord(stop, new Vec3(10f, 3f, 20f), 0f),
                new ItemRecord(speed, new Vec3(50f, 3f, 20f), 0f)
            };

            Program.Near(Spacing.NearestSame(items, new Vec3(10f, 99f, 20.3f), stop), 0.3f, 1e-4f, "height does not count");
            Program.Check(Spacing.TooClose(items, new Vec3(10f, 3f, 20.3f), stop), "0.3 m from a sign of the same type is too close");
            Program.Check(Spacing.TooClose(items, new Vec3(10f, 3f, 20f), stop), "the same spot is too close");
            Program.Check(!Spacing.TooClose(items, new Vec3(10f, 3f, 20.5f), stop), "0.5 m is far enough");
            Program.Check(!Spacing.TooClose(items, new Vec3(10f, 3f, 20f), speed), "another type may stand on the same spot");
            Program.Check(Spacing.NearestSame(items, new Vec3(0f, 0f, 0f), 12345u) == float.MaxValue, "no sign of the type, no distance");
            Program.Check(!Spacing.TooClose(new List<ItemRecord>(), new Vec3(1f, 1f, 1f), stop), "no items, never too close");
        }

        private static Dictionary<int, ItemRecord> Set(params (int key, uint type, float x, float z)[] items)
        {
            var set = new Dictionary<int, ItemRecord>();
            foreach (var item in items)
                set[item.key] = new ItemRecord(item.type, new Vec3(item.x, 3f, item.z), 0f);
            return set;
        }

        private static void TileDiffRules()
        {
            uint stop = SignCatalog.ById("gb.stop").Hash;
            var tileA = Tiles.Of(10f, 10f);
            var tileB = Tiles.Of(1000f, 10f);
            var before = Set((1, stop, 10f, 10f), (2, stop, 1000f, 10f));

            Program.Check(TileDiff.Dirty(before, Set((1, stop, 10f, 10f), (2, stop, 1000f, 10f))).Count == 0, "nothing changed, nothing to rebuild");

            HashSet<TileKey> added = TileDiff.Dirty(before, Set((1, stop, 10f, 10f), (2, stop, 1000f, 10f), (3, stop, 12f, 10f)));
            Program.Check(added.Count == 1 && added.Contains(tileA), "a new sign dirties its own tile only");

            HashSet<TileKey> removed = TileDiff.Dirty(before, Set((1, stop, 10f, 10f)));
            Program.Check(removed.Count == 1 && removed.Contains(tileB), "a removed sign dirties the tile it was in");

            HashSet<TileKey> moved = TileDiff.Dirty(before, Set((1, stop, 1010f, 10f), (2, stop, 1000f, 10f)));
            Program.Check(moved.Contains(tileA) && moved.Count == 2, "a sign that moves to another tile dirties both");

            HashSet<TileKey> turned = TileDiff.Dirty(before, new Dictionary<int, ItemRecord> { [1] = new ItemRecord(stop, new Vec3(10f, 3f, 10f), 1f), [2] = before[2] });
            Program.Check(turned.Count == 1 && turned.Contains(tileA), "a turned sign dirties its tile");

            // The 0.1.0 failure: a resync that read nothing (or only the changed ones) turned every known sign into a removed one.
            HashSet<TileKey> empty = TileDiff.Dirty(before, Set());
            Program.Check(empty.Count == 2, "reading nothing marks every tile with a sign: that is what a filtered query did");

            HashSet<TileKey> fromNothing = TileDiff.Dirty(Set(), before);
            Program.Check(fromNothing.Count == 2, "the first read of a loaded city marks all its tiles");

            Dictionary<TileKey, List<ItemRecord>> byTile = TileDiff.ByTile(before);
            Program.Check(byTile.Count == 2 && byTile[tileA].Count == 1 && byTile[tileB].Count == 1, "records are grouped by tile");
            Program.Check(TileDiff.ByTile(Set()).Count == 0, "no records, no tiles");
        }

        private static void ChunkSkips()
        {
            var items = Items(2, "gb.stop");
            items.Add(new ItemRecord(12345u, new Vec3(1f, 0f, 1f), 0f));
            items.Add(new ItemRecord(SignCatalog.ById("gb.stop").Hash, new Vec3(float.NaN, 0f, 1f), 0f));
            items.Add(new ItemRecord(SignCatalog.ById("gb.stop").Hash, new Vec3(1f, 0f, 1f), float.PositiveInfinity));
            List<ChunkPart> parts = ChunkBuilder.Build(items, Vec3.Zero, out int skipped);
            Program.Check(skipped == 3, $"three skipped, got {skipped}");
            Program.Check(parts.Count > 0, "the good ones are built");
            foreach (ChunkPart part in parts)
            {
                foreach (Vec3 p in part.Mesh.Positions)
                {
                    if (!p.IsFinite)
                    {
                        Program.Check(false, "a bad item leaked a non-finite vertex");
                        return;
                    }
                }
            }

            List<ChunkPart> none = ChunkBuilder.Build(new List<ItemRecord>(), Vec3.Zero, out int zero);
            Program.Check(none.Count == 0 && zero == 0, "no items, no meshes");
        }
    }
}
