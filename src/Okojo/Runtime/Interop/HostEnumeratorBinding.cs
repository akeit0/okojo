using System.Collections;

namespace Okojo.Runtime.Interop;

public sealed class HostEnumeratorBinding(Func<object, IEnumerator> getEnumerator)
{
    public Func<object, IEnumerator> GetEnumerator { get; } = getEnumerator;
}
