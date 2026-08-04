using System;
using System.Collections.Generic;
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
        private const string FormatIdentifier = "unity-odb-json-1";

        private static readonly Dictionary<Type, DataContractJsonSerializer> __serializers = new();

        internal static async Task ExportAsync(OdbExportSession session, Stream destination, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var buffer = new MemoryStream())
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(buffer, Encoding.UTF8, false))
                {
                    writer.WriteStartElement("root");
                    writer.WriteAttributeString("type", "object");
                    writer.WriteElementString("format", FormatIdentifier);
                    writer.WriteStartElement("schemaVersion");
                    writer.WriteAttributeString("type", "number");
                    writer.WriteValue(session.ModelSchemaVersion);
                    writer.WriteEndElement();
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
                    if (reader.ReadElementContentAsString("format", string.Empty) != FormatIdentifier)
                        throw new InvalidDataException("The JSON snapshot format is not supported.");

                    var schemaVersion = reader.ReadElementContentAsInt("schemaVersion", string.Empty);
                    if (schemaVersion != session.ModelSchemaVersion)
                        throw new InvalidDataException(
                            $"The JSON snapshot schema version '{schemaVersion}' does not match model schema version '{session.ModelSchemaVersion}'.");

                    reader.ReadStartElement("entities");
                    foreach (var entityBuilder in session.EntityBuilders)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        reader.ReadStartElement("item");
                        var entityName = reader.ReadElementContentAsString("name", string.Empty);
                        if (entityName != entityBuilder.Name)
                            throw new InvalidDataException($"Expected entity '{entityBuilder.Name}' but found '{entityName}'.");

                        reader.ReadStartElement("rows");
                        var serializer = GetSerializer(entityBuilder.EntityType);
                        while (reader.IsStartElement("item"))
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var entity = serializer.ReadObject(reader, true);
                            if (entity == null)
                                throw new InvalidDataException($"Entity '{entityBuilder.Name}' contains a null row.");

                            if (session.TryAdd(entityBuilder, entity) == false)
                                throw new InvalidDataException($"Entity '{entityBuilder.Name}' contains a duplicate primary or unique key.");
                        }

                        reader.ReadEndElement();
                        reader.ReadEndElement();
                    }

                    reader.ReadEndElement();
                    reader.ReadEndElement();
                }
            }
        }
    }
}
