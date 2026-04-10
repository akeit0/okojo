namespace Okojo.Compiler.Experimental;

internal sealed class CompilerBindingPlan : IDisposable
{
    private readonly PooledArrayBuilder<CompilerPlannedBinding> bindings;
    private bool disposed;

    internal CompilerBindingPlan(PooledArrayBuilder<CompilerPlannedBinding> bindings)
    {
        this.bindings = bindings;
    }

    public ReadOnlySpan<CompilerPlannedBinding> Bindings => bindings.AsSpan();

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        bindings.Dispose();
    }
}
