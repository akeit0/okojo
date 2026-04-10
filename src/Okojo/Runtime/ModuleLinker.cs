using Okojo.Parsing;

namespace Okojo.Runtime;

internal sealed class ModuleLinker(Func<IModuleSourceLoader> loaderProvider)
{
    private readonly Func<IModuleSourceLoader> loaderProvider = loaderProvider;

    public static ModuleDiagnostic CreateDiagnostic(string code, string resolvedId, JsProgram program,
        int position, string message)
    {
        var line = 0;
        var column = 0;
        if (program.SourceText is not null)
            (line, column) = SourceLocation.GetLineColumn(program.SourceText, position);

        return new(code, message, resolvedId, position, line, column);
    }

    public static JsRuntimeException ToRuntimeException(ModuleDiagnostic diagnostic)
    {
        var withLocation = diagnostic.Line > 0 && diagnostic.Column > 0
            ? $"{diagnostic.Message} ({diagnostic.ResolvedId}:{diagnostic.Line}:{diagnostic.Column})"
            : diagnostic.Message;
        return new(JsErrorKind.SyntaxError, withLocation, diagnostic.Code);
    }

    public ModuleLinkPlan BuildPlan(string moduleResolvedId, JsProgram moduleProgram)
    {
        return BuildPlanResult(moduleResolvedId, moduleProgram).Plan;
    }

    public ModuleLinkResult BuildPlanResult(string moduleResolvedId, JsProgram moduleProgram)
    {
        var (executionPlan, requestedSpecifiers, imports, exportFromBindings, exportNamespaceFromBindings,
                exportStarFromSpecifiers) =
            BuildExecutionPlan(moduleProgram);

        var loader = loaderProvider();

        var requestedDependencyResolvedIds = new List<string>(requestedSpecifiers.Count);
        var requestedDepsSeen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < requestedSpecifiers.Count; i++)
        {
            var depResolvedId = loader.ResolveSpecifier(requestedSpecifiers[i], moduleResolvedId);
            if (requestedDepsSeen.Add(depResolvedId))
                requestedDependencyResolvedIds.Add(depResolvedId);
        }

