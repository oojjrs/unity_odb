using System;
using System.Collections.Generic;
using System.Globalization;

namespace oojjrs.odb
{
    public sealed class OdbIdentity<TKey> where TKey : notnull
    {
        private interface KeyOperationsInterface<TValue>
        {
            TValue FromInt64(long value);
            long ToInt64(TValue value);
        }

        private sealed class Int32KeyOperations : KeyOperationsInterface<int>
        {
            internal static readonly Int32KeyOperations Instance = new();

            int KeyOperationsInterface<int>.FromInt64(long value)
            {
                return checked((int)value);
            }

            long KeyOperationsInterface<int>.ToInt64(int value)
            {
                return value;
            }
        }

        private sealed class Int64KeyOperations : KeyOperationsInterface<long>
        {
            internal static readonly Int64KeyOperations Instance = new();

            long KeyOperationsInterface<long>.FromInt64(long value)
            {
                return value;
            }

            long KeyOperationsInterface<long>.ToInt64(long value)
            {
                return value;
            }
        }

        private sealed class IdentityDefinition : OdbModelBuilder.IdentityInterface
        {
            private readonly OdbIdentity<TKey> Identity;

            bool OdbModelBuilder.IdentityInterface.HasParticipants => Identity._participantCount > 0;
            string OdbModelBuilder.IdentityInterface.Name => Identity.Name;
            string OdbModelBuilder.IdentityInterface.Scope => Identity.Scope;
            string OdbModelBuilder.IdentityInterface.StateKey => Identity.StateKey;

            internal IdentityDefinition(OdbIdentity<TKey> identity)
            {
                Identity = identity;
            }

            OdbModelBuilder.IdentityRuntimeInterface OdbModelBuilder.IdentityInterface.CreateRuntime()
            {
                return new IdentityRuntime(Identity);
            }
        }

        internal sealed class IdentityRuntime : OdbModelBuilder.IdentityRuntimeInterface
        {
            private readonly HashSet<TKey> ActiveKeys;
            private readonly OdbIdentity<TKey> Definition;
            private readonly KeyOperationsInterface<TKey> KeyOperations;
            private readonly long MaximumValue;

            private long _highWaterMark;

            string OdbModelBuilder.IdentityRuntimeInterface.KeyTypeName => typeof(TKey) == typeof(int) ? "Int32" : "Int64";
            long OdbModelBuilder.IdentityRuntimeInterface.HighWaterMark => _highWaterMark;
            string OdbModelBuilder.IdentityRuntimeInterface.Name => Definition.Name;
            string OdbModelBuilder.IdentityRuntimeInterface.Scope => Definition.Scope;
            string OdbModelBuilder.IdentityRuntimeInterface.StateKey => Definition.StateKey;

            internal IdentityRuntime(OdbIdentity<TKey> definition)
            {
                Definition = definition;
                if (typeof(TKey) == typeof(int))
                {
                    KeyOperations = (KeyOperationsInterface<TKey>)(object)Int32KeyOperations.Instance;
                    MaximumValue = int.MaxValue;
                }
                else
                {
                    KeyOperations = (KeyOperationsInterface<TKey>)(object)Int64KeyOperations.Instance;
                    MaximumValue = long.MaxValue;
                }

                ActiveKeys = definition.IsGlobal ? new HashSet<TKey>() : null;
            }

            void OdbModelBuilder.IdentityRuntimeInterface.AdvanceToAtLeast(long highWaterMark)
            {
                AdvanceToAtLeast(highWaterMark);
            }

            void OdbModelBuilder.IdentityRuntimeInterface.Reset()
            {
                _highWaterMark = 0;
                ActiveKeys?.Clear();
            }

            private void AdvanceToAtLeast(long highWaterMark)
            {
                if ((highWaterMark < 0) || (highWaterMark > MaximumValue))
                    throw new ArgumentOutOfRangeException(nameof(highWaterMark), highWaterMark, $"The identity high-water mark must be between 0 and {MaximumValue.ToString(CultureInfo.InvariantCulture)}.");

                if (highWaterMark > _highWaterMark)
                    _highWaterMark = highWaterMark;
            }

            internal bool CanRegister(TKey primaryKey)
            {
                return (ActiveKeys == null) || (ActiveKeys.Contains(primaryKey) == false);
            }

            internal TKey Generate()
            {
                if (_highWaterMark >= MaximumValue)
                    throw new OverflowException($"The identity '{Definition.Name}' exhausted the key type '{typeof(TKey).FullName}'.");

                ++_highWaterMark;
                return KeyOperations.FromInt64(_highWaterMark);
            }

            internal bool HasRegistered(TKey primaryKey)
            {
                return (ActiveKeys == null) || ActiveKeys.Contains(primaryKey);
            }

            internal void Observe(TKey primaryKey)
            {
                AdvanceToAtLeast(GetPositiveValue(primaryKey));
            }

            internal void Register(TKey primaryKey)
            {
                if ((ActiveKeys != null) && (ActiveKeys.Add(primaryKey) == false))
                    throw new InvalidOperationException($"The global identity '{Definition.Name}' already contains the primary key.");
            }

            internal void Unregister(TKey primaryKey)
            {
                if ((ActiveKeys != null) && (ActiveKeys.Remove(primaryKey) == false))
                    throw new InvalidOperationException($"The global identity '{Definition.Name}' does not contain the primary key.");
            }

            private long GetPositiveValue(TKey primaryKey)
            {
                var value = KeyOperations.ToInt64(primaryKey);
                if (value <= 0)
                    throw new OdbGeneratedPrimaryKeyException("A generated identity key must be greater than zero.");

                return value;
            }

            internal void Validate(TKey primaryKey)
            {
                GetPositiveValue(primaryKey);
            }
        }

        private readonly IdentityDefinition Definition;
        private readonly OdbModelBuilder Owner;

        private int _participantCount;

        public string Name { get; }
        internal OdbModelBuilder.IdentityInterface DefinitionInterface => Definition;
        internal bool IsGlobal { get; }
        internal string Scope { get; }
        internal string StateKey { get; }

        internal OdbIdentity(OdbModelBuilder owner, string scope, string name, bool isGlobal)
        {
            var keyType = typeof(TKey);
            if ((keyType != typeof(int)) && (keyType != typeof(long)))
                throw new NotSupportedException($"Identity generation does not support the key type '{keyType.FullName}'.");

            Owner = owner;
            Scope = scope;
            Name = name;
            StateKey = scope + "\0" + name;
            IsGlobal = isGlobal;
            Definition = new IdentityDefinition(this);
        }

        internal bool IsOwnedBy(OdbModelBuilder owner)
        {
            return ReferenceEquals(Owner, owner);
        }

        internal void RegisterParticipant()
        {
            ++_participantCount;
        }
    }
}
