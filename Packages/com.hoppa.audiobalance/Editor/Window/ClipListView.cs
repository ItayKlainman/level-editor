using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// Filtering, sorting and bulk edits for the clip table. Kept as pure functions over the
    /// row list so they test without any UI.
    ///
    /// <para>
    /// <c>BuildVisible</c> takes a <c>categoryOf</c> lookup rather than the profile on
    /// purpose. It runs on every OnGUI event, and <c>AudioBalanceProfile.SettingsFor</c>
    /// appends a new <c>ClipSettings</c> on a miss -- so taking the profile made a "pure"
    /// function write to a ScriptableObject during rendering, with no <c>Undo.RecordObject</c>
    /// and no <c>EditorUtility.SetDirty</c>. Those writes are not undoable and not reliably
    /// persisted, but they ARE picked up by any <c>AssetDatabase.SaveAssets()</c>. It was also
    /// O(n^2) per repaint, since <c>SettingsFor</c> is a linear scan run once per row. The
    /// window resolves settings ONCE, outside the render path, via
    /// <see cref="BuildSettingsLookup"/>, and passes a read-only lookup here.
    /// </para>
    /// </summary>
    public static class ClipListView
    {
        public static List<AudioBalanceRow> BuildVisible(
            IReadOnlyList<AudioBalanceRow> rows,
            string filter,
            ClipSortMode sort,
            bool ascending,
            Func<AudioBalanceRow, string> categoryOf)
        {
            var visible = new List<AudioBalanceRow>();
            if (rows == null)
            {
                return visible;
            }

            foreach (var row in rows)
            {
                if (row?.Clip == null)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(filter) &&
                    row.Clip.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                visible.Add(row);
            }

            // OrderBy/OrderByDescending are both stable, so rows with equal keys keep their
            // discovery order in EITHER direction. Sorting ascending and then calling
            // List.Reverse() would not: Reverse inverts tie groups too, silently destroying
            // stability the moment the user flips the sort direction.
            switch (sort)
            {
                case ClipSortMode.Loudness:
                    // An unmeasurable clip has no loudness. float.MinValue parks it at the
                    // quiet end rather than letting a garbage 0f sort it in among real
                    // measurements as the LOUDEST thing in the library.
                    return Order(visible, ascending,
                        r => r.Analysis.Status == ClipStatus.Ok ? r.Analysis.Lufs : float.MinValue);
                case ClipSortMode.Gain:
                    return Order(visible, ascending, r => r.Gain.FinalGainDb);
                case ClipSortMode.Category:
                    return Order(visible, ascending,
                        r => categoryOf?.Invoke(r) ?? string.Empty, StringComparer.OrdinalIgnoreCase);
                default:
                    return Order(visible, ascending,
                        r => r.Clip.name, StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// One key selector, both directions -- so ascending and descending can never drift
        /// apart, and stability holds either way.
        /// </summary>
        private static List<AudioBalanceRow> Order<TKey>(
            List<AudioBalanceRow> rows, bool ascending,
            Func<AudioBalanceRow, TKey> key, IComparer<TKey> comparer = null)
        {
            return (ascending
                ? rows.OrderBy(key, comparer)
                : rows.OrderByDescending(key, comparer)).ToList();
        }

        /// <summary>
        /// The clip-settings map the window hands to the render path, resolved with
        /// <see cref="AudioBalanceProfile.FindSettings"/> and therefore incapable of writing to
        /// the profile.
        ///
        /// <para>
        /// Every row originates from <c>profile.Clips</c> (see
        /// <see cref="AudioBalanceSession.Analyze"/>), so a miss is rare -- but it IS reachable:
        /// undoing the "Scan Audio Folders" enrolment shrinks <c>profile.Clips</c> after the
        /// rows were built. Read-only is the correct response to that, not re-enrolment.
        /// Resolving with <c>SettingsFor</c> "just in case" would trade a benign miss for a real
        /// hazard: an asset write during a repaint, outside any Undo scope. A missing clip is
        /// simply absent from the map, and its row draws without category/trim controls while
        /// still showing its measurement.
        /// </para>
        /// </summary>
        public static Dictionary<AudioClip, ClipSettings> BuildSettingsLookup(
            AudioBalanceProfile profile, IReadOnlyList<AudioBalanceRow> rows)
        {
            var map = new Dictionary<AudioClip, ClipSettings>();
            if (profile == null || rows == null)
            {
                return map;
            }

            foreach (var row in rows)
            {
                if (row?.Clip == null || map.ContainsKey(row.Clip))
                {
                    continue;
                }

                var settings = profile.FindSettings(row.Clip);
                if (settings != null)
                {
                    map[row.Clip] = settings;
                }
            }

            return map;
        }

        /// <summary>
        /// The entries for a row's category popup, plus the index to show as selected.
        ///
        /// <para>
        /// A clip whose <c>Category</c> resolves to nothing -- the category was renamed outside
        /// <see cref="AudioBalanceProfile.RenameCategory"/>, or deleted -- used to clamp to
        /// index 0 via <c>Mathf.Max(0, IndexOf(...))</c>. That made the control a <b>dead end</b>:
        /// it displayed the first category, which is not the category the clip is in, and
        /// picking that first category to correct it did nothing, because the popup's value had
        /// not changed. The user saw a wrong value they could not fix. Appending an explicit
        /// placeholder instead makes the orphaned state visible, names the missing category so
        /// it is diagnosable, and leaves every real category one click away.
        /// </para>
        /// </summary>
        public static string[] CategoryPopupOptions(string[] categoryNames, string current,
            out int index)
        {
            var names = categoryNames ?? new string[0];
            index = Array.IndexOf(names, current);

            if (index >= 0)
            {
                return names;
            }

            var withPlaceholder = new string[names.Length + 1];
            Array.Copy(names, withPlaceholder, names.Length);
            withPlaceholder[names.Length] = string.IsNullOrEmpty(current)
                ? "(none)"
                : $"(unknown: {current})";

            index = names.Length;
            return withPlaceholder;
        }

        /// <summary>
        /// Caption for the bulk-assign button, naming the part of the selection the filter is
        /// hiding.
        ///
        /// <para>
        /// Selection deliberately survives filtering -- typing in the filter box must not
        /// destroy what you already picked. The consequence is that the button can act on rows
        /// that are not on screen: select 12, filter down to 2, and "Set Category (12)" quietly
        /// reassigns 10 rows the user cannot see. The count alone reads as a bug report waiting
        /// to happen; stating the hidden portion makes it a deliberate, visible choice.
        /// </para>
        /// </summary>
        public static string BulkCategoryCaption(int selectedCount, int visibleSelectedCount)
        {
            var hidden = selectedCount - visibleSelectedCount;

            return hidden > 0
                ? $"Set Category ({selectedCount}, {hidden} hidden)"
                : $"Set Category ({selectedCount})";
        }

        /// <summary>Empty string for a healthy row -- the table should not be a wall of icons.</summary>
        public static string StatusIcon(AudioBalanceRow row)
        {
            if (row == null)
            {
                return string.Empty;
            }

            switch (row.Analysis.Status)
            {
                case ClipStatus.Silent:
                    return "silent";
                case ClipStatus.Unanalyzable:
                    return "!";
                default:
                    return row.Gain.IsOutlier ? "outlier" : string.Empty;
            }
        }

        /// <summary>
        /// Assigns <paramref name="category"/> to every supplied row.
        ///
        /// <para>
        /// This one keeps taking the profile: it is an explicit user action, not a render-path
        /// call, and it wraps its writes in <c>Undo.RecordObject</c> / <c>SetDirty</c>.
        /// <c>SettingsFor</c> creating a missing entry here is the intended behaviour.
        /// </para>
        ///
        /// <para>
        /// The CALLER must follow this with a re-analysis, not a re-solve: a category carries
        /// its own <see cref="MeasureMode"/>, so moving clips between categories changes how
        /// they must be MEASURED.
        /// </para>
        /// </summary>
        public static void BulkAssignCategory(IEnumerable<AudioBalanceRow> rows,
            AudioBalanceProfile profile, string category)
        {
            if (rows == null || profile == null || string.IsNullOrEmpty(category))
            {
                return;
            }

            Undo.RecordObject(profile, "Assign Audio Category");

            foreach (var row in rows)
            {
                if (row?.Clip == null)
                {
                    continue;
                }

                var settings = profile.SettingsFor(row.Clip);
                if (settings != null)
                {
                    settings.Category = category;
                }
            }

            EditorUtility.SetDirty(profile);
        }
    }
}
