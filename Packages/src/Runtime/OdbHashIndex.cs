using System;
using System.Collections.Generic;

namespace oojjrs.odb
{
    internal sealed class OdbHashIndex<TEntity, TKey, TIndexKey> : OdbIndexRuntimeInterface<TEntity, TKey>
        where TEntity : class
        where TKey : notnull
        where TIndexKey : notnull
    {
        private readonly OdbHashIndexDefinition<TEntity, TKey, TIndexKey> Definition;
        private readonly Dictionary<TKey, TEntity> Entities;
        private readonly Dictionary<TIndexKey, HashSet<TKey>> PrimaryKeysByIndexKey;

        string OdbIndexRuntimeInterface<TEntity, TKey>.Name => Definition.Name;

        internal OdbHashIndex(OdbHashIndexDefinition<TEntity, TKey, TIndexKey> definition, Dictionary<TKey, TEntity> entities, int capacityHint)
        {
            Definition = definition;
            Entities = entities;
            PrimaryKeysByIndexKey = new Dictionary<TIndexKey, HashSet<TKey>>(capacityHint, definition.KeyComparer);
        }

        private static void ValidateIndexKey(TIndexKey indexKey)
        {
            if (indexKey is null)
                throw new InvalidOperationException("An index selector returned a null key.");
        }

        void OdbIndexRuntimeInterface<TEntity, TKey>.Add(TEntity entity, TKey primaryKey)
        {
            AddPrimaryKey(GetIndexKey(entity), primaryKey);
        }

        bool OdbIndexRuntimeInterface<TEntity, TKey>.CanAdd(TEntity entity)
        {
            try
            {
                GetIndexKey(entity);
                return true;
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
                GetIndexKey(replacement);
                return true;
            }
            catch
            {
                return false;
            }
        }

        void OdbIndexRuntimeInterface<TEntity, TKey>.Clear()
        {
            foreach (var primaryKeys in PrimaryKeysByIndexKey.Values)
                primaryKeys.Clear();

            PrimaryKeysByIndexKey.Clear();
        }

        void OdbIndexRuntimeInterface<TEntity, TKey>.Remove(TEntity entity, TKey primaryKey)
        {
            RemovePrimaryKey(GetIndexKey(entity), primaryKey);
        }

        void OdbIndexRuntimeInterface<TEntity, TKey>.Replace(TEntity previous, TEntity replacement, TKey primaryKey)
        {
            var previousIndexKey = GetIndexKey(previous);
            var replacementIndexKey = GetIndexKey(replacement);
            if (Definition.KeyComparer.Equals(previousIndexKey, replacementIndexKey))
                return;

            RemovePrimaryKey(previousIndexKey, primaryKey);
            AddPrimaryKey(replacementIndexKey, primaryKey);
        }

        bool OdbIndexRuntimeInterface<TEntity, TKey>.Validate()
        {
            var indexedEntityCount = 0;

            foreach (var pair in PrimaryKeysByIndexKey)
            {
                indexedEntityCount += pair.Value.Count;

                foreach (var primaryKey in pair.Value)
                {
                    if (Entities.TryGetValue(primaryKey, out var entity) == false)
                        return false;

                    if (Definition.KeyComparer.Equals(pair.Key, GetIndexKey(entity)) == false)
                        return false;
                }
            }

            if (indexedEntityCount != Entities.Count)
                return false;

            foreach (var pair in Entities)
                if ((PrimaryKeysByIndexKey.TryGetValue(GetIndexKey(pair.Value), out var primaryKeys) == false)
                    || (primaryKeys.Contains(pair.Key) == false))
                    return false;

            return true;
        }

        private void AddPrimaryKey(TIndexKey indexKey, TKey primaryKey)
        {
            if (PrimaryKeysByIndexKey.TryGetValue(indexKey, out var primaryKeys) == false)
            {
                primaryKeys = new HashSet<TKey>(Entities.Comparer);
                PrimaryKeysByIndexKey.Add(indexKey, primaryKeys);
            }

            if (primaryKeys.Add(primaryKey) == false)
                throw new InvalidOperationException($"The hash index '{Definition.Name}' already contains the primary key.");
        }

        public OdbIndexView<TEntity, TKey> Find(TIndexKey indexKey)
        {
            ValidateIndexKey(indexKey);
            PrimaryKeysByIndexKey.TryGetValue(indexKey, out var primaryKeys);
            return new OdbIndexView<TEntity, TKey>(Entities, primaryKeys);
        }

        private TIndexKey GetIndexKey(TEntity entity)
        {
            var indexKey = Definition.KeySelector(entity);
            ValidateIndexKey(indexKey);
            return indexKey;
        }

        private void RemovePrimaryKey(TIndexKey indexKey, TKey primaryKey)
        {
            if ((PrimaryKeysByIndexKey.TryGetValue(indexKey, out var primaryKeys) == false) || (primaryKeys.Remove(primaryKey) == false))
                throw new InvalidOperationException($"The hash index '{Definition.Name}' does not contain the primary key.");
        }
    }
}
