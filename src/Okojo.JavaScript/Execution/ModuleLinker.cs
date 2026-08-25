using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Execution;

internal sealed class ModuleLinker(Func<IModuleSourceLoader> loaderProvider)
{
    private readonly Func<IModuleSourceLoader> loaderProvider = loaderProvider;

    public static ModuleDiagnostic CreateDiagnostic(
        string code,
        string resolvedId,
        string sourceText,
        int position,
        string message
    )
    {
        var line = 0;
        var column = 0;
        if (sourceText.Length != 0)
            (line, column) = SourceLocation.GetLineColumn(sourceText, position);

        return new(code, message, resolvedId, position, line, column);
    }

    public static JsRuntimeException ToRuntimeException(ModuleDiagnostic diagnostic)
    {
        var withLocation =
            diagnostic.Line > 0 && diagnostic.Column > 0
                ? $"{diagnostic.Message} ({diagnostic.ResolvedId}:{diagnostic.Line}:{diagnostic.Column})"
                : diagnostic.Message;
        return new(JsErrorKind.SyntaxError, withLocation, diagnostic.Code);
    }

    public ModuleLinkResult BuildPlanResult(string moduleResolvedId, JsAst moduleProgram)
    {
        var loader = loaderProvider();
        var resolvedRequests = new ResolvedModuleDependency[moduleProgram.ModuleRequests.Length];
        var requestedDependencies = new List<ResolvedModuleDependency>(resolvedRequests.Length);
        var requestedDepsSeen = new HashSet<string>(
            resolvedRequests.Length,
            StringComparer.Ordinal
        );
        for (var i = 0; i < resolvedRequests.Length; i++)
        {
            ref readonly var request = ref moduleProgram.ModuleRequests[i];
            var importType = GetImportType(moduleProgram, request);
            var resolved = new ResolvedModuleDependency(
                loader.ResolveSpecifier(
                    moduleProgram.GetString(request.SpecifierStringIndex),
                    moduleResolvedId
                ),
                importType
            );
            resolvedRequests[i] = resolved;
            if (requestedDepsSeen.Add(GetRequestedDependencyKey(resolved.ResolvedId, importType)))
                requestedDependencies.Add(resolved);
        }

        var resolvedImports = new List<JsResolvedImportBinding>(moduleProgram.ImportEntries.Length);
        var importDependencyResolvedIds = new List<string>(resolvedRequests.Length);
        var importDepsSeen = new HashSet<string>(resolvedRequests.Length, StringComparer.Ordinal);
        foreach (ref readonly var import in moduleProgram.ImportEntries)
        {
            var request = resolvedRequests[import.ModuleRequestIndex];
            if (importDepsSeen.Add(request.ResolvedId))
                importDependencyResolvedIds.Add(request.ResolvedId);
            resolvedImports.Add(
                new(
                    moduleProgram.GetString(import.LocalNameStringIndex),
                    import.Kind == JsImportKind.Namespace
                        ? ModuleImportBindingKind.Namespace
                        : ModuleImportBindingKind.Named,
                    request.ResolvedId,
                    import.Kind == JsImportKind.Namespace
                        ? string.Empty
                        : moduleProgram.GetString(import.ImportedNameStringIndex),
                    import.Position,
                    request.ImportType
                )
            );
        }

        var exportLocalByName = new Dictionary<string, string>(
            moduleProgram.ExportEntries.Length,
            StringComparer.Ordinal
        );
        var resolvedExportFromBindings = new List<ExportFromBindingResolved>(
            moduleProgram.ExportEntries.Length
        );
        var resolvedExportNamespaceFromBindings = new List<ExportNamespaceFromBindingResolved>(
            moduleProgram.ExportEntries.Length
        );
        var exportStars = new List<string>(moduleProgram.ExportEntries.Length);
        foreach (ref readonly var export in moduleProgram.ExportEntries)
        {
            switch (export.Kind)
            {
                case JsExportKind.Local:
                case JsExportKind.DefaultExpression:
                case JsExportKind.DefaultDeclaration:
                    exportLocalByName[moduleProgram.GetString(export.ExportNameStringIndex)] =
                        moduleProgram.GetString(export.LocalNameStringIndex);
                    break;
                case JsExportKind.Indirect:
                {
                    var request = resolvedRequests[export.ModuleRequestIndex];
                    resolvedExportFromBindings.Add(
                        new(
                            request.ResolvedId,
                            moduleProgram.GetString(export.ImportNameStringIndex),
                            moduleProgram.GetString(export.ExportNameStringIndex),
                            export.Position,
                            request.ImportType
                        )
                    );
                    break;
                }
                case JsExportKind.Namespace:
                {
                    var request = resolvedRequests[export.ModuleRequestIndex];
                    resolvedExportNamespaceFromBindings.Add(
                        new(
                            request.ResolvedId,
                            moduleProgram.GetString(export.ExportNameStringIndex),
                            request.ImportType
                        )
                    );
                    break;
                }
                case JsExportKind.Star:
                    exportStars.Add(resolvedRequests[export.ModuleRequestIndex].ResolvedId);
                    break;
            }
        }

        var preinitializedLocalExportNames = new HashSet<string>(StringComparer.Ordinal);
        if (moduleProgram.ModuleVarBindings is not null)
            foreach (var localName in exportLocalByName.Values)
                if (moduleProgram.ModuleVarBindings.Contains(localName))
                    preinitializedLocalExportNames.Add(localName);

        var executionPlan = new ModuleExecutionPlan(
            exportLocalByName,
            preinitializedLocalExportNames,
            CollectDefaultNameEligibleExportLocals(moduleProgram),
            moduleProgram.HasTopLevelAwait,
            moduleProgram.HasTopLevelUsingLike,
            moduleProgram.HasTopLevelAwaitUsingLike
        );
        return new(
            new(
                executionPlan,
                requestedDependencies,
                importDependencyResolvedIds,
                resolvedImports,
                resolvedExportFromBindings,
                resolvedExportNamespaceFromBindings,
                exportStars
            ),
            Array.Empty<ModuleDiagnostic>()
        );
    }

