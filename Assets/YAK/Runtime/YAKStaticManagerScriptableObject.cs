using System;
using System.Collections.Generic;
using UnityEngine;

namespace YAK.Gamelogic
{
    // Editor-side mirror of the game's StaticManager ScriptableObject. Same
    // namespace + class name so a YAML copy of StaticManager.asset from the game
    // project deserializes against this type. Game-only members (Initialize,
    // OutlineColorHash, GetColorTypeHelper) are intentionally stripped — only
    // the serialized data fields need to match.
    //
    // Source of truth for all colors in the editor: paint, palette UI, export,
    // import, and validation swatches all resolve through this asset (via
    // YAKStaticManagerColorSource).
    [CreateAssetMenu(fileName = "StaticManager", menuName = "YAK/StaticManager")]
    public class YAKStaticManagerScriptableObject : ScriptableObject
    {
        [Header("Colors")] [SerializeField] private YAKColorTypeHelper[] _colorTypeHelpers;

        public IReadOnlyList<YAKColorTypeHelper> ColorTypeHelpers
            => _colorTypeHelpers ?? Array.Empty<YAKColorTypeHelper>();
    }

    [Serializable]
    public class YAKColorTypeHelper
    {
        public YAKColorType ColorType;
        public Color Color;

        // Game-side material reference. In the editor project this will deserialize
        // as null (the materials live in the game repo); harmless — the editor
        // never reads it.
        public Material PixelMaterial;
    }

    public enum YAKColorType
    {
        None = 0,
        Blue = 1,
        Cyan = 2,
        Yellow = 3,
        Green = 4,
        Magenta = 5,
        Orange = 6,
        Pink = 7,
        Purple = 8,
        Red = 9,
        BlueDark = 10,
        GreenLime = 11,
        Turquoise = 12,
        PurpleBright = 13,
        White = 14,
        Grey = 15,
        DarkGrey = 16,
        Black = 17,
        Brown = 18,
        BrownDark = 19,
        BrownLight = 20,
        Skin = 21,
        GreyLight = 22,
        GreyDark = 23,
        BlueOcean = 24,
        BlueRoyal = 25,
        BlueSky = 26,
        GreenDark = 27,
        GreenGrass = 28,
        Gold = 29,
        OrangeRed = 30,
        OrangeLight = 31,
        PurpleDark = 32,
        TurquoiseLight = 33,
        PinkDark = 34,
        YellowPale = 35,
        BrownVeryDark = 36
    }
}
