using System.Diagnostics.CodeAnalysis;

namespace Okojo.Runtime.Interop;

public sealed class HostBinding(
    [DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.NonPublicConstructors |
        DynamicallyAccessedMemberTypes.PublicMethods |
        DynamicallyAccessedMemberTypes.NonPublicMethods |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicNestedTypes |
        DynamicallyAccessedMemberTypes.NonPublicNestedTypes |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties |
        DynamicallyAccessedMemberTypes.PublicEvents |
        DynamicallyAccessedMemberTypes.NonPublicEvents)]
    Type clrType,
    HostMemberBinding[] instanceMembers,
    HostMemberBinding[] staticMembers)
{
    [DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.NonPublicConstructors |
        DynamicallyAccessedMemberTypes.PublicMethods |
        DynamicallyAccessedMemberTypes.NonPublicMethods |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicNestedTypes |
        DynamicallyAccessedMemberTypes.NonPublicNestedTypes |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties |
        DynamicallyAccessedMemberTypes.PublicEvents |
        DynamicallyAccessedMemberTypes.NonPublicEvents)]
    public Type ClrType { get; } = clrType;

    public HostMemberBinding[] InstanceMembers { get; } = instanceMembers;
    public HostMemberBinding[] StaticMembers { get; } = staticMembers;
    public HostIndexerBinding? Indexer { get; init; }
    public HostEnumeratorBinding? Enumerator { get; init; }
    public HostAsyncEnumeratorBinding? AsyncEnumerator { get; init; }
}
