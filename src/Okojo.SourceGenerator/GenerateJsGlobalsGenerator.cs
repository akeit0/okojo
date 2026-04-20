using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Okojo.SourceGenerator.GlobalGeneration;

namespace Okojo.SourceGenerator;

[Generator(LanguageNames.CSharp)]
public sealed class GenerateJsGlobalsGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var provider = context.SyntaxProvider.ForAttributeWithMetadataName(
            AttributeMetadataNames.GenerateJsGlobalsAttribute,
            static (node, _) => node is ClassDeclarationSyntax,
            static (ctx, _) => GlobalExportCollector.Collect((INamedTypeSymbol)ctx.TargetSymbol));

        context.RegisterSourceOutput(provider, static (spc, model) =>
        {
            if (model is null)
                return;
            var overloadSets = OverloadDispatchAnalysis.AnalyzeByName(
                model.Functions,
                static x => x.Name,
                static x => x.Symbol,
                static x => x.Parameters,
                static x => x.Type,
                static x => x.HasDefaultValue);
            var hasErrors = false;
            for (var i = 0; i < overloadSets.Count; i++)
                for (var j = 0; j < overloadSets[i].Diagnostics.Count; j++)
                {
                    hasErrors = true;
                    var diagnostic = overloadSets[i].Diagnostics[j];
                    spc.ReportDiagnostic(Diagnostic.Create(
                        SourceGeneratorDiagnostics.AmbiguousGeneratedOverload,
                        diagnostic.Location,
                        diagnostic.Message));
                }

            if (hasErrors)
                return;
            spc.AddSource(CSharpGlobalInstallerEmitter.GetHintName(model.Symbol),
                CSharpGlobalInstallerEmitter.Emit(model));
        });
    }
}
