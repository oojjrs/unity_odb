using oojjrs.odb;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Assets.Sources.Scripts
{
    public sealed class Test6 : MonoBehaviour
    {
        public sealed class AssignedItem
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        public sealed class GlobalItem
        {
            public string Code { get; set; }
            public long Id { get; set; }
        }

        public sealed class GlobalOwner
        {
            public long Id { get; set; }
            public string Name { get; set; }
        }

        private sealed class ImmutableItem
        {
            public long Id { get; }
            public string Name { get; }

            internal ImmutableItem(long id, string name)
            {
                Id = id;
                Name = name;
            }
        }

        private sealed class ImmutableOdbContext : OdbContext
        {
            internal OdbSet<ImmutableItem, long> Items => GetSet<ImmutableItem, long>();

            protected override void OnModelCreating(OdbModelBuilder modelBuilder)
            {
                modelBuilder.AddEntity<ImmutableItem, long>("ImmutableItem", item => item.Id).ValueGeneratedOnAdd();
            }
        }

        private sealed class AliasKeyComparer : IEqualityComparer<int>
        {
            public bool Equals(int left, int right)
            {
                return (left % 100) == (right % 100);
            }

            public int GetHashCode(int value)
            {
                return value % 100;
            }
        }

        private sealed class AliasOdbContext : OdbContext
        {
            internal OdbSet<IntIdentityItem, int> Items => GetSet<IntIdentityItem, int>();

            protected override void OnModelCreating(OdbModelBuilder modelBuilder)
            {
                modelBuilder.AddEntity<IntIdentityItem, int>("IntIdentityItem", item => item.Id, new AliasKeyComparer()).ValueGeneratedOnAdd(modelBuilder.AddGlobalIdentity<int>("AliasGlobalId"), (item, id) => item.Id = id);
            }
        }

        private sealed class IdentityStateProbeExporter : OdbExporterInterface
        {
            public Task ExportAsync(OdbExportSession session, Stream destination, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Require(session.IdentityStates.Count == 3, "The custom exporter did not receive every identity state.");
                Require(ContainsIdentityState(session.IdentityStates, "entity", "IntIdentityItem", "Int32", 3), "The custom exporter received the wrong int identity state.");
                Require(ContainsIdentityState(session.IdentityStates, "entity", "LongIdentityItem", "Int64", 2), "The custom exporter received the wrong long identity state.");
                Require(ContainsIdentityState(session.IdentityStates, "global", "GlobalId", "Int64", 4), "The custom exporter received the wrong global identity state.");
                return Task.CompletedTask;
            }
        }

        private sealed class IdentityStateProbeImporter : OdbImporterInterface
        {
            public Task ImportAsync(OdbImportSession session, Stream source, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                session.AdvanceIdentityState("entity", "IntIdentityItem", "Int32", 10);
                session.AdvanceIdentityState("global", "GlobalId", "Int64", 20);
                return Task.CompletedTask;
            }
        }

        public sealed class IntIdentityItem
        {
            public string Code { get; set; }
            public int Id { get; set; }
        }

        public sealed class LongIdentityItem
        {
            public long Id { get; set; }
            public string Name { get; set; }
        }

        private sealed class TestOdbContext : OdbContext
        {
            internal OdbSet<AssignedItem, int> AssignedItems => GetSet<AssignedItem, int>();
            internal OdbSet<GlobalItem, long> GlobalItems => GetSet<GlobalItem, long>();
            internal OdbSet<GlobalOwner, long> GlobalOwners => GetSet<GlobalOwner, long>();
            internal OdbSet<IntIdentityItem, int> IntIdentityItems => GetSet<IntIdentityItem, int>();
            internal OdbSet<LongIdentityItem, long> LongIdentityItems => GetSet<LongIdentityItem, long>();

            protected override void OnModelCreating(OdbModelBuilder modelBuilder)
            {
                var globalIdentity = modelBuilder.AddGlobalIdentity<long>("GlobalId");
                modelBuilder.SetSchemaVersion(6);
                modelBuilder.AddEntity<AssignedItem, int>("AssignedItem", item => item.Id);
                modelBuilder.AddEntity<GlobalItem, long>("GlobalItem", item => item.Id).ValueGeneratedOnAdd(globalIdentity, (item, id) => item.Id = id).AddUniqueIndex(item => item.Code, StringComparer.Ordinal);
                modelBuilder.AddEntity<GlobalOwner, long>("GlobalOwner", owner => owner.Id).ValueGeneratedOnAdd(globalIdentity, (owner, id) => owner.Id = id).AddUniqueIndex(owner => owner.Name, StringComparer.Ordinal);
                modelBuilder.AddEntity<IntIdentityItem, int>("IntIdentityItem", item => item.Id).ValueGeneratedOnAdd((item, id) => item.Id = id).AddUniqueIndex(item => item.Code, StringComparer.Ordinal);
                modelBuilder.AddEntity<LongIdentityItem, long>("LongIdentityItem", item => item.Id).ValueGeneratedOnAdd((item, id) => item.Id = id);
            }
        }

        private const string DuplicateGlobalJsonData = @"{""format"":""unity-odb-json-1"",""schemaVersion"":6,""entities"":[{""name"":""GlobalItem"",""rows"":[{""Code"":""item"",""Id"":10}]},{""name"":""GlobalOwner"",""rows"":[{""Id"":10,""Name"":""owner""}]}]}";
        private const string DuplicateGlobalXmlData = @"<odbSnapshot format=""unity-odb-xml-1"" schemaVersion=""6""><entity name=""GlobalItem""><row><Code>item</Code><Id>10</Id></row></entity><entity name=""GlobalOwner""><row><Id>10</Id><Name>owner</Name></row></entity></odbSnapshot>";
        private const string EmptyIdentityKeyTypeJsonData = @"{""format"":""unity-odb-json-2"",""schemaVersion"":6,""identityGenerators"":[{""scope"":""entity"",""name"":""IntIdentityItem"",""keyType"":"""",""highWaterMark"":""1""}],""entities"":[{""name"":""IntIdentityItem"",""rows"":[{""Code"":""invalid-state"",""Id"":1}]}]}";
        private const string ExcessIdentityXmlData = @"<odbSnapshot format=""unity-odb-xml-2"" schemaVersion=""6""><identityGenerators><identity scope=""entity"" name=""IntIdentityItem"" keyType=""Int32"" highWaterMark=""10"" /></identityGenerators><entity name=""AssignedItem""><row><Id>42</Id><Name>assigned</Name></row></entity></odbSnapshot>";
        private const string ExistingGlobalCollisionJsonData = @"{""format"":""unity-odb-json-1"",""schemaVersion"":6,""entities"":[{""name"":""GlobalOwner"",""rows"":[{""Id"":1,""Name"":""conflict""}]}]}";
        private const string IncrementalGlobalJsonData = @"{""format"":""unity-odb-json-1"",""schemaVersion"":6,""entities"":[{""name"":""GlobalOwner"",""rows"":[{""Id"":3,""Name"":""imported""}]}]}";
        private const string InvalidGeneratedKeyJsonData = @"{""format"":""unity-odb-json-1"",""schemaVersion"":6,""entities"":[{""name"":""IntIdentityItem"",""rows"":[{""Code"":""invalid-key"",""Id"":0}]}]}";
        private const string LegacyJsonData = @"{""format"":""unity-odb-json-1"",""schemaVersion"":6,""entities"":[{""name"":""IntIdentityItem"",""rows"":[{""Code"":""legacy-int"",""Id"":20}]},{""name"":""LongIdentityItem"",""rows"":[{""Id"":30,""Name"":""legacy-long""}]},{""name"":""GlobalItem"",""rows"":[{""Code"":""legacy-item"",""Id"":40}]},{""name"":""GlobalOwner"",""rows"":[{""Id"":42,""Name"":""legacy-owner""}]}]}";
        private const string LegacyXmlData = @"<odbSnapshot format=""unity-odb-xml-1"" schemaVersion=""6""><entity name=""IntIdentityItem""><row><Code>legacy-int</Code><Id>20</Id></row></entity><entity name=""LongIdentityItem""><row><Id>30</Id><Name>legacy-long</Name></row></entity><entity name=""GlobalItem""><row><Code>legacy-item</Code><Id>40</Id></row></entity><entity name=""GlobalOwner""><row><Id>42</Id><Name>legacy-owner</Name></row></entity></odbSnapshot>";
        private const string MissingIdentityJsonData = @"{""format"":""unity-odb-json-2"",""schemaVersion"":6,""identityGenerators"":[],""entities"":[{""name"":""IntIdentityItem"",""rows"":[{""Code"":""missing-state"",""Id"":1}]}]}";
        private const string MissingIdentityMetadataJsonData = @"{""format"":""unity-odb-json-2"",""schemaVersion"":6,""entities"":[]}";
        private const string OverflowJsonData = @"{""format"":""unity-odb-json-1"",""schemaVersion"":6,""entities"":[{""name"":""IntIdentityItem"",""rows"":[{""Code"":""maximum"",""Id"":2147483647}]}]}";
        private const string PartialFailureIdentityJsonData = @"{""format"":""unity-odb-json-2"",""schemaVersion"":6,""identityGenerators"":[{""scope"":""global"",""name"":""GlobalId"",""keyType"":""Int64"",""highWaterMark"":""100""}],""entities"":[{""name"":""GlobalItem"",""rows"":[{""Code"":""duplicate"",""Id"":10},{""Code"":""duplicate"",""Id"":11}]}]}";

        private async void Start()
        {
            ValidateImmutableIdentityGeneration();
            ValidateRuntimeIdentityGeneration();
            ValidateGeneratedCustomComparer();
            await ValidateLegacyReseedAsync(LegacyJsonData, new OdbJsonSnapshotImporter());
            await ValidateLegacyReseedAsync(LegacyXmlData, new OdbXmlSnapshotImporter());
            await ValidateIncrementalImportAsync();
            await ValidateRejectedGlobalDuplicateAsync();
            await ValidateRejectedIdentityStateMismatchAsync();
            await ValidateFailedIdentityImportStateAsync();
            await ValidateOverflowAsync();
            await ValidateSnapshotRoundTripAsync(new OdbJsonSnapshotExporter(), new OdbJsonSnapshotImporter(), "unity-odb-json-2");
            await ValidateSnapshotRoundTripAsync(new OdbXmlSnapshotExporter(), new OdbXmlSnapshotImporter(), "unity-odb-xml-2");
            await ValidatePartialSnapshotAsync();
            await ValidateCustomIdentityStateApiAsync();

            Debug.Log($"{nameof(Test6)}> PASSED.");
        }

        private static TestOdbContext CreateSnapshotSource()
        {
            var database = new TestOdbContext();
            database.AssignedItems.Add(new AssignedItem { Id = 42, Name = "assigned" });

            var intFirst = new IntIdentityItem { Code = "int-first" };
            database.IntIdentityItems.Add(intFirst);
            Require(intFirst.Id == 1, "The per-entity int identity did not start at 1.");
            var intConflict = new IntIdentityItem { Code = "int-first" };
            Require(database.IntIdentityItems.TryAdd(intConflict) == false, "The per-entity int identity accepted a duplicate unique key.");
            Require(intConflict.Id == 0, "A failed generated Add did not restore the default int key.");
            var intThird = new IntIdentityItem { Code = "int-third" };
            database.IntIdentityItems.Add(intThird);
            Require(intThird.Id == 3, "The per-entity int identity did not preserve a failed-Add gap.");
            Require(database.IntIdentityItems.Remove(intThird.Id), "The generated int entity was not removed.");

            var longFirst = new LongIdentityItem { Name = "long-first" };
            var longSecond = new LongIdentityItem { Name = "long-second" };
            database.LongIdentityItems.Add(longFirst);
            database.LongIdentityItems.Add(longSecond);
            Require((longFirst.Id == 1) && (longSecond.Id == 2), "The per-entity long identity sequence is invalid.");
            Require(database.LongIdentityItems.Remove(longSecond.Id), "The generated long entity was not removed.");

            var globalItem = new GlobalItem { Code = "global-item" };
            var globalOwner = new GlobalOwner { Name = "global-owner" };
            database.GlobalItems.Add(globalItem);
            database.GlobalOwners.Add(globalOwner);
            Require((globalItem.Id == 1) && (globalOwner.Id == 2), "The shared global identity sequence is invalid.");
            var globalConflict = new GlobalItem { Code = "global-item" };
            Require(database.GlobalItems.TryAdd(globalConflict) == false, "The global identity accepted a duplicate unique key.");
            Require(globalConflict.Id == 0, "A failed generated Add did not restore the default global key.");
            var globalFourth = new GlobalOwner { Name = "global-fourth" };
            database.GlobalOwners.Add(globalFourth);
            Require(globalFourth.Id == 4, "The global identity did not preserve a failed-Add gap.");
            Require(database.GlobalOwners.Remove(globalFourth.Id), "The generated global entity was not removed.");
            return database;
        }

        private static bool ContainsIdentityState(IReadOnlyList<OdbIdentityState> identityStates, string scope, string name, string keyTypeName, long highWaterMark)
        {
            foreach (var identityState in identityStates)
                if ((identityState.Scope == scope) && (identityState.Name == name) && (identityState.KeyTypeName == keyTypeName) && (identityState.HighWaterMark == highWaterMark))
                    return true;

            return false;
        }

        private static async Task ExportSnapshotAsync(TestOdbContext target, Stream destination, OdbExporterInterface exporter)
        {
            await target.ExportAsync(destination, exporter, CancellationToken.None);
            destination.Position = 0;
        }

        private static async Task InitializeSnapshotAsync(TestOdbContext target, string data, OdbImporterInterface importer)
        {
            using (var source = new MemoryStream(Encoding.UTF8.GetBytes(data), false))
                await target.InitializeAsync(source, importer, CancellationToken.None);
        }

        private static void Require(bool condition, string message)
        {
            if (condition == false)
                throw new InvalidOperationException(message);
        }

        private static async Task RequireImportRejectedAsync(TestOdbContext target, string data, OdbImporterInterface importer, string message)
        {
            var wasRejected = false;
            try
            {
                using (var source = new MemoryStream(Encoding.UTF8.GetBytes(data), false))
                    await target.ImportAsync(source, importer, CancellationToken.None);
            }
            catch (InvalidDataException)
            {
                wasRejected = true;
            }

            Require(wasRejected, message);
        }

        private static void RequireThrows<TException>(Action action, string message) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }

            throw new InvalidOperationException(message);
        }

        private static void ValidateImmutableIdentityGeneration()
        {
            var database = new ImmutableOdbContext();
            Require(database.Items.AddGenerated(id => new ImmutableItem(id, "first")).Id == 1, "The immutable factory identity did not start at 1.");
            RequireThrows<InvalidOperationException>(() => database.Items.AddGenerated(id => new ImmutableItem(id + 1, "invalid")), "The immutable factory accepted a different generated key.");
            Require(database.Items.AddGenerated(id => new ImmutableItem(id, "third")).Id == 3, "The immutable factory identity did not preserve an exception gap.");
            RequireThrows<InvalidOperationException>(() => database.Items.Add(new ImmutableItem(0, "invalid")), "A generated entity without a key setter accepted the entity Add overload.");
        }

        private static async Task ValidateLegacyReseedAsync(string data, OdbImporterInterface importer)
        {
            var database = new TestOdbContext();
            await InitializeSnapshotAsync(database, data, importer);
            var intItem = new IntIdentityItem { Code = "after-legacy-int" };
            var longItem = new LongIdentityItem { Name = "after-legacy-long" };
            var globalItem = new GlobalItem { Code = "after-legacy-item" };
            var globalOwner = new GlobalOwner { Name = "after-legacy-owner" };
            database.IntIdentityItems.Add(intItem);
            database.LongIdentityItems.Add(longItem);
            database.GlobalItems.Add(globalItem);
            database.GlobalOwners.Add(globalOwner);
            Require(intItem.Id == 21, "The legacy snapshot did not reseed the int identity.");
            Require(longItem.Id == 31, "The legacy snapshot did not reseed the long identity.");
            Require((globalItem.Id == 43) && (globalOwner.Id == 44), "The legacy snapshot did not reseed the global identity.");
        }

        private static async Task ValidateIncrementalImportAsync()
        {
            var database = CreateSnapshotSource();
            using (var source = new MemoryStream(Encoding.UTF8.GetBytes(IncrementalGlobalJsonData), false))
                await database.ImportAsync(source, new OdbJsonSnapshotImporter(), CancellationToken.None);
            Require(database.GlobalItems.TryFind(1, out _), "Incremental import removed an existing global item.");
            Require((database.GlobalOwners.Count == 2) && database.GlobalOwners.TryFind(2, out _) && database.GlobalOwners.TryFind(3, out _), "Incremental import did not merge the global owner rows.");
            var globalItem = new GlobalItem { Code = "after-incremental" };
            database.GlobalItems.Add(globalItem);
            Require(globalItem.Id == 5, "Incremental import did not preserve the existing global high-water mark.");
            await RequireImportRejectedAsync(database, ExistingGlobalCollisionJsonData, new OdbJsonSnapshotImporter(), "Incremental import accepted a primary key active in another global identity Set.");
            Require((database.GlobalItems.Count == 2) && (database.GlobalOwners.Count == 2), "A rejected incremental import changed the global Sets.");
        }

        private static async Task ValidateOverflowAsync()
        {
            var database = new TestOdbContext();
            await InitializeSnapshotAsync(database, OverflowJsonData, new OdbJsonSnapshotImporter());
            RequireThrows<OverflowException>(() => database.IntIdentityItems.Add(new IntIdentityItem { Code = "overflow" }), "The exhausted int identity generated another key.");
        }

        private static async Task ValidateCustomIdentityStateApiAsync()
        {
            using (var destination = new MemoryStream())
                await CreateSnapshotSource().ExportAsync(destination, new IdentityStateProbeExporter(), CancellationToken.None);

            var database = new TestOdbContext();
            using (var source = new MemoryStream())
                await database.InitializeAsync(source, new IdentityStateProbeImporter(), CancellationToken.None);
            var intItem = new IntIdentityItem { Code = "after-custom-state" };
            var globalItem = new GlobalItem { Code = "after-custom-state" };
            database.IntIdentityItems.Add(intItem);
            database.GlobalItems.Add(globalItem);
            Require(intItem.Id == 11, "The custom importer did not restore the int identity state.");
            Require(globalItem.Id == 21, "The custom importer did not restore the global identity state.");
        }

        private static void ValidateGeneratedCustomComparer()
        {
            var database = new AliasOdbContext();
            database.Items.Add(new IntIdentityItem { Code = "first" });
            Require(database.Items.TryReplace(101, new IntIdentityItem { Id = 1, Code = "replacement" }), "A generated replacement using a comparer-equivalent lookup key failed.");
            RequireThrows<InvalidOperationException>(() => database.Items.TryReplace(101, new IntIdentityItem { Id = 101, Code = "invalid" }), "A generated replacement changed the exact primary key.");
            Require(database.Items.Remove(101), "A generated removal using a comparer-equivalent lookup key failed.");
            database.Items.Add(new IntIdentityItem { Code = "second" });
            Require(database.Items.ValidateIndexes(), "A generated Set with a custom comparer became inconsistent.");
            RequireThrows<ArgumentNullException>(() => database.Items.Add(null), "Add(null) did not retain the entity overload's null validation.");
        }

        private static async Task ValidateFailedIdentityImportStateAsync()
        {
            var database = new TestOdbContext();
            await RequireImportRejectedAsync(database, PartialFailureIdentityJsonData, new OdbJsonSnapshotImporter(), "The importer accepted a duplicate row after identity state metadata.");
            Require((database.GlobalItems.Count == 1) && database.GlobalItems.TryFind(10, out _), "A failed import did not retain its successfully imported prefix row.");
            var globalOwner = new GlobalOwner { Name = "after-failed-import" };
            database.GlobalOwners.Add(globalOwner);
            Require(globalOwner.Id == 101, "A failed import lost the identity high-water mark that it had already consumed.");
        }

        private static async Task ValidateRejectedGlobalDuplicateAsync()
        {
            await RequireImportRejectedAsync(new TestOdbContext(), DuplicateGlobalJsonData, new OdbJsonSnapshotImporter(), "The importer accepted a duplicate global identity across entity types.");
            await RequireImportRejectedAsync(new TestOdbContext(), DuplicateGlobalXmlData, new OdbXmlSnapshotImporter(), "The XML importer accepted a duplicate global identity across entity types.");
        }

        private static async Task ValidateRejectedIdentityStateMismatchAsync()
        {
            await RequireImportRejectedAsync(new TestOdbContext(), MissingIdentityJsonData, new OdbJsonSnapshotImporter(), "The JSON importer accepted missing identity state for a generated entity.");
            await RequireImportRejectedAsync(new TestOdbContext(), MissingIdentityMetadataJsonData, new OdbJsonSnapshotImporter(), "The JSON importer accepted a v2 snapshot without identity metadata.");
            await RequireImportRejectedAsync(new TestOdbContext(), EmptyIdentityKeyTypeJsonData, new OdbJsonSnapshotImporter(), "The JSON importer accepted incomplete identity state.");
            await RequireImportRejectedAsync(new TestOdbContext(), InvalidGeneratedKeyJsonData, new OdbJsonSnapshotImporter(), "The JSON importer accepted a zero generated primary key.");
            await RequireImportRejectedAsync(new TestOdbContext(), ExcessIdentityXmlData, new OdbXmlSnapshotImporter(), "The XML importer accepted identity state unrelated to its entity rows.");
        }

        private static void ValidateRuntimeIdentityGeneration()
        {
            var database = CreateSnapshotSource();
            Require(database.AssignedItems.TryFind(42, out var assignedItem) && (assignedItem.Name == "assigned"), "The caller-assigned primary key changed.");
            Require(database.IntIdentityItems.TryFind(1, out _), "The generated int entity is missing.");
            Require(database.LongIdentityItems.TryFind(1, out _), "The generated long entity is missing.");
            Require(database.GlobalItems.TryFind(1, out _), "The generated global item is missing.");
            Require(database.GlobalOwners.TryFind(2, out _), "The generated global owner is missing.");
            RequireThrows<InvalidOperationException>(() => database.IntIdentityItems.Add(database.IntIdentityItems.TryFind(1, out var item) ? item : null), "A generated entity with a non-default key was added again.");
            Require(database.IntIdentityItems.ValidateIndexes(), "The generated int indexes are inconsistent.");
            Require(database.GlobalItems.ValidateIndexes(), "The generated global item indexes are inconsistent.");
            Require(database.GlobalOwners.ValidateIndexes(), "The generated global owner indexes are inconsistent.");
        }

        private static async Task ValidateSnapshotRoundTripAsync(OdbExporterInterface exporter, OdbImporterInterface importer, string formatIdentifier)
        {
            using (var destination = new MemoryStream())
            {
                await ExportSnapshotAsync(CreateSnapshotSource(), destination, exporter);
                var snapshot = Encoding.UTF8.GetString(destination.ToArray());
                Require(snapshot.Contains(formatIdentifier), "The identity snapshot format identifier is missing.");
                Require(snapshot.Contains("identityGenerators"), "The identity snapshot does not contain generator state.");

                var targetDatabase = new TestOdbContext();
                await targetDatabase.InitializeAsync(destination, importer, CancellationToken.None);
                Require((targetDatabase.AssignedItems.Count == 1) && targetDatabase.AssignedItems.TryFind(42, out var assignedItem) && (assignedItem.Name == "assigned"), "The snapshot did not restore the caller-assigned row.");
                Require((targetDatabase.IntIdentityItems.Count == 1) && targetDatabase.IntIdentityItems.TryFind(1, out _), "The snapshot did not restore the generated int row.");
                Require((targetDatabase.LongIdentityItems.Count == 1) && targetDatabase.LongIdentityItems.TryFind(1, out _), "The snapshot did not restore the generated long row.");
                Require((targetDatabase.GlobalItems.Count == 1) && targetDatabase.GlobalItems.TryFind(1, out _), "The snapshot did not restore the generated global item row.");
                Require((targetDatabase.GlobalOwners.Count == 1) && targetDatabase.GlobalOwners.TryFind(2, out _), "The snapshot did not restore the generated global owner row.");
                Require(targetDatabase.IntIdentityItems.ValidateIndexes(), "The restored int indexes are inconsistent.");
                Require(targetDatabase.GlobalItems.ValidateIndexes(), "The restored global item indexes are inconsistent.");
                Require(targetDatabase.GlobalOwners.ValidateIndexes(), "The restored global owner indexes are inconsistent.");
                var intItem = new IntIdentityItem { Code = "after-snapshot-int" };
                var longItem = new LongIdentityItem { Name = "after-snapshot-long" };
                var globalItem = new GlobalItem { Code = "after-snapshot-item" };
                var globalOwner = new GlobalOwner { Name = "after-snapshot-owner" };
                targetDatabase.IntIdentityItems.Add(intItem);
                targetDatabase.LongIdentityItems.Add(longItem);
                targetDatabase.GlobalItems.Add(globalItem);
                targetDatabase.GlobalOwners.Add(globalOwner);
                Require(intItem.Id == 4, "The snapshot did not restore the deleted int high-water mark.");
                Require(longItem.Id == 3, "The snapshot did not restore the deleted long high-water mark.");
                Require((globalItem.Id == 5) && (globalOwner.Id == 6), "The snapshot did not restore the shared global high-water mark.");
            }
        }

        private static async Task ValidatePartialSnapshotAsync()
        {
            using (var destination = new MemoryStream())
            {
                await CreateSnapshotSource().ExportAsync(destination, new OdbJsonSnapshotExporter(), CancellationToken.None, typeof(GlobalItem));
                destination.Position = 0;
                var snapshot = Encoding.UTF8.GetString(destination.ToArray());
                Require(snapshot.Contains("unity-odb-json-2"), "A generated-key partial snapshot did not use the identity format.");
                Require(snapshot.Contains("identityGenerators"), "A generated-key partial snapshot omitted its identity state.");

                var targetDatabase = new TestOdbContext();
                await targetDatabase.InitializeAsync(destination, new OdbJsonSnapshotImporter(), CancellationToken.None);
                Require((targetDatabase.GlobalItems.Count == 1) && targetDatabase.GlobalItems.TryFind(1, out _), "The partial snapshot did not restore its selected global row.");
                Require((targetDatabase.AssignedItems.Count == 0) && (targetDatabase.GlobalOwners.Count == 0) && (targetDatabase.IntIdentityItems.Count == 0) && (targetDatabase.LongIdentityItems.Count == 0), "The partial snapshot restored an unselected entity Set.");
                var globalOwner = new GlobalOwner { Name = "after-partial" };
                targetDatabase.GlobalOwners.Add(globalOwner);
                Require(globalOwner.Id == 5, "The partial snapshot did not restore the shared global high-water mark.");
            }

            using (var destination = new MemoryStream())
            {
                var sourceDatabase = new TestOdbContext();
                sourceDatabase.AssignedItems.Add(new AssignedItem { Id = 42, Name = "assigned" });
                await sourceDatabase.ExportAsync(destination, new OdbJsonSnapshotExporter(), CancellationToken.None, typeof(AssignedItem));
                destination.Position = 0;
                var snapshot = Encoding.UTF8.GetString(destination.ToArray());
                Require(snapshot.Contains("unity-odb-json-1"), "A caller-assigned partial snapshot did not retain the legacy format.");
                Require(snapshot.Contains("identityGenerators") == false, "A caller-assigned partial snapshot contains unrelated identity state.");

                var targetDatabase = new TestOdbContext();
                await targetDatabase.InitializeAsync(destination, new OdbJsonSnapshotImporter(), CancellationToken.None);
                Require((targetDatabase.AssignedItems.Count == 1) && targetDatabase.AssignedItems.TryFind(42, out _), "The caller-assigned partial snapshot did not restore its selected row.");
            }
        }
    }
}
