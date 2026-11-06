using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>One board of a support that the eyedropper took: its sign and written settings (without where they were put), and where it stood on the support it was picked from.</summary>
    public struct CarriedBoard
    {
        public SignDef Def;
        public string Spec;
        public int Slot, Lane;
        public float OffsetX, Turn, Rise, Clearance, Tilt;
        public bool Back, Manual;
    }

    /// <summary>What an item of a carried board is written with (TsItem's own fields, in their own ranges).</summary>
    public struct BoardFields
    {
        public byte Slot, Lane;
        public float OffsetX, Turn, Rise, Clearance;
        public int TiltSteps;
        public bool Back, Manual;
    }

    /// <summary>
    /// All the boards of one pole (0.12.88; the owner: the eyedropper takes one board, and with a key held all the boards on the pole): the boards of the picked support as they stood on it, and
    /// which of them was clicked. Placing puts them down together on a new support, in one click. Pure.
    /// </summary>
    public sealed class CarryPick
    {
        /// <summary>The boards in the order of ItemGroups.Compare, and the place of the one that was clicked among them: its entry is the one the panel shows and the placing starts from.</summary>
        public List<CarriedBoard> Boards = new List<CarriedBoard>();
        public int Lead;

        public int Count => Boards.Count;

        /// <summary>What tells one carried pole from another (the preview is made again when it changes).</summary>
        public string Key
        {
            get
            {
                var key = new System.Text.StringBuilder();
                foreach (CarriedBoard board in Boards)
                    key.Append(board.Def.Hash).Append(':').Append(board.Spec).Append(':').Append(board.Slot).Append(':').Append(board.Lane).Append('@').Append((int)Math.Round(board.OffsetX * 100f)).Append(board.Manual ? 'M' : 'a').Append(board.Back ? 'B' : 'F').Append(';');
                return key.Append('|').Append(Lead).ToString();
            }
        }

        /// <summary>Whether a click picks the whole pole: the eyedropper, or Ctrl while placing (picking is only ever set in mode 0), with Shift held, and no text field has the keyboard.</summary>
        public static bool Wants(int mode, bool picking, bool shift, bool typing) => shift && !typing && (mode == StylePick.Mode || (mode == 0 && picking));

        /// <summary>
        /// Which supports put the boards of a pole down together: the ones that stand or hang by a road. A wall, a signal's arm, a structure over the road and the game's own lamp posts and signal poles
        /// are put onto something that is there, board by board, and take one. The planner's support (null) is the road's.
        /// </summary>
        public static bool Takes(SupportKind? kind) => !kind.HasValue || !(SupportKinds.OnWall(kind.Value) || SupportKinds.OverRoad(kind.Value) || SupportKinds.Hosted(kind.Value));

        /// <summary>
        /// The boards of the support an item is on (members: in the order of ItemGroups.Compare; clicked: the place of the item that was pointed at). Null when the pole holds one board of its own
        /// kind of entry only, or the board clicked is not one. A board of a type that is no more in the catalogue, or has no entry in the panel (hasEntry), is left out; skipped counts them.
        /// </summary>
        public static CarryPick Of(IReadOnlyList<ItemRecord> members, int clicked, Func<SignDef, bool> hasEntry, out int skipped)
        {
            skipped = 0;
            var carry = new CarryPick { Lead = -1 };
            for (int i = 0; i < members.Count; i++)
            {
                ItemRecord record = members[i];
                SignDef def = SignCatalog.ByHash(record.TypeHash);
                if (def == null || def.IsMarking || (hasEntry != null && !hasEntry(def)))
                {
                    skipped++;
                    continue;
                }

                if (i == clicked)
                    carry.Lead = carry.Boards.Count;
                carry.Boards.Add(new CarriedBoard
                {
                    Def = def,
                    Spec = def.Template != null ? MarkingTemplates.Unplaced(def.Template, record.Spec) : "",
                    Slot = record.Slot,
                    Lane = record.Lane,
                    OffsetX = record.OffsetX,
                    Turn = record.Turn,
                    Rise = record.Rise,
                    Clearance = record.Clearance,
                    Tilt = record.Tilt,
                    Back = record.Back,
                    Manual = record.Manual
                });
            }

            return carry.Lead >= 0 && carry.Boards.Count > 1 ? carry : null;
        }

        /// <summary>The fields an item of a board is written with: the place on the support it was picked from, each in the range the item keeps it in.</summary>
        public static BoardFields FieldsOf(CarriedBoard board) => new BoardFields
        {
            Slot = (byte)Math.Max(0, Math.Min(255, board.Slot)),
            Lane = (byte)Math.Max(0, Math.Min(255, board.Lane)),
            OffsetX = board.OffsetX,
            Turn = board.Turn,
            Rise = Assembly.ClampRise(board.Rise),
            Clearance = SupportLayouts.ClampClearance(board.Clearance),
            TiltSteps = BoardTilt.StepsOf(board.Tilt),
            Back = board.Back,
            Manual = board.Manual
        };

        /// <summary>
        /// The carried boards as the records of a support, to draw it where it will be put (ChunkBuilder.PartsOfGroup draws a placed one the same way): the lead takes the settings now in the draft
        /// (leadSpec) and the tilt now chosen (leadTilt, radians), the others are as they were picked; the support, the reach and the paint are the ones the placing will use. The records are in the order of the boards, which Of takes from the members in the order of ItemGroups.Compare.
        /// </summary>
        public static List<ItemRecord> Records(CarryPick carry, string leadSpec, float leadTilt, uint supportHash, float reach, byte style, Vec3 position, float yaw)
        {
            var records = new List<ItemRecord>(carry.Count);
            for (int i = 0; i < carry.Count; i++)
            {
                CarriedBoard board = carry.Boards[i];
                bool lead = i == carry.Lead;
                records.Add(new ItemRecord(board.Def.Hash, position, yaw, supportHash, lead ? leadSpec : board.Spec, reach, 1u, board.Slot, board.Lane, board.OffsetX)
                {
                    Turn = board.Turn,
                    Rise = board.Rise,
                    Clearance = board.Clearance,
                    Tilt = lead ? leadTilt : board.Tilt,
                    Back = board.Back,
                    Manual = board.Manual,
                    Style = style
                });
            }

            return records;
        }

        /// <summary>Rings for a pole of this many boards: one for each, one inside the other, so that the cursor shows how many are taken. A single board has the one ring it always had.</summary>
        public static float[] RingRadii(int count)
        {
            var radii = new float[Math.Max(1, count)];
            for (int i = 0; i < radii.Length; i++)
                radii[i] = 1.6f + 0.4f * i;
            return radii;
        }

        /// <summary>Whether a placing of a pole fits what the renderer is told to keep: the items there are and the ones that are to come are not more than the most (one of them is placed as a single sign is).</summary>
        public static bool Fits(int known, int count, int max) => known + Math.Max(0, count - 1) < max;
    }

    /// <summary>
    /// The pole the tool carries to its next placing (0.12.88): set by a pick of the whole pole, kept for as long as the same entry and support are placed, and let go the moment another entry, another
    /// support or another mode is chosen, or the tool is left. Pure, so that the moments are checked.
    /// </summary>
    public sealed class Carrying
    {
        public CarryPick Pole { get; private set; }

        public bool Active => Pole != null && Pole.Count > 1;

        public void Take(CarryPick pole) => Pole = pole;

        /// <summary>An entry of the panel was chosen (the pick makes its own choice first and takes the pole after it).</summary>
        public void ChoseEntry() => Pole = null;

        /// <summary>A support was chosen, by the panel or the keys.</summary>
        public void ChoseSupport() => Pole = null;

        /// <summary>The tool's mode changed: only placing goes on with a pole.</summary>
        public void ModeChanged(int mode)
        {
            if (mode != 0)
                Pole = null;
        }

        /// <summary>The tool was left (right click, Esc).</summary>
        public void Left() => Pole = null;
    }
}
