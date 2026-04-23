using System.Collections.Generic;

namespace Hoppa.LevelEditor.Core
{
    public enum ValidationScope { Level, Cell, Color, Stack }

    public interface IValidationRule
    {
        string Id { get; }
        ValidationScope Scope { get; }
        IEnumerable<ValidationEntry> Evaluate(ValidationContext ctx);
    }
}
