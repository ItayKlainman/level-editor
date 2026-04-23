using System.Collections.Generic;
using UnityEngine;

namespace Hoppa.LevelEditor.Core
{
    // Abstract ScriptableObject base for data-rules that designers configure
    // entirely in the Inspector — no code required for simple counting rules.
    public abstract class ValidationRuleBase : ScriptableObject, IValidationRule
    {
        [SerializeField] private string _id;

        public string Id => _id;

        // Allows scripted and test setup without the Inspector.
        public void Configure(string id) => _id = id;

        public abstract ValidationScope Scope { get; }
        public abstract IEnumerable<ValidationEntry> Evaluate(ValidationContext ctx);
    }
}
