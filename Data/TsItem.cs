using System;
using System.Text;
using CDLC.TS.Geo;
using Colossal.Serialization.Entities;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CDLC.TS
{
    /// <summary>
    /// One placed sign. The entity carries nothing else but LivePath (0.12.38): the game's serializer leaves out an entity that has it, so a save holds no item, and the items are kept in a store
    /// of their own beside it (SaveDataSystem, DESIGN-save-data.md). The type stays serializable for the saves made before 0.12.38, which hold their items: the game saves every entity that has a
    /// serializable component of a mod assembly, writes an Entity field through its entity table (a destroyed target comes back as Entity.Null) and skips the data of a component whose type is not
    /// loaded. Such a save's items are read as before and go to the store at its next save.
    ///
    /// The position and the facing are the truth. The road edge is only a hint: a road that is split, replaced or deleted gets new
    /// entities, so the item looks its edge up again by position (ItemRoadSystem) and, when there is none, is kept where it is.
    ///
    /// NEVER rename or move this type, its namespace or the assembly: the saved type name is the assembly-qualified name. Only append
    /// fields, bump kVersion, write the new payload size, and read a field only when the saved version has it.
    /// </summary>
    public struct TsItem : IComponentData, ISerializable
    {
        public const ushort kVersion = 11;

        /// <summary>The bytes of the payload of version 1: hash 4, edge 4, t 4, position 12, yaw 4, flags 1.</summary>
        public const ushort kPayloadV1 = 29;

        /// <summary>Version 2 appends the support hash (4): 33 bytes. A version 1 save reads with the support 0, the sign's default.</summary>
        public const ushort kPayloadV2 = 33;

        /// <summary>Version 3 appends the settings of a guide board: a length (2 bytes) and that many bytes of UTF-8 (the written GuideSpec), so 35 bytes and the text.</summary>
        public const ushort kPayloadV3Fixed = 35;

        /// <summary>Version 4 appends the reach (4 bytes) after the settings: the span of a gantry or the arm of a cantilever in metres, 0 for the support's default.</summary>
        public const ushort kReachBytes = 4;

        /// <summary>Version 5 appends the group (4 bytes) and the slot (1 byte) after the reach: 5 bytes. A save of an older version reads with no group (a sign on a support of its own).</summary>
        public const ushort kGroupBytes = 5;

        /// <summary>Version 6 appends the lane (1 byte) and the position over it (4 bytes) after the group: 5 bytes. A save of an older version reads with no lane (side by side).</summary>
        public const ushort kLaneBytes = 5;

        /// <summary>Version 7 (0.12.8) appends the turn of the board on its post (4 bytes) after the lane. A save of an older version reads with no turn (the board faces as its support does).</summary>
        public const ushort kTurnBytes = 4;

        /// <summary>Version 7 also appends the support's own reach (4 bytes) after the turn (0.12.9; the unreleased build 0.12.8 wrote a version 7 without it, which reads with none).</summary>
        public const ushort kBaseReachBytes = 4;

        /// <summary>Version 8 (0.12.57) appends the support's style (1 byte, SupportStyle.Packed: the finish and the black backs). A save of an older version reads galvanised.</summary>
        public const ushort kStyleBytes = 1;

        /// <summary>Version 9 (0.12.67) appends the batch the item was written by (4 bytes, the hash of the batch id; 0 for none). An older mod skips it with the other trailing bytes.</summary>
        public const ushort kBatchBytes = 4;

        /// <summary>Version 10 (0.12.73) appends the rise of a board hung on an arm or a beam (4 bytes, metres above the support's clearance). An older mod skips it with the other trailing bytes and draws the board at the clearance.</summary>
        public const ushort kRiseBytes = 4;

        /// <summary>Version 11 (0.12.78) appends the clearance of a hung support (4 bytes, metres; 0 for the support's own). An older mod skips it with the other trailing bytes and draws the support at its own clearance.</summary>
        public const ushort kClearanceBytes = 4;

        /// <summary>The most a board turns on its post either way (radians): half a turn, which faces it the other way.</summary>
        public const float MaxTurn = 3.1415927f;

        public const byte FlagOrphan = 1;

        /// <summary>The boards of the support this item is on are in an order of the player's (the slots), not the standard's. Stored in the flags byte, which is saved already: no new version.</summary>
        public const byte FlagManual = 2;

        /// <summary>
        /// A marking placed by the automatic marking of a stretch (0.12.2, mk.auto): marking the stretch again replaces it; a marking the player placed, or edited since, is never touched. In the flags byte:
        /// no new version.
        /// </summary>
        public const byte FlagAuto = 4;

        /// <summary>BaseReach is the support's own reach (0.12.9). An item of an older version has none: its reach may have been made longer for its boards, and what it was before is not known.</summary>
        public const byte FlagBaseReach = 8;

        /// <summary>The player set BaseReach in the editor (0.12.12): an arm keeps it; without this an arm is as long as its boards need (a gantry keeps its BaseReach either way: its posts stand by the kerbs).</summary>
        public const byte FlagReachByHand = 16;

        /// <summary>The board hangs on the back of its gantry's beam, facing the other way (0.12.22, AssemblyBoard.Back). In the flags byte: no new version; a mod before it draws the board on the front.</summary>
        public const byte FlagBack = 32;

        /// <summary>
        /// How far the board leans forward, in steps of BoardTilt.Step (0.12.55): the two high bits of the flags. In the flags byte: no new version; a mod before it draws the board upright. Only the
        /// boards on an arm or a beam lean (SignBuilder).
        /// </summary>
        public const int TiltShift = 6;

        public const byte TiltMask = 0xC0;

        /// <summary>The catalogue hash of the sign type (SignCatalog.HashOf of its id).</summary>
        public uint TypeHash;

        /// <summary>The road edge the item stood at when it was placed or last looked up; a hint only.</summary>
        public Entity Edge;

        /// <summary>The position along that edge, 0 to 1; a hint only.</summary>
        public float EdgeT;

        public float3 Position;

        /// <summary>The way the sign faces, as the angle about Y (0 faces +Z).</summary>
        public float Yaw;

        public byte Flags;

        /// <summary>The hash of the support style (SupportDef.Hash); 0 is the planner's choice for the board. Appended in version 2.</summary>
        public uint SupportHash;

        /// <summary>The written settings of a guide board (GuideSpec); empty for a sign with none or one that keeps its defaults. Appended in version 3.</summary>
        public FixedString512Bytes Spec;

        /// <summary>
        /// The span of a gantry or the arm of a cantilever (m); 0 is the support's default. Appended in version 4. On a wall (0.12.2) the height of the lower edge of the lowest board above the foot of the
        /// wall, where the item stands (0: the sign's own clearance); no new version (the same float).
        /// </summary>
        public float Reach;

        /// <summary>
        /// Items that share a non-zero group are the boards of one support (at most four, GB 5768.2 4.8.2); they carry the same position, facing, support and reach. 0: a sign on a support of
        /// its own. A number made at random when the second board is put on (not an entity, which is not the same after a load). Appended in version 5.
        /// </summary>
        public uint Group;

        /// <summary>The order the boards were put on one support (0 for the first); what the order of the boards on the support (prohibition, mandatory, warning) is made from. Appended in version 5.</summary>
        public byte Slot;

        /// <summary>
        /// The lane the board is over, counted from the driver's left (0: side by side with the others on an arm or a beam; no meaning on posts). Appended in version 6. 254 (0.12.2, Assembly.OnPost):
        /// on the post of a hung support; 255: at a place of its own (Attach.LaneHand). An older build reads 254 as a lane and puts the board on the arm.
        /// </summary>
        public byte Lane;

        /// <summary>With a lane: the sign-frame X of the middle of the board (m from the first post, toward the driver's left), as it was when the lane was chosen. Appended in version 6.</summary>
        public float OffsetX;

        /// <summary>
        /// How far the board is turned about its post from the way its support faces (radians, the sense of Yaw: + turns its face from the support's +Z toward its +X, clockwise seen from above), for a
        /// board in the stack on one post (a single post, a lamp post or a signal pole, the post of a hung support; Assembly.TurnAxis); 0 everywhere else. Appended in version 7 (0.12.8: the owner, "一个杆子上多个牌子情况下也应该支持能够调整每一个牌子的朝向").
        /// </summary>
        public float Turn;

        /// <summary>
        /// The span of a gantry or the arm of a cantilever of the support itself (m; 0 the support's default): what it was placed with (from the road) or what the player set in the editor, before boards
        /// put on it made it longer (Reach is then what they need). A board taken off or moved in gives the length back (Assembly.ReachFor; 0.12.9, the owner: "悬架原本的路牌删除或者移动之后，长度应该能自动缩小").
        /// Only with FlagBaseReach. Appended in version 7.
        /// </summary>
        public float BaseReach;

        /// <summary>How the support is painted (SupportStyle.Packed; 0 galvanised). Appended in version 8 (0.12.57).</summary>
        public byte Style;

        /// <summary>The batch (BatchApi) that wrote the item, so that the same batch submitted again replaces it; 0 for an item placed by hand or by the automatic markings. Appended in version 9.</summary>
        public uint BatchId;

        /// <summary>
        /// How far the lower edge of a board hung on an arm or a beam is above the support's clearance (m, 0 to Assembly.MaxRise; the up and down arrow keys, 0.12.73: the owner, "牌子挂在横杆上也应该支持上下方向键调节上下位置");
        /// the arm or the beam is made for the highest board. 0 everywhere else. Appended in version 10.
        /// </summary>
        public float Rise;

        /// <summary>
        /// The height of the lower edge of the boards of a gantry or a cantilever as the player set it in the editor (m, SupportLayouts.MinClearance to MaxClearance; 0.12.78: the owner, the clearance slider and the
        /// height-limit frame at a tunnel); 0 is the support's own (5.5 m). The support's, so every board of it carries the same. Appended in version 11.
        /// </summary>
        public float Clearance;

        public bool HasBaseReach => (Flags & FlagBaseReach) != 0;

        public bool ReachByHand => (Flags & FlagReachByHand) != 0;

        public bool Orphan => (Flags & FlagOrphan) != 0;

        public bool Manual => (Flags & FlagManual) != 0;

        public bool Auto => (Flags & FlagAuto) != 0;

        public bool Back => (Flags & FlagBack) != 0;

        public int TiltSteps => (Flags & TiltMask) >> TiltShift;

        public void SetTilt(int steps) => Flags = (byte)((Flags & ~TiltMask) | ((Math.Max(0, Math.Min(BoardTilt.Max, steps)) << TiltShift) & TiltMask));

        /// <summary>The settings as text (empty when there are none).</summary>
        public string SpecText => Spec.Length == 0 ? "" : Spec.ToString();

        /// <summary>Sets the settings from text; a text that does not fit (the pure side keeps it to 480 bytes) is cut at what fits rather than thrown on.</summary>
        public void SetSpec(string text)
        {
            Spec = default;
            if (string.IsNullOrEmpty(text))
                return;
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            if (bytes.Length > 500)
                return;
            Spec = new FixedString512Bytes(text);
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            byte[] spec = Spec.Length == 0 ? new byte[0] : Encoding.UTF8.GetBytes(Spec.ToString());
            writer.Write(kVersion);
            writer.Write((ushort)(kPayloadV3Fixed + spec.Length + kReachBytes + kGroupBytes + kLaneBytes + kTurnBytes + kBaseReachBytes + kStyleBytes + kBatchBytes + kRiseBytes + kClearanceBytes));
            writer.Write(TypeHash);
            writer.Write(Edge);
            writer.Write(EdgeT);
            writer.Write(Position);
            writer.Write(Yaw);
            writer.Write(Flags);
            writer.Write(SupportHash);
            writer.Write((ushort)spec.Length);
            foreach (byte b in spec)
                writer.Write(b);
            writer.Write(Reach);
            writer.Write(Group);
            writer.Write(Slot);
            writer.Write(Lane);
            writer.Write(OffsetX);
            writer.Write(Turn);
            writer.Write(BaseReach);
            writer.Write(Style);
            writer.Write(BatchId);
            writer.Write(Rise);
            writer.Write(Clearance);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out ushort version);
            reader.Read(out ushort size);
            int consumed = 0;
            this = default;
            if (version >= 1 && size >= kPayloadV1)
            {
                reader.Read(out uint hash);
                reader.Read(out Entity edge);
                reader.Read(out float t);
                reader.Read(out float3 position);
                reader.Read(out float yaw);
                reader.Read(out byte flags);
                TypeHash = hash;
                Edge = edge;
                EdgeT = t;
                Position = position;
                Yaw = yaw;
                Flags = flags;
                consumed = kPayloadV1;

                if (version >= 2 && size >= kPayloadV2)
                {
                    reader.Read(out uint support);
                    SupportHash = support;
                    consumed = kPayloadV2;

                    if (version >= 3 && size >= kPayloadV3Fixed)
                    {
                        reader.Read(out ushort length);
                        consumed = kPayloadV3Fixed;
                        // Never read more than the payload says it has.
                        int available = size - kPayloadV3Fixed;
                        int count = length < available ? length : available;
                        var bytes = new byte[count];
                        for (int i = 0; i < count; i++)
                        {
                            reader.Read(out byte b);
                            bytes[i] = b;
                        }

                        consumed += count;
                        if (count > 0 && count <= 500)
                            Spec = new FixedString512Bytes(Encoding.UTF8.GetString(bytes));

                        if (version >= 4 && size >= consumed + kReachBytes)
                        {
                            reader.Read(out float reach);
                            Reach = WallHeight.ReadReach(reach);
                            consumed += kReachBytes;

                            if (version >= 5 && size >= consumed + kGroupBytes)
                            {
                                reader.Read(out uint group);
                                reader.Read(out byte slot);
                                Group = group;
                                Slot = slot;
                                consumed += kGroupBytes;

                                if (version >= 6 && size >= consumed + kLaneBytes)
                                {
                                    reader.Read(out byte lane);
                                    reader.Read(out float offset);
                                    Lane = lane;
                                    OffsetX = offset >= -200f && offset <= 200f ? offset : 0f;
                                    consumed += kLaneBytes;

                                    if (version >= 7 && size >= consumed + kTurnBytes)
                                    {
                                        reader.Read(out float turn);
                                        Turn = turn >= -MaxTurn && turn <= MaxTurn ? turn : 0f;
                                        consumed += kTurnBytes;

                                        if (size >= consumed + kBaseReachBytes)
                                        {
                                            reader.Read(out float baseReach);
                                            BaseReach = WallHeight.ReadReach(baseReach);
                                            consumed += kBaseReachBytes;

                                            if (version >= 8 && size >= consumed + kStyleBytes)
                                            {
                                                reader.Read(out byte style);
                                                Style = style;
                                                consumed += kStyleBytes;

                                                if (version >= 9 && size >= consumed + kBatchBytes)
                                                {
                                                    reader.Read(out uint batch);
                                                    BatchId = batch;
                                                    consumed += kBatchBytes;

                                                    if (version >= 10 && size >= consumed + kRiseBytes)
                                                    {
                                                        reader.Read(out float rise);
                                                        Rise = rise >= 0f && rise <= Assembly.MaxRise ? rise : 0f;
                                                        consumed += kRiseBytes;

                                                        if (version >= 11 && size >= consumed + kClearanceBytes)
                                                        {
                                                            reader.Read(out float clearance);
                                                            Clearance = SupportLayouts.ClampClearance(clearance);
                                                            consumed += kClearanceBytes;
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // A newer save carries more than this version knows (or a payload too small to use): the rest is skipped, never read as data.
            for (int i = consumed; i < size; i++)
                reader.Read(out byte _);
        }
    }
}
