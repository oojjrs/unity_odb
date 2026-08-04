using System;
using System.Collections;
using System.Collections.Generic;

namespace oojjrs.odb
{
    public readonly struct OdbIndexView<TEntity, TKey> : IReadOnlyCollection<TEntity>
        where TEntity : class
        where TKey : notnull
    {
        public struct Enumerator : IEnumerator<TEntity>
        {
            private readonly Dictionary<TKey, TEntity> Entities;
            private readonly bool HasPrimaryKeys;

            private HashSet<TKey>.Enumerator _primaryKeyEnumerator;

            object IEnumerator.Current => Current;
            TEntity IEnumerator<TEntity>.Current => Current;

            public TEntity Current { get; private set; }

            internal Enumerator(Dictionary<TKey, TEntity> entities, HashSet<TKey> primaryKeys)
            {
                Entities = entities;
                HasPrimaryKeys = primaryKeys != null;

                _primaryKeyEnumerator = primaryKeys != null ? primaryKeys.GetEnumerator() : default;

                Current = null;
            }

            void IDisposable.Dispose()
            {
                _primaryKeyEnumerator.Dispose();
            }

            bool IEnumerator.MoveNext()
            {
                return MoveNext();
            }

            void IEnumerator.Reset()
            {
                throw new NotSupportedException();
            }

            public bool MoveNext()
            {
                if (HasPrimaryKeys == false)
                {
                    Current = null;
                    return false;
                }

                while (_primaryKeyEnumerator.MoveNext())
                {
                    if (Entities.TryGetValue(_primaryKeyEnumerator.Current, out var entity))
                    {
                        Current = entity;
                        return true;
                    }
                }

                Current = null;
                return false;
            }
        }

        private readonly Dictionary<TKey, TEntity> Entities;
        private readonly HashSet<TKey> PrimaryKeys;

        int IReadOnlyCollection<TEntity>.Count => Count;

        public int Count => PrimaryKeys?.Count ?? 0;

        internal OdbIndexView(Dictionary<TKey, TEntity> entities, HashSet<TKey> primaryKeys)
        {
            Entities = entities;
            PrimaryKeys = primaryKeys;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator<TEntity> IEnumerable<TEntity>.GetEnumerator()
        {
            return GetEnumerator();
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(Entities, PrimaryKeys);
        }
    }
}
