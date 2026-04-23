using System;
using Newtonsoft.Json;

namespace Hoppa.LevelEditor.Core
{
    [Serializable]
    public sealed class GridData<TCell> where TCell : class
    {
        [JsonProperty("width")]
        public int Width { get; set; }

        [JsonProperty("height")]
        public int Height { get; set; }

        [JsonProperty("rowOrder")]
        public string RowOrder { get; set; } = "bottomUp";

        [JsonProperty("cells")]
        public TCell[] Cells { get; set; }

        public GridData() { }

        public GridData(int width, int height)
        {
            Width = width;
            Height = height;
            Cells = new TCell[width * height];
        }

        public TCell Get(int x, int y) => Cells[y * Width + x];
        public void Set(int x, int y, TCell cell) => Cells[y * Width + x] = cell;
        public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;
        public int ToIndex(int x, int y) => y * Width + x;
    }
}
