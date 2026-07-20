using System;
using System.Collections.Generic;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// ITU-R BS.1770-4 loudness measurement. Pure C# with no UnityEngine dependency so it
    /// runs in unit tests against generated signals.
    /// </summary>
    public static class LufsMeter
    {
        public const float AbsoluteGateLufs = -70f;
        public const float RelativeGateLu = -10f;

        private const double LoudnessOffset = -0.691;
        private const double BlockSeconds = 0.4;
        private const double StepSeconds = 0.1;
        private const double MomentarySeconds = 0.4;

        public static LoudnessResult MeasureIntegrated(float[] interleaved, int channels, int sampleRate)
        {
            var blocks = ComputeBlockPowers(interleaved, channels, sampleRate, BlockSeconds, StepSeconds);
            if (blocks.Count == 0)
            {
                return LoudnessResult.Silent;
            }

            var weights = ChannelWeights(channels);

            var aboveAbsolute = new List<double[]>();
            foreach (var block in blocks)
            {
                if (BlockLoudness(block, weights) > AbsoluteGateLufs)
                {
                    aboveAbsolute.Add(block);
                }
            }

            if (aboveAbsolute.Count == 0)
            {
                return LoudnessResult.Silent;
            }

            var relativeGate = BlockLoudness(MeanPerChannel(aboveAbsolute, channels), weights) + RelativeGateLu;

            var kept = new List<double[]>();
            foreach (var block in aboveAbsolute)
            {
                if (BlockLoudness(block, weights) > relativeGate)
                {
                    kept.Add(block);
                }
            }

            if (kept.Count == 0)
            {
                return LoudnessResult.Silent;
            }

            var loudness = BlockLoudness(MeanPerChannel(kept, channels), weights);
            return double.IsNegativeInfinity(loudness)
                ? LoudnessResult.Silent
                : LoudnessResult.At((float)loudness);
        }

        public static LoudnessResult Measure(float[] interleaved, int channels, int sampleRate,
            MeasureMode mode)
        {
            return mode == MeasureMode.MomentaryMax
                ? MeasureMomentaryMax(interleaved, channels, sampleRate)
                : MeasureIntegrated(interleaved, channels, sampleRate);
        }

        /// <summary>
        /// Loudest 400 ms window, ungated. For a clip shorter than the window,
        /// ComputeBlockPowers collapses to a single block over the whole clip -- exactly the
        /// desired behaviour for the short SFX this mode exists to serve.
        ///
        /// The window is 400 ms, not 3 s, for a measured reason: on a one-shot with a long
        /// quiet tail a 3 s window averages the attack with the silence and reads BELOW
        /// integrated loudness, which is the opposite of this mode's purpose.
        /// </summary>
        public static LoudnessResult MeasureMomentaryMax(float[] interleaved, int channels, int sampleRate)
        {
            var blocks = ComputeBlockPowers(interleaved, channels, sampleRate, MomentarySeconds, StepSeconds);
            if (blocks.Count == 0)
            {
                return LoudnessResult.Silent;
            }

            var weights = ChannelWeights(channels);
            var max = double.NegativeInfinity;

            foreach (var block in blocks)
            {
                var loudness = BlockLoudness(block, weights);
                if (loudness > max)
                {
                    max = loudness;
                }
            }

            return double.IsNegativeInfinity(max) || max <= AbsoluteGateLufs
                ? LoudnessResult.Silent
                : LoudnessResult.At((float)max);
        }

        /// <summary>
        /// K-weights each channel, then returns the per-channel mean square of every block.
        /// A signal shorter than one block yields a single block spanning the whole signal --
        /// without this, short SFX produce no blocks at all.
        /// </summary>
        internal static List<double[]> ComputeBlockPowers(float[] interleaved, int channels,
            int sampleRate, double blockSeconds, double stepSeconds)
        {
            var blocks = new List<double[]>();
            if (interleaved == null || interleaved.Length == 0 || channels <= 0 || sampleRate <= 0)
            {
                return blocks;
            }

            var frames = interleaved.Length / channels;
            if (frames == 0)
            {
                return blocks;
            }

            var filtered = new double[channels][];
            var shelf = KWeighting.HighShelf(sampleRate);
            var pass = KWeighting.HighPass(sampleRate);

            for (var ch = 0; ch < channels; ch++)
            {
                var channelData = new double[frames];
                for (var frame = 0; frame < frames; frame++)
                {
                    channelData[frame] = interleaved[frame * channels + ch];
                }

                KWeighting.ApplyInPlace(channelData, shelf);
                KWeighting.ApplyInPlace(channelData, pass);
                filtered[ch] = channelData;
            }

            var blockFrames = (int)Math.Round(blockSeconds * sampleRate);
            var stepFrames = Math.Max(1, (int)Math.Round(stepSeconds * sampleRate));

            if (frames < blockFrames)
            {
                blocks.Add(MeanSquarePerChannel(filtered, 0, frames));
                return blocks;
            }

            for (var start = 0; start + blockFrames <= frames; start += stepFrames)
            {
                blocks.Add(MeanSquarePerChannel(filtered, start, blockFrames));
            }

            return blocks;
        }

        internal static double BlockLoudness(double[] meanSquares, double[] weights)
        {
            var sum = 0.0;
            for (var ch = 0; ch < meanSquares.Length; ch++)
            {
                sum += weights[ch] * meanSquares[ch];
            }

            return sum <= 0.0 ? double.NegativeInfinity : LoudnessOffset + 10.0 * Math.Log10(sum);
        }

        /// <summary>
        /// This meter supports mono and stereo only. The `ch &gt;= 3` weighting is a
        /// simplification, not a BS.1770-4 implementation: in the standard 5.1 interleave
        /// (L, R, C, LFE, Ls, Rs) index 3 is the LFE channel, which the standard excludes
        /// from the measurement entirely (G = 0) rather than weighting it 1.41. 5.1 and quad
        /// layouts are NOT correctly weighted by this code.
        /// </summary>
        internal static double[] ChannelWeights(int channels)
        {
            var weights = new double[channels];
            for (var ch = 0; ch < channels; ch++)
            {
                weights[ch] = ch >= 3 ? 1.41 : 1.0;
            }

            return weights;
        }

        private static double[] MeanSquarePerChannel(double[][] filtered, int start, int count)
        {
            var result = new double[filtered.Length];
            for (var ch = 0; ch < filtered.Length; ch++)
            {
                var sum = 0.0;
                var data = filtered[ch];
                for (var i = start; i < start + count; i++)
                {
                    sum += data[i] * data[i];
                }

                result[ch] = sum / count;
            }

            return result;
        }

        private static double[] MeanPerChannel(List<double[]> blocks, int channels)
        {
            var result = new double[channels];
            foreach (var block in blocks)
            {
                for (var ch = 0; ch < channels; ch++)
                {
                    result[ch] += block[ch];
                }
            }

            for (var ch = 0; ch < channels; ch++)
            {
                result[ch] /= blocks.Count;
            }

            return result;
        }
    }
}
