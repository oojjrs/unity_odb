using System;
using System.Collections.Generic;

namespace oojjrs.odb
{
    internal sealed class OdbUniqueIndex<TEntity, TKey, TIndexKey> : OdbIndexRuntimeInterface<TEntity, TKey>
        where TEntity : class
        where TKey : notnull
        where TIndexKey : notnull
    {
        private readonly OdbUniqueIndexDefinition<TEntity, TKey, TIndexKey> Definition;
        private readonly Dictionary<TKey, TEntity> Entities;
        private readonly Dictionary<TIndexKey, TEntity> EntitiesByIndexKey;

        string OdbIndexRuntimeInterface<TEntity, TKey>.Name => Definition.Name;

        internal OdbUniqueIndex(OdbUniqueIndexDefinition<TEntity, TKey, TIndexKey> definition, Dictionary<TKey, TEntity> entities, int capacityHint)
        {
            Definition = definition;
            Entities = entities;
            EntitiesByIndexKey = new Dictionary<TIndexKey, TEntity>(capacityHint, definition.KeyComparer);
        }

        private static void ValidateIndexKey(TIndexKey indexKey)
        {
            if (indexKey is null)
                throw new InvalidOperationException("An index selector returned a null key.");
        }

        void OdbIndexRuntimeInterface<TEntity, TKey>.Add(TEntity entity, TKey primaryKey)
        {
            EntitiesByIndexKey.Add(GetIndexKey(entity), entity);
        }

        bool OdbIndexRuntimeInterface<TEntity, TKey>.CanAdd(TEntity entity)
        {
            try
            {
                return EntitiesByIndexKey.ContainsKey(GetIndexKey(entity)) == false;
            }
            catch
            {
                return false;
            }
        }

        bool OdbIndexRuntimeInterface<TEntity, TKey>.CanReplace(TEntity previous, TEntity replacement)
        {
            try
            {
                GetIndexKey(previous);
                if (EntitiesByIndexKey.TryGetValue(GetIndexKey(replacement), out var indexedEntity) == false)
                    return true;

                return ReferenceEquals(indexedEntity, previous);
            }
            catch
            {
                return false;
            }
        }

        void OdbIndexRuntimeInterface<TEntity, TKey>.Clear()
        {
            EntitiesByIndexKey.Clear();
        }

        void OdbIndexRuntimeInterface<TEntity, TKey>.Remove(TEntity entity, TKey primaryKey)
        {
            RemoveEntity(GetIndexKey(entity), entity);
        }

        void OdbIndexRuntimeInterface<TEntity, TKey>.Replace(TEntity previous, TEntity replacement, TKey primaryKey)
        {
            var previousIndexKey = GetIndexKey(previous);
            var replacementIndexKey = GetIndexKey(replacement);
            if (Definition.KeyComparer.Equals(previousIndexKey, replacementIndexKey))
            {
                EntitiesByIndexKey[replacementIndexKey] = replacement;
                return;
            }

            RemoveEntity(previousIndexKey, previous);
            EntitiesByIndexKey.Add(replacementIndexKey, replacement);
        }

        bool OdbIndexRuntimeInterface<TEntity, TKey>.Validate()
        {
            if (EntitiesByIndexKey.Count != Entities.Count)
                return false;

            foreach (var pair in Entities)
                if ((EntitiesByIndexKey.TryGetValue(GetIndexKey(pair.Value), out var indexedEntity) == false)
                    || (ReferenceEquals(indexedEntity, pair.Value) == false))
                    return false;

            return true;
        }

        private TIndexKey GetIndexKey(TEntity entity)
        {
            var indexKey = Definition.KeySelector(entity);
            ValidateIndexKey(indexKey);
            return indexKey;
        }

        private void RemoveEntity(TIndexKey indexKey, TEntity entity)
        {
            if ((EntitiesByIndexKey.TryGetValue(indexKey, out var indexedEntity) == false)
                || (ReferenceEquals(indexedEntity, entity) == false)
                || (EntitiesByIndexKey.Remove(indexKey) == false))
                throw new InvalidOperationException($"The unique index '{Definition.Name}' does not contain the primary key.");
        }

        public bool TryFind(TIndexKey indexKey, out TEntity entity)
        {
            ValidateIndexKey(indexKey);
            return EntitiesByIndexKey.TryGetValue(indexKey, out entity);
        }
    }
}
