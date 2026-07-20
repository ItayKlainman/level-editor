using System;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// One cached measurement. Status is stored as an int because JsonUtility handles enums
    /// inconsistently across Unity versions.
    /// </summary>
    [Serializable]
    public sealed class CachedLoudness
    {
        public int Status;
        public float Lufs;
        public float PeakDb;
    }
}
