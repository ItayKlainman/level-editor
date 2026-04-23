using System;
using System.Collections.Generic;
using Hoppa.LevelEditor.Core;

namespace Hoppa.LevelEditor.Core.Editor
{
    public sealed class ValidationRuleRegistry
    {
        private readonly List<IValidationRule> _rules = new List<IValidationRule>();

        public void Register(IValidationRule rule)
        {
            if (rule != null) _rules.Add(rule);
        }

        public int Count => _rules.Count;

        public ValidationReport RunAll(ValidationContext ctx)
        {
            var report = new ValidationReport();
            foreach (var rule in _rules)
            {
                try
                {
                    report.AddRange(rule.Evaluate(ctx));
                }
                catch (Exception e)
                {
                    report.Add(new ValidationEntry(
                        rule.Id ?? "unknown",
                        ValidationSeverity.Error,
                        $"Rule \"{rule.Id}\" threw: {e.Message}"));
                }
            }
            return report;
        }
    }
}
