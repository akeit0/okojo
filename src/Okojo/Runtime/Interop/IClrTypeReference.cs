using System.Diagnostics.CodeAnalysis;

namespace Okojo.Runtime.Interop;

internal interface IClrTypeReference
{
    [DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    Type ClrType { get; }
}
