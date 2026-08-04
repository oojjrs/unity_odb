using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace oojjrs.odb
{
    public sealed class OdbEntityBuilder<TEntity, TKey> : OdbEntityBuilderInterface
        where TEntity : class
        where TKey : notnull
    {
        private readonly HashSet<OdbIndexDefinitionInterface<TEntity, TKey>.Property> IndexedProperties = new();
        private readonly List<OdbIndexDefinitionInterface<TEntity, TKey>> RegisteredIndexDefinitions = new();

        private bool _isFrozen;

        Type OdbEntityBuilderInterface.EntityType => typeof(TEntity);
        string OdbEntityBuilderInterface.Name => Name;

        internal int CapacityHint { get; private set; }
        internal IReadOnlyList<OdbIndexDefinitionInterface<TEntity, TKey>> IndexDefinitions => RegisteredIndexDefinitions;
        internal IEqualityComparer<TKey> KeyComparer { get; }
        internal Func<TEntity, TKey> KeySelector { get; }
        internal string Name { get; }

        internal OdbEntityBuilder(string name, Func<TEntity, TKey> keySelector, IEqualityComparer<TKey> keyComparer)
        {
            Name = name;
            KeySelector = keySelector;
            KeyComparer = keyComparer;
        }

        object OdbEntityBuilderInterface.CreateSet()
        {
            return new OdbSet<TEntity, TKey>(this);
        }

        void OdbEntityBuilderInterface.Freeze()
        {
            _isFrozen = true;
        }

        IEnumerable OdbEntityBuilderInterface.GetEntities(object set)
        {
            return ((OdbSet<TEntity, TKey>)set).ExportEntities;
        }

        void OdbEntityBuilderInterface.ResetSet(object set)
        {
            ((OdbSet<TEntity, TKey>)set).ResetForImport();
        }

        bool OdbEntityBuilderInterface.TryAdd(object set, object entity)
        {
            return ((OdbSet<TEntity, TKey>)set).TryAdd((TEntity)entity);
        }

        public OdbEntityBuilder<TEntity, TKey> AddIndex<TIndexKey>(
            Expression<Func<TEntity, TIndexKey>> indexKeySelector, IEqualityComparer<TIndexKey> keyComparer = null)
            where TIndexKey : notnull
        {
            EnsureMutable();

            var indexedProperty = OdbIndexDefinitionInterface<TEntity, TKey>.Property.Create(indexKeySelector);
            ValidateIndex(indexedProperty);

            RegisteredIndexDefinitions.Add(new OdbHashIndexDefinition<TEntity, TKey, TIndexKey>(
                indexedProperty, indexedProperty.CreateKeySelector<TIndexKey>(), keyComparer ?? EqualityComparer<TIndexKey>.Default));
            return this;
        }

        public OdbEntityBuilder<TEntity, TKey> AddUniqueIndex<TIndexKey>(
            Expression<Func<TEntity, TIndexKey>> indexKeySelector, IEqualityComparer<TIndexKey> keyComparer = null)
            where TIndexKey : notnull
        {
            EnsureMutable();

            var indexedProperty = OdbIndexDefinitionInterface<TEntity, TKey>.Property.Create(indexKeySelector);
            ValidateIndex(indexedProperty);

            RegisteredIndexDefinitions.Add(new OdbUniqueIndexDefinition<TEntity, TKey, TIndexKey>(
                indexedProperty, indexedProperty.CreateKeySelector<TIndexKey>(), keyComparer ?? EqualityComparer<TIndexKey>.Default));
            return this;
        }

        private void EnsureMutable()
        {
            if (_isFrozen)
                throw new InvalidOperationException($"The entity model '{Name}' is already frozen.");
        }

        public OdbEntityBuilder<TEntity, TKey> SetCapacityHint(int capacity)
        {
            EnsureMutable();

            if (capacity < 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            CapacityHint = capacity;
            return this;
        }

        private void ValidateIndex(OdbIndexDefinitionInterface<TEntity, TKey>.Property indexedProperty)
        {
            if (IndexedProperties.Add(indexedProperty) == false)
                throw new InvalidOperationException($"The property '{indexedProperty.Name}' is already registered as an index for entity '{Name}'.");
        }
    }
}
