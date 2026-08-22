namespace Okojo.JavaScript.Execution.Interop;

internal interface IClrTypeFunctionData : IClrTypeReference
{
    string DisplayTag { get; }
    HostRealmLayoutInfo LayoutInfo { get; }
}
