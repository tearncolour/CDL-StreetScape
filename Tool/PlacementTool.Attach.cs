using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using Colossal.Mathematics;
using Game.Rendering;
using Game.Tools;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CDLC.TS
{
    internal sealed partial class PlacementTool
    {
        /// <summary>A support is looked for only among the items whose position is this near (m) to the line of the cursor: the longest gantry is 40 m wide.</summary>
        private const float kPickRadius = 70f;

        /// <summary>How far along the cursor's ray a sign can be pointed at (m).</summary>
        private const float kPickRange = 600f;

        /// <summary>How much further than Picking.Tolerance beside a line the ray may pass and still pick it, when the area that clears the game's markings would be picked otherwise (m).</summary>
        private const float kLineGrow = 0.25f;

        /// <summary>The items of the tiles near the ray of the cursor (reused every frame: pointing is asked every frame in the modes of the tool).</summary>
        private readonly List<KeyValuePair<Entity, ItemRecord>> m_Candidates = new List<KeyValuePair<Entity, ItemRecord>>();

        private readonly List<KeyValuePair<Entity, ItemRecord>> m_NearPairs = new List<KeyValuePair<Entity, ItemRecord>>();
        private readonly List<ItemRecord> m_NearRecords = new List<ItemRecord>();

        /// <summary>The distance to the nearest item of this type within a few metres of a place (float.MaxValue when there is none): the tiles round the place are looked at, not every item.</summary>
        private float NearestSameHere(Vec3 position, uint typeHash)
        {
            if (ChunkSystem.Instance == null)
                return float.MaxValue;
            m_NearPairs.Clear();
            m_NearRecords.Clear();
            ChunkSystem.Instance.Store.ItemsNear(position.X, position.Z, Spacing.MinDistance * 4f, m_NearPairs);
            foreach (KeyValuePair<Entity, ItemRecord> pair in m_NearPairs)
                m_NearRecords.Add(pair.Value);
            return Spacing.NearestSame(m_NearRecords, position, typeHash);
        }

        /// <summary>The state of "the sign is over a support": which support, what the keys asked for, and what was worked out.</summary>
        private sealed class AttachState
        {
            public List<KeyValuePair<Entity, ItemRecord>> Members;
            public AttachResult Result;
            public int Index = -1;
            public float Offset = float.NaN;

            /// <summary>On a post: the lower edge the arrow keys took the board to (0.12.24); NaN: it goes into the stack.</summary>
            public float Height = float.NaN;
            public bool Locked;
            public float LockMouse;

            /// <summary>The arrow keys have taken the board along the beam: it may be taken past the end of a gantry, which is then made longer.</summary>
            public bool Grow;

            /// <summary>On an arm or a beam: how far the up and down keys have raised the new board above the support's clearance (0.12.73).</summary>
            public float Rise;
            public string PreviewKey = "";
            public SignParts Parts;

            /// <summary>TextPlans.Epoch when Parts was built: a text board's parts built without its plan (provisional) are built again when a plan has been made since.</summary>
            public int PartsEpoch;
        }

        private AttachState m_Attach;
        private int m_ArrowX, m_ArrowY;
        private bool m_Shift;
        private readonly Dictionary<Key, float> m_ArrowNext = new Dictionary<Key, float>(), m_ArrowSince = new Dictionary<Key, float>();

        /// <summary>The distance of one press of the up or down key this frame: it grows while the key is held (WallHeight.LiftStep), so a tower is climbed in seconds.</summary>
        private float m_LiftStep = FacadeMove.Step;

        /// <summary>What the panel shows as the line of help for the state the tool is in.</summary>
        internal static HintInfo Hint { get; private set; }

        private static void SetHint(string kind, int count = 0, float left = 0f, float need = 0f, float x = 0f, bool manual = false, bool hung = false, string name = "", bool bad = false, int max = 0, float span = 0f, bool grew = false, string host = "", bool turns = false, bool twoSided = false, bool back = false, bool kerb = false, bool midKey = false, bool midOn = false, string nameEn = "", bool midHere = false) =>
            Hint = new HintInfo { Kind = kind, Count = count, Left = left, Need = need, X = x, Manual = manual, Hung = hung, Name = name, Bad = bad, Max = max, Span = span, Grew = grew, Host = host, Turns = turns, TwoSided = twoSided, Back = back, Kerb = kerb, NameEn = nameEn, Tilt = s_StandTilt, MidKey = midKey, MidOn = midOn, MidHere = midHere };

        // ------------------------------------------------------------------ the arrow keys

        /// <summary>One press of an arrow key per frame it should act: the first frame it is down, then, held, after 0.4 s every 0.07 s.</summary>
        private int ArrowPress(Keyboard keyboard, Key key)
        {
            var control = keyboard[key];
            float now = UnityEngine.Time.unscaledTime;
            if (control.wasPressedThisFrame)
            {
                m_ArrowNext[key] = now + 0.4f;
                m_ArrowSince[key] = now;
                return 1;
            }

            if (control.isPressed && m_ArrowNext.TryGetValue(key, out float next) && now >= next)
            {
                m_ArrowNext[key] = now + 0.07f;
                return 1;
            }

            return 0;
        }

        /// <summary>
        /// The arrow keys, and the numpad 4 6 8 2 as a second set: the game binds the arrow keys to its camera as well (the player may find the camera moving with the board), and the numpad
        /// ones are free. Either set works, and both together are one press, not two.
        /// </summary>
        private void ReadArrows(Keyboard keyboard)
        {
            m_ArrowX = Mathf.Clamp(ArrowPress(keyboard, Key.RightArrow) + ArrowPress(keyboard, Key.Numpad6) - ArrowPress(keyboard, Key.LeftArrow) - ArrowPress(keyboard, Key.Numpad4), -1, 1);
            m_ArrowY = Mathf.Clamp(ArrowPress(keyboard, Key.DownArrow) + ArrowPress(keyboard, Key.Numpad2) - ArrowPress(keyboard, Key.UpArrow) - ArrowPress(keyboard, Key.Numpad8), -1, 1);
            m_Shift = keyboard[Key.LeftShift].isPressed || keyboard[Key.RightShift].isPressed;
            m_LiftStep = WallHeight.LiftStep(m_Shift, Mathf.Max(Mathf.Max(HeldFor(keyboard, Key.UpArrow), HeldFor(keyboard, Key.DownArrow)), Mathf.Max(HeldFor(keyboard, Key.Numpad8), HeldFor(keyboard, Key.Numpad2))));
        }

        /// <summary>How long an arrow key has been held (s); 0 when it is not down.</summary>
        private float HeldFor(Keyboard keyboard, Key key) => keyboard[key].isPressed && m_ArrowSince.TryGetValue(key, out float since) ? UnityEngine.Time.unscaledTime - since : 0f;

        /// <summary>Along a beam, an arrow key as the player sees the screen: +1 moves toward larger sign-frame X (toward the driver's left), -1 toward smaller. The sign's X axis in the world is (cos yaw, 0, -sin yaw).</summary>
        private int BeamDirection(float yaw)
        {
            if (m_ArrowX == 0 || m_Camera.activeCamera == null)
                return 0;
            Vector3 right = m_Camera.activeCamera.transform.right;
            float along = right.x * Mathf.Cos(yaw) - right.z * Mathf.Sin(yaw);
            return m_ArrowX * (along >= 0f ? 1 : -1);
        }

        // ------------------------------------------------------------------ the boards of a support, as the pure part wants them

        private static List<ItemRecord> RecordsOf(List<KeyValuePair<Entity, ItemRecord>> members)
        {
            var records = new List<ItemRecord>(members.Count);
            foreach (KeyValuePair<Entity, ItemRecord> member in members)
                records.Add(member.Value);
            return records;
        }

        private AttachResult Evaluate(List<KeyValuePair<Entity, ItemRecord>> members, SignInstance board, int index, float offset, bool grow = false, bool onPost = false, bool back = false, float height = float.NaN, float rise = 0f)
        {
            ItemRecord lead = members[0].Value;
            HostCandidate host = HostOf(lead);
            return Attach.Evaluate(new AttachRequest
            {
                Back = back,
                Height = height,
                Rise = rise,
                Existing = ItemGroups.Boards(RecordsOf(members)),
                Support = SupportCatalog.ByHash(lead.SupportHash),
                Reach = lead.Reach,
                Board = board,
                Index = index,
                Offset = offset,
                Grow = grow,
                OnPost = onPost,
                HostHighest = host.Highest,
                HostBusy = host.Busy
            });
        }

        /// <summary>The kind of the line of help that says why a board does not go on a support.</summary>
        private static string RefusalKind(AttachResult result, bool tooMany)
        {
            if (tooMany || result.Reason == AttachReason.Full)
                return "refuse-full";
            if (result.Reason == AttachReason.Large)
                return "refuse-large";
            if (result.Reason == AttachReason.NoHeight)
                return result.OnPost ? "refuse-post-height" : result.Support != null && result.Support.Kind == SupportKind.WallBracket ? "refuse-bracket-height" : "refuse-height";
            return "refuse-space";
        }

        /// <summary>The sign-frame X where the line of the cursor meets the plane of the support's boards (the plane z = 0 of the sign's frame).</summary>
        private static float MouseAlong(ItemRecord lead, Vec3 from, Vec3 to)
        {
            float cos = Mathf.Cos(lead.Yaw), sin = Mathf.Sin(lead.Yaw);
            float ax = from.X - lead.Position.X, az = from.Z - lead.Position.Z, bx = to.X - lead.Position.X, bz = to.Z - lead.Position.Z;
            float lax = ax * cos - az * sin, laz = ax * sin + az * cos, lbx = bx * cos - bz * sin, lbz = bx * sin + bz * cos;
            if (Mathf.Abs(lbz - laz) < 1e-6f)
                return lax;
            float t = (0f - laz) / (lbz - laz);
            return lax + (lbx - lax) * Mathf.Clamp(t, -4f, 4f);
        }

        // ------------------------------------------------------------------ place mode: over a support the sign joins it

        /// <summary>Leaves the "over a support" state: the preview goes, and the support that was hidden is drawn again.</summary>
        private void EndAttach()
        {
            if (m_Attach == null)
                return;
            m_Attach = null;
            ChunkSystem.SetHidden(null);
            DestroyPreview();
        }

        /// <summary>
        /// When the cursor is over a placed sign or its support (any part: board, post, foot, arm or beam) the sign that is chosen is not put on the road: it joins that support. A post takes up to
        /// four small boards in a stack (the up and down keys move the new board in it); a beam or an arm takes boards side by side where there is room (the left and right keys move it). The
        /// support is drawn with the sign on it, and a click puts it there. Alt held puts a separate sign instead. True when the tool has dealt with the frame.
        /// </summary>
        private bool TryUpdateAttach(SignChoice choice, bool click)
        {
            Camera camera = m_Camera.activeCamera;
            Keyboard keyboard = Keyboard.current;
            // With a signal's arm chosen a board goes onto an arm, never onto a board that is there (0.12.51: one board to an item on an arm).
            if (camera == null || ChunkSystem.Instance == null || (keyboard != null && (keyboard[Key.LeftAlt].isPressed || keyboard[Key.RightAlt].isPressed)) || ArmChosen())
            {
                EndAttach();
                return false;
            }

            Line3.Segment ray = ToolRaycastSystem.CalculateRaycastLine(camera);
            Vec3 from = RoadReader.ToVec(ray.a), to = RoadReader.ToVec(ray.b);

            // The support we are over: the one we were over while the cursor is still on it or on the boards that are drawn now (the new board may stand beyond the old ones), else what is under it.
            // Pointing at the post of a hung support (its post or its foot, or a board that is on the post) puts the board on the post (0.12.2); anything else of it, on the arm or the beam.
            Entity target = Entity.Null;
            bool wantPost = false;
            if (m_Attach != null && m_Attach.Parts != null && m_Attach.Members.Count > 0)
            {
                ItemRecord old = m_Attach.Members[0].Value;
                if (Picking.Pick(m_Attach.Parts.Picks, old.Position, old.Yaw, from, to, out _, out PickShape shown))
                {
                    target = m_Attach.Members[0].Key;
                    AssemblyBoard hit = shown.Name == "board" && m_Attach.Result != null ? m_Attach.Result.Boards.Find(b => b.Member == shown.Member) : null;
                    wantPost = shown.Name == "post" || shown.Name == "foot" || (hit != null && Assembly.IsPostBoard(hit));
                }
            }

            if (target == Entity.Null)
            {
                if (!FindUnderCursor(out target, out ItemRecord pointed, out bool onBoard, out string shape))
                {
                    EndAttach();
                    return false;
                }

                wantPost = shape == "post" || shape == "foot" || (onBoard && pointed.Lane == Assembly.OnPost);
            }

            if (!EntityManager.Exists(target) || !EntityManager.HasComponent<TsItem>(target))
            {
                EndAttach();
                return false;
            }

            List<KeyValuePair<Entity, ItemRecord>> members = MembersOf(target, EntityManager.GetComponentData<TsItem>(target));
            if (m_Attach == null || m_Attach.Members[0].Key != members[0].Key)
            {
                EndAttach();
                m_Attach = new AttachState();
            }

            AttachState state = m_Attach;
            state.Members = members;
            ItemRecord lead = members[0].Value;
            SignDef def = choice.Def;
            TsItem leadItem = EntityManager.GetComponentData<TsItem>(members[0].Key);
            var instance = SignInstances.Of(def, SpecForNew(def, leadItem.Edge, leadItem.Position, leadItem.Yaw));

            // First look, to know what the support is (a beam or a post) and where the free places are.
            float mouse = MouseAlong(lead, from, to);
            if (state.Locked && !float.IsNaN(state.Offset) && Mathf.Abs(mouse - state.LockMouse) > 0.75f)
            {
                // The mouse has moved well away from where the keys took over: it leads again.
                state.Locked = false;
                state.Index = -1;
                state.Grow = false;
            }

            float asked = state.Locked && !float.IsNaN(state.Offset) ? state.Offset : Mathf.Round(mouse * 20f) / 20f;
            // Seen from behind a gantry the board goes on its back (0.12.22): the camera is on the side the boards' backs face.
            Vector3 eye = camera.transform.position;
            bool behind = (eye.x - lead.Position.X) * Mathf.Sin(lead.Yaw) + (eye.z - lead.Position.Z) * Mathf.Cos(lead.Yaw) < 0f;
            AttachResult result = Evaluate(members, instance, state.Locked ? state.Index : -1, asked, state.Grow, wantPost, behind, rise: state.Rise);

            // The arrow keys: up and down an arm or a beam from the clearance (0.12.73; the owner: "牌子挂在横杆上也应该支持上下方向键调节上下位置"); along the beam by the free places; up and down a
            // post by the places in the stack. (On the post of a hung support the stack is in the standard's order.)
            if (result.Hung && !result.OnPost && m_ArrowY != 0)
            {
                state.Rise = Attach.StepRise(state.Rise, -m_ArrowY, m_Shift);
                result = Evaluate(members, instance, state.Locked ? state.Index : -1, asked, state.Grow, wantPost, behind, rise: state.Rise);
            }

            if (result.Hung && !result.OnPost && m_ArrowX != 0)
            {
                // The keys may take the board past the end of the beam: the places are those of the longest beam, and the beam is made as long as the board then needs.
                float step = m_Shift ? Attach.Step * 4f : Attach.Step;
                AttachResult wide = Evaluate(members, instance, -1, result.Offset, true, false, behind, rise: state.Rise);
                float moved = Attach.Move(wide.Free.Count > 0 ? wide.Free : result.Free, result.Offset, BeamDirection(lead.Yaw), step);
                state.Offset = Mathf.Round(moved * 100f) / 100f;
                state.Locked = true;
                state.Grow = true;
                state.LockMouse = mouse;
                result = Evaluate(members, instance, -1, state.Offset, true, false, behind, rise: state.Rise);
            }
            else if (ByHeight(result) && m_ArrowY != 0 && (result.Ok || result.Reason == AttachReason.None))
            {
                // Up and down a post by the free heights (0.12.24; the owner: "在竖杆上放置的时候应该可以按上下方向键调整位置，最低可以到地面高度"): the others stay, a post of the support's own grows.
                float from0 = !float.IsNaN(state.Height) ? state.Height : NewBottom(result, lead.Reach);
                AttachResult here = Evaluate(members, instance, -1, float.NaN, false, result.OnPost, behind, from0);
                state.Height = Mathf.Round(Attach.Move(here.Free, here.Ok ? here.Offset : from0, -m_ArrowY, m_Shift ? Attach.HeightStepCoarse : Attach.HeightStep) * 100f) / 100f;
                state.Locked = true;
                result = Evaluate(members, instance, -1, float.NaN, false, result.OnPost, behind, state.Height);
            }
            else if (ByHeight(result) && !float.IsNaN(state.Height))
            {
                result = Evaluate(members, instance, -1, float.NaN, false, result.OnPost, behind, state.Height);
            }
            else if (!result.Hung && m_ArrowY != 0 && (result.Ok || result.Reason == AttachReason.None))
            {
                // On a wall the arrow keys change the order of the stack (its height is the wall's own, in the editor).
                int from0 = state.Locked && state.Index >= 0 ? state.Index : result.Index;
                state.Index = Mathf.Clamp(from0 + m_ArrowY, 0, members.Count);
                state.Locked = true;
                state.LockMouse = mouse;
                result = Evaluate(members, instance, state.Index, float.NaN);
            }
            else if (!result.Hung && state.Locked && state.Index >= 0)
            {
                state.Index = Mathf.Clamp(state.Index, 0, members.Count);
                result = Evaluate(members, instance, state.Index, float.NaN);
            }

            state.Result = result;
            OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle dependencies);
            dependencies.Complete();
            var ring = new float3(lead.Position.X, lead.Position.Y + 0.1f, lead.Position.Z);
            bool tooMany = ChunkSystem.Instance.Records.Count >= kMaxItems;
            if (!result.Ok || tooMany)
            {
                // Cannot join: nothing is shown on the support, the reason is said, and a click does nothing.
                buffer.DrawCircle(new UnityEngine.Color(1f, 0.2f, 0.15f, 0.9f), ring, 1.8f);
                ChunkSystem.SetHidden(null);
                if (state.Parts != null)
                {
                    DestroyPreview();
                    state.Parts = null;
                    state.PreviewKey = "";
                }

                SetHint(RefusalKind(result, tooMany), members.Count, result.SpaceBefore, def.Width, 0f, false, result.Hung, def.Zh, true, result.MaxBoards, result.Span);
                if (m_WantText)
                    Debug = $"Over a support of {members.Count} board(s): cannot add {def.Id}: {(tooMany ? "too many signs" : result.Reason.ToString())}";
                if (click)
                    m_Outcome = "refused: " + Debug;
                return true;
            }

            buffer.DrawCircle(new UnityEngine.Color(0.2f, 1f, 0.35f, 0.9f), ring, 1.8f);
            ShowAttachPreview(state, result, lead);
            ChunkSystem.SetHidden(MemberEntities(members));
            bool gantry = result.Support.Kind == SupportKind.Gantry && !result.OnPost;
            AssemblyBoard joined = result.Boards.Find(b => b.Member == result.NewMember);
            bool byHeight = ByHeight(result);
            SetHint(result.OnPost ? "attach-post" : "attach", result.Count, result.SpaceLeft, def.Width, byHeight ? NewBottom(result, lead.Reach) : result.Offset, result.Manual, result.Hung && !result.OnPost, def.Zh, false, result.MaxBoards, result.Span,
                byHeight ? OwnPost(result.Support.Kind) : result.Grew, "", false, gantry, gantry && joined != null && joined.Back);
            if (m_WantText)
                Debug = result.OnPost ? $"Over the post of a hung support: {def.Id} as board {result.Index + 1} of {result.Count} on the post, {result.SpaceLeft:0.0} m left under the arm"
                    : result.Hung ? $"Over a beam: {def.Id} at {result.Offset:0.00} m, {result.SpaceLeft:0.0} m left, arm/span {result.Reach:0.0} m" : $"Over a post: {def.Id} as board {result.Index + 1} of {result.Count}, {result.SpaceLeft:0.0} m of height left";
            if (click)
            {
                CommitAttach(members, result, def, instance.Spec);
                m_Outcome = $"joined a support of {members.Count}: {def.Id}";
                EndAttach();
            }

            return true;
        }

        private static List<Entity> MemberEntities(List<KeyValuePair<Entity, ItemRecord>> members)
        {
            var list = new List<Entity>(members.Count);
            foreach (KeyValuePair<Entity, ItemRecord> member in members)
                list.Add(member.Key);
            return list;
        }

        /// <summary>Builds the whole support with the boards as they would be (only when they differ from what is shown) and puts it where the support stands.</summary>
        private void ShowAttachPreview(AttachState state, AttachResult result, ItemRecord lead)
        {
            var key = new System.Text.StringBuilder();
            foreach (AssemblyBoard board in result.Boards)
                key.Append(board.Member).Append(':').Append(board.Slot).Append(':').Append(board.Lane).Append('@').Append((int)Math.Round(board.OffsetX * 100f)).Append(board.Manual ? 'M' : 'a').Append(board.Back ? 'B' : 'F').Append('^').Append(Assembly.TurnKey(board)).Append('/').Append(Assembly.TiltKey(board)).Append('r').Append(Assembly.RiseKey(board)).Append(':').Append(board.Instance.Key).Append('&');
            key.Append('|').Append(result.Support.Hash).Append('|').Append((int)Math.Round(result.Reach * 10f)).Append('|').Append(lead.Style).Append('|').Append(Assembly.ClearanceKey(result.Boards.Count > 0 ? result.Boards[0] : null));
            string text = key.ToString();
            bool waiting = state.Parts != null && state.Parts.Provisional && state.PartsEpoch != TextPlans.Epoch;
            if (m_Preview == null || state.PreviewKey != text || waiting)
            {
                DestroyPreview();
                state.PartsEpoch = TextPlans.Epoch;
                state.Parts = SignBuilder.BuildAssembly(result.Boards, result.Support, result.Reach, SupportDetail.Full, SupportStyle.Of(lead.Style));
                BuildPreview(state.Parts, lead.Position, lead.Yaw, result.Reach);
                state.PreviewKey = text;
                m_PreviewKey = text;
            }

            m_Preview.SetActive(true);
            m_Preview.transform.SetPositionAndRotation(new Vector3(lead.Position.X, lead.Position.Y, lead.Position.Z), Quaternion.Euler(0f, lead.Yaw * Mathf.Rad2Deg, 0f));
        }

        // ------------------------------------------------------------------ writing what the pure part worked out

        /// <summary>
        /// Writes the boards of a result back to the items: the order, the lanes and places, the flag of the player's order, the reach of an arm. A new board (newDef is not null) is made as a copy of
        /// the lead with its own settings; a board that was moved is the one whose member is the result's NewMember, which already exists.
        /// </summary>
        private Entity WriteBoards(List<KeyValuePair<Entity, ItemRecord>> members, AttachResult result, SignDef newDef, string newSpec)
        {
            Entity leadEntity = members[0].Key;
            TsItem lead = EntityManager.GetComponentData<TsItem>(leadEntity);
            uint group = lead.Group;
            if (group == 0u && newDef != null)
                group = ItemGroups.NewId(ChunkSystem.Instance.Records.Values, m_Random);

            foreach (AssemblyBoard board in result.Boards)
            {
                if (newDef != null && board.Member == result.NewMember)
                    continue;
                if (board.Member < 0 || board.Member >= members.Count)
                    continue;
                Entity entity = members[board.Member].Key;
                TsItem item = EntityManager.GetComponentData<TsItem>(entity);
                WriteBoard(ref item, board, result, group);
                WriteItem(entity, item);
            }

            if (newDef == null)
                return Entity.Null;

            AssemblyBoard added = result.Boards.Find(b => b.Member == result.NewMember);
            TsItem fresh = lead;
            fresh.TypeHash = newDef.Hash;
            fresh.Spec = default;
            fresh.SetSpec(newDef.Template != null ? newSpec : "");
            WriteBoard(ref fresh, added, result, group);
            return NewItem(fresh);
        }

        /// <summary>The arrow keys take the board up and down: on posts, on a host and on the post of a hung support, not on a wall or a beam.</summary>
        private static bool ByHeight(AttachResult result) => result.OnPost || (!result.Hung && result.Support != null && !SupportKinds.OnWall(result.Support.Kind));

        /// <summary>A post that is the support's own, not the game's lamp post or signal pole, nor the post of a hung support (it reaches the arm): it grows and shrinks with the boards on it.</summary>
        private static bool OwnPost(SupportKind kind) => !SupportKinds.Hung(kind) && !SupportKinds.Hosted(kind);

        /// <summary>Where the new board's lower edge is in a result (in the stack, before the keys take it).</summary>
        private static float NewBottom(AttachResult result, float reach)
        {
            AssemblyPlan plan = Assembly.Plan(Assembly.Order(result.Boards), result.Support, result.Reach > 0f ? result.Reach : reach);
            Placement placed = plan.Placed.Find(p => p.Board.Member == result.NewMember);
            return placed != null ? placed.Bottom : Assembly.LowestOnPost;
        }

        private static void WriteBoard(ref TsItem item, AssemblyBoard board, AttachResult result, uint group)
        {
            if (group != 0u)
                item.Group = group;
            item.Slot = (byte)Mathf.Clamp(board.Slot, 0, 255);
            // A board keeps its turn on the post; the new one (made from a copy of the lead) faces as the support does.
            item.Turn = board.Turn;
            if (result.Hung)
            {
                item.Lane = (byte)Mathf.Clamp(board.Lane, 0, 255);
                item.OffsetX = board.OffsetX;
                item.Rise = Assembly.ClampRise(board.Rise);
                item.Clearance = SupportLayouts.ClampClearance(board.Clearance);
                item.Flags = board.Back ? (byte)(item.Flags | TsItem.FlagBack) : (byte)(item.Flags & ~TsItem.FlagBack);
                if (result.Reach > 0f)
                    item.Reach = result.Reach;
            }
            else
            {
                // On posts and on a host the height a board was given by hand (0.12.24); 0 in a stack.
                item.Lane = 0;
                item.OffsetX = board.OffsetX;
                item.Rise = 0f;
                item.Clearance = 0f;
                item.Flags = result.Manual ? (byte)(item.Flags | TsItem.FlagManual) : (byte)(item.Flags & ~TsItem.FlagManual);
                item.Flags = (byte)(item.Flags & ~TsItem.FlagBack);
            }
        }

        private void CommitAttach(List<KeyValuePair<Entity, ItemRecord>> members, AttachResult result, SignDef def, string spec)
        {
            WriteBoards(members, result, def, spec);
            EditorRevision++;
            m_Placed++;
            if (m_Placed <= 60)
                Mod.log.Info($"Joined {def.Id} to a support of {members.Count} board(s) at {members[0].Value.Position}: {(result.OnPost ? $"board {result.Index + 1} of {result.Count} on the post of the {result.Support.Id}" : result.Hung ? $"over the beam at {result.Offset:0.00} m, arm/span {result.Reach:0.0} m" : $"board {result.Index + 1} of {result.Count} on the post{(result.Manual ? " (order by hand)" : "")}")}, {result.SpaceLeft:0.0} m left.");
        }

        /// <summary>
        /// Puts the chosen sign on the support that is being edited (the editor's button): the same rules and places as when it is put on by pointing, with no place asked for (the middle of the
        /// beam, or the standard's place in the stack). False when it does not fit; the reason is kept for the editor.
        /// </summary>
        private bool TryAddBoard(List<KeyValuePair<Entity, ItemRecord>> members, SignChoice choice)
        {
            SignDef def = choice.Def;
            ItemRecord lead = members[0].Value;
            TsItem leadItem = EntityManager.GetComponentData<TsItem>(members[0].Key);
            var instance = SignInstances.Of(def, SpecForNew(def, leadItem.Edge, leadItem.Position, leadItem.Yaw));
            // From the editor's button the beam may be made longer for the board.
            AttachResult result = Evaluate(members, instance, -1, float.NaN, true);
            if (!result.Ok)
            {
                LastRefusal = new HintInfo { Kind = RefusalKind(result, false), Count = members.Count, Left = result.SpaceBefore, Need = def.Width, Hung = result.Hung, Name = def.Zh, Bad = true, Max = result.MaxBoards, Span = result.Span };
                EditorRevision++;
                Mod.log.Info($"Edit: {def.Id} does not go on this support: {result.Reason} ({members.Count} board(s), {result.SpaceBefore:0.0} m of room).");
                return false;
            }

            LastRefusal = default;
            CommitAttach(members, result, def, instance.Spec);
            return true;
        }

        /// <summary>Why the last Add in the editor did not work (Kind is empty when it did); shown in the editor.</summary>
        internal static HintInfo LastRefusal { get; private set; }

        // ------------------------------------------------------------------ edit mode: the arrow keys move the board that is being edited

        /// <summary>
        /// The arrow keys move the board being edited: along a beam by the free places (left and right, as the screen shows) and up and down it from the clearance (0.12.73), up and down in the stack of a
        /// post (up and down). The others stay where they are; what does not fit is not done.
        /// </summary>
        private void MoveEditedBoard()
        {
            if ((m_ArrowX == 0 && m_ArrowY == 0) || !TryReadEdit(out TsItem item))
                return;
            List<KeyValuePair<Entity, ItemRecord>> members = MembersOf(EditEntity, item);
            int own = members.FindIndex(pair => pair.Key == EditEntity);
            if (own < 0)
                return;

            List<AssemblyBoard> all = ItemGroups.Boards(RecordsOf(members));
            AssemblyBoard self = all.Find(b => b.Member == own);
            if (self == null)
                return;
            var others = all.FindAll(b => b.Member != own);
            ItemRecord lead = members[0].Value;
            SupportDef support = SupportCatalog.ByHash(lead.SupportHash);
            // On a building the arrows (and the editor's four buttons) move the board on the facade or across the roof (0.12.76).
            if (support != null && SupportKinds.OnWall(support.Kind))
            {
                MoveOnFacade(item, support);
                return;
            }

            HostCandidate host = HostOf(lead);
            AttachRequest request = new AttachRequest { Existing = others, Support = support, Reach = lead.Reach, Board = self.Instance, Member = own, Slot = self.Slot, Moving = true, Grow = true, Back = self.Back, HostHighest = host.Highest, HostBusy = host.Busy };

            // Where it is now. A board on the post of a hung support stays in its place in the stack (the standard's order).
            AssemblyPlan plan = Assembly.Plan(Assembly.Order(all), support, lead.Reach);
            Placement here0 = plan.Placed.Find(p => p.Board.Member == own);
            if (here0 == null)
                return;
            if (SupportKinds.OnArm(plan.Support.Kind))
            {
                MoveBoardOnArm(item);
                return;
            }

            if (here0.OnPost || (!plan.Hung && !SupportKinds.OnWall(plan.Support.Kind)))
            {
                // Up and down the post by the free heights (0.12.24), down to the ground.
                if (m_ArrowY == 0)
                    return;
                request.OnPost = here0.OnPost;
                request.Height = here0.Bottom;
                AttachResult here = Attach.Evaluate(request);
                if (here.Free.Count == 0)
                    return;
                request.Height = Mathf.Round(Attach.Move(here.Free, here0.Bottom, -m_ArrowY, m_Shift ? Attach.HeightStepCoarse : Attach.HeightStep) * 100f) / 100f;
            }
            else if (plan.Hung)
            {
                // Along the beam by the free places (left and right), up and down from the clearance (0.12.73): the board stays where it is in the other way.
                if (m_ArrowX == 0 && m_ArrowY == 0)
                    return;
                request.Offset = here0.Cx;
                request.Rise = self.Rise;
                if (m_ArrowX != 0)
                {
                    AttachResult here = Attach.Evaluate(request);
                    if (here.Free.Count == 0)
                        return;
                    request.Offset = Mathf.Round(Attach.Move(here.Free, here0.Cx, BeamDirection(lead.Yaw), m_Shift ? Attach.Step * 4f : Attach.Step) * 100f) / 100f;
                }

                if (m_ArrowY != 0)
                {
                    request.Rise = Attach.StepRise(self.Rise, -m_ArrowY, m_Shift);
                    if (m_ArrowX == 0 && request.Rise == Assembly.ClampRise(self.Rise))
                        return;
                }
            }
            else
            {
                if (m_ArrowY == 0)
                    return;
                List<AssemblyBoard> shown = Assembly.Order(all);
                int current = shown.FindIndex(b => b.Member == own);
                request.Index = Mathf.Clamp(current + m_ArrowY, 0, others.Count);
                if (request.Index == current)
                    return;
            }

            AttachResult moved = Attach.Evaluate(request);
            if (!moved.Ok)
                return;
            WriteBoards(members, moved, null, null);
            // Moved in, the arm or the beam gives back what the board took (0.12.9).
            RefitReach(EntitiesOf(members, Entity.Null), "a board was moved");
            EditorRevision++;
        }
    }
}
