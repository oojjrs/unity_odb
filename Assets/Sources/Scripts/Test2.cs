using oojjrs.odb;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using UnityEngine;

namespace Assets.Sources.Scripts
{
    public sealed class Test2 : MonoBehaviour
    {
        public sealed class Item
        {
            public string Code { get; set; }
            public string Group { get; set; }
            public int Id { get; set; }
            [XmlElement(IsNullable = true)]
            public string Name { get; set; }
            public int OwnerId { get; set; }
        }

        private sealed class OwnerItemExporter : OdbExporterInterface
        {
            private readonly int OwnerId;

            internal OwnerItemExporter(int ownerId)
            {
                OwnerId = ownerId;
            }

            async Task OdbExporterInterface.ExportAsync(
                OdbExportSession session, Stream destination, CancellationToken cancellationToken)
            {
                foreach (var item in session.GetEntities<Item>())
                {
                    if (item.OwnerId != OwnerId)
                        continue;

                    var bytes = Encoding.UTF8.GetBytes(item.Code);
                    await destination.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
                }
            }
        }

        private sealed class TestOdbContext : OdbContext
        {
            internal OdbSet<Item, int> Items => GetSet<Item, int>();

            protected override void OnModelCreating(OdbModelBuilder modelBuilder)
            {
                modelBuilder.SetSchemaVersion(3);
                modelBuilder.AddEntity<Item, int>("Item", item => item.Id)
                    .AddIndex(item => item.Group, StringComparer.Ordinal)
                    .AddIndex(item => item.OwnerId)
                    .AddUniqueIndex(item => item.Code, StringComparer.OrdinalIgnoreCase);
            }
        }

        private const string InitialData = @"<?xml version=""1.0"" encoding=""utf-8""?>
<odbSnapshot format=""unity-odb-xml-1"" schemaVersion=""3"">
  <entity name=""Item"">
    <row>
      <Code>potion</Code>
      <Group>consumable</Group>
      <Id>1</Id>
      <Name>Potion&#xD;&#xA;Line2&#xD;Line3&#xA;Line4</Name>
      <OwnerId>10</OwnerId>
    </row>
    <row>
      <Code>sword</Code>
      <Group>equipment</Group>
      <Id>2</Id>
      <Name />
      <OwnerId>10</OwnerId>
    </row>
    <row>
      <Code>shield</Code>
      <Group>equipment</Group>
      <Id>3</Id>
      <Name xsi:nil=""true"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" />
      <OwnerId>20</OwnerId>
    </row>
  </entity>
</odbSnapshot>";

        private async void Start()
        {
            var importer = new OdbXmlSnapshotImporter();
            var database = new TestOdbContext();
            var originalItems = database.Items;
            originalItems.Add(new Item { Code = "old", Group = "old", Id = 99, Name = "Old", OwnerId = 99 });
            using (var source = new MemoryStream(Encoding.UTF8.GetBytes(InitialData), false))
                await database.InitializeAsync(source, importer);
            Require(ReferenceEquals(originalItems, database.Items), "Initialization replaced the ODB set instance.");
            Require(originalItems.TryFind(99, out _) == false, "Initialization did not clear the previous database contents.");
            ValidateDatabase(database);

            using (var destination = new MemoryStream())
            {
                await database.ExportAsync(destination, new OdbXmlSnapshotExporter());
                Require(destination.Length > 0, "The XML export is empty.");
                var bytes = destination.ToArray();
                Require((bytes.Length < 3) || (bytes[0] != 0xef) || (bytes[1] != 0xbb) || (bytes[2] != 0xbf), "The XML export contains a UTF-8 BOM.");

                destination.Position = 0;
                var roundTripDatabase = new TestOdbContext();
                await roundTripDatabase.InitializeAsync(destination, importer);
                ValidateDatabase(roundTripDatabase);
            }

            using (var partialDestination = new MemoryStream())
            {
                await database.ExportAsync(partialDestination, new OwnerItemExporter(20));
                Require(Encoding.UTF8.GetString(partialDestination.ToArray()) == "shield", "The partial exporter wrote an invalid row selection.");
            }

            Debug.Log($"{nameof(Test2)}> PASSED.");

            static void Require(bool condition, string message)
            {
                if (condition == false)
                    throw new InvalidOperationException(message);
            }

            static void ValidateDatabase(TestOdbContext target)
            {
                Require(target.Items.Count == 3, "The imported entity count is invalid.");
                Require(target.Items.TryFind(1, out var primaryItem), "The imported primary index lookup failed.");
                Require(primaryItem.Code == "potion", "The imported primary index is invalid.");
                Require(primaryItem.Name == "Potion\r\nLine2\rLine3\nLine4", "The imported line endings are invalid.");
                Require(target.Items.TryFindBy(item => item.Code, "SWORD", out var uniqueItem), "The imported unique index lookup failed.");
                Require(uniqueItem.Id == 2, "The imported unique index is invalid.");
                Require(uniqueItem.Name == string.Empty, "The imported empty string is invalid.");
                Require(target.Items.TryFind(3, out var nullNameItem), "The imported null value item is missing.");
                Require(nullNameItem.Name == null, "The imported null value is invalid.");
                Require(target.Items.FindBy(item => item.Group, "equipment").Count == 2, "The imported group index is invalid.");
                Require(target.Items.FindBy(item => item.OwnerId, 10).Count == 2, "The imported owner index is invalid.");
                Require(target.Items.ValidateIndexes(), "The imported indexes are inconsistent.");
            }
        }
    }
}
