namespace Okojo.JavaScript.Compiler.Experimental;

internal sealed class CompilerBindingCollectionResult : IDisposable
{
    private readonly PooledArrayBuilder<CompilerCollectedScope> scopes;
    private readonly PooledArrayBuilder<CompilerCollectedBinding> bindings;
    private readonly PooledArrayBuilder<CompilerCollectedReference> references;
    private List<string>? annexBIfFunctionNames;
    private bool disposed;

    internal CompilerBindingCollectionResult(
        PooledArrayBuilder<CompilerCollectedScope> scopes,
        PooledArrayBuilder<CompilerCollectedBinding> bindings,
        PooledArrayBuilder<CompilerCollectedReference> references
    )
    {
        this.scopes = scopes;
        this.bindings = bindings;
        this.references = references;
    }

    public int RootScopeId => 0;

    public ReadOnlySpan<CompilerCollectedScope> Scopes => scopes.AsSpan();
    public ReadOnlySpan<CompilerCollectedBinding> Bindings => bindings.AsSpan();
    public ReadOnlySpan<CompilerCollectedReference> References => references.AsSpan();

    internal void AddAnnexBIfFunctionName(string name)
    {
        annexBIfFunctionNames ??= [];
        if (!annexBIfFunctionNames.Contains(name))
            annexBIfFunctionNames.Add(name);
    }

    internal List<string>? AnnexBIfFunctionNames => annexBIfFunctionNames;

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        scopes.Dispose();
        bindings.Dispose();
        references.Dispose();
    }
}
