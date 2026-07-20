using UnityEngine;

namespace Hoppa.AudioBalance
{
    /// <summary>Decibel/linear conversions shared by the editor tooling and the runtime lookup.</summary>
    public static class AudioGainMath
    {
        /// <summary>Floor reported instead of negative infinity for a zero-amplitude signal.</summary>
        public const float MinDb = -80f;

        public static float LinearFromDb(float db)
        {
            return Mathf.Pow(10f, db / 20f);
        }

        public static float DbFromLinear(float linear)
        {
            return linear <= 0f ? MinDb : 20f * Mathf.Log10(linear);
        }
    }
}
