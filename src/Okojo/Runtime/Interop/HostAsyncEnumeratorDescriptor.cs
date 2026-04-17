using System.Threading.Tasks;

namespace Okojo.Runtime.Interop;

internal sealed class HostAsyncEnumeratorDescriptor
{
    internal HostAsyncEnumeratorDescriptor(Func<object, HostAsyncEnumeratorAdapter> createEnumerator)
    {
        CreateEnumerator = createEnumerator;
    }

    internal Func<object, HostAsyncEnumeratorAdapter> CreateEnumerator { get; }
}

internal sealed class HostAsyncEnumeratorAdapter(
    object enumerator,
    Func<object, ValueTask<bool>> moveNextAsync,
    Func<object, object?> getCurrent,
    Func<object, ValueTask>? disposeAsync)
{
    internal object Enumerator { get; } =
        enumerator ?? throw new InvalidOperationException("GetAsyncEnumerator returned null.");

    internal ValueTask<bool> MoveNextAsync()
    {
        return moveNextAsync(Enumerator);
    }

    internal JsValue GetCurrentAsJsValue(JsRealm realm)
    {
        return HostValueConverter.ConvertToJsValue(realm, getCurrent(Enumerator));
    }

    internal ValueTask DisposeAsync()
    {
        return disposeAsync?.Invoke(Enumerator) ?? ValueTask.CompletedTask;
    }
}
