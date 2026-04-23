namespace Hoppa.LevelEditor.Demo
{
    public sealed class DemoBoxCell : Hoppa.LevelEditor.Core.IColoredCell
    {
        public string CellTypeId => "demo_box";
        public string ColorId { get; set; } = "red";
    }
}
