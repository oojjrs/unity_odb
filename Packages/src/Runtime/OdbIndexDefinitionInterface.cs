using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace oojjrs.odb
{
    internal interface OdbIndexDefinitionInterface<TEntity, TKey>
        where TEntity : class
        where TKey : notnull
    {
        public readonly struct Property : IEquatable<Property>
        {
            private readonly PropertyInfo Info;

            internal string Name => Info.Name;

            private Property(PropertyInfo info)
            {
                Info = info;
            }

            internal static Property Create<TIndexKey>(Expression<Func<TEntity, TIndexKey>> indexKeySelector)
            {
                if (indexKeySelector == null)
                    throw new ArgumentNullException(nameof(indexKeySelector));

                if ((indexKeySelector.Body is MemberExpression memberExpression) && ReferenceEquals(memberExpression.Expression, indexKeySelector.Parameters[0]) && (memberExpression.Member is PropertyInfo property))
                    return new(property);

                throw new ArgumentException("An index selector must directly select an entity property.", nameof(indexKeySelector));
            }

            public override bool Equals(object value)
            {
                return (value is Property other) && Object.Equals(Info, other.Info);
            }

            public override int GetHashCode()
            {
                return Info?.GetHashCode() ?? 0;
            }

            bool IEquatable<Property>.Equals(Property other)
            {
                return Object.Equals(Info, other.Info);
            }

            internal Func<TEntity, TIndexKey> CreateKeySelector<TIndexKey>()
            {
                var getMethod = Info.GetGetMethod(true);
                if (getMethod != null)
                {
                    try
                    {
                        return (Func<TEntity, TIndexKey>)getMethod.CreateDelegate(typeof(Func<TEntity, TIndexKey>));
                    }
                    catch (ArgumentException exception)
                    {
                        throw new InvalidOperationException($"The indexed property '{Name}' cannot be bound to entity type '{typeof(TEntity).FullName}'.", exception);
                    }
                }

                throw new InvalidOperationException($"The indexed property '{Name}' does not have a getter.");
            }
        }

        Property IndexedProperty { get; }

        OdbIndexRuntimeInterface<TEntity, TKey> CreateIndex(Dictionary<TKey, TEntity> entities, int capacityHint);
    }
}
