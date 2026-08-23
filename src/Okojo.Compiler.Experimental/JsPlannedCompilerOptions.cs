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
            if (HasUnsupportedHoistedExport(ast))
                throw new NotSupportedException(
                    "The planned module compiler does not own hoisted export instantiation yet."
                );
            return new JsPlannedModuleCompiler(realm).CompileForExecution(ast);
        };
        return options;
    }

    private static bool HasUnsupportedHoistedExport(FlatAst ast)
    {
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
                statement.Kind == AstKind.ExportDeclaration
                && value.Kind == AstKind.FunctionExpression
            )
                return true;
        }
        return false;
    }
}
