using System.Collections;

namespace Okojo.Runtime.Interop;

internal sealed class HostEnumeratorAdapter(
    object enumerator,
    Func<object, bool> moveNext,
    Func<object, object?> getCurrent,
    Action<object>? dispose)
{
    internal object Enumerator { get; } = enumerator ?? throw new InvalidOperationException("GetEnumerator returned null.");

    internal bool MoveNext()
    {
        return moveNext(Enumerator);
    }

    internal JsValue GetCurrentAsJsValue(JsRealm realm)
    {
        return HostValueConverter.ConvertToJsValue(realm, getCurrent(Enumerator));
    }

    internal void Dispose()
    {
        dispose?.Invoke(Enumerator);
    }

    internal static HostEnumeratorAdapter FromInterface(IEnumerator enumerator)
    {
        return new(enumerator,
            static state => ((IEnumerator)state).MoveNext(),
            static state => ((IEnumerator)state).Current,
            static state =>
            {
                if (state is IDisposable disposable)
                    disposable.Dispose();
            });
    }
}
