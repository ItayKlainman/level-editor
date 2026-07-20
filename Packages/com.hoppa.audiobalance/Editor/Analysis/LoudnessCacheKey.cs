namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// Identifies one <see cref="LoudnessCache"/> entry: which asset, under which
    /// <see cref="MeasureMode"/> (the same clip measured Integrated and MomentaryMax are two
    /// different answers, so the mode is part of the key, not an afterthought).
    ///
    /// <para>
    /// Production code determining a real clip's key MUST go through
    /// <see cref="LoudnessCache.KeyFor"/> (or its pure file-path sibling
    /// <see cref="LoudnessCache.KeyForPaths"/>) -- that is the ONLY place that derives
    /// <see cref="Ticks"/>, and it folds in the <c>.meta</c> file's timestamp as well as the
    /// asset's, because what is actually measured is the decoded <c>AudioClip</c>, which depends
    /// on <c>.meta</c> importer settings (Force To Mono, Quality, Sample Rate Override, ...) as
    /// much as it does the source bytes. A caller that assembles a key from a hand-stat'd asset
    /// timestamp alone reintroduces the exact defect that made this type exist.
    /// </para>
    ///
    /// <para>
    /// The public constructor exists so tests can build synthetic keys without touching real
    /// files or the AssetDatabase.
    /// </para>
    /// </summary>
    public readonly struct LoudnessCacheKey
    {
        public readonly string Guid;
        public readonly long Length;
        public readonly long Ticks;
        public readonly MeasureMode Mode;

        public LoudnessCacheKey(string guid, long length, long ticks, MeasureMode mode)
        {
            Guid = guid;
            Length = length;
            Ticks = ticks;
            Mode = mode;
        }

        /// <summary>
        /// False for a key with no addressable asset -- e.g. a procedural clip built in a test,
        /// which has no asset path and therefore no guid. <see cref="LoudnessCache"/> refuses an
        /// invalid key in both <see cref="LoudnessCache.TryGet"/> and
        /// <see cref="LoudnessCache.Put"/>, so such a clip simply bypasses the cache instead of
        /// colliding on an empty guid.
        /// </summary>
        public bool IsValid => !string.IsNullOrEmpty(Guid);
    }
}
