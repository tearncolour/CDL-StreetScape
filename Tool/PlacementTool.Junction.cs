using System;
using CDLC.TS.Geo;
using Game.UI;
using Unity.Entities;
using Unity.Mathematics;

namespace CDLC.TS
{
    /// <summary>
    /// The junction signs (路1, 路3, 路4, 路6, 路7) filled in from the junction ahead: when one is placed (or put on a support, or its type is replaced) the settings come from what the game's road
    /// network says about the junction in front of it (JunctionReader, JunctionFill); the editor has a button that reads it again for a sign that stands. What is read is written into the sign's settings,
    /// like anything the player types: nothing is read again when the sign is drawn, and every setting can be changed afterwards.
    /// </summary>
    internal sealed partial class PlacementTool
    {
        /// <summary>Whether a junction sign that is placed takes its settings from the junction ahead (the panel's switch, on by default; for the session).</summary>
        internal static bool ReadJunctions { get; private set; } = true;

        /// <summary>What the last reading said (for the editor), and a number that changes when it does.</summary>
        internal static JrNote LastReadNote { get; private set; }

        internal static int ReadNoteRevision { get; private set; }

        private NameSystem m_Names;
        private JrResult m_ReadCache;
        private string m_ReadCacheKey = "";

        private void SetReadNote(JrNote note)
        {
            string was = LastReadNote == null ? "" : LastReadNote.Zh + "|" + LastReadNote.En + "|" + string.Join("|", LastReadNote.ZhNotes);
            string now = note == null ? "" : note.Zh + "|" + note.En + "|" + string.Join("|", note.ZhNotes);
            if (was == now)
                return;
            LastReadNote = note;
            ReadNoteRevision++;
            EditorRevision++;
        }

        internal static void SetReadJunctionsOn(bool on)
        {
            if (ReadJunctions == on)
                return;
            ReadJunctions = on;
            Mod.log.Info("Junction signs: placing " + (on ? "reads" : "does not read") + " the junction ahead.");
            EditorRevision++;
        }

        /// <summary>The junction ahead of a sign at a place, read from the game; kept for the same road, nearly the same place and the same facing, so that moving the cursor does not read the network every frame.</summary>
        private JrResult ReadAt(Entity edge, float3 position, float yaw, string why, bool sequence)
        {
            string key = $"{edge.Index}:{edge.Version}|{(int)Math.Round(position.x / 6f)}|{(int)Math.Round(position.z / 6f)}|{(int)Math.Round(yaw * 20f)}|{NetRevision.Value}|{sequence}";
            if (key == m_ReadCacheKey && m_ReadCache != null)
                return m_ReadCache;
            m_Names = m_Names ?? World.GetOrCreateSystemManaged<NameSystem>();
            m_ReadCache = JunctionReader.Read(EntityManager, m_Names, edge, position, yaw, why, sequence);
            m_ReadCacheKey = key;
            return m_ReadCache;
        }

        /// <summary>
        /// The spec of a junction sign with the junction ahead read into it, or the spec as it is when the sign is not one of those, reading is off, or nothing could be read (the note says why).
        /// The reading is made for the sign at position on the edge, facing yaw.
        /// </summary>
        private string ApplyJunction(SignDef def, string spec, Entity edge, float3 position, float yaw, string why)
        {
            if (def?.Template == null || !JunctionFill.CanRead(def.Id) || !ReadJunctions || edge == Entity.Null || !EntityManager.Exists(edge))
                return spec;
            JrResult read = ReadAt(edge, position, yaw, why, JunctionFill.WantsSequence(def.Id));
            string filled = JunctionFill.Apply(def, spec, read, out JrNote note);
            SetReadNote(note);
            return filled ?? spec;
        }

        /// <summary>The editor's button: reads the junction ahead of the sign being edited again and writes what it says into its settings.</summary>
        internal void ReadJunctionNow()
        {
            if (Mode != 2 || !TryReadEdit(out TsItem item))
                return;
            SignDef def = SignCatalog.ByHash(item.TypeHash);
            if (def?.Template == null || !JunctionFill.CanRead(def.Id))
                return;
            m_ReadCacheKey = "";
            if (item.Edge == Entity.Null || !EntityManager.Exists(item.Edge))
            {
                SetReadNote(new JrNote { Zh = "这块牌不在道路上（或它的路已被改掉），不能读取路口。", En = "This sign is not on a road (or its road has been changed): the junction cannot be read." });
                return;
            }

            JrResult read = ReadAt(item.Edge, item.Position, item.Yaw, "button", JunctionFill.WantsSequence(def.Id));
            string filled = JunctionFill.Apply(def, item.SpecText, read, out JrNote note);
            SetReadNote(note);
            if (filled == null)
            {
                EditorRevision++;
                return;
            }

            item.SetSpec(filled);
            WriteItem(EditEntity, item);
            EditorRevision++;
            EditorTarget++;
            Mod.log.Info($"Edit: the junction ahead was read into {def.Id}: '{filled}'.");
        }
    }
}
