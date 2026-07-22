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

        // Header (More options) + Road Blocks title + 3 grid rows + padding.
        public override float PreferredHeight => 104f;

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
        }
    }
}
