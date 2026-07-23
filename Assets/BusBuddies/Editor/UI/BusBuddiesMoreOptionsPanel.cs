using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // Bus Buddies "More options" panel. Rendered in the LEFT column of the Level
    // Editor (between the cell/brush palette and the TOOLS section) via the generic
    // ProfileLeftPanel hook. Hosts the Road-Block grid: a compact
    // # Slot / Block? / Amount strip. Ticking a slot blocks it (prefill amount 10);
    // the Amount IntField appears only for ticked slots and clamps to >= 1.
    //
    // The road-block STATE logic lives in BusBuddiesSlotConfigs and is unit-tested
    // (BusBuddiesSlotConfigsTests); GUI is not unit-tested (house style). All edits
    // flow through BusBuddiesSlotConfigs with the session's undo/dirty bookkeeping.
    // Relocated here from BusBuddiesQueuePanel.DrawRoadBlockStrip (2026-07-22).
    public sealed class BusBuddiesMoreOptionsPanel : ProfileLeftPanel
    {
        private const float RBRowH   = 18f;
        private const int   RBMaxSlots = 8;   // safety cap on strip width
        private static readonly Color RBHeaderBg = new Color(0.20f, 0.16f, 0.10f);
        private static readonly Color RBAccent   = new Color(0.95f, 0.65f, 0.25f);

        private const float PlateRowH = 18f;
        private static readonly Color PlateAccent = new Color(0.25f, 0.70f, 0.95f);

        private const float LevelTypeRowH = 18f;

        // Header (More options) + Level Type (1 row) + Road Blocks (title + 3 grid rows)
        // + Plates (title + column header + up to ~4 rows + Add button) + padding.
        public override float PreferredHeight => 104f + 16f + PlateRowH * 6f + 8f + LevelTypeRowH + 4f;

        public override void OnGUI(Rect rect, LevelEditorSession session, GameProfile profile)
        {
            EditorGUI.DrawRect(rect, RBHeaderBg);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2f), RBAccent);

            var doc = session?.Document;

            float x0 = rect.x + 6f;
            float y  = rect.y + 4f;

            GUI.Label(new Rect(x0, y, rect.width - 12f, 16f), "More options", EditorStyles.boldLabel);
            y += 18f;

            if (doc == null)
            {
                GUI.Label(new Rect(x0, y, rect.width - 12f, 16f),
                    "No level open.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            // ── Level Type ────────────────────────────────────────────────────
            const float LevelTypeLabelW = 66f;
            GUI.Label(new Rect(x0, y, LevelTypeLabelW, LevelTypeRowH), "Level Type", EditorStyles.miniLabel);
            var currentLevelType = BusBuddiesLevelType.Get(doc);
            EditorGUI.BeginChangeCheck();
            var newLevelType = (BusLevelType)EditorGUI.EnumPopup(
                new Rect(x0 + LevelTypeLabelW, y, rect.width - 12f - LevelTypeLabelW, LevelTypeRowH),
                currentLevelType);
            if (EditorGUI.EndChangeCheck() && newLevelType != currentLevelType)
            {
                session.PushUndoSnapshot();
                BusBuddiesLevelType.Set(doc, newLevelType);
                session.MarkDirty();
            }
            y += LevelTypeRowH + 4f;

            var titleStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = RBAccent } };
            GUI.Label(new Rect(x0, y, rect.width - 12f, 14f), "Road Blocks", titleStyle);
            y += 16f;

            int slots = Mathf.Clamp(doc.GameData?["conveyorCount"]?.Value<int>() ?? 5, 1, RBMaxSlots);

            const float LabelW = 46f;
            float gridX = x0 + LabelW;
            float slotW = (rect.xMax - 6f - gridX) / slots;

            GUI.Label(new Rect(x0, y,              LabelW, RBRowH), "# Slot", EditorStyles.miniLabel);
            GUI.Label(new Rect(x0, y + RBRowH,      LabelW, RBRowH), "Block?", EditorStyles.miniLabel);
            GUI.Label(new Rect(x0, y + RBRowH * 2f, LabelW, RBRowH), "Amount", EditorStyles.miniLabel);

            for (int i = 0; i < slots; i++)
            {
                float cx = gridX + i * slotW;

                GUI.Label(new Rect(cx, y, slotW, RBRowH), (i + 1).ToString(),
                    EditorStyles.centeredGreyMiniLabel);

                bool wasBlocked = BusBuddiesSlotConfigs.IsBlocked(doc, i);
                float toggleX   = cx + (slotW - 16f) * 0.5f;
                bool nowBlocked = EditorGUI.Toggle(new Rect(toggleX, y + RBRowH, 16f, RBRowH), wasBlocked);
                if (nowBlocked != wasBlocked)
                {
                    session.PushUndoSnapshot();
                    if (nowBlocked) BusBuddiesSlotConfigs.SetBlocked(doc, i, BusBuddiesSlotConfigs.DefaultAmount);
                    else            BusBuddiesSlotConfigs.Clear(doc, i);
                    session.MarkDirty();
                }

                if (nowBlocked && BusBuddiesSlotConfigs.TryGet(doc, i, out var cfg))
                {
                    float fieldW = Mathf.Min(slotW - 4f, 40f);
                    float fieldX = cx + (slotW - fieldW) * 0.5f;
                    EditorGUI.BeginChangeCheck();
                    int newAmt = EditorGUI.IntField(
                        new Rect(fieldX, y + RBRowH * 2f, fieldW, RBRowH - 2f),
                        cfg.Amount, EditorStyles.miniTextField);
                    if (EditorGUI.EndChangeCheck() && newAmt != cfg.Amount)
                    {
                        session.PushUndoSnapshot();
                        BusBuddiesSlotConfigs.SetAmount(doc, i, newAmt);
                        session.MarkDirty();
                    }
                }
            }

            // ── Plates ────────────────────────────────────────────────────────
            float py = y + RBRowH * 3f + 8f;
            DrawPlates(new Rect(rect.x, py, rect.width, rect.yMax - py), session, doc);
        }

        // Numeric fine-tune list for rectangular plates: one row per plate with
        // X / Y / W / H / Amount IntFields + a Remove button, plus an Add button.
        // All edits flow through BusBuddiesPlateConfigs with undo/dirty bookkeeping.
        // (The drag tool is the primary authoring path; this is for precise tweaks.)
        private void DrawPlates(Rect rect, LevelEditorSession session, LevelDocument doc)
        {
            float x0 = rect.x + 6f;
            float y  = rect.y;

            var titleStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = PlateAccent } };
            GUI.Label(new Rect(x0, y, rect.width - 12f, 14f), "Plates", titleStyle);
            y += 16f;

            var plates = BusBuddiesPlateConfigs.All(doc);

            // Column header.
            const float NumW = 20f, FieldW = 26f, RemW = 18f, Gap = 2f;
            float fx = x0 + NumW;
            void Head(string s, float w, ref float cx)
            {
                GUI.Label(new Rect(cx, y, w, PlateRowH), s, EditorStyles.centeredGreyMiniLabel);
                cx += w + Gap;
            }
            if (plates.Count > 0)
            {
                float cx = fx;
                Head("X", FieldW, ref cx); Head("Y", FieldW, ref cx);
                Head("W", FieldW, ref cx); Head("H", FieldW, ref cx);
                Head("Amt", FieldW, ref cx);
                y += PlateRowH;
            }

            for (int i = 0; i < plates.Count; i++)
            {
                var p = plates[i];
                GUI.Label(new Rect(x0, y, NumW, PlateRowH), (i + 1).ToString(), EditorStyles.miniLabel);

                float cx = fx;
                int nx = IntField(ref cx, y, FieldW, p.X);
                int ny = IntField(ref cx, y, FieldW, p.Y);
                int nw = IntField(ref cx, y, FieldW, p.W);
                int nh = IntField(ref cx, y, FieldW, p.H);
                int na = IntField(ref cx, y, FieldW, p.Amount);

                if (nx != p.X || ny != p.Y || nw != p.W || nh != p.H)
                {
                    session.PushUndoSnapshot();
                    BusBuddiesPlateConfigs.SetRect(doc, i, nx, ny, nw, nh);
                    session.MarkDirty();
                }
                if (na != p.Amount)
                {
                    session.PushUndoSnapshot();
                    BusBuddiesPlateConfigs.SetAmount(doc, i, na);
                    session.MarkDirty();
                }

                if (GUI.Button(new Rect(cx, y, RemW, PlateRowH - 1f), "x", EditorStyles.miniButton))
                {
                    session.PushUndoSnapshot();
                    BusBuddiesPlateConfigs.Remove(doc, i);
                    session.MarkDirty();
                    GUI.changed = true;
                    break; // list mutated — stop iterating this frame
                }

                y += PlateRowH;
            }

            // Add button: a small default plate at the origin (clamped to the grid).
            if (GUI.Button(new Rect(x0, y, 90f, PlateRowH), "+ Add plate", EditorStyles.miniButton))
            {
                int w = Mathf.Min(3, doc.Grid?.Width ?? 3);
                int h = Mathf.Min(3, doc.Grid?.Height ?? 3);
                session.PushUndoSnapshot();
                BusBuddiesPlateConfigs.Add(doc, 0, 0, w, h, BusBuddiesPlateConfigs.DefaultAmount);
                session.MarkDirty();
                GUI.changed = true;
            }
        }

        private static int IntField(ref float cx, float y, float w, int value)
        {
            int v = EditorGUI.IntField(new Rect(cx, y, w, PlateRowH - 2f), value, EditorStyles.miniTextField);
            cx += w + 2f;
            return v;
        }
    }
}
