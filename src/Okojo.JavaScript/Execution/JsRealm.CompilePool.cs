namespace Okojo.JavaScript.Execution;

public sealed partial class JsRealm
{
    private readonly CompileCollectionPool compileCollectionPool = new();
    internal CompileCollectionPool CompilationPool => compileCollectionPool;

    internal List<T> RentCompileList<T>(int minCapacity = 0)
    {
        return compileCollectionPool.RentCompileList<T>(minCapacity);
    }

    internal void ReturnCompileList<T>(List<T>? list)
    {
        compileCollectionPool.ReturnCompileList(list);
    }

    internal List<T> RentScratchList<T>(int minCapacity = 0)
    {
        return compileCollectionPool.RentCompileList<T>(minCapacity);
    }

    internal void ReturnScratchList<T>(List<T>? list)
    {
        compileCollectionPool.ReturnCompileList(list);
    }

    internal Dictionary<TKey, TValue> RentCompileDictionary<TKey, TValue>(
        int minCapacity = 0,
        IEqualityComparer<TKey>? comparer = null
    )
        where TKey : notnull
    {
        return compileCollectionPool.RentCompileDictionary<TKey, TValue>(minCapacity, comparer);
    }

    internal void ReturnCompileDictionary<TKey, TValue>(Dictionary<TKey, TValue>? dictionary)
        where TKey : notnull
    {
        compileCollectionPool.ReturnCompileDictionary(dictionary);
    }

    internal HashSet<T> RentCompileHashSet<T>(
        int minCapacity = 0,
        IEqualityComparer<T>? comparer = null
    )
    {
        return compileCollectionPool.RentCompileHashSet(minCapacity, comparer);
    }

    internal void ReturnCompileHashSet<T>(HashSet<T>? set)
    {
        compileCollectionPool.ReturnCompileHashSet(set);
    }

    internal HashSet<T> RentScratchHashSet<T>(
        int minCapacity = 0,
        IEqualityComparer<T>? comparer = null
    )
    {
        return compileCollectionPool.RentCompileHashSet(minCapacity, comparer);
    }

    internal void ReturnScratchHashSet<T>(HashSet<T>? set)
    {
        compileCollectionPool.ReturnCompileHashSet(set);
    }

    internal Stack<T> RentCompileStack<T>(int minCapacity = 0)
    {
        return compileCollectionPool.RentCompileStack<T>(minCapacity);
    }

    internal void ReturnCompileStack<T>(Stack<T>? stack)
    {
        compileCollectionPool.ReturnCompileStack(stack);
    }
}
