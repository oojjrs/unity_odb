using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace oojjrs.odb
{
    internal static class OdbXmlSnapshot
    {
        private const int ExportBufferSize = 81920;
        private const string FormatIdentifier = "unity-odb-xml-1";

        private static readonly XmlSerializerNamespaces __emptyNamespaces = CreateEmptyNamespaces();
        private static readonly Dictionary<Type, XmlSerializer> __serializers = new();
        private static readonly UTF8Encoding __utf8 = new(false, true);

        private static XmlSerializerNamespaces CreateEmptyNamespaces()
        {
            var namespaces = new XmlSerializerNamespaces();
            namespaces.Add(string.Empty, string.Empty);
            return namespaces;
        }

        internal static async Task ExportAsync(OdbExportSession session, Stream destination, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var buffer = new MemoryStream(ExportBufferSize))
            using (var writer = XmlWriter.Create(buffer, new XmlWriterSettings
            {
                CloseOutput = false,
                Encoding = __utf8,
                Indent = true,
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Entitize,
                OmitXmlDeclaration = false
            }))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("odbSnapshot");
                writer.WriteAttributeString("format", FormatIdentifier);
                writer.WriteAttributeString("schemaVersion", XmlConvert.ToString(session.ModelSchemaVersion));
                foreach (var entityBuilder in session.EntityBuilders)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    writer.WriteStartElement("entity");
                    writer.WriteAttributeString("name", entityBuilder.Name);
                    var serializer = GetSerializer(entityBuilder.EntityType);
                    foreach (var entity in session.GetEntities(entityBuilder))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        serializer.Serialize(writer, entity, __emptyNamespaces);
                        if (buffer.Length >= ExportBufferSize)
                            await FlushExportBufferAsync(writer, buffer, destination, cancellationToken);
                    }

                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
                writer.WriteEndDocument();
                await FlushExportBufferAsync(writer, buffer, destination, cancellationToken);
                await destination.FlushAsync(cancellationToken);
            }
        }

        private static async Task FlushExportBufferAsync(
            XmlWriter writer, MemoryStream buffer, Stream destination, CancellationToken cancellationToken)
        {
            writer.Flush();
            if (buffer.Length == 0)
                return;

            await destination.WriteAsync(buffer.GetBuffer(), 0, (int)buffer.Length, cancellationToken);
            buffer.Position = 0;
            buffer.SetLength(0);
        }

        private static string GetRequiredAttribute(XmlReader reader, string name)
        {
            var value = reader.GetAttribute(name);
            if (value != null)
                return value;

            throw new InvalidDataException($"Element '{reader.Name}' requires attribute '{name}'.");
        }

        private static XmlSerializer GetSerializer(Type entityType)
        {
            if (__serializers.TryGetValue(entityType, out var serializer))
                return serializer;

            serializer = new XmlSerializer(entityType, new XmlRootAttribute("row"));
            __serializers.Add(entityType, serializer);
            return serializer;
        }

        internal static async Task ImportAsync(OdbImportSession session, Stream source, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var buffer = new MemoryStream())
            {
                await source.CopyToAsync(buffer, 81920, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                buffer.Position = 0;
                using (var reader = XmlReader.Create(buffer, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                }))
                {
                    if (reader.MoveToContent() != XmlNodeType.Element || reader.IsStartElement("odbSnapshot") == false)
                        throw new InvalidDataException("The XML snapshot root is invalid.");

                    if (GetRequiredAttribute(reader, "format") != FormatIdentifier)
                        throw new InvalidDataException("The XML snapshot format is not supported.");

                    var schemaVersion = XmlConvert.ToInt32(GetRequiredAttribute(reader, "schemaVersion"));
                    if (schemaVersion != session.ModelSchemaVersion)
                        throw new InvalidDataException(
                            $"The XML snapshot schema version '{schemaVersion}' does not match model schema version '{session.ModelSchemaVersion}'.");

                    var snapshotIsEmpty = reader.IsEmptyElement;
                    reader.ReadStartElement("odbSnapshot");
                    foreach (var entityBuilder in session.EntityBuilders)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (snapshotIsEmpty)
                            throw new InvalidDataException("The XML snapshot does not contain every registered entity.");

                        if (reader.MoveToContent() != XmlNodeType.Element || reader.IsStartElement("entity") == false)
                            throw new InvalidDataException($"The XML snapshot does not contain entity '{entityBuilder.Name}'.");

                        var entityName = GetRequiredAttribute(reader, "name");
                        if (entityName != entityBuilder.Name)
                            throw new InvalidDataException($"Expected entity '{entityBuilder.Name}' but found '{entityName}'.");

                        var entityIsEmpty = reader.IsEmptyElement;
                        reader.ReadStartElement("entity");
                        if (entityIsEmpty)
                            continue;

                        var serializer = GetSerializer(entityBuilder.EntityType);
                        while (reader.MoveToContent() == XmlNodeType.Element)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            if (serializer.CanDeserialize(reader) == false)
                                throw new InvalidDataException($"Entity '{entityBuilder.Name}' contains an unexpected '{reader.Name}' element.");

                            var entity = serializer.Deserialize(reader);
                            if (entity == null)
                                throw new InvalidDataException($"Entity '{entityBuilder.Name}' contains a null row.");

                            if (session.TryAdd(entityBuilder, entity) == false)
                                throw new InvalidDataException($"Entity '{entityBuilder.Name}' contains a duplicate primary or unique key.");
                        }

                        if ((reader.NodeType != XmlNodeType.EndElement) || (reader.LocalName != "entity") || (reader.NamespaceURI.Length != 0))
                            throw new InvalidDataException($"Entity '{entityBuilder.Name}' is invalid.");

                        reader.ReadEndElement();
                    }

                    if (snapshotIsEmpty == false)
                    {
                        reader.MoveToContent();
                        if ((reader.NodeType != XmlNodeType.EndElement) || (reader.LocalName != "odbSnapshot") || (reader.NamespaceURI.Length != 0))
                            throw new InvalidDataException("The XML snapshot contains too many entities or an unexpected element.");

                        reader.ReadEndElement();
                    }

                    if (reader.MoveToContent() != XmlNodeType.None)
                        throw new InvalidDataException("The XML snapshot contains trailing content.");
                }
            }
        }
    }
}
