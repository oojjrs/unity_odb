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
        private readonly OdbModelBuilder ModelBuilder;
        private readonly List<OdbIndexDefinitionInterface<TEntity, TKey>> RegisteredIndexDefinitions = new();

        private bool _isFrozen;

        Type OdbEntityBuilderInterface.EntityType => typeof(TEntity);
        OdbModelBuilder.IdentityInterface OdbEntityBuilderInterface.Identity => Identity?.DefinitionInterface;
        string OdbEntityBuilderInterface.Name => Name;

        internal int CapacityHint { get; private set; }
        internal OdbIdentity<TKey> Identity { get; private set; }
        internal IReadOnlyList<OdbIndexDefinitionInterface<TEntity, TKey>> IndexDefinitions => RegisteredIndexDefinitions;
        internal IEqualityComparer<TKey> KeyComparer { get; }
        internal Func<TEntity, TKey> KeySelector { get; }
        internal Action<TEntity, TKey> KeySetter { get; private set; }
        internal string Name { get; }

        internal OdbEntityBuilder(OdbModelBuilder modelBuilder, string name, Func<TEntity, TKey> keySelector, IEqualityComparer<TKey> keyComparer)
        {
            ModelBuilder = modelBuilder;
            Name = name;
            KeySelector = keySelector;
            KeyComparer = keyComparer;
        }

        object OdbEntityBuilderInterface.CreateSet(IReadOnlyDictionary<string, OdbModelBuilder.IdentityRuntimeInterface> identities)
        {
            OdbIdentity<TKey>.IdentityRuntime identityRuntime = null;
            if (Identity != null)
            {
                if (identities.TryGetValue(Identity.StateKey, out var runtime) == false)
                    throw new InvalidOperationException($"The identity runtime for entity '{Name}' is missing.");

                var typedRuntime = runtime as OdbIdentity<TKey>.IdentityRuntime;
                if (typedRuntime == null)
                    throw new InvalidOperationException($"The identity runtime for entity '{Name}' has a different key type.");

                identityRuntime = typedRuntime;
            }

            return new OdbSet<TEntity, TKey>(this, identityRuntime);
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

        bool OdbEntityBuilderInterface.TryAddExisting(object set, object entity)
        {
            return ((OdbSet<TEntity, TKey>)set).TryAddExisting((TEntity)entity);
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

        private void EnsureValueGenerationUnconfigured()
        {
            if (Identity != null)
                throw new InvalidOperationException($"The entity model '{Name}' already has a value generation strategy.");
        }

        public OdbEntityBuilder<TEntity, TKey> SetCapacityHint(int capacity)
        {
            EnsureMutable();

            if (capacity < 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            CapacityHint = capacity;
            return this;
        }

        public OdbEntityBuilder<TEntity, TKey> ValueGeneratedOnAdd(Action<TEntity, TKey> keySetter = null)
        {
            EnsureMutable();
            EnsureValueGenerationUnconfigured();

            Identity = ModelBuilder.AddEntityIdentity<TKey>(Name);
            Identity.RegisterParticipant();
            KeySetter = keySetter;
            return this;
        }

        public OdbEntityBuilder<TEntity, TKey> ValueGeneratedOnAdd(OdbIdentity<TKey> identity, Action<TEntity, TKey> keySetter = null)
        {
            EnsureMutable();
            EnsureValueGenerationUnconfigured();

            if (identity == null)
                throw new ArgumentNullException(nameof(identity));

            if (identity.IsOwnedBy(ModelBuilder) == false)
                throw new InvalidOperationException("A global identity must belong to the same database model.");

            if (identity.IsGlobal == false)
                throw new InvalidOperationException("Only a global identity can be shared between entity models.");

            Identity = identity;
            Identity.RegisterParticipant();
            KeySetter = keySetter;
            return this;
        }

        private void ValidateIndex(OdbIndexDefinitionInterface<TEntity, TKey>.Property indexedProperty)
        {
            if (IndexedProperties.Add(indexedProperty) == false)
                throw new InvalidOperationException($"The property '{indexedProperty.Name}' is already registered as an index for entity '{Name}'.");
        }
    }
}