        var resolvedImports = new List<JsResolvedImportBinding>();
        var importDependencyResolvedIds = new List<string>();
        var importDepsSeen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < imports.Count; i++)
        {
            var importDecl = imports[i];
            var depResolvedId = loader.ResolveSpecifier(importDecl.Specifier, moduleResolvedId);
            if (importDepsSeen.Add(depResolvedId))
                importDependencyResolvedIds.Add(depResolvedId);
            for (var b = 0; b < importDecl.Bindings.Count; b++)
            {
                var binding = importDecl.Bindings[b];
                resolvedImports.Add(new(
                    binding.LocalName,
                    binding.Kind,
                    depResolvedId,
                    binding.ImportedName,
                    binding.Position));
            }
        }

        var resolvedExportFromBindings = new List<ExportFromBindingResolved>(exportFromBindings.Count);
        for (var i = 0; i < exportFromBindings.Count; i++)
        {
            var binding = exportFromBindings[i];
            resolvedExportFromBindings.Add(new(
                loader.ResolveSpecifier(binding.SourceSpecifier, moduleResolvedId),
                binding.ImportedName,
                binding.ExportedName,
                binding.Position));
        }

        var resolvedExportNamespaceFromBindings =
            new List<ExportNamespaceFromBindingResolved>(exportNamespaceFromBindings.Count);
        for (var i = 0; i < exportNamespaceFromBindings.Count; i++)
        {
            var binding = exportNamespaceFromBindings[i];
            resolvedExportNamespaceFromBindings.Add(new(
                loader.ResolveSpecifier(binding.SourceSpecifier, moduleResolvedId),
                binding.ExportedName));
        }

        var exportStars = new List<string>(exportStarFromSpecifiers.Count);
        for (var i = 0; i < exportStarFromSpecifiers.Count; i++)
            exportStars.Add(loader.ResolveSpecifier(exportStarFromSpecifiers[i], moduleResolvedId));

        var plan = new ModuleLinkPlan(
            executionPlan,
            requestedDependencyResolvedIds,
            importDependencyResolvedIds,
            resolvedImports,
            resolvedExportFromBindings,
            resolvedExportNamespaceFromBindings,
            exportStars);
        return new(plan, Array.Empty<ModuleDiagnostic>());
    }

    private static (
        ModuleExecutionPlan ExecutionPlan,
        List<string> RequestedSpecifiers,
        List<ImportDeclaration> Imports,
        List<ExportFromBinding> ExportFromBindings,
        List<ExportNamespaceFromBinding> ExportNamespaceFromBindings,
        List<string> ExportStarFromSpecifiers)
        BuildExecutionPlan(JsProgram moduleProgram)
    {
        var requestedSpecifiers = new List<string>();
        var imports = new List<ImportDeclaration>();
        var exportLocalByName = new Dictionary<string, string>(StringComparer.Ordinal);
        var preinitializedLocalExportNames = new HashSet<string>(StringComparer.Ordinal);
        var exportFromBindings = new List<ExportFromBinding>();
        var exportNamespaceFromBindings = new List<ExportNamespaceFromBinding>();
        var exportStarFromSpecifiers = new List<string>();
        var operations = new List<ModuleExecutionOp>(moduleProgram.Statements.Count);

        for (var i = 0; i < moduleProgram.Statements.Count; i++)
        {
            var statement = moduleProgram.Statements[i];
            switch (statement)
            {
                case JsImportDeclaration importDecl:
                    imports.Add(BuildImportDeclaration(importDecl));
                    requestedSpecifiers.Add(importDecl.Source);
                    break;
                case JsExportDeclarationStatement exportDeclaration:
                    CollectDeclarationExports(exportDeclaration.Declaration, exportLocalByName);
                    CollectPreinitializedLocalExportNames(exportDeclaration.Declaration,
                        preinitializedLocalExportNames);
                    operations.Add(new(
                        ModuleExecutionOpKind.ExecuteStatement,
                        exportDeclaration.Declaration,
                        null,
                        null,
                        false));
                    break;
                case JsExportDefaultDeclaration exportDefault:
                    if (exportDefault.Expression is JsFunctionExpression fnExpr &&
                        !string.IsNullOrEmpty(fnExpr.Name))
                    {
                        var fnName = fnExpr.Name!;
                        var decl = new JsFunctionDeclaration(
                            fnName,
                            fnExpr.Parameters,
                            fnExpr.Body,
                            fnExpr.IsGenerator,
                            fnExpr.IsAsync,
                            fnExpr.ParameterInitializers,
                            fnExpr.ParameterPatterns,
                            fnExpr.ParameterPositions,
                            fnExpr.ParameterBindingKinds,
                            fnExpr.FunctionLength,
                            fnExpr.HasSimpleParameterList,
                            fnExpr.HasDuplicateParameters);
                        operations.Add(new(
                            ModuleExecutionOpKind.ExecuteStatement,
                            decl,
                            null,
                            null,
                            false));
                        exportLocalByName["default"] = fnName;
                        break;
                    }

                    if (exportDefault.Expression is JsClassExpression classExpr &&
                        !string.IsNullOrEmpty(classExpr.Name))
                    {
                        var className = classExpr.Name!;
                        var decl = new JsClassDeclaration(className, classExpr);
                        operations.Add(new(
                            ModuleExecutionOpKind.ExecuteStatement,
                            decl,
                            null,
                            null,
                            false));
                        exportLocalByName["default"] = className;
                        break;
                    }

                    if (exportDefault.IsDeclaration &&
                        exportDefault.Expression is JsFunctionExpression anonymousFnExpr &&
                        string.IsNullOrEmpty(anonymousFnExpr.Name))
                    {
                        operations.Add(new(
                            ModuleExecutionOpKind.InitializeHoistedDefaultExport,
                            null,
                            anonymousFnExpr,
                            ModuleRuntimeNames.DefaultExportLocal,
                            true));
                        exportLocalByName["default"] = ModuleRuntimeNames.DefaultExportLocal;
                        break;
                    }

                    operations.Add(new(
                        ModuleExecutionOpKind.ExportDefaultExpression,
                        null,
                        exportDefault.Expression,
                        ModuleRuntimeNames.DefaultExportLocal,
                        ShouldSetDefaultExportName(exportDefault.Expression)));
                    exportLocalByName["default"] = ModuleRuntimeNames.DefaultExportLocal;
                    break;
                case JsExportNamedDeclaration named:
                    if (named.Source is null)
                    {
                        for (var s = 0; s < named.Specifiers.Count; s++)
                        {
                            var spec = named.Specifiers[s];
                            exportLocalByName[spec.ExportedName] = spec.LocalName;
                        }
                    }
                    else
                    {
                        requestedSpecifiers.Add(named.Source);
                        for (var s = 0; s < named.Specifiers.Count; s++)
                        {
                            var spec = named.Specifiers[s];
                            exportFromBindings.Add(new(named.Source, spec.LocalName,
                                spec.ExportedName, spec.Position));
                        }
                    }

                    break;
                case JsExportAllDeclaration exportAll:
                    requestedSpecifiers.Add(exportAll.Source);
                    if (string.IsNullOrEmpty(exportAll.ExportedName))
                        exportStarFromSpecifiers.Add(exportAll.Source);
                    else
                        exportNamespaceFromBindings.Add(
                            new(exportAll.Source, exportAll.ExportedName!));
                    break;
                default:
                    CollectPreinitializedLocalExportNames(statement, preinitializedLocalExportNames);
                    operations.Add(new(
                        ModuleExecutionOpKind.ExecuteStatement,
                        statement,
                        null,
                        null,
                        false));
                    break;
            }
        }

        return (
            new(
                operations,
                exportLocalByName,
                preinitializedLocalExportNames,
                moduleProgram.HasTopLevelAwait),
            requestedSpecifiers,
            imports,
            exportFromBindings,
            exportNamespaceFromBindings,
            exportStarFromSpecifiers);
    }

    private static void CollectPreinitializedLocalExportNames(
        JsStatement statement,
        HashSet<string> preinitializedLocalExportNames)
    {
        switch (statement)
        {
            case JsVariableDeclarationStatement { Kind: JsVariableDeclarationKind.Var } variable:
                for (var i = 0; i < variable.Declarators.Count; i++)
                    preinitializedLocalExportNames.Add(variable.Declarators[i].Name);
                break;
        }
    }

    private static ImportDeclaration BuildImportDeclaration(JsImportDeclaration import)
    {
        var bindings = new List<ImportBinding>();
        if (!string.IsNullOrEmpty(import.DefaultBinding))
            bindings.Add(new(ModuleImportBindingKind.Named, import.DefaultBinding!, "default",
                import.Position));
        if (!string.IsNullOrEmpty(import.NamespaceBinding))
            bindings.Add(new(ModuleImportBindingKind.Namespace, import.NamespaceBinding!,
                string.Empty, import.Position));
        for (var i = 0; i < import.NamedBindings.Count; i++)
        {
            var binding = import.NamedBindings[i];
            bindings.Add(new(ModuleImportBindingKind.Named, binding.LocalName, binding.ImportedName,
                binding.Position));
        }

        return new(import.Source, bindings);
    }

    private static void CollectDeclarationExports(JsStatement declaration, Dictionary<string, string> exportLocalByName)
    {
        var names = ExtractExportedDeclarationBindingNames(declaration);
        if (names.Count == 0)
            throw new NotImplementedException(
                $"Unsupported export declaration syntax in Okojo module mode: {declaration.GetType().Name}.");

        for (var i = 0; i < names.Count; i++)
            exportLocalByName[names[i]] = names[i];
    }

    private static List<string> ExtractExportedDeclarationBindingNames(JsStatement declaration)
    {
        switch (declaration)
        {
            case JsVariableDeclarationStatement variable:
            {
                var names = new List<string>(variable.Declarators.Count);
                for (var i = 0; i < variable.Declarators.Count; i++)
                    names.Add(variable.Declarators[i].Name);
                return names;
            }
            case JsFunctionDeclaration fn:
                return [fn.Name];
            case JsClassDeclaration cls:
                return [cls.Name];
            default:
                return [];
        }
    }

    private static bool ShouldSetDefaultExportName(JsExpression expression)
    {
        return expression switch
        {
            JsFunctionExpression { Name: null } => true,
            JsClassExpression { Name: null, Elements: var elements } => ShouldInferAnonymousClassName(elements),
            _ => false
        };
    }

    private static bool ShouldInferAnonymousClassName(IReadOnlyList<JsClassElement> elements)
    {
        for (var i = 0; i < elements.Count; i++)
        {
            var element = elements[i];
            if (!element.IsStatic || element.IsComputedKey)
                continue;
            if (string.Equals(element.Key, "name", StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private sealed class ImportDeclaration(string specifier, IReadOnlyList<ImportBinding> bindings)
    {
        public string Specifier { get; } = specifier;
        public IReadOnlyList<ImportBinding> Bindings { get; } = bindings;
    }

    private sealed class ImportBinding(
        ModuleImportBindingKind kind,
        string localName,
        string importedName,
        int position)
    {
        public ModuleImportBindingKind Kind { get; } = kind;
        public string LocalName { get; } = localName;
        public string ImportedName { get; } = importedName;
        public int Position { get; } = position;
    }

    private sealed class ExportFromBinding(
        string sourceSpecifier,
        string importedName,
        string exportedName,
        int position)
    {
        public string SourceSpecifier { get; } = sourceSpecifier;
        public string ImportedName { get; } = importedName;
        public string ExportedName { get; } = exportedName;
        public int Position { get; } = position;
    }

    private sealed class ExportNamespaceFromBinding(string sourceSpecifier, string exportedName)
    {
        public string SourceSpecifier { get; } = sourceSpecifier;
        public string ExportedName { get; } = exportedName;
    }
}
