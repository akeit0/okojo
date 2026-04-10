using Microsoft.CodeAnalysis;

namespace Okojo.SourceGenerator;

internal static class SourceGeneratorDiagnostics
{
    internal static readonly DiagnosticDescriptor AmbiguousGeneratedOverload = new(
        "OKOJOGEN001",
        "Ambiguous generated overload",
        "{0}",
        "Okojo.SourceGenerator",
        DiagnosticSeverity.Error,
        true);
}
