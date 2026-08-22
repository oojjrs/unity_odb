using System;
using System.Collections.Generic;
using System.Globalization;
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
        private const string IdentityFormatIdentifier = "unity-odb-xml-2";
        private const string LegacyFormatIdentifier = "unity-odb-xml-1";

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
                var hasIdentities = session.IdentityStates.Count > 0;
                writer.WriteStartDocument();
                writer.WriteStartElement("odbSnapshot");
                writer.WriteAttributeString("format", hasIdentities ? IdentityFormatIdentifier : LegacyFormatIdentifier);
                writer.WriteAttributeString("schemaVersion", XmlConvert.ToString(session.ModelSchemaVersion));
                if (hasIdentities)
                    WriteIdentityStates(session, writer);
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

        private static IReadOnlyList<OdbIdentityState> ReadIdentityStates(XmlReader reader)
        {
            var identityStates = new List<OdbIdentityState>();
            var identitiesAreEmpty = reader.IsEmptyElement;
            reader.ReadStartElement("identityGenerators");
            if (identitiesAreEmpty == false)
            {
                while ((reader.MoveToContent() == XmlNodeType.Element) && reader.IsStartElement("identity"))
                {
                    if (reader.IsEmptyElement == false)
                        throw new InvalidDataException("An XML snapshot identity element must be empty.");

                    var scope = GetRequiredAttribute(reader, "scope");
                    var name = GetRequiredAttribute(reader, "name");
                    var keyTypeName = GetRequiredAttribute(reader, "keyType");
                    var highWaterMarkText = GetRequiredAttribute(reader, "highWaterMark");
                    if (long.TryParse(highWaterMarkText, NumberStyles.None, CultureInfo.InvariantCulture, out var highWaterMark) == false)
                        throw new InvalidDataException($"The XML snapshot identity '{name}' contains an invalid high-water mark.");

                    identityStates.Add(new OdbIdentityState(scope, name, keyTypeName, highWaterMark));
                    reader.ReadStartElement("identity");
                }

                reader.ReadEndElement();
            }

            return identityStates;
        }

        private static void ImportEntityRows(OdbImportSession session, XmlReader reader, OdbEntityBuilderInterface entityBuilder, CancellationToken cancellationToken)
        {
            var entityIsEmpty = reader.IsEmptyElement;
            reader.ReadStartElement("entity");
            if (entityIsEmpty)
                return;

            var serializer = GetSerializer(entityBuilder.EntityType);
            while (reader.MoveToContent() == XmlNodeType.Element)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (serializer.CanDeserialize(reader) == false)
                    throw new InvalidDataException($"Entity '{entityBuilder.Name}' contains an unexpected '{reader.Name}' element.");

                var entity = serializer.Deserialize(reader);
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

            if ((reader.NodeType != XmlNodeType.EndElement) || (reader.LocalName != "entity") || (reader.NamespaceURI.Length != 0))
                throw new InvalidDataException($"Entity '{entityBuilder.Name}' is invalid.");

            reader.ReadEndElement();
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

                    var formatIdentifier = GetRequiredAttribute(reader, "format");
                    var hasIdentityStates = formatIdentifier == IdentityFormatIdentifier;
                    if ((hasIdentityStates == false) && (formatIdentifier != LegacyFormatIdentifier))
                        throw new InvalidDataException("The XML snapshot format is not supported.");

                    var schemaVersion = XmlConvert.ToInt32(GetRequiredAttribute(reader, "schemaVersion"));
                    if (schemaVersion != session.ModelSchemaVersion)
                        throw new InvalidDataException(
                            $"The XML snapshot schema version '{schemaVersion}' does not match model schema version '{session.ModelSchemaVersion}'.");

                    var snapshotIsEmpty = reader.IsEmptyElement;
                    if (hasIdentityStates && snapshotIsEmpty)
                        throw new InvalidDataException("The XML snapshot does not contain identity generator state.");

                    reader.ReadStartElement("odbSnapshot");
                    IReadOnlyList<OdbIdentityState> identityStates = null;
                    var importedEntityBuilders = new List<OdbEntityBuilderInterface>();
                    if (snapshotIsEmpty == false)
                    {
                        if (hasIdentityStates)
                        {
                            reader.MoveToContent();
                            if (reader.IsStartElement("identityGenerators") == false)
                                throw new InvalidDataException("The XML snapshot does not contain identity generator state.");

                            identityStates = ReadIdentityStates(reader);
                            session.AdvanceIdentityStates(identityStates);
                        }

                        var importedEntityNames = new HashSet<string>(StringComparer.Ordinal);
                        while ((reader.MoveToContent() == XmlNodeType.Element) && reader.IsStartElement("entity"))
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var entityName = GetRequiredAttribute(reader, "name");
                            if (importedEntityNames.Add(entityName) == false)
                                throw new InvalidDataException($"The XML snapshot contains entity '{entityName}' more than once.");

                            if (session.TryGetEntityBuilder(entityName, out var entityBuilder) == false)
                                throw new InvalidDataException($"The entity '{entityName}' is not registered in this import session.");

                            importedEntityBuilders.Add(entityBuilder);
                            ImportEntityRows(session, reader, entityBuilder, cancellationToken);
                        }
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

                    if (hasIdentityStates)
                        session.ValidateIdentityStatesExact(identityStates, importedEntityBuilders);
                }
            }
        }

        private static void WriteIdentityStates(OdbExportSession session, XmlWriter writer)
        {
            writer.WriteStartElement("identityGenerators");
            foreach (var identity in session.IdentityStates)
            {
                writer.WriteStartElement("identity");
                writer.WriteAttributeString("scope", identity.Scope);
                writer.WriteAttributeString("name", identity.Name);
                writer.WriteAttributeString("keyType", identity.KeyTypeName);
                writer.WriteAttributeString("highWaterMark", identity.HighWaterMark.ToString(CultureInfo.InvariantCulture));
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }
    }
}
