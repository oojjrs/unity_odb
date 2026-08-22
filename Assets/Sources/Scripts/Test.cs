using oojjrs.odb;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.Sources.Scripts
{
    public sealed class Test : MonoBehaviour
    {
        private sealed class IndexComparerException : Exception
        {
        }

        private sealed class IndexSelectorException : Exception
        {
        }

        private sealed class Item
        {
            public string Code { get; }
            public string Group { get; }
            public int Id { get; }
            public string Name { get; }
            public int OwnerId { get; }

            public Item(int id, string code, string group, int ownerId, string name)
            {
                Id = id;
                Code = code;
                Group = group;
                OwnerId = ownerId;
                Name = name;
            }
        }

        private sealed class Label
        {
            public string Id { get; }

            public Label(string id)
            {
                Id = id;
            }
        }

        private sealed class ThrowingIndexItem
        {
            private readonly bool ThrowsOnHashKeyAccess;
            private readonly bool ThrowsOnUniqueKeyAccess;

            public string HashKey
            {
                get
                {
                    if (ThrowsOnHashKeyAccess)
                        throw new IndexSelectorException();

                    return "hash";
                }
            }

            public int Id { get; }

            public string UniqueKey
            {
                get
                {
                    if (ThrowsOnUniqueKeyAccess)
                        throw new IndexSelectorException();

                    return Id.ToString();
                }
            }

            public ThrowingIndexItem(int id, bool throwsOnHashKeyAccess = false, bool throwsOnUniqueKeyAccess = false)
            {
                Id = id;
                ThrowsOnHashKeyAccess = throwsOnHashKeyAccess;
                ThrowsOnUniqueKeyAccess = throwsOnUniqueKeyAccess;
            }
        }

        private sealed class ThrowingStringComparer : IEqualityComparer<string>
        {
            internal bool IsThrowing { get; set; }

            bool IEqualityComparer<string>.Equals(string x, string y)
            {
                ThrowIfRequired();
                return StringComparer.Ordinal.Equals(x, y);
            }

            int IEqualityComparer<string>.GetHashCode(string value)
            {
                ThrowIfRequired();
                return StringComparer.Ordinal.GetHashCode(value);
            }

            private void ThrowIfRequired()
            {
                if (IsThrowing)
                    throw new IndexComparerException();
            }
        }

        private sealed class TestOdbContext : OdbContext
        {
            internal OdbSet<Item, int> Items => GetSet<Item, int>();
            internal OdbSet<Label, string> Labels => GetSet<Label, string>();
            internal ThrowingStringComparer ThrowingIndexComparer { get; } = new();
            internal OdbSet<ThrowingIndexItem, int> ThrowingIndexItems => GetSet<ThrowingIndexItem, int>();

            protected override void OnModelCreating(OdbModelBuilder modelBuilder)
            {
                modelBuilder.AddEntity<Item, int>("Item", item => item.Id)
                    .SetCapacityHint(8)
                    .AddIndex(item => item.Group)
                    .AddIndex(item => item.OwnerId)
                    .AddUniqueIndex(item => item.Code, StringComparer.OrdinalIgnoreCase);

                modelBuilder.AddEntity<Label, string>("Label", label => label.Id, StringComparer.OrdinalIgnoreCase);

                modelBuilder.AddEntity<ThrowingIndexItem, int>("ThrowingIndexItem", item => item.Id)
                    .AddIndex(item => item.HashKey)
                    .AddUniqueIndex(item => item.UniqueKey, ThrowingIndexComparer);
            }
        }

        private void Start()
        {
            var comparerDatabase = new TestOdbContext();
            comparerDatabase.Labels.Add(new Label("alpha"));

            Require(comparerDatabase.Labels.TryFind("ALPHA", out var label) && (label.Id == "alpha"), "The primary key comparer was ignored.");
            Require(comparerDatabase.Labels.TryAdd(new Label("ALPHA")) == false, "A duplicate primary key was accepted by the configured comparer.");

            var database = new TestOdbContext();
            database.Items.Add(new Item(1, "potion", "consumable", 10, "Potion"));
            database.Items.Add(new Item(2, "sword", "equipment", 10, "Sword"));
            database.Items.Add(new Item(3, "shield", "equipment", 20, "Shield"));

            Require(database.Items.Count == 3, "The entity count is invalid.");
            Require(database.Items.TryFind(1, out var primaryItem) && (primaryItem.Code == "potion"), "The primary index lookup failed.");
            Require(database.Items.TryFindBy(item => item.Code, "SWORD", out var uniqueItem) && (uniqueItem.Id == 2), "The unique index comparer was ignored.");

            var ownedItems = database.Items.FindBy(item => item.OwnerId, 10);
            Require(ownedItems.Count == 2, "The hash index count is invalid.");

            var ownedItemIdSum = 0;
            foreach (var item in ownedItems)
                ownedItemIdSum += item.Id;
            Require(ownedItemIdSum == 3, "The hash index result is invalid.");

            Require(database.Items.TryAdd(new Item(4, "POTION", "consumable", 30, "Duplicate")) == false, "A duplicate unique key was accepted by the configured comparer.");
            Require(database.Items.TryAdd(new Item(4, null, "consumable", 30, "Invalid")) == false, "A null unique index key was accepted.");
            Require(database.Items.TryAdd(new Item(4, "bow", null, 30, "Invalid")) == false, "A null hash index key was accepted.");

            var originalThrowingIndexItem = new ThrowingIndexItem(1);
            database.ThrowingIndexItems.Add(originalThrowingIndexItem);
            RequireThrows<IndexSelectorException>(() => database.ThrowingIndexItems.TryAdd(new ThrowingIndexItem(2, throwsOnHashKeyAccess: true)));
            RequireThrows<IndexSelectorException>(() => database.ThrowingIndexItems.TryAdd(new ThrowingIndexItem(2, throwsOnUniqueKeyAccess: true)));
            RequireThrows<IndexSelectorException>(() => database.ThrowingIndexItems.TryReplace(1, new ThrowingIndexItem(1, throwsOnHashKeyAccess: true)));
            RequireThrows<IndexSelectorException>(() => database.ThrowingIndexItems.TryReplace(1, new ThrowingIndexItem(1, throwsOnUniqueKeyAccess: true)));

            database.ThrowingIndexComparer.IsThrowing = true;
            RequireThrows<IndexComparerException>(() => database.ThrowingIndexItems.TryAdd(new ThrowingIndexItem(2)));
            RequireThrows<IndexComparerException>(() => database.ThrowingIndexItems.TryReplace(1, new ThrowingIndexItem(1)));
            database.ThrowingIndexComparer.IsThrowing = false;

            Require(database.ThrowingIndexItems.Count == 1, "An index exception changed the entity count.");
            Require(database.ThrowingIndexItems.TryFind(1, out var itemAfterIndexException) && ReferenceEquals(itemAfterIndexException, originalThrowingIndexItem), "An index exception changed the entity.");

            Require(database.Items.TryReplace(1, new Item(1, "SWORD", "consumable", 10, "Potion")) == false, "A replacement with a duplicate unique key was accepted.");
            Require(database.Items.TryReplace(1, new Item(1, null, "consumable", 10, "Potion")) == false, "A replacement with a null unique index key was accepted.");
            Require(database.Items.TryReplace(1, new Item(1, "potion", null, 10, "Potion")) == false, "A replacement with a null hash index key was accepted.");
            Require(database.Items.TryFind(1, out var itemAfterConflict) && (itemAfterConflict.Code == "potion"), "A failed replacement changed the entity.");
            Require(database.Items.FindBy(item => item.OwnerId, 10).Count == 2, "A failed replacement changed a hash index.");

            database.Items.Replace(1, new Item(1, "potion", "equipment", 20, "Potion"));
            Require(database.Items.FindBy(item => item.OwnerId, 10).Count == 1, "The previous owner bucket was not updated.");
            Require(database.Items.FindBy(item => item.OwnerId, 20).Count == 2, "The replacement owner bucket was not updated.");
            Require(database.Items.FindBy(item => item.Group, "consumable").Count == 0, "The previous group bucket was not updated.");
            Require(database.Items.FindBy(item => item.Group, "equipment").Count == 3, "The replacement group bucket was not updated.");

            Require(database.Items.Remove(2), "The entity removal failed.");
            Require(database.Items.TryFindBy(item => item.Code, "SWORD", out _) == false, "The unique index retained a removed entity.");
            Require(database.Items.Remove(999) == false, "A missing entity was removed.");
            Require(database.Items.ValidateIndexes(), "The index validation failed.");

            RequireThrows<InvalidOperationException>(() => database.Items.FindBy(item => item.Code, "potion"));
            RequireThrows<InvalidOperationException>(() => database.Items.TryFindBy(item => item.OwnerId, 20, out _));
            RequireThrows<InvalidOperationException>(() => database.Items.FindBy(item => item.Name, "Potion"));

            Debug.Log($"{nameof(Test)}> PASSED.");

            static void Require(bool condition, string message)
            {
                if (condition == false)
                    throw new InvalidOperationException(message);
            }

            static void RequireThrows<TException>(Action action)
                where TException : Exception
            {
                try
                {
                    action();
                }
                catch (TException)
                {
                    return;
                }

                throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
            }
        }
    }
}
