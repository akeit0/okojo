namespace Okojo.Runtime;

internal sealed class UnsupportedMessagePayload(string typeName)
{
    public string TypeName { get; } = typeName;
}
