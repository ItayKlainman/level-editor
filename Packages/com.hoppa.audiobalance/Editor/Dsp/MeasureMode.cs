namespace Hoppa.AudioBalance.Editor
{
    /// <summary>How a category's clips are measured.</summary>
    public enum MeasureMode
    {
        /// <summary>Full gated BS.1770 integrated loudness. Correct for music beds.</summary>
        Integrated = 0,

        /// <summary>
        /// Loudest 400 ms window, ungated -- the standard's "momentary" loudness. Correct
        /// for short one-shots: it lands on the attack, where integrated loudness is pulled
        /// down by the blocks straddling the decay into silence.
        /// </summary>
        MomentaryMax = 1
    }
}
