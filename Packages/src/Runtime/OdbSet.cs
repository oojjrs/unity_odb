using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace oojjrs.odb
{
    public sealed class OdbSet<TEntity, TKey> : IReadOnlyCollection<TEntity>
        where TEntity : class
        where TKey : notnull
    {
        private readonly Dictionary<TKey, TEntity> Entities;
        private readonly List<OdbIndexRuntimeInterface<TEntity, TKey>> Indexes;
        private readonly Dictionary<OdbIndexDefinitionInterface<TEntity, TKey>.Property, OdbIndexRuntimeInterface<TEntity, TKey>> IndexesByProperty;
        private readonly IEqualityComparer<TKey> KeyComparer;
        private readonly Func<TEntity, TKey> KeySelector;

        int IReadOnlyCollection<TEntity>.Count => Count;

        public int Count => Entities.Count;
        public string EntityName { get; }
        internal IReadOnlyCollection<TEntity> ExportEntities => Entities.Values;

        internal OdbSet(OdbEntityBuilder<TEntity, TKey> entityBuilder)
        {
            EntityName = entityBuilder.Name;
            KeyComparer = entityBuilder.KeyComparer;
            KeySelector = entityBuilder.KeySelector;
            Entities = new Dictionary<TKey, TEntity>(entityBuilder.CapacityHint, KeyComparer);
            Indexes = new List<OdbIndexRuntimeInterface<TEntity, TKey>>(entityBuilder.IndexDefinitions.Count);
            IndexesByProperty = new(entityBuilder.IndexDefinitions.Count);

            foreach (var definition in entityBuilder.IndexDefinitions)
            {
                var index = definition.CreateIndex(Entities, entityBuilder.CapacityHint);
                Indexes.Add(index);
                IndexesByProperty.Add(definition.IndexedProperty, index);
            }
        }

        private static void ValidatePrimaryKey(TKey primaryKey)
        {
            if (primaryKey is null)
                throw new InvalidOperationException("A primary key cannot be null.");
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator<TEntity> IEnumerable<TEntity>.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Add(TEntity entity)
        {
            if (TryAdd(entity) == false)
                throw new InvalidOperationException($"The entity '{EntityName}' conflicts with an existing primary or unique key.");
        }

        public OdbIndexView<TEntity, TKey> FindBy<TIndexKey>(Expression<Func<TEntity, TIndexKey>> indexKeySelector, TIndexKey indexKey)
            where TIndexKey : notnull
        {
            if (indexKey is null)
                throw new ArgumentNullException(nameof(indexKey));

            var index = GetIndex(indexKeySelector);
            if (index is OdbHashIndex<TEntity, TKey, TIndexKey> hashIndex)
                return hashIndex.Find(indexKey);

            throw new InvalidOperationException($"The index '{index.Name}' is unique. Use TryFindBy for a unique index.");
        }

        public Dictionary<TKey, TEntity>.ValueCollection.Enumerator GetEnumerator()
        {
            return Entities.Values.GetEnumerator();
        }

        private OdbIndexRuntimeInterface<TEntity, TKey> GetIndex<TIndexKey>(Expression<Func<TEntity, TIndexKey>> indexKeySelector)
        {
            var indexedProperty = OdbIndexDefinitionInterface<TEntity, TKey>.Property.Create(indexKeySelector);
            if (IndexesByProperty.TryGetValue(indexedProperty, out var index))
                return index;

            throw new InvalidOperationException($"The property '{indexedProperty.Name}' is not registered as an index for entity '{EntityName}'.");
        }

        private TKey GetPrimaryKey(TEntity entity)
        {
            var primaryKey = KeySelector(entity);
            ValidatePrimaryKey(primaryKey);
            return primaryKey;
        }

        public bool Remove(TKey primaryKey)
        {
            ValidatePrimaryKey(primaryKey);

            if (Indexes.Count == 0)
                return Entities.Remove(primaryKey);

            if (Entities.TryGetValue(primaryKey, out var entity) == false)
                return false;

            foreach (var index in Indexes)
                index.Remove(entity, primaryKey);
            return Entities.Remove(primaryKey);
        }

        public void Replace(TKey primaryKey, TEntity replacement)
        {
            if (TryReplace(primaryKey, replacement) == false)
                throw new InvalidOperationException($"The entity '{EntityName}' was not found or the replacement conflicts with an existing key.");
        }

        internal void ResetForImport()
        {
            Entities.Clear();
            foreach (var index in Indexes)
                index.Clear();
        }

        public bool TryAdd(TEntity entity)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            var primaryKey = GetPrimaryKey(entity);
            if (Indexes.Count == 0)
                return Entities.TryAdd(primaryKey, entity);

            foreach (var index in Indexes)
                if (index.CanAdd(entity) == false)
                    return false;

            if (Entities.TryAdd(primaryKey, entity) == false)
                return false;

            foreach (var index in Indexes)
                index.Add(entity, primaryKey);
            return true;
        }

        private bool TryApplyReplacement(TKey previousPrimaryKey, TEntity previous, TEntity replacement)
        {
            var replacementPrimaryKey = GetPrimaryKey(replacement);
            if (KeyComparer.Equals(previousPrimaryKey, replacementPrimaryKey) == false)
                throw new InvalidOperationException("A replacement cannot change the primary key.");

            if (Indexes.Count == 0)
            {
                Entities[previousPrimaryKey] = replacement;
                return true;
            }

            foreach (var index in Indexes)
                if (index.CanReplace(previous, replacement) == false)
                    return false;

            Entities[previousPrimaryKey] = replacement;
            foreach (var index in Indexes)
                index.Replace(previous, replacement, previousPrimaryKey);
            return true;
        }

        public bool TryFind(TKey primaryKey, out TEntity entity)
        {
            ValidatePrimaryKey(primaryKey);
            return Entities.TryGetValue(primaryKey, out entity);
        }

        public bool TryFindBy<TIndexKey>(Expression<Func<TEntity, TIndexKey>> indexKeySelector, TIndexKey indexKey, out TEntity entity)
            where TIndexKey : notnull
        {
            if (indexKey is null)
                throw new ArgumentNullException(nameof(indexKey));

            var index = GetIndex(indexKeySelector);
            if (index is OdbUniqueIndex<TEntity, TKey, TIndexKey> uniqueIndex)
                return uniqueIndex.TryFind(indexKey, out entity);

            throw new InvalidOperationException($"The index '{index.Name}' is not unique. Use FindBy for a non-unique index.");
        }

        public bool TryReplace(TKey primaryKey, TEntity replacement)
        {
            ValidatePrimaryKey(primaryKey);
            if (replacement == null)
                throw new ArgumentNullException(nameof(replacement));

            if (Entities.TryGetValue(primaryKey, out var previous) == false)
                return false;

            if (ReferenceEquals(previous, replacement))
                throw new InvalidOperationException("Replace requires a new entity instance so previous index values remain available.");

            return TryApplyReplacement(primaryKey, previous, replacement);
        }

        public bool ValidateIndexes()
        {
            foreach (var pair in Entities)
                if (KeyComparer.Equals(pair.Key, GetPrimaryKey(pair.Value)) == false)
                    return false;

            foreach (var index in Indexes)
                if (index.Validate() == false)
                    return false;

            return true;
        }
    }
}
