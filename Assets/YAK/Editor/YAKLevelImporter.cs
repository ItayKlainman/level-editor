using System;
using System.IO;
using Hoppa.LevelEditor.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hoppa.YAK.Editor
{
    // Parses a YAK LevelConfig JSON file (game-format) and reconstructs a LevelDocument
    // the framework editor can open. Int → colorId resolution goes through the
    // YAKStaticManagerColorSource so the StaticManager remains the single source
    // of truth. PixelColors is row-major bottom-up which matches the framework's
    // GridData layout directly — no inversion needed.
    public static class YAKLevelImporter
    {
        public static LevelDocument Import(string yakJsonPath, YAKStaticManagerColorSource colorSource, string schemaId)
        {
            if (string.IsNullOrEmpty(yakJsonPath))
                throw new ArgumentException("yakJsonPath must be non-empty.");
            if (colorSource == null)
                throw new ArgumentNullException(nameof(colorSource));

            var text = File.ReadAllText(yakJsonPath);
            var root = JObject.Parse(text);

            int width  = root["Width"]?.Value<int>()  ?? 0;
            int height = root["Height"]?.Value<int>() ?? 0;
            int conveyorCount = root["ConveyorCount"]?.Value<int>() ?? 5;

            if (width <= 0 || height <= 0)
                throw new InvalidDataException($"Invalid Width/Height ({width}x{height}) in '{yakJsonPath}'.");

            var grid   = new GridData<ICellData>(width, height);
            var pixels = root["PixelColors"] as JArray;
            int total  = width * height;
            if (pixels == null || pixels.Count != total)
                throw new InvalidDataException(
                    $"PixelColors has {pixels?.Count ?? 0} entries; expected {total} ({width}*{height}).");

            for (int i = 0; i < total; i++)
            {
                int value = pixels[i].Value<int>();
                if (value == 0)
                {
                    grid.Cells[i] = new YAKEmptyCell();
                    continue;
                }

                var colorId = colorSource.GetColorId(value);
                if (!string.IsNullOrEmpty(colorId))
                {
                    grid.Cells[i] = new YAKWoolCell { ColorId = colorId };
                }
                else
                {
                    Debug.LogWarning($"[YAKLevelImporter] No colorId mapping for int {value} (cell index {i}); marking empty.");
                    grid.Cells[i] = new YAKEmptyCell();
                }
            }

            var topData = new YAKTopSectionData();
            var spoolCols = root["SpoolColumnConfigs"] as JArray;
            if (spoolCols != null)
            {
                foreach (var colTok in spoolCols)
                {
                    var col = new YAKSpoolColumn();
                    var configs = colTok?["SpoolConfigs"] as JArray;
                    if (configs != null)
                    {
                        foreach (var s in configs)
                        {
                            int colorInt = s?["ColorType"]?.Value<int>() ?? 0;
                            int cap      = s?["Capacity"]?.Value<int>()  ?? 0;
                            bool hidden  = s?["IsHidden"]?.Value<bool>() ?? false;

                            string colorId = colorSource.GetColorId(colorInt);
                            // Fallback: an empty/None entry shouldn't appear on real spools, but
                            // keep something benign so import doesn't blow up.
                            if (string.IsNullOrEmpty(colorId))
                                colorId = colorSource.GetColorId(1) ?? string.Empty;

                            col.Spools.Add(new YAKSpoolEntry
                            {
                                ColorId  = colorId,
                                Capacity = cap,
                                Hidden   = hidden,
                            });
                        }
                    }
                    topData.Columns.Add(col);
                }
            }

            string fileId = Path.GetFileNameWithoutExtension(yakJsonPath);
            var doc = new LevelDocument
            {
                SchemaVersion = (schemaId ?? "yak") + ".v1",
                LevelId       = string.IsNullOrEmpty(fileId) ? "imported" : fileId,
                DisplayName   = fileId,
                Metadata      = new LevelMetadata
                {
                    Author     = Environment.UserName,
                    CreatedAt  = DateTime.UtcNow.ToString("o"),
                    ModifiedAt = DateTime.UtcNow.ToString("o"),
                },
                Grid          = grid,
                TopSection    = JObject.FromObject(topData),
                GameData      = new JObject { ["conveyorCount"] = conveyorCount },
            };
            return doc;
        }
    }
}
