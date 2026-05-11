using System.Collections.Generic;
using UnityEngine;

namespace Hoppa.LevelEditor.Core
{
    public sealed class ValidationEntry
    {
        public string RuleId { get; }
        public ValidationSeverity Severity { get; }
        public string Message { get; }
        // Optional: cell/color/stack that caused the issue (null = level-wide)
        public CellRef? OffendingCell { get; }
        // Optional: color swatch rendered next to the message in the validation panel
        public Color? Swatch { get; }

        public ValidationEntry(string ruleId, ValidationSeverity severity, string message,
            CellRef? offendingCell = null, Color? swatch = null)
        {
            RuleId = ruleId;
            Severity = severity;
            Message = message;
            OffendingCell = offendingCell;
            Swatch = swatch;
        }
    }

    public sealed class ValidationReport
    {
        private readonly List<ValidationEntry> _entries = new List<ValidationEntry>();

        public IReadOnlyList<ValidationEntry> Entries => _entries;

        public bool HasErrors => _entries.Exists(e => e.Severity == ValidationSeverity.Error);

        public void Add(ValidationEntry entry) => _entries.Add(entry);

        public void AddRange(IEnumerable<ValidationEntry> entries) => _entries.AddRange(entries);
    }
}
