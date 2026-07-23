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

            // Hidden pixels: sparse indices using the game's x*width+y stride (inverse:
            // x = p / width, y = p % width — exact for square grids).
            if (root["HiddenPixels"] is JArray hiddenArr)
            {
                foreach (var tok in hiddenArr)
                {
                    int p = (int)tok;
                    int x = p / width;
                    int y = p % width;
                    if (grid.InBounds(x, y) && grid.Get(x, y) is BBPixelCell cell)
                        cell.Hidden = true;
                }
            }

            var doc = new LevelDocument
            {
                SchemaVersion = "busbuddies.v1",
                LevelId = levelId,
                Grid = grid,
                GameData = new JObject { ["conveyorCount"] = slots },
            };

            // Player-facing difficulty tag: accepts EITHER the string enum name
            // (case-insensitive) or the int ordinal. Missing key → None (default).
            BusBuddiesLevelType.Set(doc, ParseLevelType(root["LevelType"]));

            // Road-Block slots: TOP-LEVEL sparse SlotConfigs[] → GameData["slotConfigs"]
            // via the helper (reversing IndexBase back to a 0-based internal index).
            // SlotType is accepted as the string "RoadBlock" OR the int ordinal 1.
            if (root["SlotConfigs"] is JArray slotConfigs)
            {
                foreach (var tok in slotConfigs)
                {
                    if (tok is not JObject o) continue;
                    if (!IsRoadBlock(o["SlotType"])) continue;
                    int slotIndex = (o.Value<int?>("SlotIndex") ?? 0) - BusBuddiesSlotConfigs.IndexBase;
                    int amount    = o.Value<int?>("RoadBlockAmount") ?? 1;
                    BusBuddiesSlotConfigs.SetBlocked(doc, slotIndex, amount);
                }
            }

            // Rectangular plates: TOP-LEVEL PlateConfigs[] → GameData["plateConfigs"]
            // via the helper. Position is the MIN (bottom-left) corner; Size is (w=x,
            // h=y). Y passes straight through — same bottom-left grid space as
            // PixelColors (see the exporter's Y-ORIGIN note).
            if (root["PlateConfigs"] is JArray plateConfigs)
            {
                foreach (var tok in plateConfigs)
                {
                    if (tok is not JObject o) continue;
                    var pos = o["Position"];
                    var size = o["Size"];
                    int x = pos?["x"]?.Value<int>() ?? 0;
                    int y = pos?["y"]?.Value<int>() ?? 0;
                    int w = size?["x"]?.Value<int>() ?? 1;
                    int h = size?["y"]?.Value<int>() ?? 1;
                    int amount = o.Value<int?>("PixelAmount") ?? 1;
                    BusBuddiesPlateConfigs.Add(doc, x, y, w, h, amount);
                }
            }

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

            // Connected buses: each {BusA,BusB} pair gets a fresh shared ConnectedId,
            // set on both referenced (ColumnIndex, Index) buses. Rebuild TopSection after.
            if (root["ConnectedBuses"] is JArray connections)
            {
                int nextId = 0;
                foreach (var conn in connections)
                {
                    var a = conn["BusA"]; var b = conn["BusB"];
                    if (a == null || b == null) continue;
                    var busA = BusAt(queue, a.Value<int?>("ColumnIndex"), a.Value<int?>("Index"));
                    var busB = BusAt(queue, b.Value<int?>("ColumnIndex"), b.Value<int?>("Index"));
                    if (busA == null || busB == null) continue;
                    busA.ConnectedId = busB.ConnectedId = nextId++;
                }
                doc.TopSection = JObject.FromObject(queue);
            }

            return new ImportedLevel
            {
                Document = doc,
                SlotsAmount = slots,
                ColumnCount = columnCount,
                OriginalBuses = originalBuses,
            };
        }

        // A SlotConfigs entry counts as a Road Block when SlotType is the string
        // "RoadBlock" (case-insensitive) or the int ordinal 1 (BUBSlotType.RoadBlock).
        private static bool IsRoadBlock(JToken slotType)
        {
            if (slotType == null) return false;
            if (slotType.Type == JTokenType.Integer) return slotType.Value<int>() == 1;
            return string.Equals(slotType.Value<string>(), "RoadBlock",
                System.StringComparison.OrdinalIgnoreCase);
        }

        // LevelType accepts EITHER the string enum name (case-insensitive) or the int
        // ordinal — mirroring the game's plain JsonConvert, which reads both forms.
        // Missing / unrecognized → None.
        private static BusLevelType ParseLevelType(JToken token)
        {
            if (token == null) return BusLevelType.None;
            if (token.Type == JTokenType.Integer)
            {
                int ordinal = token.Value<int>();
                return System.Enum.IsDefined(typeof(BusLevelType), ordinal)
                    ? (BusLevelType)ordinal
                    : BusLevelType.None;
            }
            string name = token.Value<string>();
            return System.Enum.TryParse<BusLevelType>(name, true, out var result)
                ? result
                : BusLevelType.None;
        }

        private static BusEntry BusAt(BusQueueData queue, int? col, int? index)
        {
            if (col == null || index == null) return null;
            if (col < 0 || col >= queue.Columns.Count) return null;
            var buses = queue.Columns[col.Value]?.Buses;
            if (buses == null || index < 0 || index >= buses.Count) return null;
            return buses[index.Value];
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
