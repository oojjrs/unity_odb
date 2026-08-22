using oojjrs.odb;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Assets.Sources.Scripts
{
    public sealed class Test5 : MonoBehaviour
    {
        public sealed class Item
        {
            public string Code { get; set; }
            public string Group { get; set; }
            public int Id { get; set; }
            public string Name { get; set; }
            public int OwnerId { get; set; }
        }

        public sealed class Owner
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Region { get; set; }
        }

        private sealed class TestOdbContext : OdbContext
        {
            internal OdbSet<Item, int> Items => GetSet<Item, int>();
            internal OdbSet<Owner, int> Owners => GetSet<Owner, int>();

            protected override void OnModelCreating(OdbModelBuilder modelBuilder)
            {
                modelBuilder.SetSchemaVersion(5);
                modelBuilder.AddEntity<Item, int>("Item", item => item.Id).AddIndex(item => item.Group, StringComparer.Ordinal).AddIndex(item => item.OwnerId).AddUniqueIndex(item => item.Code, StringComparer.OrdinalIgnoreCase);
                modelBuilder.AddEntity<Owner, int>("Owner", owner => owner.Id).AddIndex(owner => owner.Region, StringComparer.Ordinal).AddUniqueIndex(owner => owner.Name, StringComparer.OrdinalIgnoreCase);
            }
        }

        private const string ItemJsonData = @"{""format"":""unity-odb-json-1"",""schemaVersion"":5,""entities"":[{""name"":""Item"",""rows"":[{""Code"":""potion"",""Group"":""consumable"",""Id"":1,""Name"":""Potion"",""OwnerId"":10},{""Code"":""sword"",""Group"":""equipment"",""Id"":2,""Name"":""Sword"",""OwnerId"":10},{""Code"":""shield"",""Group"":""equipment"",""Id"":3,""Name"":""Shield"",""OwnerId"":20}]}]}";
        private const string ItemXmlData = @"<odbSnapshot format=""unity-odb-xml-1"" schemaVersion=""5""><entity name=""Item""><row><Code>potion</Code><Group>consumable</Group><Id>1</Id><Name>Potion</Name><OwnerId>10</OwnerId></row><row><Code>sword</Code><Group>equipment</Group><Id>2</Id><Name>Sword</Name><OwnerId>10</OwnerId></row><row><Code>shield</Code><Group>equipment</Group><Id>3</Id><Name>Shield</Name><OwnerId>20</OwnerId></row></entity></odbSnapshot>";
        private const string OwnerJsonData = @"{""format"":""unity-odb-json-1"",""schemaVersion"":5,""entities"":[{""name"":""Owner"",""rows"":[{""Id"":10,""Name"":""Alice"",""Region"":""north""},{""Id"":20,""Name"":""Bob"",""Region"":""south""},{""Id"":30,""Name"":""Carol"",""Region"":""north""}]}]}";
        private const string OwnerXmlData = @"<odbSnapshot format=""unity-odb-xml-1"" schemaVersion=""5""><entity name=""Owner""><row><Id>10</Id><Name>Alice</Name><Region>north</Region></row><row><Id>20</Id><Name>Bob</Name><Region>south</Region></row><row><Id>30</Id><Name>Carol</Name><Region>north</Region></row></entity></odbSnapshot>";

        private async void Start()
        {
            await ValidateJsonSnapshotsAsync();
            await ValidateXmlSnapshotsAsync();

            Debug.Log($"{nameof(Test5)}> PASSED.");

            static async Task ExportSnapshotAsync(TestOdbContext target, Stream destination, OdbExporterInterface exporter, params Type[] entityTypes)
            {
                await target.ExportAsync(destination, exporter, CancellationToken.None, entityTypes);
                destination.Position = 0;
            }

            static async Task ImportSnapshotAsync(TestOdbContext target, string data, OdbImporterInterface importer)
            {
                using (var source = new MemoryStream(Encoding.UTF8.GetBytes(data), false))
                    await target.ImportAsync(source, importer, CancellationToken.None);
            }

            static async Task InitializeSnapshotAsync(TestOdbContext target, string data, OdbImporterInterface importer)
            {
                using (var source = new MemoryStream(Encoding.UTF8.GetBytes(data), false))
                    await target.InitializeAsync(source, importer, CancellationToken.None);
            }

            static void Require(bool condition, string message)
            {
                if (condition == false)
                    throw new InvalidOperationException(message);
            }

            static async Task RequireImportRejectedAsync(TestOdbContext target, string data, OdbImporterInterface importer, string message)
            {
                var wasRejected = false;
                try
                {
                    await ImportSnapshotAsync(target, data, importer);
                }
                catch (InvalidDataException)
                {
                    wasRejected = true;
                }
                Require(wasRejected, message);
            }

            static async Task ValidateJsonSnapshotsAsync()
            {
                var importer = new OdbJsonSnapshotImporter();
                var database = new TestOdbContext();
                database.Owners.Add(new Owner { Id = 99, Name = "Old", Region = "old" });
                await InitializeSnapshotAsync(database, ItemJsonData, importer);
                Require(database.Items.Count == 3, "The JSON item snapshot was not initialized.");
                Require(database.Owners.Count == 0, "JSON initialization did not reset an omitted entity set.");
                await ImportSnapshotAsync(database, OwnerJsonData, importer);
                Require(database.Items.Count == 3, "The JSON owner import cleared the existing item set.");
                ValidateDatabase(database);

                using (var itemDestination = new MemoryStream())
                {
                    using (var ownerDestination = new MemoryStream())
                    {
                        var exporter = new OdbJsonSnapshotExporter();
                        await ExportSnapshotAsync(database, itemDestination, exporter, typeof(Item));
                        await ExportSnapshotAsync(database, ownerDestination, exporter, typeof(Owner));
                        var itemJson = Encoding.UTF8.GetString(itemDestination.ToArray());
                        var ownerJson = Encoding.UTF8.GetString(ownerDestination.ToArray());
                        Require(itemJson.Contains(@"""name"":""Item"""), "The selected JSON export does not contain Item.");
                        Require(itemJson.Contains(@"""name"":""Owner""") == false, "The selected JSON export contains Owner.");
                        Require(ownerJson.Contains(@"""name"":""Owner"""), "The selected JSON export does not contain Owner.");
                        Require(ownerJson.Contains(@"""name"":""Item""") == false, "The selected JSON export contains Item.");

                        var splitDatabase = new TestOdbContext();
                        splitDatabase.Owners.Add(new Owner { Id = 99, Name = "Old", Region = "old" });
                        await splitDatabase.InitializeAsync(itemDestination, importer, CancellationToken.None);
                        Require(splitDatabase.Owners.Count == 0, "Selected JSON initialization did not reset an omitted entity set.");
                        await splitDatabase.ImportAsync(ownerDestination, importer, CancellationToken.None);
                        ValidateDatabase(splitDatabase);
                    }
                }

                await RequireImportRejectedAsync(database, ItemJsonData, importer, "The JSON snapshot importer accepted a duplicate primary key.");
                await RequireImportRejectedAsync(database, ItemJsonData.Replace(@"""Id"":1", @"""Id"":9"), importer, "The JSON snapshot importer accepted a duplicate unique key.");
                ValidateDatabase(database);
            }

            static async Task ValidateXmlSnapshotsAsync()
            {
                var importer = new OdbXmlSnapshotImporter();
                var database = new TestOdbContext();
                database.Items.Add(new Item { Code = "old", Group = "old", Id = 99, Name = "Old", OwnerId = 99 });
                await InitializeSnapshotAsync(database, OwnerXmlData, importer);
                Require(database.Owners.Count == 3, "The XML owner snapshot was not initialized.");
                Require(database.Items.Count == 0, "XML initialization did not reset an omitted entity set.");
                await ImportSnapshotAsync(database, ItemXmlData, importer);
                Require(database.Owners.Count == 3, "The XML item import cleared the existing owner set.");
                ValidateDatabase(database);

                using (var itemDestination = new MemoryStream())
                {
                    using (var ownerDestination = new MemoryStream())
                    {
                        var exporter = new OdbXmlSnapshotExporter();
                        await ExportSnapshotAsync(database, itemDestination, exporter, typeof(Item));
                        await ExportSnapshotAsync(database, ownerDestination, exporter, typeof(Owner));
                        var itemXml = Encoding.UTF8.GetString(itemDestination.ToArray());
                        var ownerXml = Encoding.UTF8.GetString(ownerDestination.ToArray());
                        Require(itemXml.Contains("name=\"Item\""), "The selected XML export does not contain Item.");
                        Require(itemXml.Contains("name=\"Owner\"") == false, "The selected XML export contains Owner.");
                        Require(ownerXml.Contains("name=\"Owner\""), "The selected XML export does not contain Owner.");
                        Require(ownerXml.Contains("name=\"Item\"") == false, "The selected XML export contains Item.");

                        var splitDatabase = new TestOdbContext();
                        splitDatabase.Items.Add(new Item { Code = "old", Group = "old", Id = 99, Name = "Old", OwnerId = 99 });
                        await splitDatabase.InitializeAsync(ownerDestination, importer, CancellationToken.None);
                        Require(splitDatabase.Items.Count == 0, "Selected XML initialization did not reset an omitted entity set.");
                        await splitDatabase.ImportAsync(itemDestination, importer, CancellationToken.None);
                        ValidateDatabase(splitDatabase);
                    }
                }

                await RequireImportRejectedAsync(database, ItemXmlData, importer, "The XML snapshot importer accepted a duplicate primary key.");
                await RequireImportRejectedAsync(database, ItemXmlData.Replace("<Id>1</Id>", "<Id>9</Id>"), importer, "The XML snapshot importer accepted a duplicate unique key.");
                ValidateDatabase(database);
            }

            static void ValidateDatabase(TestOdbContext target)
            {
                Require(target.Items.Count == 3, "The imported item count is invalid.");
                Require(target.Items.TryFind(1, out var primaryItem), "The imported item primary index lookup failed.");
                Require(primaryItem.Code == "potion", "The imported item primary index is invalid.");
                Require(target.Items.TryFindBy(item => item.Code, "SWORD", out var uniqueItem), "The imported item unique index lookup failed.");
                Require(uniqueItem.Id == 2, "The imported item unique index is invalid.");
                Require(target.Items.FindBy(item => item.Group, "equipment").Count == 2, "The imported item group index is invalid.");
                Require(target.Items.FindBy(item => item.OwnerId, 10).Count == 2, "The imported item owner index is invalid.");
                Require(target.Items.ValidateIndexes(), "The imported item indexes are inconsistent.");

                Require(target.Owners.Count == 3, "The imported owner count is invalid.");
                Require(target.Owners.TryFind(10, out var primaryOwner), "The imported owner primary index lookup failed.");
                Require(primaryOwner.Name == "Alice", "The imported owner primary index is invalid.");
                Require(target.Owners.TryFindBy(owner => owner.Name, "BOB", out var uniqueOwner), "The imported owner unique index lookup failed.");
                Require(uniqueOwner.Id == 20, "The imported owner unique index is invalid.");
                Require(target.Owners.FindBy(owner => owner.Region, "north").Count == 2, "The imported owner region index is invalid.");
                Require(target.Owners.ValidateIndexes(), "The imported owner indexes are inconsistent.");
            }
        }
    }
}
