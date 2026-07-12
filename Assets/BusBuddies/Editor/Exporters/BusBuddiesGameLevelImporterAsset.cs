using System;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // Plugs BusBuddiesGameLevelImporter into the editor's Open flow. Assigned to
    // BusBuddiesProfile._importers so a designer can Open a game level file
    // (Assets/_BUB/Resources/Configs/Levels/level_N.json) directly — the window
    // auto-detects the game schema, imports it (picture + editable bus queue), and
    // Save round-trips back through BusBuddiesGameLevelExporter.
    [CreateAssetMenu(menuName = "Hoppa/BusBuddies/Game Level Importer")]
    public sealed class BusBuddiesGameLevelImporterAsset : LevelImporterAsset
    {
        public override string Name => "BusBuddiesGameLevel";

        // Recognize the game's LevelConfig schema by its distinctive keys. Must never
        // throw on unrelated / editor-native JSON — returns false instead.
        public override bool CanImport(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                var root = JObject.Parse(json);
                return root["PixelColors"] != null
                    && root["Width"] != null
                    && root["BusColumnConfigs"] != null;
            }
            catch { return false; }
        }

        public override LevelDocument Import(string json, CellTypeRegistry registry)
            => BusBuddiesGameLevelImporter.Import(json).Document;
    }
}
