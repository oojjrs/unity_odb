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
        public int ModelSchemaVersion { get; }

        internal OdbExportSession(OdbModelBuilder modelBuilder, Dictionary<Type, object> setsByType)
        {
            EntityBuilders = modelBuilder.EntityBuilders;
            EntityBuildersByType = modelBuilder.EntityBuildersByEntityType;
            ModelSchemaVersion = modelBuilder.ModelSchemaVersion;
            SetsByType = setsByType;
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
                throw new InvalidOperationException($"The entity type '{entityType.FullName}' is not registered in this export session.");

            return (IReadOnlyCollection<TEntity>)GetEntities(entityBuilder);
        }
    }
}
