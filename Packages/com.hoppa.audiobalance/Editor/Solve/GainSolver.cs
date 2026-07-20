using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// Turns measurements into bakeable gains.
    ///
    ///   raw   = (anchorLufs + categoryOffset + trim) - measuredLufs
    ///   final = raw - max(raw over analyzable clips)
    ///
    /// The second line is the headroom pass. AudioSource.volume is hard-capped at 1.0, so a
    /// clip needing +6 dB simply cannot receive it -- the request silently does nothing and
    /// the table no longer describes what you hear. Subtracting the maximum pins the clip
    /// that needed the MOST gain -- the quietest one relative to its target -- at exactly
    /// 0 dB; every other clip is attenuated, and the clip loudest relative to its target is
    /// attenuated most. Relative spacing is preserved exactly and clipping becomes
    /// structurally impossible rather than merely warned about. The cost is that overall
    /// output is quieter, which is compensated once on the master mixer.
    ///
    /// Note that <c>anchorLufs</c> appears in every raw gain and therefore
    /// cancels exactly in the subtraction: FinalGainDb is provably independent of the
    /// anchor's measured loudness. Relative placement between clips comes from the category
    /// offsets alone. The anchor's only live effect here is on IsOutlier, which is computed
    /// from the raw (pre-headroom) gain.
    /// </summary>
    public static class GainSolver
    {
        /// <summary>Beyond this, a clip is almost always broken rather than genuinely quiet.</summary>
        public const float OutlierThresholdDb = 12f;

        public static IReadOnlyList<GainResult> Solve(
            IReadOnlyList<ClipAnalysis> analyses,
            float anchorLufs,
            Func<AudioClip, float> categoryOffsetDb,
            Func<AudioClip, float> trimDb)
        {
            var results = new List<GainResult>();
            if (analyses == null || analyses.Count == 0)
            {
                return results;
            }

            var raw = new float[analyses.Count];
            var maxRaw = float.NegativeInfinity;

            for (var i = 0; i < analyses.Count; i++)
            {
                var analysis = analyses[i];
                if (analysis.Status != ClipStatus.Ok)
                {
                    continue;
                }

                var offset = categoryOffsetDb?.Invoke(analysis.Clip) ?? 0f;
                var trim = trimDb?.Invoke(analysis.Clip) ?? 0f;

                raw[i] = anchorLufs + offset + trim - analysis.Lufs;

                if (raw[i] > maxRaw)
                {
                    maxRaw = raw[i];
                }
            }

            // No analyzable clip means nothing defines the ceiling; leave every gain at unity.
            var headroomOffset = float.IsNegativeInfinity(maxRaw) ? 0f : maxRaw;

            for (var i = 0; i < analyses.Count; i++)
            {
                var analysis = analyses[i];

                if (analysis.Status != ClipStatus.Ok)
                {
                    results.Add(new GainResult(analysis.Clip, analysis.Status, 0f, 0f, false));
                    continue;
                }

                results.Add(new GainResult(
                    analysis.Clip,
                    analysis.Status,
                    raw[i],
                    raw[i] - headroomOffset,
                    Mathf.Abs(raw[i]) > OutlierThresholdDb));
            }

            return results;
        }
    }
}
