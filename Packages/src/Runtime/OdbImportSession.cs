using System;
using System.Collections.Generic;
using System.IO;

namespace oojjrs.odb
{
    public sealed class OdbImportSession
    {
        private readonly IReadOnlyDictionary<Type, OdbEntityBuilderInterface> EntityBuildersByType;
        private readonly IReadOnlyDictionary<string, OdbModelBuilder.IdentityRuntimeInterface> IdentitiesByStateKey;
        private readonly Dictionary<Type, object> SetsByType;

        internal IReadOnlyList<OdbEntityBuilderInterface> EntityBuilders { get; }
        public int ModelSchemaVersion { get; }

        internal OdbImportSession(OdbModelBuilder modelBuilder, IReadOnlyDictionary<string, OdbModelBuilder.IdentityRuntimeInterface> identitiesByStateKey, Dictionary<Type, object> setsByType)
        {
            EntityBuilders = modelBuilder.EntityBuilders;
            EntityBuildersByType = modelBuilder.EntityBuildersByEntityType;
            IdentitiesByStateKey = identitiesByStateKey;
            ModelSchemaVersion = modelBuilder.ModelSchemaVersion;
            SetsByType = setsByType;
        }

        public void AdvanceIdentityState(string scope, string name, string keyTypeName, long highWaterMark)
        {
            if (string.IsNullOrWhiteSpace(scope))
                throw new ArgumentException("An identity scope is required.", nameof(scope));

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("An identity name is required.", nameof(name));

            if (string.IsNullOrWhiteSpace(keyTypeName))
                throw new ArgumentException("An identity key type name is required.", nameof(keyTypeName));

            if (IdentitiesByStateKey.TryGetValue(scope + "\0" + name, out var identity) == false)
                throw new InvalidDataException($"The identity '{name}' is not registered in scope '{scope}'.");

            if (identity.KeyTypeName != keyTypeName)
                throw new InvalidDataException($"The identity '{name}' uses key type '{identity.KeyTypeName}', not '{keyTypeName}'.");

            try
            {
                identity.AdvanceToAtLeast(highWaterMark);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new InvalidDataException($"The identity '{name}' contains an invalid high-water mark.", exception);
            }
        }

        internal void AdvanceIdentityStates(IReadOnlyList<OdbIdentityState> identityStates)
        {
            var actualStateKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var identityState in identityStates)
            {
                if (string.IsNullOrWhiteSpace(identityState.Scope) || string.IsNullOrWhiteSpace(identityState.Name) || string.IsNullOrWhiteSpace(identityState.KeyTypeName))
                    throw new InvalidDataException("The snapshot contains incomplete identity generator state.");

                if (actualStateKeys.Add(identityState.StateKey) == false)
                    throw new InvalidDataException($"The snapshot contains identity '{identityState.Name}' more than once in scope '{identityState.Scope}'.");

                AdvanceIdentityState(identityState.Scope, identityState.Name, identityState.KeyTypeName, identityState.HighWaterMark);
            }
        }

        internal void ValidateIdentityStatesExact(IReadOnlyList<OdbIdentityState> identityStates, IReadOnlyCollection<OdbEntityBuilderInterface> importedEntityBuilders)
        {
            var actualStateKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var identityState in identityStates)
                actualStateKeys.Add(identityState.StateKey);

            var expectedStateKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entityBuilder in importedEntityBuilders)
                if (entityBuilder.Identity != null)
                    expectedStateKeys.Add(entityBuilder.Identity.StateKey);

            if (actualStateKeys.SetEquals(expectedStateKeys) == false)
                throw new InvalidDataException("The snapshot identity generator states do not match its generated entity models.");
        }

        internal bool TryAdd(OdbEntityBuilderInterface entityBuilder, object entity)
        {
            return entityBuilder.TryAddExisting(SetsByType[entityBuilder.EntityType], entity);
        }

        internal bool TryGetEntityBuilder(string entityName, out OdbEntityBuilderInterface entityBuilder)
        {
            foreach (var candidate in EntityBuilders)
            {
                if (candidate.Name != entityName)
                    continue;

                entityBuilder = candidate;
                return true;
            }

            entityBuilder = null;
            return false;
        }

        public bool TryAdd<TEntity>(TEntity entity)
            where TEntity : class
        {
            var entityType = typeof(TEntity);
            if (EntityBuildersByType.TryGetValue(entityType, out var entityBuilder) == false)
                throw new InvalidOperationException($"The entity type '{entityType.FullName}' is not registered in this import session.");

            return entityBuilder.TryAddExisting(SetsByType[entityType], entity);
        }
    }
}
