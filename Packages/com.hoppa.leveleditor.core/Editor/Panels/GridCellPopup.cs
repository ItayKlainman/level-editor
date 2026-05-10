using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    public sealed class GridCellPopup : PopupWindowContent
    {
        private readonly LevelEditorSession  _session;
        private readonly CellRef             _cellRef;
        private          ICellData           _cell;
        private readonly ICellTypeDefinition _def;

        // Cached so direction-picker state survives across OnGUI calls
        private List<CellContextAction> _actions;

        private const float W        = 240f;
        private const float HeaderH  = 28f;
        private const float Pad      = 6f;
        private const float BtnH     = 20f;
        private const float BtnGap   = 4f;
        private const float OptionsGap = 2f;

        private static readonly Color HeaderBg  = new Color(0.17f, 0.21f, 0.33f);
        private static readonly Color Accent     = new Color(0.30f, 0.65f, 1.00f);
        private static readonly Color DeleteBg   = new Color(0.70f, 0.22f, 0.22f);
        private static readonly Color DividerCol = new Color(1f, 1f, 1f, 0.06f);

        public GridCellPopup(LevelEditorSession session, CellRef cellRef)
        {
            _session = session;
            _cellRef = cellRef;
            _cell    = session.Document.Grid.Get(cellRef.X, cellRef.Y);
            if (_cell != null)
                session.CellTypes.TryGetDefinition(_cell.CellTypeId, out _def);
        }

        private List<CellContextAction> Actions
        {
            get
            {
                if (_actions == null)
                {
                    _actions = new List<CellContextAction>();
                    if (_def is ICellContextActions ca)
                        foreach (var a in ca.GetContextActions(_cell, _session.CellTypes))
                            _actions.Add(a);
                }
                return _actions;
            }
        }

        public override Vector2 GetWindowSize()
        {
            float inspH    = _def is CellTypeDefinition ctd ? ctd.InspectorPreferredHeight : 0f;
            float actionsH = 0f;
            foreach (var a in Actions)
            {
                if (a.OptionsHeight > 0f) actionsH += a.OptionsHeight + OptionsGap;
                actionsH += BtnH + BtnGap;
            }
            float total = HeaderH + Pad
                        + (inspH > 0f ? inspH + Pad : 0f)
                        + actionsH
                        + (Actions.Count > 0 ? Pad : 0f)  // divider gap before delete
                        + BtnH + Pad;
            return new Vector2(W, total);
        }

        public override void OnGUI(Rect rect)
        {
            // ── Header ──────────────────────────────────────────────────
            EditorGUI.DrawRect(new Rect(0f, 0f, rect.width, HeaderH), HeaderBg);
            EditorGUI.DrawRect(new Rect(0f, 0f, rect.width, 2f), Accent);
            GUI.Label(new Rect(8f, 6f, rect.width - 16f, HeaderH - 12f),
                _def?.DisplayName ?? "Cell", EditorStyles.boldLabel);

            float y = HeaderH + Pad;

            // ── Cell inspector ───────────────────────────────────────────
            float inspH = _def is CellTypeDefinition ctd ? ctd.InspectorPreferredHeight : 0f;
            if (inspH > 0f && _def != null && _cell != null)
            {
                var inspRect = new Rect(Pad, y, rect.width - Pad * 2f, inspH);
                EditorGUI.BeginChangeCheck();
                _def.DrawInspector(inspRect, ref _cell);
                if (EditorGUI.EndChangeCheck())
                {
                    _session.Document.Grid.Set(_cellRef.X, _cellRef.Y, _cell);
                    _session.MarkDirty();
                    (_def as CellTypeDefinition)?.OnAfterInspectorChanged(_cellRef.X, _cellRef.Y, _session);
                    _session.RunValidation();
                    editorWindow?.Repaint();
                }
                y += inspH + Pad;
            }

            // ── Context actions ──────────────────────────────────────────
            foreach (var action in Actions)
            {
                // Optional setup UI (e.g. direction picker) above the button
                if (action.OptionsHeight > 0f)
                {
                    action.DrawOptions(new Rect(Pad, y, rect.width - Pad * 2f, action.OptionsHeight));
                    y += action.OptionsHeight + OptionsGap;
                }

                if (GUI.Button(new Rect(Pad, y, rect.width - Pad * 2f, BtnH),
                    action.Label, EditorStyles.miniButton))
                {
                    _session.SetCell(_cellRef.X, _cellRef.Y, action.Create());
                    _session.RunValidation();
                    editorWindow?.Close();
                }
                y += BtnH + BtnGap;
            }

            // ── Divider + delete ─────────────────────────────────────────
            if (Actions.Count > 0)
            {
                y += Pad * 0.5f;
                EditorGUI.DrawRect(new Rect(Pad, y, rect.width - Pad * 2f, 1f), DividerCol);
                y += Pad * 0.5f;
            }

            var old = GUI.backgroundColor;
            GUI.backgroundColor = DeleteBg;
            if (GUI.Button(new Rect(Pad, rect.yMax - BtnH - Pad, rect.width - Pad * 2f, BtnH),
                "Delete Cell", EditorStyles.miniButton))
            {
                var emptyDef = _session.Profile.CellTypes.Count > 0 ? _session.Profile.CellTypes[0] : null;
                _session.SetCell(_cellRef.X, _cellRef.Y, emptyDef?.CreateDefault());
                _session.RunValidation();
                editorWindow?.Close();
            }
            GUI.backgroundColor = old;
        }
    }
}
