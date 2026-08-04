using oojjrs.odb;
using System;
using System.Collections.Generic;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Assets.Sources.Scripts
{
    public sealed class Test3 : MonoBehaviour
    {
        private sealed class Item
        {
            public int Id { get; }
            public long Value { get; }

            public Item(int id, long value)
            {
                Id = id;
                Value = value;
            }
        }

        private sealed class TestOdbContext : OdbContext
        {
            private readonly int CapacityHint;

            internal OdbSet<Item, int> Items => GetSet<Item, int>();

            internal TestOdbContext(int capacityHint)
            {
                CapacityHint = capacityHint;
            }

            protected override void OnModelCreating(OdbModelBuilder modelBuilder)
            {
                modelBuilder.AddEntity<Item, int>("Item", item => item.Id)
                    .SetCapacityHint(CapacityHint);
            }
        }

        private const int ItemCount = 10_000;
        private const int LookupCount = 1_000_000;
        private const int MeasurementCount = 6;
        private const int RandomSeed = 20260804;

        private void Start()
        {
            var items = new Item[ItemCount];
            for (var index = 0; index < items.Length; ++index)
                items[index] = new Item(index, index * 31L);

            var lookupKeys = new int[LookupCount];
            var random = new System.Random(RandomSeed);
            for (var index = 0; index < lookupKeys.Length; ++index)
                lookupKeys[index] = random.Next(ItemCount);

            var warmupItems = new[] { items[0] };
            var warmupDictionary = new Dictionary<int, Item>(warmupItems.Length);
            var warmupOdbItems = new TestOdbContext(warmupItems.Length).Items;
            warmupDictionary.Add(warmupItems[0].Id, warmupItems[0]);
            warmupOdbItems.Add(warmupItems[0]);
            MeasureDictionaryAdd(warmupItems);
            MeasureOdbAdd(warmupItems);
            MeasureDictionaryLookup(warmupDictionary, new[] { warmupItems[0].Id }, out _);
            MeasureOdbLookup(warmupOdbItems, new[] { warmupItems[0].Id }, out _);

            var dictionaryAddSamples = new long[MeasurementCount];
            var odbAddSamples = new long[MeasurementCount];
            for (var measurement = 0; measurement < MeasurementCount; ++measurement)
            {
                CollectGarbage();
                if ((measurement & 1) == 0)
                {
                    dictionaryAddSamples[measurement] = MeasureDictionaryAdd(items);
                    odbAddSamples[measurement] = MeasureOdbAdd(items);
                }
                else
                {
                    odbAddSamples[measurement] = MeasureOdbAdd(items);
                    dictionaryAddSamples[measurement] = MeasureDictionaryAdd(items);
                }
            }

            var dictionary = new Dictionary<int, Item>(items.Length);
            var odbItems = new TestOdbContext(items.Length).Items;
            foreach (var item in items)
            {
                dictionary.Add(item.Id, item);
                odbItems.Add(item);
            }
            Require(odbItems.ValidateIndexes(), "The ODB indexes are inconsistent after additions.");

            CollectGarbage();
            var dictionaryLookupSamples = new long[MeasurementCount];
            var odbLookupSamples = new long[MeasurementCount];
            var dictionaryChecksum = 0L;
            var odbChecksum = 0L;
            for (var measurement = 0; measurement < MeasurementCount; ++measurement)
            {
                if ((measurement & 1) == 0)
                {
                    dictionaryLookupSamples[measurement] = MeasureDictionaryLookup(dictionary, lookupKeys, out dictionaryChecksum);
                    odbLookupSamples[measurement] = MeasureOdbLookup(odbItems, lookupKeys, out odbChecksum);
                }
                else
                {
                    odbLookupSamples[measurement] = MeasureOdbLookup(odbItems, lookupKeys, out odbChecksum);
                    dictionaryLookupSamples[measurement] = MeasureDictionaryLookup(dictionary, lookupKeys, out dictionaryChecksum);
                }
            }

            Require(dictionaryChecksum == odbChecksum, "The Dictionary and ODB lookup checksums are different.");

            var dictionaryAddMilliseconds = ToMilliseconds(GetMedianTicks(dictionaryAddSamples));
            var odbAddMilliseconds = ToMilliseconds(GetMedianTicks(odbAddSamples));
            var dictionaryLookupMilliseconds = ToMilliseconds(GetMedianTicks(dictionaryLookupSamples));
            var odbLookupMilliseconds = ToMilliseconds(GetMedianTicks(odbLookupSamples));
            Debug.Log($"{nameof(Test3)}> ENVIRONMENT : UNITY {Application.unityVersion} / {Application.platform} / EDITOR {Application.isEditor} / DEBUG {Debug.isDebugBuild}");
            Debug.Log($"{nameof(Test3)}> CONFIG : {ItemCount} ITEMS / {LookupCount} LOOKUPS / {MeasurementCount} MEASUREMENTS / PRIMARY ONLY / 0 SECONDARY INDEXES");
            Debug.Log($"{nameof(Test3)}> ADD MEDIAN : DICTIONARY {dictionaryAddMilliseconds:F3} MS / ODB {odbAddMilliseconds:F3} MS / ODB RATIO {odbAddMilliseconds / dictionaryAddMilliseconds:F2} X");
            Debug.Log($"{nameof(Test3)}> LOOKUP MEDIAN : DICTIONARY {dictionaryLookupMilliseconds:F3} MS / ODB {odbLookupMilliseconds:F3} MS / ODB RATIO {odbLookupMilliseconds / dictionaryLookupMilliseconds:F2} X");
            Debug.Log($"{nameof(Test3)}> CHECKSUM : {dictionaryChecksum}");
            Debug.Log($"{nameof(Test3)}> PASSED.");

            static void CollectGarbage()
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            static long GetMedianTicks(long[] samples)
            {
                Array.Sort(samples);
                var middleIndex = samples.Length / 2;
                if ((samples.Length & 1) == 0)
                    return (samples[middleIndex - 1] + samples[middleIndex]) / 2;

                return samples[middleIndex];
            }

            static long MeasureDictionaryAdd(Item[] targetItems)
            {
                var target = new Dictionary<int, Item>(targetItems.Length);
                var startedAt = Stopwatch.GetTimestamp();
                foreach (var item in targetItems)
                    target.Add(item.Id, item);
                var elapsedTicks = Stopwatch.GetTimestamp() - startedAt;
                Require(target.Count == targetItems.Length, "The Dictionary add count is invalid.");
                return elapsedTicks;
            }

            static long MeasureDictionaryLookup(Dictionary<int, Item> target, int[] targetLookupKeys, out long checksum)
            {
                var valueSum = 0L;
                var startedAt = Stopwatch.GetTimestamp();
                foreach (var key in targetLookupKeys)
                {
                    if (target.TryGetValue(key, out var item) == false)
                        throw new InvalidOperationException($"The Dictionary lookup key '{key}' was not found.");
                    valueSum += item.Value;
                }
                var elapsedTicks = Stopwatch.GetTimestamp() - startedAt;
                checksum = valueSum;
                return elapsedTicks;
            }

            static long MeasureOdbAdd(Item[] targetItems)
            {
                var target = new TestOdbContext(targetItems.Length).Items;
                var startedAt = Stopwatch.GetTimestamp();
                foreach (var item in targetItems)
                    target.Add(item);
                var elapsedTicks = Stopwatch.GetTimestamp() - startedAt;
                Require(target.Count == targetItems.Length, "The ODB add count is invalid.");
                return elapsedTicks;
            }

            static long MeasureOdbLookup(OdbSet<Item, int> target, int[] targetLookupKeys, out long checksum)
            {
                var valueSum = 0L;
                var startedAt = Stopwatch.GetTimestamp();
                foreach (var key in targetLookupKeys)
                {
                    if (target.TryFind(key, out var item) == false)
                        throw new InvalidOperationException($"The ODB lookup key '{key}' was not found.");
                    valueSum += item.Value;
                }
                var elapsedTicks = Stopwatch.GetTimestamp() - startedAt;
                checksum = valueSum;
                return elapsedTicks;
            }

            static void Require(bool condition, string message)
            {
                if (condition == false)
                    throw new InvalidOperationException(message);
            }

            static double ToMilliseconds(long ticks)
            {
                return ticks * 1_000.0 / Stopwatch.Frequency;
            }
        }
    }
}
