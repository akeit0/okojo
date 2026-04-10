using System.Diagnostics;
using Okojo.Bytecode;
using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Node;

public sealed class NodeReplEvaluator
{
    private const int StrictModeAuto = 0;
    private const int StrictModeStrict = 1;
    private const int StrictModeSloppy = 2;
    private readonly JsCompilerContext compileContext;

    private readonly Func<bool> pumpHostTurn;
    private readonly HashSet<string> topLevelConstNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> topLevelLexicalNames = new(StringComparer.Ordinal);

    public NodeReplEvaluator(JsRealm realm, Func<bool> pumpHostTurn)
    {
        Realm = realm ?? throw new ArgumentNullException(nameof(realm));
        this.pumpHostTurn = pumpHostTurn ?? throw new ArgumentNullException(nameof(pumpHostTurn));
        compileContext = new()
        {
            IsRepl = true,
            ReplTopLevelLexicalNames = topLevelLexicalNames,
            ReplTopLevelConstNames = topLevelConstNames
        };
    }

    public JsRealm Realm { get; }

    public async Task<JsValue> EvaluateAsync(
        string source,
        int strictMode,
        string sourcePath,
        bool awaitPromiseResult = false,
        Action<JsScript>? onCompiled = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourcePath);

        var adjustedSource = ApplyStrictMode(source, strictMode);
        var program = JavaScriptParser.ParseScript(adjustedSource, false, false, true, sourcePath);
        ValidateTopLevelLexicalRedeclaration(program);

        var script = program.HasTopLevelAwait
            ? JsCompiler.Compile(Realm, program, compileContext, JsBytecodeFunctionKind.Async)
            : JsCompiler.Compile(Realm, program, compileContext);
        onCompiled?.Invoke(script);

        JsValue rawResult;
        if (program.HasTopLevelAwait)
        {
            var root = new JsBytecodeFunction(
                Realm,
                script,
                "root",
                isStrict: script.StrictDeclared,
                kind: JsBytecodeFunctionKind.Async);
            rawResult = Realm.Call(root, JsValue.FromObject(Realm.GlobalObject));
        }
        else
        {
            Realm.Execute(script);
            rawResult = Realm.Accumulator;
        }

        RegisterTopLevelLexicalDeclarations(program);
        return awaitPromiseResult || program.HasTopLevelAwait
            ? await AwaitIfPromiseAsync(rawResult).ConfigureAwait(false)
            : rawResult;
    }

    private static string ApplyStrictMode(string source, int strictMode)
    {
        return strictMode switch
        {
            StrictModeStrict => "'use strict';\n" + source,
            StrictModeSloppy => "void 0;\n" + source,
            _ => source
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
            throw new InvalidOperationException($"UnhandledPromiseRejection: {promise.SettledResult}");

        return promise.SettledResult;
    }

    private void ValidateTopLevelLexicalRedeclaration(JsProgram program)
    {
        foreach (var name in EnumerateTopLevelLexicalNames(program))
            if (topLevelLexicalNames.Contains(name))
                throw new InvalidOperationException($"SyntaxError: Identifier '{name}' has already been declared");
    }

    private void RegisterTopLevelLexicalDeclarations(JsProgram program)
    {
        foreach (var stmt in program.Statements)
        {
            if (stmt is not JsVariableDeclarationStatement decl)
                continue;
            if (decl.Kind is not (JsVariableDeclarationKind.Let or JsVariableDeclarationKind.Const))
                continue;

            foreach (var declarator in decl.Declarators)
            {
                topLevelLexicalNames.Add(declarator.Name);
                if (decl.Kind == JsVariableDeclarationKind.Const)
                    topLevelConstNames.Add(declarator.Name);
            }
        }
    }

    private static IEnumerable<string> EnumerateTopLevelLexicalNames(JsProgram program)
    {
        foreach (var stmt in program.Statements)
        {
            if (stmt is not JsVariableDeclarationStatement decl)
                continue;
            if (decl.Kind is not (JsVariableDeclarationKind.Let or JsVariableDeclarationKind.Const))
                continue;

            foreach (var declarator in decl.Declarators)
                yield return declarator.Name;
        }
    }
}
