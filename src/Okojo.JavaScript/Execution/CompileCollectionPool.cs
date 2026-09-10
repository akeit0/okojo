namespace Okojo.JavaScript.Execution;

// A compiler workspace has no realm semantics and is never retained by emitted code.
internal sealed class CompileCollectionPool
{
    private const int MaxRetainedCollectionCapacity = 4096;
    private readonly Dictionary<Type, Stack<object>> dictionaries = new();
    private readonly Dictionary<Type, Stack<object>> lists = new();
    private readonly Dictionary<Type, Stack<object>> sets = new();
    private readonly Dictionary<Type, Stack<object>> stacks = new();

    public List<T> RentCompileList<T>(int minCapacity = 0)
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

        return minCapacity > 0 ? new(minCapacity) : new List<T>();
    }

    public void ReturnCompileList<T>(List<T>? list)
    {
        if (list is null)
            return;
        if (list.Capacity > MaxRetainedCollectionCapacity)
            return;

        list.Clear();
        var key = typeof(List<T>);
        if (!lists.TryGetValue(key, out var p))
        {
            p = new();
            lists[key] = p;
        }
        p.Push(list);
    }

    public Dictionary<TKey, TValue> RentCompileDictionary<TKey, TValue>(
        int minCapacity = 0,
        IEqualityComparer<TKey>? comparer = null
    )
        where TKey : notnull
    {
        var key = typeof(Dictionary<TKey, TValue>);
        if (dictionaries.TryGetValue(key, out var pool) && pool.Count != 0)
        {
            var dictionary = (Dictionary<TKey, TValue>)pool.Pop();
            if (!ReferenceEquals(dictionary.Comparer, comparer ?? EqualityComparer<TKey>.Default))
                return minCapacity > 0
                    ? new(minCapacity, comparer)
                    : new Dictionary<TKey, TValue>(comparer);

            dictionary.Clear();
            dictionary.EnsureCapacity(minCapacity);
            return dictionary;
        }

        if (comparer is not null)
            return minCapacity > 0
                ? new(minCapacity, comparer)
                : new Dictionary<TKey, TValue>(comparer);
        return minCapacity > 0 ? new(minCapacity) : new Dictionary<TKey, TValue>();
    }

    public void ReturnCompileDictionary<TKey, TValue>(Dictionary<TKey, TValue>? dictionary)
        where TKey : notnull
    {
        if (dictionary is null)
            return;
        if (dictionary.EnsureCapacity(0) > MaxRetainedCollectionCapacity)
            return;

        dictionary.Clear();
        var key = typeof(Dictionary<TKey, TValue>);
        if (!dictionaries.TryGetValue(key, out var p))
        {
            p = new();
            dictionaries[key] = p;
        }
        p.Push(dictionary);
    }

    public HashSet<T> RentCompileHashSet<T>(
        int minCapacity = 0,
        IEqualityComparer<T>? comparer = null
    )
    {
        var key = typeof(HashSet<T>);
        if (sets.TryGetValue(key, out var pool) && pool.Count != 0)
        {
            var set = (HashSet<T>)pool.Pop();
            if (!ReferenceEquals(set.Comparer, comparer ?? EqualityComparer<T>.Default))
                return minCapacity > 0 ? new(minCapacity, comparer) : new HashSet<T>(comparer);

            set.Clear();
            set.EnsureCapacity(minCapacity);
            return set;
        }

        if (comparer is not null)
            return minCapacity > 0 ? new(minCapacity, comparer) : new HashSet<T>(comparer);
        return minCapacity > 0 ? new(minCapacity) : new HashSet<T>();
    }

    public void ReturnCompileHashSet<T>(HashSet<T>? set)
    {
        if (set is null)
            return;
        if (set.EnsureCapacity(0) > MaxRetainedCollectionCapacity)
            return;

        set.Clear();
        var key = typeof(HashSet<T>);
        if (!sets.TryGetValue(key, out var p))
        {
            p = new();
            sets[key] = p;
        }
        p.Push(set);
    }

    public Stack<T> RentCompileStack<T>(int minCapacity = 0)
    {
        var key = typeof(Stack<T>);
        if (stacks.TryGetValue(key, out var pool) && pool.Count != 0)
        {
            var stack = (Stack<T>)pool.Pop();
            stack.Clear();
            stack.EnsureCapacity(minCapacity);
            return stack;
        }

        return minCapacity > 0 ? new(minCapacity) : new Stack<T>();
    }

    public void ReturnCompileStack<T>(Stack<T>? stack)
    {
        if (stack is null)
            return;
        if (stack.EnsureCapacity(0) > MaxRetainedCollectionCapacity)
            return;

        stack.Clear();
        var key = typeof(Stack<T>);
        if (!stacks.TryGetValue(key, out var p))
        {
            p = new();
            stacks[key] = p;
        }
        p.Push(stack);
    }
}
