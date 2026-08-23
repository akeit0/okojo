using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler.Experimental;

internal static class JsPlannedCompilerOptions
{
    internal static JsAgentOptions UsePlannedModuleCompiler(this JsAgentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ModuleExecutionCompiler = static (realm, ast, plan) =>
        {
            if (
                plan.RequiresTopLevelAwait
                || plan.HasTopLevelUsingLike
                || plan.HasTopLevelAwaitUsingLike
            )
                throw new NotSupportedException(
                    "The planned module compiler does not support top-level await or resource management yet."
                );
            if (HasHoistedExport(ast))
                throw new NotSupportedException(
                    "The planned module compiler does not own hoisted export instantiation yet."
                );
            return new JsPlannedModuleCompiler(realm).Compile(ast);
        };
        return options;
    }

    private static bool HasHoistedExport(FlatAst ast)
    {
        var exportedLocals = new HashSet<string>(StringComparer.Ordinal);
        foreach (ref readonly var export in ast.ExportEntries)
            if (export.LocalNameStringIndex >= 0)
                exportedLocals.Add(ast.GetString(export.LocalNameStringIndex));

        ref readonly var root = ref ast[ast.Root];
        var statements = ast.ChildRange(root.Arg0, root.Arg1);
        for (var i = 0; i < statements.Length; i++)
        {
            ref readonly var statement = ref ast[statements[i]];
            var valueIndex =
                statement.Kind == AstKind.ExportDeclaration ? statement.Arg0 : statements[i];
            if (valueIndex < 0)
                continue;
            ref readonly var value = ref ast[valueIndex];
            if (
                value.Kind == AstKind.FunctionDeclaration
                && exportedLocals.Contains(
                    ast.GetString(ast.GetFunction(value.Arg0).NameStringIndex)
                )
            )
                return true;
            if (
                statement.Kind == AstKind.ExportDeclaration
                && value.Kind == AstKind.FunctionExpression
            )
                return true;
        }
        return false;
    }
}
