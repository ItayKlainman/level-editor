namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// A loudness measurement. Silence has no defined loudness, so it is reported as a
    /// distinct state rather than as -Infinity, which would become NaN once a gain is solved.
    /// </summary>
    public readonly struct LoudnessResult
    {
        public readonly bool IsSilent;
        public readonly float Lufs;

        private LoudnessResult(bool isSilent, float lufs)
        {
            IsSilent = isSilent;
            Lufs = lufs;
        }

        public static LoudnessResult Silent => new LoudnessResult(true, 0f);

        public static LoudnessResult At(float lufs) => new LoudnessResult(false, lufs);
    }
}
