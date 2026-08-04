using System;
using System.Collections.Generic;

namespace oojjrs.odb
{
    public sealed class OdbModelBuilder
    {
        private readonly Dictionary<Type, OdbEntityBuilderInterface> EntityBuildersByType = new();
        private readonly HashSet<string> EntityNames = new(StringComparer.Ordinal);
        private readonly List<OdbEntityBuilderInterface> RegisteredEntityBuilders = new();

        private bool _isFrozen;
        private int _schemaVersion = 1;

        internal IReadOnlyList<OdbEntityBuilderInterface> EntityBuilders => RegisteredEntityBuilders;
        internal IReadOnlyDictionary<Type, OdbEntityBuilderInterface> EntityBuildersByEntityType => EntityBuildersByType;
        internal int ModelSchemaVersion => _schemaVersion;

        internal OdbModelBuilder()
        {
        }

        public OdbEntityBuilder<TEntity, TKey> AddEntity<TEntity, TKey>(
            string name, Func<TEntity, TKey> keySelector, IEqualityComparer<TKey> keyComparer = null)
            where TEntity : class
            where TKey : notnull
        {
            EnsureMutable();

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("An entity name is required.", nameof(name));

            if (keySelector == null)
                throw new ArgumentNullException(nameof(keySelector));

            var entityType = typeof(TEntity);
            if (EntityBuildersByType.ContainsKey(entityType))
                throw new InvalidOperationException($"The entity type '{entityType.FullName}' is already registered.");

            if (EntityNames.Add(name) == false)
                throw new InvalidOperationException($"The entity name '{name}' is already registered.");

            var entityBuilder = new OdbEntityBuilder<TEntity, TKey>(name, keySelector, keyComparer ?? EqualityComparer<TKey>.Default);
            EntityBuildersByType.Add(entityType, entityBuilder);
            RegisteredEntityBuilders.Add(entityBuilder);
            return entityBuilder;
        }

        internal Dictionary<Type, object> CreateSets()
        {
            var sets = new Dictionary<Type, object>(RegisteredEntityBuilders.Count);
            foreach (var entityBuilder in RegisteredEntityBuilders)
                sets.Add(entityBuilder.EntityType, entityBuilder.CreateSet());
            return sets;
        }

        private void EnsureMutable()
        {
            if (_isFrozen)
                throw new InvalidOperationException("The database model is already frozen.");
        }

        internal void Freeze()
        {
            if (_isFrozen)
                return;

            foreach (var entityBuilder in RegisteredEntityBuilders)
                entityBuilder.Freeze();
            _isFrozen = true;
        }

        public void SetSchemaVersion(int schemaVersion)
        {
            EnsureMutable();

            if (schemaVersion < 1)
                throw new ArgumentOutOfRangeException(nameof(schemaVersion), schemaVersion, "The schema version must be greater than zero.");

            _schemaVersion = schemaVersion;
        }
    }
}
