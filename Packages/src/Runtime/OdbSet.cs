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
        private readonly OdbIdentity<TKey>.IdentityRuntime Identity;
        private readonly List<OdbIndexRuntimeInterface<TEntity, TKey>> Indexes;
        private readonly Dictionary<OdbIndexDefinitionInterface<TEntity, TKey>.Property, OdbIndexRuntimeInterface<TEntity, TKey>> IndexesByProperty;
        private readonly IEqualityComparer<TKey> KeyComparer;
        private readonly Func<TEntity, TKey> KeySelector;
        private readonly Action<TEntity, TKey> KeySetter;

        int IReadOnlyCollection<TEntity>.Count => Count;

        public int Count => Entities.Count;
        public string EntityName { get; }
        internal IReadOnlyCollection<TEntity> ExportEntities => Entities.Values;

        internal OdbSet(OdbEntityBuilder<TEntity, TKey> entityBuilder, OdbIdentity<TKey>.IdentityRuntime identity)
        {
            EntityName = entityBuilder.Name;
            Identity = identity;
            KeyComparer = entityBuilder.KeyComparer;
            KeySelector = entityBuilder.KeySelector;
            KeySetter = entityBuilder.KeySetter;
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

        public TEntity AddGenerated(Func<TKey, TEntity> entityFactory)
        {
            if (TryAddGenerated(entityFactory, out var entity) == false)
                throw new InvalidOperationException($"The entity '{EntityName}' conflicts with an existing primary or unique key.");

            return entity;
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

        private bool CanRegisterIdentity(TKey primaryKey)
        {
            if (Identity == null)
                return true;

            Identity.Validate(primaryKey);
            return Identity.CanRegister(primaryKey);
        }

        public bool Remove(TKey primaryKey)
        {
            ValidatePrimaryKey(primaryKey);

            if (Indexes.Count == 0)
            {
                if (Entities.TryGetValue(primaryKey, out var removedEntity) == false)
                    return false;

                var removedPrimaryKey = Identity == null ? primaryKey : GetPrimaryKey(removedEntity);
                if (Entities.Remove(primaryKey) == false)
                    throw new InvalidOperationException($"The entity '{EntityName}' disappeared while it was being removed.");

                UnregisterIdentity(removedPrimaryKey);
                return true;
            }

            if (Entities.TryGetValue(primaryKey, out var entity) == false)
                return false;

            var storedPrimaryKey = Identity == null ? primaryKey : GetPrimaryKey(entity);
            foreach (var index in Indexes)
                index.Remove(entity, storedPrimaryKey);
            if (Entities.Remove(primaryKey) == false)
                throw new InvalidOperationException($"The entity '{EntityName}' disappeared while it was being removed.");

            UnregisterIdentity(storedPrimaryKey);
            return true;
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

            if (Identity == null)
                return TryAddCore(entity, GetPrimaryKey(entity), false);

            if (KeySetter == null)
                throw new InvalidOperationException($"The entity '{EntityName}' requires AddGenerated because its generated key has no setter.");

            var currentPrimaryKey = GetPrimaryKey(entity);
            if (EqualityComparer<TKey>.Default.Equals(currentPrimaryKey, default) == false)
                throw new InvalidOperationException($"The entity '{EntityName}' must have the default primary key before a generated Add.");

            var generatedPrimaryKey = Identity.Generate();
            KeySetter(entity, generatedPrimaryKey);
            var primaryKey = GetPrimaryKey(entity);
            if (EqualityComparer<TKey>.Default.Equals(primaryKey, generatedPrimaryKey) == false)
            {
                KeySetter(entity, default);
                throw new InvalidOperationException($"The generated primary key setter for entity '{EntityName}' did not assign the requested key.");
            }

            if (TryAddCore(entity, primaryKey, false))
                return true;

            KeySetter(entity, default);
            return false;
        }

        public bool TryAddGenerated(Func<TKey, TEntity> entityFactory, out TEntity entity)
        {
            if (Identity == null)
                throw new InvalidOperationException($"The entity '{EntityName}' does not have generated primary keys.");

            if (entityFactory == null)
                throw new ArgumentNullException(nameof(entityFactory));

            var generatedPrimaryKey = Identity.Generate();
            entity = entityFactory(generatedPrimaryKey);
            if (entity == null)
                throw new InvalidOperationException($"The generated entity factory for '{EntityName}' returned null.");

            var primaryKey = GetPrimaryKey(entity);
            if (EqualityComparer<TKey>.Default.Equals(primaryKey, generatedPrimaryKey) == false)
                throw new InvalidOperationException($"The generated entity factory for '{EntityName}' did not use the requested primary key.");

            if (TryAddCore(entity, primaryKey, false))
                return true;

            entity = null;
            return false;
        }

        private bool TryAddCore(TEntity entity, TKey primaryKey, bool observeIdentity)
        {
            if (CanRegisterIdentity(primaryKey) == false)
                return false;

            if (Indexes.Count == 0)
            {
                if (Entities.TryAdd(primaryKey, entity) == false)
                    return false;

                RegisterIdentity(primaryKey, observeIdentity);
                return true;
            }

            foreach (var index in Indexes)
                if (index.CanAdd(entity) == false)
                    return false;

            if (Entities.TryAdd(primaryKey, entity) == false)
                return false;

            foreach (var index in Indexes)
                index.Add(entity, primaryKey);
            RegisterIdentity(primaryKey, observeIdentity);
            return true;
        }

        internal bool TryAddExisting(TEntity entity)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            return TryAddCore(entity, GetPrimaryKey(entity), true);
        }

        private void RegisterIdentity(TKey primaryKey, bool observeIdentity)
        {
            if (Identity == null)
                return;

            Identity.Register(primaryKey);
            if (observeIdentity)
                Identity.Observe(primaryKey);
        }

        private bool TryApplyReplacement(TKey previousPrimaryKey, TEntity previous, TEntity replacement)
        {
            var storedPrimaryKey = Identity == null ? previousPrimaryKey : GetPrimaryKey(previous);
            var replacementPrimaryKey = GetPrimaryKey(replacement);
            var keysAreEqual = Identity == null ? KeyComparer.Equals(storedPrimaryKey, replacementPrimaryKey) : EqualityComparer<TKey>.Default.Equals(storedPrimaryKey, replacementPrimaryKey);
            if (keysAreEqual == false)
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
                index.Replace(previous, replacement, storedPrimaryKey);
            return true;
        }

        private void UnregisterIdentity(TKey primaryKey)
        {
            if (Identity != null)
                Identity.Unregister(primaryKey);
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
            {
                var entityPrimaryKey = GetPrimaryKey(pair.Value);
                var keysAreEqual = Identity == null ? KeyComparer.Equals(pair.Key, entityPrimaryKey) : EqualityComparer<TKey>.Default.Equals(pair.Key, entityPrimaryKey);
                if (keysAreEqual == false)
                    return false;

                if ((Identity != null) && (Identity.HasRegistered(pair.Key) == false))
                    return false;
            }

            foreach (var index in Indexes)
                if (index.Validate() == false)
                    return false;

            return true;
        }
    }
}
