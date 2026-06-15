using UnityEngine;

namespace Hoppa.YAK.Editor
{
    // Tuning for YAKLevelGenerator. Separate asset referenced from the generator
    // asset / GameProfile._generatorConfig (mirrors the YarnTwist split).
    [CreateAssetMenu(menuName = "Hoppa/YAK/Generator/YAK Generator Config")]
    public sealed class YAKGeneratorConfig : ScriptableObject
    {
        [Header("Image source (the 'library' the generator draws from)")]
        [Tooltip("When true, the generator builds the grid from a source image picked from SourceImageFolder. When false (or the folder is empty), it falls back to a procedural grid.")]
        public bool UseImageSource = true;
        [Tooltip("Folder of source images. 'Assets/…' is loaded via the AssetDatabase; an absolute path is read from disk. The image is picked deterministically from the seed.\n\nNOTE: the image-AI HTTP step is deferred — for now drop images here (the library flywheel). A future IImageSource can fetch from an AI service into this same flow.")]
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
        [Tooltip("Conveyor (belt slot) count stamped on generated levels.")]
        [Min(1)] public int ConveyorCount = 5;
    }
}
