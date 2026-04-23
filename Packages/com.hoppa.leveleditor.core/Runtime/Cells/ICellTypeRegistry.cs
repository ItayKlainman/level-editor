using System;

namespace Hoppa.LevelEditor.Core
{
    public interface ICellTypeRegistry
    {
        void Register(string cellTypeId, Type concreteType);
        bool TryGetType(string cellTypeId, out Type concreteType);
        bool TryGetId(Type concreteType, out string cellTypeId);
    }
}
