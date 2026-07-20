using System;

namespace Hoppa.AudioBalance.Editor.Tests
{
    /// <summary>
    /// Generates interleaved test signals so the DSP tests need no committed audio fixtures.
    /// </summary>
    public static class SignalFactory
    {
        /// <summary>
        /// A 1 kHz sine whose PEAK amplitude is the given dBFS value, written to every channel.
        /// A stereo sine built this way measures exactly that dBFS value in LUFS: the sine's
        /// -3.01 dB crest factor and the +3.01 dB of summing two equal channels cancel.
        /// </summary>
        public static float[] Sine(double peakDbfs, double seconds, int channels, int sampleRate,
            double frequency = 1000.0)
        {
            var amplitude = Math.Pow(10.0, peakDbfs / 20.0);
            var frames = (int)Math.Round(seconds * sampleRate);
            var data = new float[frames * channels];

            for (var frame = 0; frame < frames; frame++)
            {
                var value = (float)(amplitude * Math.Sin(2.0 * Math.PI * frequency * frame / sampleRate));
                for (var ch = 0; ch < channels; ch++)
                {
                    data[frame * channels + ch] = value;
                }
            }

            return data;
        }

        public static float[] Silence(double seconds, int channels, int sampleRate)
        {
            return new float[(int)Math.Round(seconds * sampleRate) * channels];
        }

        public static float[] Concat(params float[][] parts)
        {
            var length = 0;
            foreach (var part in parts)
            {
                length += part.Length;
            }

            var result = new float[length];
            var offset = 0;
            foreach (var part in parts)
            {
                Array.Copy(part, 0, result, offset, part.Length);
                offset += part.Length;
            }

            return result;
        }
    }
}
