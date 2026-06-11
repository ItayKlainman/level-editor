using System;
using System.Collections.Generic;
using Hoppa.LevelEditor.Core.Editor;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    // One-window spool editor — mirrors GridCellPopup. Opened on right-clicking a spool
    // row, replacing the old GenericMenu (Change Color / Add Connect) + separate color
    // picker. Holds swatches + a single connect button.
    //
    // Color picks apply the CHEAP way (mutate + rebuild the small top section + MarkDirty),
    // with NO full-document undo snapshot — the same path the grid swatches use, which is
    // why this no longer stutters. Connect/disconnect are structural and keep their undo
    // (handled inside YarnSpoolConnection).
    public sealed class YarnSpoolPopup : PopupWindowContent
    {
        private readonly LevelEditorSession   _session;
        private readonly YarnTopSectionData   _topData;
        private readonly ColorPaletteAsset    _palette;
        private readonly int                  _col;
        private readonly int                  _idx;
        private readonly ICollection<string>  _pickerFilter;

        private const float W       = 200f;
        private const float HeaderH = 26f;
        private const float Pad     = 8f;
        private const float BtnH    = 20f;

        private static readonly Color HeaderBg = new Color(0.17f, 0.19f, 0.26f);
        private static readonly Color Accent   = new Color(0.30f, 0.65f, 1.00f);

        public YarnSpoolPopup(LevelEditorSession session, YarnTopSectionData topData,
            ColorPaletteAsset palette, int col, int idx, ICollection<string> pickerFilter)
        {
            _session      = session;
            _topData      = topData;
            _palette      = palette;
            _col          = col;
            _idx          = idx;
            _pickerFilter = pickerFilter;
        }

        private YarnSpoolData Spool =>
            _col >= 0 && _col < _topData.Columns.Count
            && _idx >= 0 && _idx < _topData.Columns[_col].Spools.Count
                ? _topData.Columns[_col].Spools[_idx]
                : null;

        private float SwatchH =>
            _palette != null ? ColorSwatchDrawer.MeasureHeight(_palette, W - Pad * 2f, _pickerFilter) : 0f;

        public override Vector2 GetWindowSize()
        {
            float h = HeaderH + Pad + SwatchH + Pad + BtnH + Pad;
            return new Vector2(W, Mathf.Max(64f, h));
        }

        public override void OnGUI(Rect rect)
        {
            var spool = Spool;
            if (spool == null) { editorWindow?.Close(); return; }

            // Header
            EditorGUI.DrawRect(new Rect(0f, 0f, rect.width, HeaderH), HeaderBg);
            EditorGUI.DrawRect(new Rect(0f, 0f, rect.width, 2f), Accent);
            GUI.Label(new Rect(8f, 5f, rect.width - 16f, HeaderH - 8f), "Spool", EditorStyles.boldLabel);

            float y = HeaderH + Pad;

            // Color swatches — pick applies cheaply and closes.
            if (_palette != null)
            {
                var swatchRect = new Rect(Pad, y, rect.width - Pad * 2f, SwatchH);
                string newId = ColorSwatchDrawer.Draw(swatchRect, _palette, spool.ColorId, _pickerFilter);
                if (!string.Equals(newId, spool.ColorId, StringComparison.Ordinal))
                {
                    ApplyColor(spool, newId);
                    editorWindow?.Close();
                    return;
                }
                y += SwatchH + Pad;
            }

            // Connect button — single button whose label/enabled reflect the pair state.
            YarnSpoolConnection.BuildConnInfo(_topData, out var members, out var pendingId);
            var info = DescribeConnectButton(_topData, members, pendingId, _col, _idx);

            using (new EditorGUI.DisabledGroupScope(!info.Enabled))
            {
                if (GUI.Button(new Rect(Pad, y, rect.width - Pad * 2f, BtnH), info.Label, EditorStyles.miniButton))
                {
                    PerformConnect(info.Action, spool, pendingId);
                    editorWindow?.Close();
                }
            }
        }

        // Cheap color apply: mutate the captured top section, write it back (small serialize,
        // top section only), refresh validation. No PushUndoSnapshot — matches the grid swatch.
        private void ApplyColor(YarnSpoolData spool, string id)
        {
            spool.ColorId = id;
            _session.Document.TopSection = JObject.FromObject(_topData);
            _session.MarkDirty();
            _session.RunValidation();
        }

        private void PerformConnect(SpoolConnectAction action, YarnSpoolData spool, int? pendingId)
        {
            switch (action)
            {
                case SpoolConnectAction.StartPair:
                    YarnSpoolConnection.Connect(_session, _topData, spool, YarnSpoolConnection.AllocId(_topData));
                    break;
                case SpoolConnectAction.CompletePair:
                    if (pendingId.HasValue)
                        YarnSpoolConnection.Connect(_session, _topData, spool, pendingId.Value);
                    break;
                case SpoolConnectAction.Disconnect:
                    if (spool.ConnectionId.HasValue)
                        YarnSpoolConnection.DisconnectGroup(_session, _topData, spool.ConnectionId.Value);
                    break;
            }
        }

        // ── Connect-button state machine (pure — unit-tested) ────────────────
        public enum SpoolConnectAction { None, StartPair, CompletePair, Disconnect }

        public readonly struct ConnectButton
        {
            public readonly string Label;
            public readonly bool Enabled;
            public readonly SpoolConnectAction Action;
            public ConnectButton(string label, bool enabled, SpoolConnectAction action)
            { Label = label; Enabled = enabled; Action = action; }
        }

        // Mirrors the old ShowRowMenu connect options, collapsed to one button:
        //  • connected            → Disable/Cancel Connect (enabled)
        //  • no pending pair       → Add Connect (enabled, starts a pair)
        //  • pending, adjacent ok  → Add Connect (complete Pair N) (enabled)
        //  • pending, not adjacent → disabled "needs an adjacent column"
        //  • pending, would cross  → disabled "would soft-lock — links can't cross"
        public static ConnectButton DescribeConnectButton(
            YarnTopSectionData topData,
            Dictionary<int, List<(int col, int pos)>> members,
            int? pendingId, int col, int idx)
        {
            var spool = topData.Columns[col].Spools[idx];

            if (spool.ConnectionId.HasValue)
            {
                int gid   = spool.ConnectionId.Value;
                int dispN = YarnSpoolConnection.DisplayNumber(members, gid);
                bool whole = members.TryGetValue(gid, out var g) && g.Count >= 2;
                string label = whole ? $"Disable Connect (Pair {dispN})" : $"Cancel Connect (Pair {dispN})";
                return new ConnectButton(label, true, SpoolConnectAction.Disconnect);
            }

            if (pendingId == null)
                return new ConnectButton("Add Connect", true, SpoolConnectAction.StartPair);

            int pendN  = YarnSpoolConnection.DisplayNumber(members, pendingId.Value);
            var anchor = members[pendingId.Value][0];
            if (!YarnSpoolConnection.CanComplete(anchor.col, col))
                return new ConnectButton($"Add Connect (Pair {pendN} needs an adjacent column)",
                    false, SpoolConnectAction.None);
            if (YarnSpoolConnection.CompletingDeadlocks(topData, pendingId.Value, col, idx))
                return new ConnectButton($"Add Connect (Pair {pendN} would soft-lock — links can't cross)",
                    false, SpoolConnectAction.None);
            return new ConnectButton($"Add Connect (complete Pair {pendN})",
                true, SpoolConnectAction.CompletePair);
        }
    }
}
