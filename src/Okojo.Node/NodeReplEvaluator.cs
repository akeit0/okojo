using System.Diagnostics;
using Okojo.JavaScript;
using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;
using Okojo.JavaScript.Parsing;

namespace Okojo.Node;

public sealed class NodeReplEvaluator
{
    private const int StrictModeAuto = 0;
    private const int StrictModeStrict = 1;
    private const int StrictModeSloppy = 2;

    private readonly Func<bool> pumpHostTurn;
    private readonly HashSet<string> topLevelConstNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> topLevelLexicalNames = new(StringComparer.Ordinal);

    public NodeReplEvaluator(JsRealm realm, Func<bool> pumpHostTurn)
    {
        Realm = realm ?? throw new ArgumentNullException(nameof(realm));
        this.pumpHostTurn = pumpHostTurn ?? throw new ArgumentNullException(nameof(pumpHostTurn));
    }

    public JsRealm Realm { get; }

    public async Task<JsValue> EvaluateAsync(
        string source,
        int strictMode,
        string sourcePath,
        bool awaitPromiseResult = false,
        Action<JsScript>? onCompiled = null
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourcePath);

        var adjustedSource = ApplyStrictMode(source, strictMode);
        using var ast = FlatJavaScriptParser.ParseScript(
            adjustedSource,
            sourcePath,
            allowTopLevelAwait: true
        );
        ValidateTopLevelLexicalRedeclaration(ast);

        var script = new JsScriptCompiler(Realm).Compile(ast, sourcePath);
        onCompiled?.Invoke(script);

        JsValue rawResult;
        if (ast.HasTopLevelAwait)
        {
            Realm.Execute(script, pumpJobsAfterRun: false);
            rawResult = Realm.Accumulator;
            Realm.PumpJobs();
        }
        else
        {
            Realm.Execute(script);
            rawResult = Realm.Accumulator;
        }

        RegisterTopLevelLexicalDeclarations(ast);
        return awaitPromiseResult || ast.HasTopLevelAwait
            ? await AwaitIfPromiseAsync(rawResult).ConfigureAwait(false)
            : rawResult;
    }

    private static string ApplyStrictMode(string source, int strictMode)
    {
        return strictMode switch
        {
            StrictModeStrict => "'use strict';\n" + source,
            StrictModeSloppy => "void 0;\n" + source,
            _ => source,
        };
    }

    private async Task<JsValue> AwaitIfPromiseAsync(JsValue value, int timeoutMs = 30000)
    {
        if (!value.TryGetObject(out var obj) || obj is not JsPromiseObject promise)
            return value;

        var sw = Stopwatch.StartNew();
        while (promise.IsPending)
        {
            var moved = pumpHostTurn();
            Realm.PumpJobs();
            if (!promise.IsPending)
                break;
            if (sw.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException("Timed out waiting for Promise settlement.");
            if (!moved)
                await Task.Delay(1).ConfigureAwait(false);
        }

        if (promise.IsRejected)
            throw new InvalidOperationException(
                $"UnhandledPromiseRejection: {promise.SettledResult}"
            );

        return promise.SettledResult;
    }

    private void ValidateTopLevelLexicalRedeclaration(FlatAst ast)
    {
        foreach (var name in CollectTopLevelLexicalNames(ast))
            if (topLevelLexicalNames.Contains(name))
                throw new InvalidOperationException(
                    $"SyntaxError: Identifier '{name}' has already been declared"
                );
    }

    private void RegisterTopLevelLexicalDeclarations(FlatAst ast)
    {
        foreach (var name in CollectTopLevelLexicalNames(ast))
        {
            topLevelLexicalNames.Add(name);
            topLevelConstNames.Add(name);
        }
    }

    private static List<string> CollectTopLevelLexicalNames(FlatAst ast)
    {
        var names = new List<string>();
        ref readonly var root = ref ast[ast.Root];
        var statements = ast.ChildRange(root.Arg0, root.Arg1);
        for (var i = 0; i < statements.Length; i++)
        {
            ref readonly var statement = ref ast[statements[i]];
            if (statement.Kind != AstKind.VariableDeclaration)
                continue;
            if (
                (JsVariableDeclarationKind)statement.Arg2
                is not (JsVariableDeclarationKind.Let or JsVariableDeclarationKind.Const)
            )
                continue;

            var declarators = ast.ChildRange(statement.Arg0, statement.Arg1);
            for (var j = 0; j < declarators.Length; j++)
            {
                ref readonly var declarator = ref ast[declarators[j]];
                if (declarator.Kind == AstKind.VariableDeclaratorPattern)
                    continue;
                names.Add(ast.GetString(declarator.Arg0));
            }
        }
        return names;
    }
}
