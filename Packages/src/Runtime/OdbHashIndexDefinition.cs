using System;
using System.Collections.Generic;

namespace oojjrs.odb
{
    internal sealed class OdbHashIndexDefinition<TEntity, TKey, TIndexKey> : OdbIndexDefinitionInterface<TEntity, TKey>
        where TEntity : class
        where TKey : notnull
        where TIndexKey : notnull
    {
        OdbIndexDefinitionInterface<TEntity, TKey>.Property OdbIndexDefinitionInterface<TEntity, TKey>.IndexedProperty => IndexedProperty;

        internal OdbIndexDefinitionInterface<TEntity, TKey>.Property IndexedProperty { get; }
        internal IEqualityComparer<TIndexKey> KeyComparer { get; }
        internal Func<TEntity, TIndexKey> KeySelector { get; }
        internal string Name => IndexedProperty.Name;

        internal OdbHashIndexDefinition(OdbIndexDefinitionInterface<TEntity, TKey>.Property indexedProperty, Func<TEntity, TIndexKey> keySelector, IEqualityComparer<TIndexKey> keyComparer)
        {
            IndexedProperty = indexedProperty;
            KeySelector = keySelector;
            KeyComparer = keyComparer;
        }

        OdbIndexRuntimeInterface<TEntity, TKey> OdbIndexDefinitionInterface<TEntity, TKey>.CreateIndex(Dictionary<TKey, TEntity> entities, int capacityHint)
        {
            return new OdbHashIndex<TEntity, TKey, TIndexKey>(this, entities, capacityHint);
        }
    }
}
