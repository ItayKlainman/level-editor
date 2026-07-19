using UnityEngine;

namespace Hoppa.AudioBalance
{
    /// <summary>Play-sound helpers that fold the baked gain into AudioSource.volume.</summary>
    public static class AudioGainTableExtensions
    {
        public static void PlayBalanced(this AudioSource source, AudioClip clip,
            AudioGainTable table, float userVolume = 1f)
        {
            if (source == null || clip == null)
            {
                return;
            }

            source.clip = clip;
            source.volume = Resolve(table, clip, userVolume);
            source.Play();
        }

        public static void PlayOneShotBalanced(this AudioSource source, AudioClip clip,
            AudioGainTable table, float userVolume = 1f)
        {
            if (source == null || clip == null)
            {
                return;
            }

            source.PlayOneShot(clip, Resolve(table, clip, userVolume));
        }

        private static float Resolve(AudioGainTable table, AudioClip clip, float userVolume)
        {
            var gain = table != null ? table.GetGain(clip) : 1f;
            return Mathf.Clamp01(gain * userVolume);
        }
    }
}
