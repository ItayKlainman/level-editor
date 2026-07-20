namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// Which column the clip table is ordered by. Explicit values because this is drawn as an
    /// EnumPopup and persisted in window state -- reordering the members must not silently
    /// change what a serialized selection means.
    /// </summary>
    public enum ClipSortMode
    {
        Name = 0,
        Loudness = 1,
        Gain = 2,
        Category = 3
    }
}
