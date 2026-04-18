using System.Runtime.CompilerServices;
using System.Text;
using Okojo.Diagnostics;
using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Node;

internal sealed class NodeConsoleBuiltIn(NodeRuntime runtime, NodeTtyBuiltIn ttyBuiltIn)
{
    private static readonly string[] ConsoleMethods =
    [
        "assert",
        "clear",
        "count",
        "countReset",
        "debug",
        "dir",
        "dirxml",
        "error",
        "group",
        "groupCollapsed",
        "groupEnd",
        "info",
        "log",
        "table",
        "time",
        "timeEnd",
        "timeLog",
        "trace",
        "warn"
    ];

    private readonly ConditionalWeakTable<JsObject, ConsoleInstanceState> states = [];
    private JsHostFunction? consoleConstructor;
    private JsPlainObject? consolePrototype;

    private JsPlainObject? globalConsoleObject;

    public JsPlainObject GetGlobalConsoleObject()
    {
        if (globalConsoleObject is not null)
            return globalConsoleObject;

        var realm = runtime.MainRealm;
        var console = CreateConsoleInstance(
            realm,
            ttyBuiltIn.GetStdoutObject(),
            ttyBuiltIn.GetStderrObject(),
            true);
        console.DefineDataProperty("Console", JsValue.FromObject(GetConsoleConstructor()), JsShapePropertyFlags.Open);
        globalConsoleObject = console;
        return console;
    }

    public JsPlainObject GetModule()
    {
        return GetGlobalConsoleObject();
    }

    private JsHostFunction GetConsoleConstructor()
    {
        if (consoleConstructor is not null)
            return consoleConstructor;

        var realm = runtime.MainRealm;
        var ctor = new JsHostFunction(realm, "Console", 1, static (in info) =>
        {
            var state = (ConstructorState)((JsHostFunction)info.Function).UserData!;
            var owner = state.Owner;
            var stdout = owner.GetRequiredStream(info, 0, "stdout");
            var stderr = owner.GetOptionalStream(info, 1) ?? stdout;
            var ignoreErrors = info.GetArgumentOrDefault(2, JsValue.True).ToBoolean();

            if (info.IsConstruct && info.ThisValue.TryGetObject(out var receiver))
            {
                owner.InitializeConsoleReceiver(receiver, stdout, stderr, ignoreErrors, true);
                return info.ThisValue;
            }

            return JsValue.FromObject(owner.CreateConsoleInstance(info.Realm, stdout, stderr, ignoreErrors));
        }, true)
        {
            UserData = new ConstructorState(this)
        };
        ctor.DefineDataProperty("prototype", JsValue.FromObject(GetConsolePrototype()), JsShapePropertyFlags.Open);
        consoleConstructor = ctor;
        return ctor;
    }

    private JsPlainObject GetConsolePrototype()
    {
        if (consolePrototype is not null)
            return consolePrototype;

        consolePrototype = new(runtime.MainRealm);
        return consolePrototype;
    }

    private JsPlainObject CreateConsoleInstance(JsRealm realm, JsObject stdout, JsObject stderr, bool ignoreErrors)
    {
        var console = new JsPlainObject(realm, useDictionaryMode: true);
        console.Prototype = GetConsolePrototype();
        InitializeConsoleReceiver(console, stdout, stderr, ignoreErrors, true);
        return console;
    }

    private void InitializeConsoleReceiver(
        JsObject receiver,
        JsObject stdout,
        JsObject stderr,
        bool ignoreErrors,
        bool preservePrototype)
    {
        if (!preservePrototype)
            receiver.Prototype = GetConsolePrototype();

        var state = new ConsoleInstanceState(stdout, stderr, ignoreErrors);
        states.Remove(receiver);
        states.Add(receiver, state);

        foreach (var methodName in ConsoleMethods)
            receiver.DefineDataProperty(methodName,
                JsValue.FromObject(CreateMethodFunction(receiver.Realm, methodName, state)), JsShapePropertyFlags.Open);
    }

    private JsHostFunction CreateMethodFunction(JsRealm realm, string name, ConsoleInstanceState state)
    {
        return new(realm, name, 0, static (in info) =>
        {
            var methodState = (ConsoleMethodState)((JsHostFunction)info.Function).UserData!;
            return methodState.Owner.InvokeMethod(info, methodState.Instance, methodState.Name);
        }, false)
        {
            UserData = new ConsoleMethodState(this, state, name)
        };
    }

    private JsValue InvokeMethod(in CallInfo info, ConsoleInstanceState state, string name)
    {
        return name switch
        {
            "assert" => InvokeAssert(info, state),
            "count" => InvokeCount(info, state),
            "countReset" => InvokeCountReset(info, state),
            "time" => InvokeTime(info, state),
            "timeEnd" => InvokeTimeEnd(info, state),
            "timeLog" => InvokeTimeLog(info, state),
            "error" or "warn" => WriteLine(info.Realm, state.Stderr, state,
                FormatArguments(info.Realm, info.Arguments)),
            "clear" => JsValue.Undefined,
            "groupEnd" => JsValue.Undefined,
            _ => WriteLine(info.Realm, state.Stdout, state, FormatArguments(info.Realm, info.Arguments))
        };
    }

