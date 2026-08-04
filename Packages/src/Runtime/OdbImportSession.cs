using System;
using System.Collections.Generic;

namespace oojjrs.odb
{
    public sealed class OdbImportSession
    {
        private readonly IReadOnlyDictionary<Type, OdbEntityBuilderInterface> EntityBuildersByType;
        private readonly Dictionary<Type, object> SetsByType;

        internal IReadOnlyList<OdbEntityBuilderInterface> EntityBuilders { get; }
        public int ModelSchemaVersion { get; }

        internal OdbImportSession(OdbModelBuilder modelBuilder, Dictionary<Type, object> setsByType)
        {
            EntityBuilders = modelBuilder.EntityBuilders;
            EntityBuildersByType = modelBuilder.EntityBuildersByEntityType;
            ModelSchemaVersion = modelBuilder.ModelSchemaVersion;
            SetsByType = setsByType;
        }

        internal bool TryAdd(OdbEntityBuilderInterface entityBuilder, object entity)
        {
            return entityBuilder.TryAdd(SetsByType[entityBuilder.EntityType], entity);
        }

        public bool TryAdd<TEntity>(TEntity entity)
            where TEntity : class
        {
            var entityType = typeof(TEntity);
            if (EntityBuildersByType.TryGetValue(entityType, out var entityBuilder) == false)
                throw new InvalidOperationException($"The entity type '{entityType.FullName}' is not registered in this import session.");

            return entityBuilder.TryAdd(SetsByType[entityType], entity);
        }
    }
}
