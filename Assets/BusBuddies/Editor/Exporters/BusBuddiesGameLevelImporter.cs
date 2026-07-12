using System.Collections.Generic;
using System.IO;
using Hoppa.LevelEditor.Core;
using Newtonsoft.Json.Linq;

namespace Hoppa.BusBuddies.Editor
{
    // Inverse of BusBuddiesGameLevelExporter for ONE game-schema level file.
    // Parses the REAL Bus Buddies LevelConfig JSON:
    //   { SlotsAmount, Width, Height,
    //     BusColumnConfigs[ BusConfigs[ {ColorType, Capacity, BusType?} ] ],
    //     PixelColors[ int ordinals, index = y*Width + x ] }
    // back into a LevelDocument whose Grid is reconstructed from PixelColors:
    //   ordinal 0            → BBEmptyCell
    //   nonzero ordinal      → BBPixelCell { ColorId = palette id }
    // GameData["conveyorCount"] is set from SlotsAmount, and the existing bus
    // column count + original buses are recorded for the re-tweak report.
    //
    // The ordinal→color mapping is INVERTED FROM the exporter's own ColorMap
    // (via BusBuddiesGameLevelExporter.TryOrdinalToColorName), so the round-trip is
    // byte-consistent — there is no second, drift-prone mapping. Palette ids are the
    // lowercased enum names ("Blue" → "blue"), so we lowercase the canonical name.
    public static class BusBuddiesGameLevelImporter
    {
        // A single imported game level: the reconstructed document plus the
        // metadata the re-tweak batch needs (column count, slots, original buses).
        public sealed class ImportedLevel
        {
            public LevelDocument Document;
            public int SlotsAmount;
            public int ColumnCount;                 // existing BusColumnConfigs.Count
            public List<BusEntry> OriginalBuses;    // flattened, in column/queue order
        }

        public static ImportedLevel ImportFile(string path)
            => Import(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));

        public static ImportedLevel Import(string json, string levelId = null)
        {
            var root = JObject.Parse(json);

            int width  = root.Value<int?>("Width")  ?? 0;
            int height = root.Value<int?>("Height") ?? 0;
            int slots  = root.Value<int?>("SlotsAmount") ?? 5;

            var grid = new GridData<ICellData>(width, height);
            var px = root["PixelColors"] as JArray;
            int total = width * height;
            for (int i = 0; i < total; i++)
            {
                int ordinal = (px != null && i < px.Count) ? (int)px[i] : 0;
                grid.Cells[i] = OrdinalToCell(ordinal);
            }

            var doc = new LevelDocument
            {
                SchemaVersion = "busbuddies.v1",
                LevelId = levelId,
                Grid = grid,
                GameData = new JObject { ["conveyorCount"] = slots },
            };

            // Rebuild the editable bus queue (per-column) AND record the flattened
            // original buses (for the re-tweak report). Same BusEntry instances feed both.
            var queue = new BusQueueData();
            var originalBuses = new List<BusEntry>();
            int columnCount = 0;
            if (root["BusColumnConfigs"] is JArray columns)
            {
                columnCount = columns.Count;
                foreach (var col in columns)
                {
                    var qcol = new BusColumn();
                    if (col?["BusConfigs"] is JArray busConfigs)
                    {
                        foreach (var b in busConfigs)
                        {
                            int ord = b.Value<int?>("ColorType") ?? 0;
                            int cap = b.Value<int?>("Capacity") ?? 0;
                            bool hidden = (b.Value<int?>("BusType") ?? 0) == 1;
                            var entry = new BusEntry
                            {
                                ColorId = OrdinalToColorId(ord),
                                Capacity = cap,
                                Hidden = hidden,
                            };
                            qcol.Buses.Add(entry);
                            originalBuses.Add(entry);
                        }
                    }
                    queue.Columns.Add(qcol);
                }
            }
            // The queue is the editor's editable TopSection — without it the imported
            // level would open with an empty bus panel.
            doc.TopSection = JObject.FromObject(queue);

            return new ImportedLevel
            {
                Document = doc,
                SlotsAmount = slots,
                ColumnCount = columnCount,
                OriginalBuses = originalBuses,
            };
        }

        // ordinal → cell. 0 (None) is an empty cell; any mapped nonzero ordinal is a
        // colored pixel. An unmapped nonzero ordinal falls back to an empty cell.
        private static ICellData OrdinalToCell(int ordinal)
        {
            if (ordinal == 0) return new BBEmptyCell();
            var colorId = OrdinalToColorId(ordinal);
            return colorId != null ? (ICellData)new BBPixelCell { ColorId = colorId } : new BBEmptyCell();
        }

        // ordinal → palette color id (lowercased canonical name), reusing the
        // exporter's inverse map. null when unmapped or None.
        private static string OrdinalToColorId(int ordinal)
        {
            if (ordinal == 0) return null;
            return BusBuddiesGameLevelExporter.TryOrdinalToColorName(ordinal, out var name)
                ? name.ToLowerInvariant()
                : null;
        }
    }
}
