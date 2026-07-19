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
    /// the table no longer describes what you hear. Subtracting the maximum pins the loudest
    /// clip at exactly 0 dB, preserves relative spacing exactly, and makes clipping
    /// structurally impossible rather than merely warned about. The cost is that overall
    /// output is quieter, which is compensated once on the master mixer.
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
