using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // Tuning for BusBuddiesLevelGenerator. Separate asset referenced from
    // GameProfile._generatorConfig (mirrors YAKGeneratorConfig / the YarnTwist split).
    [CreateAssetMenu(menuName = "Hoppa/BusBuddies/Generator/Bus Buddies Generator Config")]
    public sealed class BusBuddiesGeneratorConfig : ScriptableObject
    {
        [Header("Image source (the 'library' the generator draws from)")]
        [Tooltip("When true, the generator builds the grid from a source image picked from SourceImageFolder. When false (or the folder is empty), it falls back to a procedural all-pixel grid.")]
        public bool UseImageSource = true;
        [Tooltip("Folder of source images. 'Assets/…' is loaded via the AssetDatabase; an absolute path is read from disk. The image is picked deterministically from the seed.")]
        public string SourceImageFolder = "";

        [Header("Difficulty target")]
        [Tooltip("Target Attempts-Per-Solve for the generated level.")]
        public float TargetAPS = 3f;
        [Tooltip("A candidate counts as Succeeded only when |measured APS − target| <= this AND it is solvable.")]
        public float ApsTolerance = 0.6f;

        [Header("Analysis")]
        [Tooltip("Monte-Carlo playouts for the generator's gate analysis (kept modest for batch speed).")]
        [Min(1)] public int AnalyzerRollouts = 120;

        [Header("Procedural fallback (no image source)")]
        [Tooltip("Distinct colors used when generating a fallback grid.")]
        [Min(1)] public int FallbackColors = 4;
        [Tooltip("Active Bus Row slot count stamped on generated levels (GameData[\"conveyorCount\"]).")]
        [Min(1)] public int ConveyorCount = 5;
    }
}
