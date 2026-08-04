namespace oojjrs.odb
{
    internal interface OdbIndexRuntimeInterface<TEntity, TKey>
        where TEntity : class
        where TKey : notnull
    {
        string Name { get; }

        void Add(TEntity entity, TKey primaryKey);
        bool CanAdd(TEntity entity);
        bool CanReplace(TEntity previous, TEntity replacement);
        void Clear();
        void Remove(TEntity entity, TKey primaryKey);
        void Replace(TEntity previous, TEntity replacement, TKey primaryKey);
        bool Validate();
    }
}
