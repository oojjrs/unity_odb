using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace oojjrs.odb
{
    public abstract class OdbContext
    {
        private bool _isBuildingModel;
        private Dictionary<string, OdbModelBuilder.IdentityRuntimeInterface> _identities;
        private OdbModelBuilder _modelBuilder;
        private Dictionary<Type, object> _sets;

        private void EnsureModelInitialized()
        {
            if (_sets != null)
                return;

            if (_isBuildingModel)
                throw new InvalidOperationException("The database model cannot be accessed while it is being created.");

            _isBuildingModel = true;
            try
            {
                _modelBuilder = new OdbModelBuilder();
                OnModelCreating(_modelBuilder);
                _modelBuilder.Freeze();

                _identities = _modelBuilder.CreateIdentityRuntimes();
                _sets = _modelBuilder.CreateSets(_identities);
            }
            finally
            {
                _isBuildingModel = false;
            }
        }

        public Task ExportAsync(Stream destination, OdbExporterInterface exporter, CancellationToken cancellationToken = default)
        {
            return ExportCoreAsync(destination, exporter, null, cancellationToken);
        }

        public Task ExportAsync(Stream destination, OdbExporterInterface exporter, CancellationToken cancellationToken, params Type[] entityTypes)
        {
            if (entityTypes == null)
                throw new ArgumentNullException(nameof(entityTypes));

            return ExportCoreAsync(destination, exporter, entityTypes, cancellationToken);
        }

        private async Task ExportCoreAsync(Stream destination, OdbExporterInterface exporter, IReadOnlyCollection<Type> entityTypes, CancellationToken cancellationToken)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            if (exporter == null)
                throw new ArgumentNullException(nameof(exporter));

            cancellationToken.ThrowIfCancellationRequested();
            EnsureModelInitialized();
            await exporter.ExportAsync(new OdbExportSession(_modelBuilder, _identities, _sets, entityTypes), destination, cancellationToken);
        }

        protected OdbSet<TEntity, TKey> GetSet<TEntity, TKey>()
            where TEntity : class
            where TKey : notnull
        {
            EnsureModelInitialized();

            var entityType = typeof(TEntity);
            if (_sets.TryGetValue(entityType, out var value) == false)
                throw new InvalidOperationException($"The entity type '{entityType.FullName}' is not registered in this context.");

            if (value is OdbSet<TEntity, TKey> set)
                return set;

            throw new InvalidOperationException($"The entity '{entityType.FullName}' was requested with a different primary key type.");
        }

        public async Task ImportAsync(Stream source, OdbImporterInterface importer, CancellationToken cancellationToken = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (importer == null)
                throw new ArgumentNullException(nameof(importer));

            cancellationToken.ThrowIfCancellationRequested();
            EnsureModelInitialized();
            await importer.ImportAsync(new OdbImportSession(_modelBuilder, _identities, _sets), source, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }

        public async Task InitializeAsync(Stream source, OdbImporterInterface importer, CancellationToken cancellationToken = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (importer == null)
                throw new ArgumentNullException(nameof(importer));

            cancellationToken.ThrowIfCancellationRequested();
            EnsureModelInitialized();
            foreach (var entityBuilder in _modelBuilder.EntityBuilders)
                entityBuilder.ResetSet(_sets[entityBuilder.EntityType]);
            foreach (var identity in _identities.Values)
                identity.Reset();
            await importer.ImportAsync(new OdbImportSession(_modelBuilder, _identities, _sets), source, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }

        protected abstract void OnModelCreating(OdbModelBuilder modelBuilder);
    }
}
