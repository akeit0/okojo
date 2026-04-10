using System.Runtime.CompilerServices;
using Okojo.Diagnostics;
using Okojo.Hosting;
using Okojo.Objects;
using Okojo.Repl;
using Okojo.Runtime;

namespace Okojo.Node;

internal sealed class NodeReplBuiltIn(NodeRuntime runtime)
{
    private readonly ConditionalWeakTable<JsObject, ReplServerState> states = [];
    private JsPlainObject? moduleObject;

    public JsPlainObject GetModule()
    {
        if (moduleObject is not null)
            return moduleObject;

        var realm = runtime.MainRealm;
        var module = new JsPlainObject(realm, useDictionaryMode: true);
        module.DefineDataProperty("start", JsValue.FromObject(CreateStartFunction(realm)), JsShapePropertyFlags.Open);
        moduleObject = module;
        return module;
    }

    private JsHostFunction CreateStartFunction(JsRealm realm)
    {
        return new(realm, "start", 0, (in info) =>
        {
            var prompt = GetPrompt(info);
            var server = new JsPlainObject(info.Realm, useDictionaryMode: true);
            server.DefineDataProperty("context", JsValue.FromObject(info.Realm.GlobalObject),
                JsShapePropertyFlags.Open);
            server.DefineDataProperty("close", JsValue.FromObject(CreateCloseFunction(info.Realm, server)),
                JsShapePropertyFlags.Open);

            var state = new ReplServerState(info.Realm, prompt);
            states.Remove(server);
            states.Add(server, state);

            info.Realm.QueueHostTask(CreateStartDriver(info.Realm, server));
            return JsValue.FromObject(server);
        }, false);
    }

    private JsHostFunction CreateCloseFunction(JsRealm realm, JsObject server)
    {
        return new(realm, "close", 0, (in info) =>
        {
            if (states.TryGetValue(server, out var state))
                state.StopRequested = true;
            return JsValue.Undefined;
        }, false);
    }

    private JsHostFunction CreateStartDriver(JsRealm realm, JsObject server)
    {
        return new(realm, "replStart", 0, (in info) =>
        {
            if (!states.TryGetValue(server, out var state) || state.StopRequested)
                return JsValue.Undefined;

            RunServer(state);
            return JsValue.Undefined;
        }, false);
    }

    private void RunServer(ReplServerState state)
    {
        var pumpTurn = CreatePumpTurn(state.Realm);
        var evaluator = new NodeReplEvaluator(state.Realm, pumpTurn);
        SystemReplLoop.RunAsync(new()
        {
            History = ReplHistoryStore.CreateEphemeral(),
            IsInputComplete = input => ReplInputParser.IsInputComplete(input, true),
            PumpTurn = pumpTurn,
            PrimaryPrompt = state.Prompt,
            ContinuationPrompt = "... ",
            HandleInputAsync = line => HandleLineAsync(state, evaluator, line)
        }).GetAwaiter().GetResult();
    }

    private static async Task<bool> HandleLineAsync(
        ReplServerState state,
        NodeReplEvaluator evaluator,
        string line)
    {
        if (state.StopRequested)
            return false;
        if (string.IsNullOrWhiteSpace(line))
            return true;

        var trimmed = line.Trim();
        switch (trimmed)
        {
            case ".exit":
                return false;
            case ".help":
                WriteHelp();
                return true;
        }

        try
        {
            var sourcePath = $"REPL{Environment.TickCount64}";
            var result = await evaluator.EvaluateAsync(line, 0, sourcePath).ConfigureAwait(false);
            Console.WriteLine(new ReplFormatter(evaluator.Realm, 2).Format(result));
        }
        catch (JsRuntimeException runtimeException)
        {
            Console.Error.WriteLine(runtimeException.ThrownValue is { } thrown
                ? $"Uncaught {new ReplFormatter(evaluator.Realm, 2).Format(thrown)}"
                : $"Uncaught {runtimeException.Kind}: {runtimeException.Message}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}");
        }

        return !state.StopRequested;
    }

    private Func<bool> CreatePumpTurn(JsRealm realm)
    {
        if (runtime.Runtime.Options.LowLevelHost.HostTaskScheduler is IHostTaskQueuePump hostLoop)
        {
            var hostPump = runtime.Runtime.CreateHostPump();
            return () => HostTurnRunner.RunTurn(hostLoop, hostPump);
        }

        return () =>
        {
            realm.PumpJobs();
            Thread.Sleep(10);
            return false;
        };
    }

    private static string GetPrompt(in CallInfo info)
    {
        if (info.Arguments.Length == 0 || info.GetArgument(0).IsUndefined)
            return "> ";

        var first = info.GetArgument(0);
        if (first.IsString)
            return first.AsString();

        if (first.TryGetObject(out var options) &&
            options.TryGetProperty("prompt", out var promptValue) &&
            promptValue.IsString)
            return promptValue.AsString();

        return "> ";
    }

    private static void WriteHelp()
    {
        Console.WriteLine(
            """
            Commands:
              .help                    Show REPL help
              .exit                    Exit the REPL
            """);
    }

    private sealed class ReplServerState(JsRealm realm, string prompt)
    {
        public JsRealm Realm { get; } = realm;
        public string Prompt { get; } = prompt;
        public bool StopRequested { get; set; }
    }
}
