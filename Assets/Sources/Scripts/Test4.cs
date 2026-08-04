using oojjrs.odb;
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Assets.Sources.Scripts
{
    public sealed class Test4 : MonoBehaviour
    {
        public sealed class Item
        {
            public string Code { get; set; }
            public string Group { get; set; }
            public int Id { get; set; }
            public string Name { get; set; }
            public int OwnerId { get; set; }
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

        private const string InitialData = @"{
  ""format"": ""unity-odb-json-1"",
  ""schemaVersion"": 3,
  ""entities"": [
    {
      ""name"": ""Item"",
      ""rows"": [
        {
          ""Code"": ""potion"",
          ""Group"": ""consumable"",
          ""Id"": 1,
          ""Name"": ""Potion\r\nLine2\rLine3\nLine4"",
          ""OwnerId"": 10
        },
        {
          ""Code"": ""sword"",
          ""Group"": ""equipment"",
          ""Id"": 2,
          ""Name"": """",
          ""OwnerId"": 10
        },
        {
          ""Code"": ""shield"",
          ""Group"": ""equipment"",
          ""Id"": 3,
          ""Name"": null,
          ""OwnerId"": 20
        }
      ]
    }
  ]
}";

        private async void Start()
        {
            var importer = new OdbJsonSnapshotImporter();
            var database = new TestOdbContext();
            var originalItems = database.Items;
            originalItems.Add(new Item { Code = "old", Group = "old", Id = 99, Name = "Old", OwnerId = 99 });
            using (var source = new MemoryStream(Encoding.UTF8.GetBytes(InitialData), false))
                await database.InitializeAsync(source, importer);
            Require(ReferenceEquals(originalItems, database.Items), "Initialization replaced the ODB set instance.");
            Require(originalItems.TryFind(99, out _) == false, "Initialization did not clear the previous database contents.");
            ValidateDatabase(database);

            await RequireImportRejectedAsync(
                InitialData.Replace(@"""schemaVersion"": 3", @"""schemaVersion"": 2"), "The JSON importer accepted a different schema version.");
            await RequireImportRejectedAsync(
                InitialData.Replace("unity-odb-json-1", "unity-odb-json-2"), "The JSON importer accepted an unknown format.");

            using (var destination = new MemoryStream())
            {
                await database.ExportAsync(destination, new OdbJsonSnapshotExporter());
                Require(destination.Length > 0, "The JSON export is empty.");
                var bytes = destination.ToArray();
                Require((bytes.Length < 3) || (bytes[0] != 0xef) || (bytes[1] != 0xbb) || (bytes[2] != 0xbf), "The JSON export contains a UTF-8 BOM.");
                var json = Encoding.UTF8.GetString(bytes);
                Require(json.Contains("\"format\":\"unity-odb-json-1\""), "The JSON format is invalid.");
                Require(json.Contains("\"schemaVersion\":3"), "The JSON schema version is invalid.");

                destination.Position = 0;
                var roundTripDatabase = new TestOdbContext();
                await roundTripDatabase.InitializeAsync(destination, importer);
                ValidateDatabase(roundTripDatabase);
            }

            using (var compressedDestination = new MemoryStream())
            {
                long compressedLengthBeforeDispose;
                await using (var compressor = new GZipStream(compressedDestination, System.IO.Compression.CompressionLevel.Fastest, true))
                {
                    await database.ExportAsync(compressor, new OdbJsonSnapshotExporter());
                    compressedLengthBeforeDispose = compressedDestination.Length;
                }
                Require(compressedDestination.Length > compressedLengthBeforeDispose, "The GZip footer was not written.");
                Require(compressedDestination.CanRead, "The GZip exporter closed the destination stream.");

                compressedDestination.Position = 0;
                using (var decompressor = new GZipStream(compressedDestination, CompressionMode.Decompress, true))
                {
                    var compressedRoundTripDatabase = new TestOdbContext();
                    await compressedRoundTripDatabase.InitializeAsync(decompressor, importer);
                    ValidateDatabase(compressedRoundTripDatabase);
                }
                Require(compressedDestination.CanRead, "The GZip importer closed the source stream.");
            }

            Debug.Log($"{nameof(Test4)}> PASSED.");

            static void Require(bool condition, string message)
            {
                if (condition == false)
                    throw new InvalidOperationException(message);
            }

            static async Task RequireImportRejectedAsync(string json, string message)
            {
                var wasRejected = false;
                try
                {
                    using (var source = new MemoryStream(Encoding.UTF8.GetBytes(json), false))
                        await new TestOdbContext().InitializeAsync(source, new OdbJsonSnapshotImporter());
                }
                catch (InvalidDataException)
                {
                    wasRejected = true;
                }
                Require(wasRejected, message);
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
