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

            if (profile.Anchor != null)
            {
                var anchorAnalysis = LoudnessAnalyzer.Analyze(
                    profile.Anchor, profile.ModeFor(profile.Anchor), cache);

                AnchorStatus = anchorAnalysis.Status;
                AnchorLufs = anchorAnalysis.Lufs;
            }

            // Snapshot first: profile.ModeFor -> SettingsFor can append to profile.Clips,
            // and mutating the list we are iterating would throw.
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

                _rows.Add(new AudioBalanceRow
                {
                    Clip = clip,
                    Analysis = LoudnessAnalyzer.Analyze(clip, profile.ModeFor(clip), cache)
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

            var solved = GainSolver.Solve(
                analyses,
                anchorOk ? AnchorLufs : NoAnchorReferenceLufs,
                profile.OffsetDbFor,
                profile.TrimDbFor);

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

        public void Clear()
        {
            _rows.Clear();
            AnchorLufs = 0f;
            AnchorStatus = ClipStatus.Unanalyzable;
        }
    }
}
