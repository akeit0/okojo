using System.Threading.Tasks;

namespace Okojo.Runtime.Interop;

public sealed class HostAsyncEnumeratorBinding
{
    internal Func<object, HostAsyncEnumeratorAdapter> CreateEnumerator { get; }

    public HostAsyncEnumeratorBinding(
        Func<object, object> getAsyncEnumerator,
        Func<object, ValueTask<bool>> moveNextAsync,
        Func<object, object?> getCurrent,
        Func<object, ValueTask>? disposeAsync = null)
    {
        ArgumentNullException.ThrowIfNull(getAsyncEnumerator);
        ArgumentNullException.ThrowIfNull(moveNextAsync);
        ArgumentNullException.ThrowIfNull(getCurrent);
        CreateEnumerator = target =>
            new HostAsyncEnumeratorAdapter(getAsyncEnumerator(target), moveNextAsync, getCurrent, disposeAsync);
    }

    public HostAsyncEnumeratorBinding(
        Func<object, object> getAsyncEnumerator,
        Func<object, Task<bool>> moveNextAsync,
        Func<object, object?> getCurrent,
        Func<object, Task>? disposeAsync = null)
        : this(
            getAsyncEnumerator,
            state => new ValueTask<bool>(moveNextAsync(state)),
            getCurrent,
            disposeAsync is null ? null : state => new ValueTask(disposeAsync(state)))
    {
    }
}
