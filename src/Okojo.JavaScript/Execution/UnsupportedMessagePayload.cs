namespace Okojo.JavaScript.Execution;

internal sealed class UnsupportedMessagePayload(string typeName)
{
    public string TypeName { get; } = typeName;
}
