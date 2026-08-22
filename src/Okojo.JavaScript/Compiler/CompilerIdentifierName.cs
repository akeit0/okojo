using Okojo.Parsing;

namespace Okojo.Compiler;

internal readonly record struct CompilerIdentifierName(string Name, int NameId = -1)
{
    public static CompilerIdentifierName From(JsIdentifierExpression identifier)
    {
        return new(identifier.Name, identifier.NameId);
    }

    public static CompilerIdentifierName From(BoundIdentifier identifier)
    {
        return new(identifier.Name, identifier.NameId);
    }
}
