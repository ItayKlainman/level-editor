using Hoppa.LevelEditor.Core.Editor;
using UnityEngine;

namespace Hoppa.LevelEditor.Demo.Editor
{
    [CreateAssetMenu(menuName = "Hoppa/Demo/Demo Level Exporter")]
    public sealed class DemoScriptableObjectExporter : ScriptableObjectExporter
    {
        protected override LevelAsset CreateLevelAssetInstance() =>
            CreateInstance<DemoLevelAsset>();
    }
}
