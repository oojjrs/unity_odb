using System;
using System.Collections;
using System.Collections.Generic;

namespace oojjrs.odb
{
    internal interface OdbEntityBuilderInterface
    {
        Type EntityType { get; }
        OdbModelBuilder.IdentityInterface Identity { get; }
        string Name { get; }

        object CreateSet(IReadOnlyDictionary<string, OdbModelBuilder.IdentityRuntimeInterface> identities);
        void Freeze();
        IEnumerable GetEntities(object set);
        void ResetSet(object set);
        bool TryAddExisting(object set, object entity);
    }
}
