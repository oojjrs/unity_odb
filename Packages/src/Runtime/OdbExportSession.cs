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
        public int ModelSchemaVersion { get; }

        internal OdbExportSession(OdbModelBuilder modelBuilder, Dictionary<Type, object> setsByType, IReadOnlyCollection<Type> entityTypes)
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
