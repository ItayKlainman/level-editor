using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>Decode -> measure -> cache. The single entry point the window calls per clip.</summary>
    public static class LoudnessAnalyzer
    {
        /// <summary>
        /// Measures <paramref name="clip"/> under <paramref name="mode"/>, consulting and
        /// populating <paramref name="cache"/> (nullable -- a null cache just means "always
        /// measure"). Cache identity comes entirely from <see cref="LoudnessCache.KeyFor"/>;
        /// this method does not re-derive any part of it (see the class doc on
        /// <see cref="LoudnessCache"/> for why that duplication is a defect, not a convenience).
        ///
        /// <para>
        /// A cache hit is rehydrated, not returned verbatim: <see cref="CachedLoudness"/> has no
        /// <c>Reason</c> field, so a <see cref="ClipStatus.Silent"/> hit reconstructs
        /// <see cref="ClipAnalysis.SilentReason"/> itself -- otherwise a freshly-measured silent
        /// clip would report the reason on first run and nothing on the next window open. Any
        /// <see cref="CachedLoudness.Status"/> outside {<see cref="ClipStatus.Ok"/>,
        /// <see cref="ClipStatus.Silent"/>} -- e.g. a hand-edited or forward-version entry in the
        /// JSON under <c>Library/</c> -- is treated as a miss and falls through to a real
        /// re-measurement (which also re-stores the corrected value) rather than being cast into
        /// a <see cref="ClipStatus"/> value no downstream <c>switch</c> handles.
        /// </para>
        /// </summary>
        public static ClipAnalysis Analyze(AudioClip clip, MeasureMode mode, LoudnessCache cache)
        {
            if (clip == null)
            {
                return ClipAnalysis.Unanalyzable(null, "clip is null");
            }

            // LoudnessCache.KeyFor is the ONLY place that derives the cache identity (guid,
            // length, ticks, mode) -- it folds in the .meta timestamp as well as the asset's.
            // Do not re-stat the asset file here; that duplication is exactly what produced the
            // round-2 plan defect this section once contained.
            var key = LoudnessCache.KeyFor(clip, mode);

            if (cache != null && key.IsValid && cache.TryGet(key, out var cached))
            {
                var cachedStatus = (ClipStatus)cached.Status;
                if (cachedStatus == ClipStatus.Ok || cachedStatus == ClipStatus.Silent)
                {
                    var reason = cachedStatus == ClipStatus.Silent ? ClipAnalysis.SilentReason : null;
                    return new ClipAnalysis(clip, cachedStatus, cached.Lufs, cached.PeakDb, reason);
                }

                // Only Ok/Silent are ever Put (ShouldCache refuses Unanalyzable, and there are no
                // other members), so anything else here is a corrupt or forward-version entry in
                // the hand-editable Library/ JSON. Fall through and re-measure/re-store below.
            }

            var analysis = Measure(clip, mode);

            if (cache != null && key.IsValid && ShouldCache(analysis.Status))
            {
                cache.Put(key, new CachedLoudness
                {
                    Status = (int)analysis.Status,
                    Lufs = analysis.Lufs,
                    PeakDb = analysis.PeakDb
                });
            }

            return analysis;
        }

        /// <summary>
        /// Whether an analysis with <paramref name="status"/> is safe to persist in the cache.
        /// <see cref="ClipStatus.Unanalyzable"/> is excluded for two independent reasons: (1) its
        /// most common cause, <see cref="ClipSampleReader.LoadPendingError"/>, is explicitly
        /// transient -- the error text itself instructs "re-run the analysis once the clip
        /// finishes loading," but re-running does not change the cache key (guid, length, ticks,
        /// mode), so a cached Unanalyzable would be served back forever, making that promised
        /// remedy impossible; (2) <see cref="CachedLoudness"/> has no <c>Reason</c> field, so a
        /// cache hit would silently drop WHY the clip failed -- a UI showing the reason on first
        /// run would show nothing on the next window open, and a later stage that treats a
        /// missing reason as "outlier" (rather than "unreadable") would misdiagnose it entirely.
        /// <see cref="ClipStatus.Ok"/> and <see cref="ClipStatus.Silent"/> are both genuine,
        /// stable measurements of the clip's actual content and are safe to cache.
        /// </summary>
        public static bool ShouldCache(ClipStatus status)
        {
            return status != ClipStatus.Unanalyzable;
        }

        /// <summary>
        /// All distinct <see cref="AudioClip"/> assets under <paramref name="projectRelativeFolders"/>,
        /// sorted by name, tiebroken by asset path when two clips share a name (e.g. "click" in
        /// two different folders) -- <see cref="List{T}.Sort"/> is not a stable sort, so a
        /// name-only comparator gives such ties a nondeterministic relative order between runs,
        /// which churns the generated gain table's diff for no reason. Folders that are
        /// null/empty or fail <see cref="AssetDatabase.IsValidFolder"/> are silently skipped
        /// rather than throwing -- a stale folder reference in a profile asset should degrade to
        /// "search fewer places," not fault the whole analysis pass.
        /// </summary>
        public static List<AudioClip> FindClips(IEnumerable<string> projectRelativeFolders)
        {
            var clips = new List<AudioClip>();
            if (projectRelativeFolders == null)
            {
                return clips;
            }

            var valid = new List<string>();
            foreach (var folder in projectRelativeFolders)
            {
                if (!string.IsNullOrEmpty(folder) && AssetDatabase.IsValidFolder(folder))
                {
                    valid.Add(folder);
                }
            }

            if (valid.Count == 0)
            {
                return clips;
            }

            var seen = new HashSet<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", valid.ToArray()))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!seen.Add(path))
                {
                    continue;
                }

                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip != null)
                {
                    clips.Add(clip);
                }
            }

            clips.Sort((a, b) =>
            {
                var byName = string.CompareOrdinal(a.name, b.name);
                return byName != 0
                    ? byName
                    : string.CompareOrdinal(AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b));
            });
            return clips;
        }

        /// <summary>
        /// Drops every <paramref name="cache"/> entry whose guid no longer resolves to an asset
        /// path -- a clip that was deleted or moved out from under its guid since it was last
        /// measured. Deliberately deferred out of <see cref="LoudnessCache"/> itself (see its
        /// class doc): the resolvability check is an <c>AssetDatabase</c> policy call, which
        /// belongs to this Editor-facing orchestration layer, not to the storage class. Returns
        /// the number of guids pruned (not the number of entries -- a guid measured under both
        /// <see cref="MeasureMode"/>s counts once here even though it removes two entries).
        /// Does not call <see cref="LoudnessCache.Save"/> -- the caller decides when to persist,
        /// so a prune can be folded into whatever save already follows a full analysis pass.
        /// A null <paramref name="cache"/> is a no-op returning 0.
        /// </summary>
        public static int PruneMissingClips(LoudnessCache cache)
        {
            if (cache == null)
            {
                return 0;
            }

            var stale = new List<string>();
            foreach (var guid in cache.Guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path))
                {
                    stale.Add(guid);
                }
            }

            foreach (var guid in stale)
            {
                cache.RemoveByGuid(guid);
            }

            return stale.Count;
        }

        private static ClipAnalysis Measure(AudioClip clip, MeasureMode mode)
        {
            if (!ClipSampleReader.TryRead(clip, out var samples, out var error))
            {
                return ClipAnalysis.Unanalyzable(clip, error);
            }

            var loudness = LufsMeter.Measure(samples, clip.channels, clip.frequency, mode);
            if (loudness.IsSilent)
            {
                return ClipAnalysis.Silent(clip);
            }

            return ClipAnalysis.Ok(
                clip,
                loudness.Lufs,
                PeakMeter.SamplePeakDb(samples));
        }
    }
}
