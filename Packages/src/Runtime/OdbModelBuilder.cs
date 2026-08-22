using System;
using System.Collections.Generic;

namespace oojjrs.odb
{
    public sealed class OdbModelBuilder
    {
        internal interface IdentityInterface
        {
            bool HasParticipants { get; }
            string Name { get; }
            string Scope { get; }
            string StateKey { get; }

            IdentityRuntimeInterface CreateRuntime();
        }

        internal interface IdentityRuntimeInterface
        {
            string KeyTypeName { get; }
            long HighWaterMark { get; }
            string Name { get; }
            string Scope { get; }
            string StateKey { get; }

            void AdvanceToAtLeast(long highWaterMark);
            void Reset();
        }

        internal const string EntityIdentityScope = "entity";
        internal const string GlobalIdentityScope = "global";

        private readonly Dictionary<Type, OdbEntityBuilderInterface> EntityBuildersByType = new();
        private readonly HashSet<string> EntityNames = new(StringComparer.Ordinal);
        private readonly Dictionary<string, IdentityInterface> IdentitiesByStateKey = new(StringComparer.Ordinal);
        private readonly List<OdbEntityBuilderInterface> RegisteredEntityBuilders = new();
        private readonly List<IdentityInterface> RegisteredIdentities = new();

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

            var entityBuilder = new OdbEntityBuilder<TEntity, TKey>(this, name, keySelector, keyComparer ?? EqualityComparer<TKey>.Default);
            EntityBuildersByType.Add(entityType, entityBuilder);
            RegisteredEntityBuilders.Add(entityBuilder);
            return entityBuilder;
        }

        public OdbIdentity<TKey> AddGlobalIdentity<TKey>(string name) where TKey : notnull
        {
            EnsureMutable();

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A global identity name is required.", nameof(name));

            var identity = new OdbIdentity<TKey>(this, GlobalIdentityScope, name, true);
            RegisterIdentity(identity.DefinitionInterface);
            return identity;
        }

        internal OdbIdentity<TKey> AddEntityIdentity<TKey>(string entityName) where TKey : notnull
        {
            var identity = new OdbIdentity<TKey>(this, EntityIdentityScope, entityName, false);
            RegisterIdentity(identity.DefinitionInterface);
            return identity;
        }

        internal Dictionary<string, IdentityRuntimeInterface> CreateIdentityRuntimes()
        {
            var identities = new Dictionary<string, IdentityRuntimeInterface>(RegisteredIdentities.Count, StringComparer.Ordinal);
            foreach (var identity in RegisteredIdentities)
                identities.Add(identity.StateKey, identity.CreateRuntime());
            return identities;
        }

        internal Dictionary<Type, object> CreateSets(IReadOnlyDictionary<string, IdentityRuntimeInterface> identities)
        {
            var sets = new Dictionary<Type, object>(RegisteredEntityBuilders.Count);
            foreach (var entityBuilder in RegisteredEntityBuilders)
                sets.Add(entityBuilder.EntityType, entityBuilder.CreateSet(identities));
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

            foreach (var identity in RegisteredIdentities)
                if (identity.HasParticipants == false)
                    throw new InvalidOperationException($"The global identity '{identity.Name}' is not used by an entity model.");

            foreach (var entityBuilder in RegisteredEntityBuilders)
                entityBuilder.Freeze();
            _isFrozen = true;
        }

        private void RegisterIdentity(IdentityInterface identity)
        {
            if (IdentitiesByStateKey.TryAdd(identity.StateKey, identity) == false)
                throw new InvalidOperationException($"The identity '{identity.Name}' is already registered in scope '{identity.Scope}'.");

            RegisteredIdentities.Add(identity);
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
