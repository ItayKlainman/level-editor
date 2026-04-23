using Hoppa.LevelEditor.Core.Editor;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    [CreateAssetMenu(menuName = "Hoppa/Yarn Twist/Yarn Sort Exporter")]
    public sealed class YarnSortExporter : ScriptableObjectExporter
    {
        protected override LevelAsset CreateLevelAssetInstance() => CreateInstance<YarnLevelAsset>();
    }
}
