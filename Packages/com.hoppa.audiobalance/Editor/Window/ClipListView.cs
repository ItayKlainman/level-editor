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
        /// <see cref="AudioBalanceSession.Analyze"/>), so a miss should not be reachable at all
        /// -- enrolment already happened in the window's <c>RunAnalysis</c>, inside an Undo
        /// scope. Resolving with <c>SettingsFor</c> "just in case" would trade an impossible
        /// miss for a real hazard: an asset write during a repaint. A missing clip is simply
        /// absent from the map, and the row draws without its category/trim controls.
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
