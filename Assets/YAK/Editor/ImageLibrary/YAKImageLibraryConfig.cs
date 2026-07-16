using Hoppa.LevelEditor.Core.Editor;
using UnityEngine;

namespace Hoppa.YAK.Editor
{
    // Tuning for the Image Library tool. Holds NO secrets (the API key is resolved
    // at call time by YAKImageApiKey). OutputFolder is project-relative.
    [CreateAssetMenu(menuName = "Hoppa/YAK/Image Library Config")]
    public sealed class YAKImageLibraryConfig : ScriptableObject
    {
        [Header("Ideas")]
        [Tooltip("Plain-text asset, one level idea per line. '#' lines are comments.")]
        public TextAsset IdeasAsset;

        [Header("Prompts")]
        [Tooltip("Plain-text asset holding the style prompts (typically prompts.txt). '[name]' opens a block; an ideas.txt section binds to one with '# @style: name'. '[rules]' is appended to every prompt; '[default]' covers untagged ideas. '{idea}' is replaced with the idea. Overrides Style Preamble below when assigned.")]
        public TextAsset StylePromptAsset;

        [Header("Palette source")]
        [Tooltip("Profile whose wool palette is injected into the prompt.")]
        public GameProfile Profile;
        [Tooltip("Color ids treated as background neutrals and EXCLUDED from the prompt (subject/wool colors only).")]
        public string[] ExcludedNeutralIds = { "Grey", "GreyLight", "GreyDark", "DarkGrey", "White", "Black" };

        [Header("Output")]
        [Tooltip("Project-relative folder for generated PNGs — typically the generator's SourceImageFolder.")]
        public string OutputFolder = "Assets/YAK/SourceImages";

        [Header("OpenAI")]
        [Tooltip("Image model id. gpt-image-1 deprecates 2026-10-23 — change here when migrating.")]
        public string Model = "gpt-image-1";
        public string ImageSize = "1024x1024";
        [Tooltip("Quality tier: low | medium | high. 'high' avoids the soft glow/bloom and gradient backgrounds gpt-image-1 produces at medium (which the converter reads as blur/aura).")]
        public string Quality = "high";
        [Tooltip("Background fill: opaque | transparent | auto. Keep 'opaque' — 'auto' lets gpt-image-1 return transparent PNGs the converter can't separate from the subject.")]
        public string Background = "opaque";

        [Header("Prompt")]
        [Tooltip("The art's native pixel grid, substituted into '{grid}' in the prompt. MUST equal the grid size you convert at — a mismatch makes the converter resample the art pixels and the blocks smear.")]
        [Min(8)] public int PixelGridSize = 40;

        [Tooltip("Fallback style used only when no Style Prompt Asset is assigned.")]
        [TextArea(3, 6)]
        public string StylePreamble = YAKImageLibraryCore.DefaultStylePreamble;

        [Header("Safety")]
        [Tooltip("Hard cap on images generated in a single run. A full boss-brief batch is 100 (5 prompts × 20 ideas).")]
        [Min(1)] public int MaxImagesPerRun = 100;
        [Tooltip("USD per image for the chosen size/quality — used only for the pre-run cost estimate dialog. ~0.19 for gpt-image-1 high @ 1024; ~0.07 at medium.")]
        public float EstimatedUsdPerImage = 0.19f;
    }
}
