using System.Collections;

namespace Okojo.Runtime.Interop;

internal sealed class HostEnumeratorDescriptor
{
    internal HostEnumeratorDescriptor(Func<object, IEnumerator> getEnumerator)
    {
        GetEnumerator = getEnumerator;
    }

    internal Func<object, IEnumerator> GetEnumerator { get; }
}
