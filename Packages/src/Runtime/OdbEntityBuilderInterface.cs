using System;
using System.Collections;

namespace oojjrs.odb
{
    internal interface OdbEntityBuilderInterface
    {
        Type EntityType { get; }
        string Name { get; }

        object CreateSet();
        void Freeze();
        IEnumerable GetEntities(object set);
        void ResetSet(object set);
        bool TryAdd(object set, object entity);
    }
}
