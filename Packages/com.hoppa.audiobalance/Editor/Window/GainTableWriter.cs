using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>Bakes solved gains into the profile's <see cref="AudioGainTable"/> asset.</summary>
    public static class GainTableWriter
    {
        public static List<AudioGainTable.Entry> BuildEntries(IReadOnlyList<AudioBalanceRow> rows)
        {
            var entries = new List<AudioGainTable.Entry>();
            if (rows == null)
            {
                return entries;
            }

            foreach (var row in rows)
            {
                // A silent or unreadable clip has no meaningful gain. Omitting it means the
                // runtime lookup falls through to unity gain rather than baking in a guess.
                if (row?.Clip == null || row.Analysis.Status != ClipStatus.Ok)
                {
                    continue;
                }

                // Gain.Clip, NOT Gain.Status: ClipStatus.Ok is 0, so an unsolved
                // default(GainResult) reports Ok and a status check here would be vacuous by
                // construction. Analysis and Gain are filled by separate passes, so an Ok
                // measurement does not by itself mean a gain was ever solved -- and the
                // unsolved value is 0 dB, which reads as a deliberate "leave at full volume"
                // rather than as missing data. Omitting the row costs nothing, since the
                // runtime lookup already returns unity gain for an absent clip.
                if (row.Gain.Clip == null)
                {
                    continue;
                }

                entries.Add(new AudioGainTable.Entry
                {
                    Clip = row.Clip,
                    GainDb = row.Gain.FinalGainDb
                });
            }

            // Stable ordering keeps the asset's git diff clean between runs.
            entries.Sort((a, b) => string.CompareOrdinal(a.Clip.name, b.Clip.name));
            return entries;
        }

        /// <summary>
        /// Replaces the assigned table's entries. A <b>pure asset mutation</b>: it marks the
        /// table dirty but does not save.
        ///
        /// <para>
        /// <see cref="AssetDatabase.SaveAssets"/> is deliberately NOT called here. This method is
        /// exercised directly by unit tests, and SaveAssets flushes <i>every</i> dirty asset in
        /// the project -- so a test run would commit unrelated in-flight edits to disk. Saving
        /// belongs to the window's button handler, where it is an explicit user action.
        /// </para>
        /// </summary>
        public static bool Write(AudioBalanceProfile profile, IReadOnlyList<AudioBalanceRow> rows)
        {
            if (profile == null || profile.Table == null)
            {
                return false;
            }

            Undo.RecordObject(profile.Table, "Write Audio Gain Table");
            profile.Table.SetEntries(BuildEntries(rows));
            EditorUtility.SetDirty(profile.Table);

            return true;
        }
    }
}
