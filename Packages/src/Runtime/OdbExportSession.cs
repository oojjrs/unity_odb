using System;
using System.Collections;
using System.Collections.Generic;

namespace oojjrs.odb
{
    public sealed class OdbExportSession
    {
        private readonly IReadOnlyDictionary<Type, OdbEntityBuilderInterface> EntityBuildersByType;
        private readonly Dictionary<Type, object> SetsByType;

        internal IReadOnlyList<OdbEntityBuilderInterface> EntityBuilders { get; }
        public IReadOnlyList<Type> EntityTypes { get; }
        public IReadOnlyList<OdbIdentityState> IdentityStates { get; }
        public int ModelSchemaVersion { get; }

        internal OdbExportSession(OdbModelBuilder modelBuilder, IReadOnlyDictionary<string, OdbModelBuilder.IdentityRuntimeInterface> identitiesByStateKey, Dictionary<Type, object> setsByType, IReadOnlyCollection<Type> entityTypes)
        {
            ModelSchemaVersion = modelBuilder.ModelSchemaVersion;
            SetsByType = setsByType;

            if (entityTypes == null)
            {
                EntityBuilders = modelBuilder.EntityBuilders;
                EntityBuildersByType = modelBuilder.EntityBuildersByEntityType;
                var allEntityTypes = new List<Type>(modelBuilder.EntityBuilders.Count);
                foreach (var entityBuilder in modelBuilder.EntityBuilders)
                    allEntityTypes.Add(entityBuilder.EntityType);
                EntityTypes = allEntityTypes.AsReadOnly();
                IdentityStates = GetIdentityStates(GetIdentities(EntityBuilders, identitiesByStateKey));
                return;
            }

            var selectedEntityTypes = new HashSet<Type>();
            foreach (var entityType in entityTypes)
            {
                if (entityType == null)
                    throw new ArgumentException("An export entity type cannot be null.", nameof(entityTypes));

                if (modelBuilder.EntityBuildersByEntityType.ContainsKey(entityType) == false)
                    throw new InvalidOperationException($"The entity type '{entityType.FullName}' is not registered in this context.");

                selectedEntityTypes.Add(entityType);
            }

            var entityBuilders = new List<OdbEntityBuilderInterface>(selectedEntityTypes.Count);
            var entityBuildersByType = new Dictionary<Type, OdbEntityBuilderInterface>(selectedEntityTypes.Count);
            var orderedEntityTypes = new List<Type>(selectedEntityTypes.Count);
            foreach (var entityBuilder in modelBuilder.EntityBuilders)
            {
                if (selectedEntityTypes.Contains(entityBuilder.EntityType) == false)
                    continue;

                entityBuilders.Add(entityBuilder);
                entityBuildersByType.Add(entityBuilder.EntityType, entityBuilder);
                orderedEntityTypes.Add(entityBuilder.EntityType);
            }

            EntityBuilders = entityBuilders;
            EntityBuildersByType = entityBuildersByType;
            EntityTypes = orderedEntityTypes.AsReadOnly();
            IdentityStates = GetIdentityStates(GetIdentities(EntityBuilders, identitiesByStateKey));
        }

        private static IReadOnlyList<OdbModelBuilder.IdentityRuntimeInterface> GetIdentities(IReadOnlyList<OdbEntityBuilderInterface> entityBuilders, IReadOnlyDictionary<string, OdbModelBuilder.IdentityRuntimeInterface> identitiesByStateKey)
        {
            var identities = new List<OdbModelBuilder.IdentityRuntimeInterface>();
            var identityStateKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entityBuilder in entityBuilders)
            {
                var identity = entityBuilder.Identity;
                if ((identity == null) || (identityStateKeys.Add(identity.StateKey) == false))
                    continue;

                identities.Add(identitiesByStateKey[identity.StateKey]);
            }

            return identities;
        }

        private static IReadOnlyList<OdbIdentityState> GetIdentityStates(IReadOnlyList<OdbModelBuilder.IdentityRuntimeInterface> identities)
        {
            var identityStates = new List<OdbIdentityState>(identities.Count);
            foreach (var identity in identities)
                identityStates.Add(new OdbIdentityState(identity.Scope, identity.Name, identity.KeyTypeName, identity.HighWaterMark));
            return identityStates.AsReadOnly();
        }

        internal IEnumerable GetEntities(OdbEntityBuilderInterface entityBuilder)
        {
            return entityBuilder.GetEntities(SetsByType[entityBuilder.EntityType]);
        }

        public IReadOnlyCollection<TEntity> GetEntities<TEntity>()
            where TEntity : class
        {
            var entityType = typeof(TEntity);
            if (EntityBuildersByType.TryGetValue(entityType, out var entityBuilder) == false)
                throw new InvalidOperationException($"The entity type '{entityType.FullName}' is not included in this export session.");

            return (IReadOnlyCollection<TEntity>)GetEntities(entityBuilder);
        }
    }
}
