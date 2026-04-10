namespace Okojo.Runtime;

public sealed partial class JsRealm
{
    private readonly CompileCollectionPool compileCollectionPool = new();

    internal List<T> RentCompileList<T>(int minCapacity = 0)
    {
        return compileCollectionPool.RentList<T>(minCapacity);
    }

    internal void ReturnCompileList<T>(List<T>? list)
    {
        compileCollectionPool.ReturnList(list);
    }

    internal List<T> RentScratchList<T>(int minCapacity = 0)
    {
        return compileCollectionPool.RentList<T>(minCapacity);
    }

    internal void ReturnScratchList<T>(List<T>? list)
    {
        compileCollectionPool.ReturnList(list);
    }

    internal Dictionary<TKey, TValue> RentCompileDictionary<TKey, TValue>(
        int minCapacity = 0,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        return compileCollectionPool.RentDictionary<TKey, TValue>(minCapacity, comparer);
    }

    internal void ReturnCompileDictionary<TKey, TValue>(Dictionary<TKey, TValue>? dictionary)
        where TKey : notnull
    {
        compileCollectionPool.ReturnDictionary(dictionary);
    }

    internal HashSet<T> RentCompileHashSet<T>(int minCapacity = 0, IEqualityComparer<T>? comparer = null)
    {
        return compileCollectionPool.RentHashSet(minCapacity, comparer);
    }

    internal void ReturnCompileHashSet<T>(HashSet<T>? set)
    {
        compileCollectionPool.ReturnHashSet(set);
    }

    internal HashSet<T> RentScratchHashSet<T>(int minCapacity = 0, IEqualityComparer<T>? comparer = null)
    {
        return compileCollectionPool.RentHashSet(minCapacity, comparer);
    }

    internal void ReturnScratchHashSet<T>(HashSet<T>? set)
    {
        compileCollectionPool.ReturnHashSet(set);
    }

    internal Stack<T> RentCompileStack<T>(int minCapacity = 0)
    {
        return compileCollectionPool.RentStack<T>(minCapacity);
    }

    internal void ReturnCompileStack<T>(Stack<T>? stack)
    {
        compileCollectionPool.ReturnStack(stack);
    }

    private sealed class CompileCollectionPool
    {
        private const int MaxRetainedCollectionCapacity = 4096;
        private readonly Dictionary<Type, Stack<object>> dictionaries = new();
        private readonly object gate = new();
        private readonly Dictionary<Type, Stack<object>> lists = new();
        private readonly Dictionary<Type, Stack<object>> sets = new();
        private readonly Dictionary<Type, Stack<object>> stacks = new();

        public List<T> RentList<T>(int minCapacity)
        {
            lock (gate)
            {
                var key = typeof(List<T>);
                if (lists.TryGetValue(key, out var pool) && pool.Count != 0)
                {
                    var list = (List<T>)pool.Pop();
                    list.Clear();
                    if (minCapacity > list.Capacity)
                        list.Capacity = minCapacity;
                    return list;
                }
            }

            return minCapacity > 0 ? new(minCapacity) : new List<T>();
        }

        public void ReturnList<T>(List<T>? list)
        {
            if (list is null)
                return;
            if (list.Capacity > MaxRetainedCollectionCapacity)
                return;

            list.Clear();
            lock (gate)
            {
                var key = typeof(List<T>);
                if (!lists.TryGetValue(key, out var pool))
                {
                    pool = new();
                    lists[key] = pool;
                }

                pool.Push(list);
            }
        }

        public Dictionary<TKey, TValue> RentDictionary<TKey, TValue>(int minCapacity, IEqualityComparer<TKey>? comparer)
            where TKey : notnull
        {
            lock (gate)
            {
                var key = typeof(Dictionary<TKey, TValue>);
                if (dictionaries.TryGetValue(key, out var pool) && pool.Count != 0)
                {
                    var dictionary = (Dictionary<TKey, TValue>)pool.Pop();
                    if (comparer is not null && !ReferenceEquals(dictionary.Comparer, comparer))
                        return minCapacity > 0
                            ? new(minCapacity, comparer)
                            : new Dictionary<TKey, TValue>(comparer);

                    dictionary.Clear();
                    dictionary.EnsureCapacity(minCapacity);
                    return dictionary;
                }
            }

            if (comparer is not null)
                return minCapacity > 0
                    ? new(minCapacity, comparer)
                    : new Dictionary<TKey, TValue>(comparer);
            return minCapacity > 0 ? new(minCapacity) : new Dictionary<TKey, TValue>();
        }

        public void ReturnDictionary<TKey, TValue>(Dictionary<TKey, TValue>? dictionary) where TKey : notnull
        {
            if (dictionary is null)
                return;
            if (dictionary.Count > MaxRetainedCollectionCapacity)
                return;

            dictionary.Clear();
            lock (gate)
            {
                var key = typeof(Dictionary<TKey, TValue>);
                if (!dictionaries.TryGetValue(key, out var pool))
                {
                    pool = new();
                    dictionaries[key] = pool;
                }

                pool.Push(dictionary);
            }
        }

        public HashSet<T> RentHashSet<T>(int minCapacity, IEqualityComparer<T>? comparer)
        {
            lock (gate)
            {
                var key = typeof(HashSet<T>);
                if (sets.TryGetValue(key, out var pool) && pool.Count != 0)
                {
                    var set = (HashSet<T>)pool.Pop();
                    if (comparer is not null && !ReferenceEquals(set.Comparer, comparer))
                        return minCapacity > 0 ? new(minCapacity, comparer) : new HashSet<T>(comparer);

                    set.Clear();
                    set.EnsureCapacity(minCapacity);
                    return set;
                }
            }

            if (comparer is not null)
                return minCapacity > 0 ? new(minCapacity, comparer) : new HashSet<T>(comparer);
            return minCapacity > 0 ? new(minCapacity) : new HashSet<T>();
        }

        public void ReturnHashSet<T>(HashSet<T>? set)
        {
            if (set is null)
                return;
            if (set.Count > MaxRetainedCollectionCapacity)
                return;

            set.Clear();
            lock (gate)
            {
                var key = typeof(HashSet<T>);
                if (!sets.TryGetValue(key, out var pool))
                {
                    pool = new();
                    sets[key] = pool;
                }

                pool.Push(set);
            }
        }

        public Stack<T> RentStack<T>(int minCapacity)
        {
            lock (gate)
            {
                var key = typeof(Stack<T>);
                if (stacks.TryGetValue(key, out var pool) && pool.Count != 0)
                {
                    var stack = (Stack<T>)pool.Pop();
                    stack.Clear();
                    stack.EnsureCapacity(minCapacity);
                    return stack;
                }
            }

            return minCapacity > 0 ? new(minCapacity) : new Stack<T>();
        }

        public void ReturnStack<T>(Stack<T>? stack)
        {
            if (stack is null)
                return;
            if (stack.Count > MaxRetainedCollectionCapacity)
                return;

            stack.Clear();
            lock (gate)
            {
                var key = typeof(Stack<T>);
                if (!stacks.TryGetValue(key, out var pool))
                {
                    pool = new();
                    stacks[key] = pool;
                }

                pool.Push(stack);
            }
        }
    }
}
