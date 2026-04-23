using System;
using System.Collections.Generic;
using Hoppa.LevelEditor.Core;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Implements both the runtime ICellTypeRegistry (id <-> Type for serialization)
    // and the editor-side lookup (id -> ICellTypeDefinition for rendering/inspection).
    public sealed class CellTypeRegistry : ICellTypeRegistry
    {
        private readonly Dictionary<string, Type> _typeById = new Dictionary<string, Type>();
        private readonly Dictionary<Type, string> _idByType = new Dictionary<Type, string>();
        private readonly Dictionary<string, ICellTypeDefinition> _defById = new Dictionary<string, ICellTypeDefinition>();

        public void Register(ICellTypeDefinition definition)
        {
            var dataType = definition.CreateDefault()?.GetType();
            if (dataType == null)
                throw new ArgumentException($"CellTypeDefinition '{definition.TypeId}' CreateDefault() returned null.");

            _typeById[definition.TypeId] = dataType;
            _idByType[dataType] = definition.TypeId;
            _defById[definition.TypeId] = definition;
        }

        // ICellTypeRegistry (runtime interface)
        public void Register(string cellTypeId, Type concreteType)
        {
            _typeById[cellTypeId] = concreteType;
            _idByType[concreteType] = cellTypeId;
        }

        public bool TryGetType(string cellTypeId, out Type concreteType)
            => _typeById.TryGetValue(cellTypeId, out concreteType);

        public bool TryGetId(Type concreteType, out string cellTypeId)
            => _idByType.TryGetValue(concreteType, out cellTypeId);

        // Editor lookup
        public bool TryGetDefinition(string cellTypeId, out ICellTypeDefinition definition)
            => _defById.TryGetValue(cellTypeId, out definition);

        public IEnumerable<ICellTypeDefinition> AllDefinitions => _defById.Values;
    }
}
