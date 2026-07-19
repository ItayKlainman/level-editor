using System;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// A group of clips that share an intended level relative to the anchor. The offset is
    /// what stops everything collapsing to the same loudness: SFX are meant to sit above the
    /// music bed, UI blips below it.
    /// </summary>
    [Serializable]
    public sealed class AudioCategory
    {
        public string Name = "SFX";
        public float OffsetDb;
        public MeasureMode Mode = MeasureMode.MomentaryMax;
    }
}
