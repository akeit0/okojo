namespace Okojo.Runtime.Interop;

internal interface IClrTypeFunctionData : IClrTypeReference
{
    string DisplayTag { get; }
    HostRealmLayoutInfo LayoutInfo { get; }
}
