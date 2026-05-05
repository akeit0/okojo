using Okojo.Bytecode;
using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime.Interop;

namespace Okojo.Runtime;

public sealed partial class JsRealm
{
    public void Execute(string source, bool pumpJobsAfterRun = true)
    {
        ArgumentNullException.ThrowIfNull(source);
        Execute(CompileScript(source), pumpJobsAfterRun);
    }

    public JsValue Evaluate(string source, bool pumpJobsAfterRun = true)
    {
        ArgumentNullException.ThrowIfNull(source);
        Execute(CompileScript(source), pumpJobsAfterRun);
        return Accumulator;
    }

    public JsValue Eval(string source, bool pumpJobsAfterRun = true)
    {
        return Evaluate(source, pumpJobsAfterRun);
    }

    public ValueTask<JsValue> EvaluateAsync(string source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var replTopLevelLexicalNames = new HashSet<string>(StringComparer.Ordinal);
        var replTopLevelConstNames = new HashSet<string>(StringComparer.Ordinal);
        var compileContext = new JsCompilerContext
        {
            IsRepl = true,
            ReplTopLevelLexicalNames = replTopLevelLexicalNames,
            ReplTopLevelConstNames = replTopLevelConstNames
        };
        var program = JavaScriptParser.ParseScript(source, false, false, true, "<eval>");
        var script = program.HasTopLevelAwait
            ? JsCompiler.Compile(this, program, compileContext, JsBytecodeFunctionKind.Async)
            : JsCompiler.Compile(this, program, compileContext);

        if (!program.HasTopLevelAwait)
        {
            Execute(script);
            return AwaitEvaluatedValueAsync(Accumulator, cancellationToken);
        }

        var root = new JsBytecodeFunction(
            this,
            script,
            "root",
            isStrict: script.StrictDeclared,
            kind: JsBytecodeFunctionKind.Async);
        var rawResult = Call(root, JsValue.FromObject(GlobalObject));
        return AwaitEvaluatedValueAsync(rawResult, cancellationToken);
    }

    public JsValue Import(string specifier, string? referrer = null)
    {
        return Agent.Modules.Evaluate(this, specifier, referrer ?? GetCurrentModuleResolvedIdOrNull());
    }

    public JsModuleLoadResult LoadModule(string specifier, string? referrer = null)
    {
        return Agent.LoadModuleResult(this, specifier, referrer ?? GetCurrentModuleResolvedIdOrNull());
    }

    public string LoadWorkerScript(string path, string? referrer = null)
    {
        return Engine.LoadWorkerScript(path,
            referrer ?? GetCurrentWorkerScriptResolvedIdOrNull() ?? GetCurrentModuleResolvedIdOrNull());
    }

    internal JsValue ExecuteWorkerScript(string source, string resolvedId)
    {
        var previous = currentWorkerScriptResolvedId;
        currentWorkerScriptResolvedId = resolvedId;
        try
        {
            return Evaluate(source);
        }
        finally
        {
            currentWorkerScriptResolvedId = previous;
        }
    }

    public JsRealm CreateRealm(Action<JsRealmOptions>? configure = null)
    {
        var options = new JsRealmOptions();
        configure?.Invoke(options);
        return Agent.CreateRealm(options);
    }

    public JsValue Call(JsFunction function, JsValue thisValue, params ReadOnlySpan<JsValue> args)
    {
        return InvokeFunction(function, thisValue, args);
    }

    public JsValue Call(JsValue function, JsValue thisValue, params ReadOnlySpan<JsValue> args)
    {
        if (!function.TryGetObject(out var functionObj) || functionObj is not JsFunction okojoFunction)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Call target is not a function",
                "CALL_TARGET_NOT_FUNCTION");

        return InvokeFunction(okojoFunction, thisValue, args);
    }

    internal string? GetCurrentModuleResolvedIdOrNull()
    {
        return Agent.TryGetCurrentModuleResolvedId(out var resolvedId) ? resolvedId : null;
    }

    private JsScript CompileScript(string source)
    {
        var program = JavaScriptParser.ParseScript(source);
        return JsCompiler.Compile(this, program);
    }

    private async ValueTask<JsValue> AwaitEvaluatedValueAsync(
        JsValue value,
        CancellationToken cancellationToken)
    {
        if (!value.TryGetObject(out var obj) || obj is not JsPromiseObject promise)
            return value;

        while (promise.IsPending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PumpJobs();
            if (!promise.IsPending)
                break;
            await Task.Yield();
        }

        if (promise.IsRejected)
            throw new PromiseRejectedException(promise.SettledResult);

        return promise.SettledResult;
    }
}
