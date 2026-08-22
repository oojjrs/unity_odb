using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace oojjrs.odb
{
    internal static class OdbJsonSnapshot
    {
        private const int BufferSize = 81920;
        private const string IdentityFormatIdentifier = "unity-odb-json-2";
        private const string LegacyFormatIdentifier = "unity-odb-json-1";

        private static readonly Dictionary<Type, DataContractJsonSerializer> __serializers = new();

        internal static async Task ExportAsync(OdbExportSession session, Stream destination, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var buffer = new MemoryStream())
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(buffer, Encoding.UTF8, false))
                {
                    var hasIdentities = session.IdentityStates.Count > 0;
                    writer.WriteStartElement("root");
                    writer.WriteAttributeString("type", "object");
                    writer.WriteElementString("format", hasIdentities ? IdentityFormatIdentifier : LegacyFormatIdentifier);
                    writer.WriteStartElement("schemaVersion");
                    writer.WriteAttributeString("type", "number");
                    writer.WriteValue(session.ModelSchemaVersion);
                    writer.WriteEndElement();
                    if (hasIdentities)
                        WriteIdentityStates(session, writer);
                    writer.WriteStartElement("entities");
                    writer.WriteAttributeString("type", "array");
                    foreach (var entityBuilder in session.EntityBuilders)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        writer.WriteStartElement("item");
                        writer.WriteAttributeString("type", "object");
                        writer.WriteElementString("name", entityBuilder.Name);
                        writer.WriteStartElement("rows");
                        writer.WriteAttributeString("type", "array");
                        var serializer = GetSerializer(entityBuilder.EntityType);
                        foreach (var entity in session.GetEntities(entityBuilder))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            serializer.WriteObject(writer, entity);
                        }

                        writer.WriteEndElement();
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                    writer.WriteEndElement();
                }

                buffer.Position = 0;
                await buffer.CopyToAsync(destination, BufferSize, cancellationToken);
                await destination.FlushAsync(cancellationToken);
            }
        }

        private static DataContractJsonSerializer GetSerializer(Type entityType)
        {
            if (__serializers.TryGetValue(entityType, out var serializer))
                return serializer;

            serializer = new DataContractJsonSerializer(entityType, "item");
            __serializers.Add(entityType, serializer);
            return serializer;
        }

        private static IReadOnlyList<OdbIdentityState> ReadIdentityStates(XmlReader reader)
        {
            var identityStates = new List<OdbIdentityState>();
            var identitiesAreEmpty = reader.IsEmptyElement;
            reader.ReadStartElement("identityGenerators");
            if (identitiesAreEmpty == false)
            {
                while (reader.IsStartElement("item"))
                {
                    reader.ReadStartElement("item");
                    var scope = reader.ReadElementContentAsString("scope", string.Empty);
                    var name = reader.ReadElementContentAsString("name", string.Empty);
                    var keyTypeName = reader.ReadElementContentAsString("keyType", string.Empty);
                    var highWaterMarkText = reader.ReadElementContentAsString("highWaterMark", string.Empty);
                    if (long.TryParse(highWaterMarkText, NumberStyles.None, CultureInfo.InvariantCulture, out var highWaterMark) == false)
                        throw new InvalidDataException($"The JSON snapshot identity '{name}' contains an invalid high-water mark.");

                    identityStates.Add(new OdbIdentityState(scope, name, keyTypeName, highWaterMark));
                    reader.ReadEndElement();
                }

                reader.ReadEndElement();
            }

            return identityStates;
        }

        private static void ImportEntityRows(OdbImportSession session, XmlReader reader, OdbEntityBuilderInterface entityBuilder, CancellationToken cancellationToken)
        {
            var rowsAreEmpty = reader.IsEmptyElement;
            reader.ReadStartElement("rows");
            if (rowsAreEmpty == false)
            {
                var serializer = GetSerializer(entityBuilder.EntityType);
                while (reader.IsStartElement("item"))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var entity = serializer.ReadObject(reader, true);
                    if (entity == null)
                        throw new InvalidDataException($"Entity '{entityBuilder.Name}' contains a null row.");

                    try
                    {
                        if (session.TryAdd(entityBuilder, entity) == false)
                            throw new InvalidDataException($"Entity '{entityBuilder.Name}' contains a duplicate primary or unique key.");
                    }
                    catch (OdbGeneratedPrimaryKeyException exception)
                    {
                        throw new InvalidDataException($"Entity '{entityBuilder.Name}' contains an invalid generated primary key.", exception);
                    }
                }

                reader.ReadEndElement();
            }

            reader.ReadEndElement();
        }

        internal static async Task ImportAsync(OdbImportSession session, Stream source, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var buffer = new MemoryStream())
            {
                await source.CopyToAsync(buffer, BufferSize, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                buffer.Position = 0;
                using (var reader = JsonReaderWriterFactory.CreateJsonReader(buffer, XmlDictionaryReaderQuotas.Max))
                {
                    reader.MoveToContent();
                    reader.ReadStartElement("root");
                    var formatIdentifier = reader.ReadElementContentAsString("format", string.Empty);
                    var hasIdentityStates = formatIdentifier == IdentityFormatIdentifier;
                    if ((hasIdentityStates == false) && (formatIdentifier != LegacyFormatIdentifier))
                        throw new InvalidDataException("The JSON snapshot format is not supported.");

                    var schemaVersion = reader.ReadElementContentAsInt("schemaVersion", string.Empty);
                    if (schemaVersion != session.ModelSchemaVersion)
                        throw new InvalidDataException(
                            $"The JSON snapshot schema version '{schemaVersion}' does not match model schema version '{session.ModelSchemaVersion}'.");

                    IReadOnlyList<OdbIdentityState> identityStates = null;
                    if (hasIdentityStates)
                    {
                        if (reader.IsStartElement("identityGenerators") == false)
                            throw new InvalidDataException("The JSON snapshot does not contain identity generator state.");

                        identityStates = ReadIdentityStates(reader);
                        session.AdvanceIdentityStates(identityStates);
                    }

                    var entitiesAreEmpty = reader.IsEmptyElement;
                    reader.ReadStartElement("entities");
                    var importedEntityBuilders = new List<OdbEntityBuilderInterface>();
                    if (entitiesAreEmpty == false)
                    {
                        var importedEntityNames = new HashSet<string>(StringComparer.Ordinal);
                        while (reader.IsStartElement("item"))
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            reader.ReadStartElement("item");
                            var entityName = reader.ReadElementContentAsString("name", string.Empty);
                            if (importedEntityNames.Add(entityName) == false)
                                throw new InvalidDataException($"The JSON snapshot contains entity '{entityName}' more than once.");

                            if (session.TryGetEntityBuilder(entityName, out var entityBuilder) == false)
                                throw new InvalidDataException($"The entity '{entityName}' is not registered in this import session.");

                            importedEntityBuilders.Add(entityBuilder);
                            ImportEntityRows(session, reader, entityBuilder, cancellationToken);
                        }
                    }

                    if (entitiesAreEmpty == false)
                        reader.ReadEndElement();
                    reader.ReadEndElement();
                    if (hasIdentityStates)
                        session.ValidateIdentityStatesExact(identityStates, importedEntityBuilders);
                }
            }
        }

        private static void WriteIdentityStates(OdbExportSession session, XmlWriter writer)
        {
            writer.WriteStartElement("identityGenerators");
            writer.WriteAttributeString("type", "array");
            foreach (var identity in session.IdentityStates)
            {
                writer.WriteStartElement("item");
                writer.WriteAttributeString("type", "object");
                writer.WriteElementString("scope", identity.Scope);
                writer.WriteElementString("name", identity.Name);
                writer.WriteElementString("keyType", identity.KeyTypeName);
                writer.WriteElementString("highWaterMark", identity.HighWaterMark.ToString(CultureInfo.InvariantCulture));
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }
    }
}
