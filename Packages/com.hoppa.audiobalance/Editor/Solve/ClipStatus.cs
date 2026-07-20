namespace Hoppa.AudioBalance.Editor
{
    public enum ClipStatus
    {
        /// <summary>Measured successfully.</summary>
        Ok = 0,

        /// <summary>All-zero or below the absolute gate -- has no defined loudness.</summary>
        Silent = 1,

        /// <summary>Could not be read, e.g. a Streaming clip whose GetData returns silence.</summary>
        Unanalyzable = 2
    }
}