    private JsValue InvokeAssert(in CallInfo info, ConsoleInstanceState state)
    {
        if (info.ArgumentCount > 0 && info.GetArgument(0).ToBoolean())
            return JsValue.Undefined;

        var message = info.ArgumentCount <= 1
            ? "Assertion failed"
            : "Assertion failed: " + FormatArguments(info.Realm, info.Arguments[1..]);
        return WriteLine(info.Realm, state.Stderr, state, message);
    }

    private JsValue InvokeCount(in CallInfo info, ConsoleInstanceState state)
    {
        var label = GetLabel(info, "default");
        var nextValue = state.Counters.TryGetValue(label, out var current) ? current + 1 : 1;
        state.Counters[label] = nextValue;
        return WriteLine(info.Realm, state.Stdout, state, $"{label}: {nextValue}");
    }

    private JsValue InvokeCountReset(in CallInfo info, ConsoleInstanceState state)
    {
        state.Counters.Remove(GetLabel(info, "default"));
        return JsValue.Undefined;
    }

    private JsValue InvokeTime(in CallInfo info, ConsoleInstanceState state)
    {
        state.Timers[GetLabel(info, "default")] = DateTimeOffset.UtcNow;
        return JsValue.Undefined;
    }

    private JsValue InvokeTimeEnd(in CallInfo info, ConsoleInstanceState state)
    {
        var label = GetLabel(info, "default");
        if (!state.Timers.Remove(label, out var startedAt))
            return JsValue.Undefined;

        var elapsedMs = (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
        return WriteLine(info.Realm, state.Stdout, state, $"{label}: {elapsedMs:0.###}ms");
    }

    private JsValue InvokeTimeLog(in CallInfo info, ConsoleInstanceState state)
    {
        var label = GetLabel(info, "default");
        if (!state.Timers.TryGetValue(label, out var startedAt))
            return JsValue.Undefined;

        var elapsedMs = (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
        var suffix = info.ArgumentCount <= 1 ? string.Empty : " " + FormatArguments(info.Realm, info.Arguments[1..]);
        return WriteLine(info.Realm, state.Stdout, state, $"{label}: {elapsedMs:0.###}ms{suffix}");
    }

    private JsValue WriteLine(JsRealm realm, JsObject stream, ConsoleInstanceState state, string text)
    {
        try
        {
            if (!stream.TryGetProperty("write", out var writeValue))
                throw new JsRuntimeException(JsErrorKind.TypeError, "Console stream must provide a write method.");

            _ = realm.Call(writeValue, JsValue.FromObject(stream), JsValue.FromString(text + "\n"));
            return JsValue.Undefined;
        }
        catch when (state.IgnoreErrors)
        {
            return JsValue.Undefined;
        }
    }

    private JsObject GetRequiredStream(in CallInfo info, int argumentIndex, string parameterName)
    {
        if (!info.GetArgument(argumentIndex).TryGetObject(out var stream))
            throw new JsRuntimeException(JsErrorKind.TypeError,
                $"Console constructor requires a {parameterName} stream object.");

        return stream;
    }

    private JsObject? GetOptionalStream(in CallInfo info, int argumentIndex)
    {
        return info.GetArgument(argumentIndex).TryGetObject(out var stream) ? stream : null;
    }

    private string GetLabel(in CallInfo info, string defaultLabel)
    {
        if (info.ArgumentCount == 0 || info.GetArgument(0).IsUndefined)
            return defaultLabel;

        return ToConsoleString(info.Realm, info.GetArgument(0));
    }

    private static string FormatArguments(JsRealm realm, ReadOnlySpan<JsValue> values)
    {
        if (values.Length == 0)
            return string.Empty;

        var builder = new StringBuilder();
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0)
                builder.Append(' ');
            builder.Append(ToConsoleString(realm, values[i]));
        }

        return builder.ToString();
    }

    private static string ToConsoleString(JsRealm realm, in JsValue value)
    {
        return value.IsString ? value.AsString() : new ReplFormatter(realm).Format(value);
    }

    private sealed class ConsoleInstanceState(JsObject stdout, JsObject stderr, bool ignoreErrors)
    {
        public JsObject Stdout { get; } = stdout;
        public JsObject Stderr { get; } = stderr;
        public bool IgnoreErrors { get; } = ignoreErrors;
        public Dictionary<string, int> Counters { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, DateTimeOffset> Timers { get; } = new(StringComparer.Ordinal);
    }

    private sealed class ConsoleMethodState(NodeConsoleBuiltIn owner, ConsoleInstanceState instance, string name)
    {
        public NodeConsoleBuiltIn Owner { get; } = owner;
        public ConsoleInstanceState Instance { get; } = instance;
        public string Name { get; } = name;
    }

    private sealed class ConstructorState(NodeConsoleBuiltIn owner)
    {
        public NodeConsoleBuiltIn Owner { get; } = owner;
    }
}
