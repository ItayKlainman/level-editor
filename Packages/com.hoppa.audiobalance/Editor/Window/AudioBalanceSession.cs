using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// The window's model. Analysis and solving live here rather than in the EditorWindow so
    /// they can be tested without opening any UI.
    /// </summary>
    public sealed class AudioBalanceSession
    {
        /// <summary>
        /// Used when no usable anchor exists. The value is arithmetically irrelevant to
        /// FinalGainDb -- the anchor term cancels in GainSolver's headroom subtraction -- but
        /// it does feed RawGainDb, and therefore the outlier check. We suppress the outlier
        /// flag outright in that case (see <see cref="Resolve"/>), so this constant only keeps
        /// RawGainDb in a sane range for the readout rather than parking it 20 dB off. Note
        /// that the suppression, not this sentinel, is what makes the behaviour correct: a
        /// clip 40 dB from the sentinel would still trip the threshold without it.
        /// </summary>
        private const float NoAnchorReferenceLufs = -23f;

        private readonly List<AudioBalanceRow> _rows = new List<AudioBalanceRow>();

        public IReadOnlyList<AudioBalanceRow> Rows => _rows;

        public float AnchorLufs { get; private set; }

        public ClipStatus AnchorStatus { get; private set; } = ClipStatus.Unanalyzable;

        /// <summary>
        /// Measures the anchor and every profile clip, then solves gains.
        ///
        /// <para>
        /// This is the correct entry point for ANY edit that can change how a clip is
        /// measured -- a category assignment, a bulk assign, or a category's MeasureMode.
        /// It is cheap to call: <see cref="LoudnessCacheKey"/> carries the mode, so a clip
        /// whose effective mode did not change is a cache hit and is never re-decoded.
        /// </para>
        ///
        /// <para>
        /// <paramref name="onProgress"/> is invoked before each clip is measured with
        /// (clip, zero-based index, total) and returns true to cancel. Returns false if the
        /// run was cancelled, true if it completed. On cancel, rows measured so far are kept
        /// and still solved, so the table shows partial-but-consistent results rather than
        /// blanking out.
        /// </para>
        /// </summary>
        public bool Analyze(AudioBalanceProfile profile, LoudnessCache cache,
            Func<AudioClip, int, int, bool> onProgress = null)
        {
            _rows.Clear();
            AnchorLufs = 0f;
            AnchorStatus = ClipStatus.Unanalyzable;

            if (profile == null)
            {
                return true;
            }

            var anchorMeasured = false;
            var anchorAnalysis = default(ClipAnalysis);

            if (profile.Anchor != null)
            {
                anchorAnalysis = LoudnessAnalyzer.Analyze(
                    profile.Anchor, ModeFor(profile, profile.Anchor), cache);

                anchorMeasured = true;
                AnchorStatus = anchorAnalysis.Status;
                AnchorLufs = anchorAnalysis.Lufs;
            }

            // Read-only snapshot. This method deliberately does NOT enrol anything: it uses
            // FindSettings, never SettingsFor, so measuring can never append a ClipSettings to
            // the asset outside an Undo scope. Enrolment -- including the anchor's -- is the
            // window's job, done explicitly inside Undo in RunAnalysis.
            var pending = new List<AudioClip>();
            foreach (var settings in profile.Clips)
            {
                if (settings?.Clip != null)
                {
                    pending.Add(settings.Clip);
                }
            }

            var completed = true;

            for (var i = 0; i < pending.Count; i++)
            {
                var clip = pending[i];

                if (onProgress != null && onProgress(clip, i, pending.Count))
                {
                    completed = false;
                    break;
                }

                // The anchor is normally also an enrolled clip, so it appears here too. Reuse
                // the measurement rather than decoding it a second time -- free when a cache is
                // present, a full second decode of (typically) the longest clip when it is not.
                var analysis = anchorMeasured && clip == profile.Anchor
                    ? anchorAnalysis
                    : LoudnessAnalyzer.Analyze(clip, ModeFor(profile, clip), cache);

                _rows.Add(new AudioBalanceRow
                {
                    Clip = clip,
                    Analysis = analysis
                });
            }

            Resolve(profile);
            return completed;
        }

        /// <summary>
        /// Re-solves gains from the existing measurements.
        ///
        /// <para>
        /// Correct for the trim slider ONLY. A trim moves the target and cannot change how
        /// the clip must be measured. A category edit is a different animal: a category
        /// carries its own <see cref="MeasureMode"/>, so changing a clip's category (or a
        /// category's mode) changes the measurement itself, which this method cannot do --
        /// it would silently keep the old-mode number and bake a wrong gain. Route those
        /// edits through <see cref="Analyze"/> instead.
        /// </para>
        /// </summary>
        public void Resolve(AudioBalanceProfile profile)
        {
            if (profile == null || _rows.Count == 0)
            {
                return;
            }

            var analyses = new List<ClipAnalysis>(_rows.Count);
            foreach (var row in _rows)
            {
                analyses.Add(row.Analysis);
            }

            var anchorOk = AnchorStatus == ClipStatus.Ok;

            // Non-mutating lookups, NOT profile.OffsetDbFor/TrimDbFor: those route through
            // SettingsFor, which enrols on a miss. Solving is a read.
            var solved = GainSolver.Solve(
                analyses,
                anchorOk ? AnchorLufs : NoAnchorReferenceLufs,
                clip => OffsetDbFor(profile, clip),
                clip => TrimDbFor(profile, clip));

            for (var i = 0; i < _rows.Count && i < solved.Count; i++)
            {
                var result = solved[i];

                // With no usable anchor there is no reference, so "this clip is 12 dB from
                // its target" is not a judgement we are entitled to make. Suppress rather
                // than mislead: an unexplained wall of outlier markers on a fresh profile
                // reads as a broken tool.
                if (!anchorOk && result.IsOutlier)
                {
                    result = new GainResult(result.Clip, result.Status,
                        result.RawGainDb, result.FinalGainDb, false);
                }

                _rows[i].Gain = result;
            }
        }

        /// <summary>
        /// The clip's effective measure mode WITHOUT enrolling it. An un-enrolled clip has no
        /// category, so it falls back to <see cref="MeasureMode.Integrated"/> -- the same
        /// answer <c>AudioBalanceProfile.ModeFor</c> gives, minus the write.
        /// </summary>
        private static MeasureMode ModeFor(AudioBalanceProfile profile, AudioClip clip)
        {
            var settings = profile.FindSettings(clip);
            if (settings == null)
            {
                return MeasureMode.Integrated;
            }

            return profile.FindCategory(settings.Category)?.Mode ?? MeasureMode.Integrated;
        }

        private static float OffsetDbFor(AudioBalanceProfile profile, AudioClip clip)
        {
            var settings = profile.FindSettings(clip);
            if (settings == null)
            {
                return 0f;
            }

            return profile.FindCategory(settings.Category)?.OffsetDb ?? 0f;
        }

        private static float TrimDbFor(AudioBalanceProfile profile, AudioClip clip)
        {
            return profile.FindSettings(clip)?.TrimDb ?? 0f;
        }

        public void Clear()
        {
            _rows.Clear();
            AnchorLufs = 0f;
            AnchorStatus = ClipStatus.Unanalyzable;
        }
    }
}