    private static HashSet<string> CollectDefaultNameEligibleExportLocals(JsAst ast)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var statements = ast.ChildRange(ast[ast.Root].Arg0, ast[ast.Root].Arg1);
        for (var i = 0; i < statements.Length; i++)
        {
            ref readonly var statement = ref ast[statements[i]];
            if (statement.Kind != AstKind.ExportDeclaration || statement.Arg0 < 0)
                continue;

            var exports = ast.GetExportEntries(statement);
            ref readonly var value = ref ast[statement.Arg0];
            var anonymous = value.Kind
                is AstKind.FunctionExpression
                    or AstKind.ArrowFunctionExpression
                ? ast.GetString(ast.GetFunction(value.Arg0).NameStringIndex).Length == 0
                : value.Kind == AstKind.ClassExpression
                    && ast.GetString(ast.GetClass(value.Arg0).NameStringIndex).Length == 0
                    && ShouldInferAnonymousClassName(ast, ast.GetClass(value.Arg0));
            if (!anonymous)
                continue;

            for (var j = 0; j < exports.Length; j++)
                if (
                    exports[j].Kind
                    is JsExportKind.DefaultExpression
                        or JsExportKind.DefaultDeclaration
                )
                    result.Add(ast.GetString(exports[j].LocalNameStringIndex));
        }

        return result;
    }

    private static bool ShouldInferAnonymousClassName(JsAst ast, in JsClassInfo info)
    {
        var elements = ast.GetClassElements(info);
        for (var i = 0; i < elements.Length; i++)
            if (
                elements[i].IsStatic
                && !elements[i].IsComputed
                && string.Equals(ast.GetString(elements[i].Key), "name", StringComparison.Ordinal)
            )
                return false;

        return true;
    }

    private static string? GetImportType(JsAst ast, in JsModuleRequest request)
    {
        var attributes = ast.GetImportAttributes(request);
        for (var i = 0; i < attributes.Length; i++)
            if (
                string.Equals(
                    ast.GetString(attributes[i].KeyStringIndex),
                    "type",
                    StringComparison.Ordinal
                )
            )
                return ast.GetString(attributes[i].ValueStringIndex);
        return null;
    }

    private static string GetRequestedDependencyKey(string resolvedId, string? importType)
    {
        return string.IsNullOrEmpty(importType) ? resolvedId : resolvedId + "\u0000" + importType;
    }
}
