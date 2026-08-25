using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Execution.Interop;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Execution;

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
        using var ast = JavaScriptParser.ParseScript(source);
        Execute(new JsScriptCompiler(this).CompileIndirectEval(ast, null), pumpJobsAfterRun);
        return Accumulator;
    }

    public JsValue Eval(string source, bool pumpJobsAfterRun = true)
    {
        return Evaluate(source, pumpJobsAfterRun);
    }

    public ValueTask<JsValue> EvaluateAsync(
        string source,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        using var ast = JavaScriptParser.ParseScript(source, "<eval>", allowTopLevelAwait: true);
        Execute(new JsScriptCompiler(this).Compile(ast, "<eval>"), pumpJobsAfterRun: false);
        var result = Accumulator;
        PumpJobs();
        return AwaitEvaluatedValueAsync(result, cancellationToken);
    }

    public JsValue Import(string specifier, string? referrer = null)
    {
        return Agent.Modules.Evaluate(this, specifier, referrer ?? CurrentModuleResolvedId);
    }

    public JsModuleLoadResult LoadModule(string specifier, string? referrer = null)
    {
        return Agent.LoadModuleResult(this, specifier, referrer ?? CurrentModuleResolvedId);
    }

    public string LoadWorkerScript(string path, string? referrer = null)
    {
        return Agent.LoadWorkerScript(path, referrer ?? CurrentModuleResolvedId);
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
        if (
            !function.TryGetObject(out var functionObj)
            || functionObj is not JsFunction okojoFunction
        )
            throw new JsRuntimeException(
                JsErrorKind.TypeError,
                "Call target is not a function",
                "CALL_TARGET_NOT_FUNCTION"
            );

        return InvokeFunction(okojoFunction, thisValue, args);
    }

    /// <summary>Performs ECMAScript ToNumber on a value.</summary>
    public double ToNumber(in JsValue value)
    {
        EnsureCompatibleValue(value, nameof(value));
        return this.ToNumberFastPath(value);
    }

    /// <summary>Performs ECMAScript ToIntegerOrInfinity on a value.</summary>
    public double ToIntegerOrInfinity(in JsValue value)
    {
        EnsureCompatibleValue(value, nameof(value));
        return this.ToIntegerOrInfinitySlowPath(value);
    }

    /// <summary>Performs ECMAScript ToUint32 on a value.</summary>
    public uint ToUint32(in JsValue value)
    {
        EnsureCompatibleValue(value, nameof(value));
        return this.ToUint32SlowPath(value);
    }

    /// <summary>Performs ECMAScript ToString on a value.</summary>
    public string ToJsString(in JsValue value)
    {
        EnsureCompatibleValue(value, nameof(value));
        return this.ToJsStringSlowPath(value);
    }

    /// <summary>Creates a Promise resolved with the supplied value.</summary>
    public JsValue CreateResolvedPromise(in JsValue value)
    {
        EnsureCompatibleValue(value, nameof(value));
        return this.PromiseResolveValue(value);
    }

    /// <summary>Creates a Promise rejected with the supplied reason.</summary>
    public JsValue CreateRejectedPromise(in JsValue reason)
    {
        EnsureCompatibleValue(reason, nameof(reason));
        return this.PromiseRejectByConstructor(Intrinsics.PromiseConstructor, reason);
    }

    /// <summary>Creates a native error constructor and its prototype pair.</summary>
    public JsHostFunction CreateErrorConstructor(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var prototype = new JsPlainObject(this, false);
        if (!prototype.TrySetPrototype(ErrorPrototype))
            throw new InvalidOperationException("Error prototype could not be assigned.");
        prototype.DefineDataProperty(
            "name",
            JsValue.FromString(name),
            JsShapePropertyFlags.Writable | JsShapePropertyFlags.Configurable
        );
        prototype.DefineDataProperty(
            "message",
            JsValue.FromString(string.Empty),
            JsShapePropertyFlags.Writable | JsShapePropertyFlags.Configurable
        );

        var constructor = Intrinsics.CreateNativeErrorConstructor(name, prototype);
        constructor.Prototype = ErrorConstructor;
        prototype.DefineDataProperty(
            "constructor",
            JsValue.FromObject(constructor),
            JsShapePropertyFlags.Writable | JsShapePropertyFlags.Configurable
        );
        constructor.InitializePrototypeProperty(prototype);
        return constructor;
    }

    /// <summary>Creates the JavaScript error value for a runtime exception.</summary>
    public JsValue CreateErrorValue(JsRuntimeException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception.ErrorRealm is not null && !ReferenceEquals(exception.ErrorRealm.Atoms, Atoms))
            throw new ArgumentException(
                "Exception belongs to a different JavaScript agent.",
                nameof(exception)
            );
        if (exception.ThrownValue is { } thrownValue)
            EnsureCompatibleValue(thrownValue, nameof(exception));
        return exception.ThrownValue ?? CreateErrorObjectFromException(exception);
    }

    /// <summary>Returns the resolved identifier of the currently evaluating module, if any.</summary>
    public string? CurrentModuleResolvedId =>
        Agent.TryGetCurrentModuleResolvedId(out var resolvedId) ? resolvedId : null;

    private void EnsureCompatibleValue(in JsValue value, string parameterName)
    {
        if (value.TryGetObject(out var obj))
        {
            if (!ReferenceEquals(obj.Realm.Atoms, Atoms))
                throw new ArgumentException(
                    "Value belongs to a different JavaScript agent.",
                    parameterName
                );
            return;
        }

        if (value.IsSymbol)
        {
            var symbol = value.AsSymbol();
            var isOwned =
                (
                    Atoms.TryGetSymbolByAtom(symbol.Atom, out var atomSymbol)
                    && ReferenceEquals(atomSymbol, symbol)
                )
                || (
                    Agent.TryGetRegisteredSymbolByAtom(symbol.Atom, out var registeredSymbol)
                    && ReferenceEquals(registeredSymbol, symbol)
                );
            if (!isOwned)
                throw new ArgumentException(
                    "Value belongs to a different JavaScript agent.",
                    parameterName
                );
        }
    }

    /// <summary>
    ///     Compiles a script through the engine's compiler pipeline.
    /// </summary>
    public JsScript CompileScript(string source, string? sourcePath = null)
    {
        using var ast = JavaScriptParser.ParseScript(source, sourcePath);
        return new JsScriptCompiler(this).Compile(ast, sourcePath);
    }

    private async ValueTask<JsValue> AwaitEvaluatedValueAsync(
        JsValue value,
        CancellationToken cancellationToken
    )
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
