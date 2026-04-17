using System.Collections;

namespace Okojo.Runtime.Interop;

public sealed class HostEnumeratorBinding
{
    internal Func<object, HostEnumeratorAdapter> CreateEnumerator { get; }

    public HostEnumeratorBinding(Func<object, IEnumerator> getEnumerator)
    {
        ArgumentNullException.ThrowIfNull(getEnumerator);
        CreateEnumerator = target => HostEnumeratorAdapter.FromInterface(getEnumerator(target));
    }

    public HostEnumeratorBinding(
        Func<object, object> getEnumerator,
        Func<object, bool> moveNext,
        Func<object, object?> getCurrent,
        Action<object>? dispose = null)
    {
        ArgumentNullException.ThrowIfNull(getEnumerator);
        ArgumentNullException.ThrowIfNull(moveNext);
        ArgumentNullException.ThrowIfNull(getCurrent);
        CreateEnumerator = target => new HostEnumeratorAdapter(getEnumerator(target), moveNext, getCurrent, dispose);
    }
}
