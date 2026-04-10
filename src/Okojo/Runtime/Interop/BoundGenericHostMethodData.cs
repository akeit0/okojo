namespace Okojo.Runtime.Interop;

internal sealed class BoundGenericHostMethodData
{
    internal BoundGenericHostMethodData(HostNamedMemberDescriptor member, object? target)
    {
        Member = member;
        Target = target;
    }

    internal HostNamedMemberDescriptor Member { get; }
    internal object? Target { get; }
}
