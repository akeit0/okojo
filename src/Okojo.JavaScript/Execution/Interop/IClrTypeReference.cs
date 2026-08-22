using System.Diagnostics.CodeAnalysis;

namespace Okojo.JavaScript.Execution.Interop;

internal interface IClrTypeReference
{
    [DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors
            | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor
    )]
    Type ClrType { get; }
}
