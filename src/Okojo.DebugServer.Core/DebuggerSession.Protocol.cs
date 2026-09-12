using System.Globalization;
using System.Text.Json.Nodes;
using Okojo.JavaScript;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;
using Okojo.JavaScript.Values;

namespace Okojo.DebugServer;

// The stdin thread only queues requests. Every method in this partial runs on the
// execution thread, either at a periodic checkpoint or inside WaitForResume.
public sealed partial class DebuggerSession
{
    private readonly Dictionary<int, InspectionContainer> inspectionHandles = new();
    private readonly Dictionary<JsObject, int> objectHandles = new(
        ReferenceEqualityComparer.Instance
    );
    private readonly Dictionary<int, int> clientBreakpointIds = new();
    private readonly Dictionary<string, List<JsBreakpointHandle>> protocolBreakpoints = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal
    );
    private int nextInspectionHandle = 1;
    private bool stopOnCaughtException;
    private bool suppressBreakpointEvents;

    private sealed record InspectionContainer(
        IReadOnlyList<PausedLocalValue>? Locals = null,
        JsObject? Object = null
    );

    public void PublishOutput(string category, string text)
    {
        WriteJson(
            new JsonObject
            {
                ["event"] = "output",
                ["category"] = category,
                ["output"] = text,
            }
        );
    }

    private void BeginInspectionPause()
    {
        inspectionHandles.Clear();
        objectHandles.Clear();
        isPaused = true;
    }

    private void EndInspectionPause()
    {
        isPaused = false;
        inspectionHandles.Clear();
        objectHandles.Clear();
        lastSnapshot = null;
    }

    private void DispatchQueuedCommand()
    {
        if (!commandLines.TryDequeue(out var line))
            return;
        try
        {
            HandleCommand(line);
        }
        catch (Exception ex)
        {
            // A bad debugger request must not unwind the JavaScript program.
            PublishError(ex);
        }
    }

    private void DrainRunningCommands()
    {
        while (commands.TryTake(out var command))
        {
            if (command == DebuggerCommand.Dispatch)
                DispatchQueuedCommand();
            else if (command == DebuggerCommand.Quit)
            {
                stopRequested = true;
                agent.Terminate();
            }
            else
            {
                // A resume consumed while running also clears the sticky
                // pause window. Continue requests received while running are
                // deliberately not banked for the next stop. Otherwise a
                // double click can skip a breakpoint.
                resumeSignaled = false;
            }
        }
    }

    private PausedExecutionSnapshot RequirePaused()
    {
        if (!isPaused || lastSnapshot is not { } snapshot)
            throw new InvalidOperationException("Execution is not paused.");
        return snapshot;
    }

    private void HandleProtocolCommand(string line)
    {
        int requestId = 0;
        try
        {
            var request =
                JsonNode.Parse(line)?.AsObject()
                ?? throw new ArgumentException("Expected a JSON request object.");
            requestId = request["id"]?.GetValue<int>() ?? 0;
            var command = request["command"]?.GetValue<string>() ?? "";
            var args = request["arguments"]?.AsObject() ?? new JsonObject();
            JsonObject body;
            switch (command)
            {
                case "setBreakpoints":
                    body = ReplaceProtocolBreakpoints(args);
                    break;
                case "setExceptionBreakpoints":
                    var filters = args["filters"]?.AsArray() ?? new JsonArray();
                    if (filters.Any(filter => filter?.GetValue<string>() != "all"))
                        throw new ArgumentException(
                            "Only the 'all' exception filter is supported."
                        );
                    stopOnCaughtException = filters.Count != 0;
                    if (stopOnCaughtException)
                        agent.EnableCaughtExceptionHook();
                    else
                        agent.DisableCaughtExceptionHook();
                    body = new JsonObject { ["breakpoints"] = new JsonArray() };
                    break;
                case "pause":
                    pauseRequested = !isPaused || resumeSignaled;
                    body = new JsonObject();
                    break;
                case "resume":
                    RequirePaused();
                    var mode = args["mode"]?.GetValue<string>() ?? "continue";
                    if (mode is not ("continue" or "step" or "stepin" or "stepout"))
                        throw new ArgumentException("Invalid resume mode.");
                    var granularity = args["granularity"]?.GetValue<string>() ?? "line";
                    if (granularity is not ("line" or "instruction"))
                        throw new ArgumentException("Invalid stepping granularity.");
                    stepGranularity =
                        granularity == "instruction"
                            ? DebugStepGranularity.Instruction
                            : DebugStepGranularity.Line;
                    HandleCommand(mode);
                    body = new JsonObject();
                    break;
                case "scopes":
                    body = InspectScopes(args["frameId"]?.GetValue<int>() ?? 1);
                    break;
                case "variables":
                    body = InspectVariables(args);
                    break;
                case "evaluate":
                    var snapshot = RequirePaused();
                    var expression = args["expression"]?.GetValue<string>() ?? "";
                    var frameId = args["frameId"]?.GetValue<int>() ?? 1;
                    var value = EvaluatePausedExpression(snapshot, expression, frameId);
                    var variable = InspectValue(expression, value, expression);
                    body = new JsonObject
                    {
                        ["result"] = variable["value"]?.DeepClone(),
                        ["type"] = variable["type"]?.DeepClone(),
                        ["variablesReference"] = variable["variablesReference"]?.DeepClone(),
                        ["indexedVariables"] = variable["indexedVariables"]?.DeepClone(),
                    };
                    break;
                case "loadedSources":
                    var sources = new List<JsonNode?>();
                    foreach (
                        var source in agent
                            .ScriptDebugRegistry.GetAllRegisteredScripts()
                            .Select(script => script.SourcePath)
                            .Where(path => !string.IsNullOrEmpty(path))
                            .Distinct()
                    )
                    {
                        sources.Add(
                            new JsonObject
                            {
                                ["name"] = Path.GetFileName(source),
                                ["path"] = source,
                            }
                        );
                    }
                    body = new JsonObject { ["sources"] = new JsonArray(sources.ToArray()) };
                    break;
                case "bytecode":
                    RequirePaused();
                    PublishBytecodeDump();
                    body = new JsonObject();
                    break;
                default:
                    throw new ArgumentException($"Unsupported host command '{command}'.");
            }
            WriteJson(
                new JsonObject
                {
                    ["event"] = "response",
                    ["requestId"] = requestId,
                    ["success"] = true,
                    ["body"] = body,
                }
            );
        }
        catch (Exception ex)
        {
            WriteJson(
                new JsonObject
                {
                    ["event"] = "response",
                    ["requestId"] = requestId,
                    ["success"] = false,
                    ["message"] = ex.Message,
                }
            );
        }
    }

    private JsonObject ReplaceProtocolBreakpoints(JsonObject args)
    {
        string sourcePath = Path.GetFullPath(
            args["sourcePath"]?.GetValue<string>()
                ?? throw new ArgumentException("Missing breakpoint source path.")
        );
        var requested = args["breakpoints"]?.AsArray() ?? new JsonArray();
        // Validate the entire replacement before disposing any existing handles.
        var specs = requested
            .Select(item =>
                (Line: item?["line"]?.GetValue<int>() ?? 0, Id: item?["id"]?.GetValue<int>() ?? 0)
            )
            .ToArray();
        if (specs.Any(spec => spec.Line <= 0 || spec.Id <= 0))
            throw new ArgumentException("Breakpoint line and id must be positive integers.");
        if (protocolBreakpoints.Remove(sourcePath, out var previous))
            foreach (var handle in previous)
                ClearBreakpoint(handle.HandleId);

        var handles = new List<JsBreakpointHandle>();
        protocolBreakpoints[sourcePath] = handles;
        var breakpointNodes = new List<JsonNode?>();
        suppressBreakpointEvents = true;
        try
        {
            foreach (var spec in specs)
            {
                var handle = AddBreakpoint(sourcePath, spec.Line);
                clientBreakpointIds[handle.HandleId] = spec.Id;
                handles.Add(handle);
                breakpointNodes.Add(CreateBreakpointPayload("breakpoint-updated", handle));
            }
        }
        finally
        {
            suppressBreakpointEvents = false;
        }
        return new JsonObject { ["breakpoints"] = new JsonArray(breakpointNodes.ToArray()) };
    }

    private JsonObject InspectScopes(int frameId)
    {
        var snapshot = RequirePaused();
        if (frameId <= 0 || frameId > snapshot.StackFrames.Count)
            throw new ArgumentException("Invalid paused frame id.");
        // ScopeChain is the CALL STACK, not a lexical scope chain. Never expose
        // another frame's locals as captured variables of the selected frame.
        var locals =
            snapshot.ScopeChain is { } chain && frameId <= chain.Count
                ? chain[frameId - 1].LocalValues
            : frameId == 1 ? snapshot.LocalValues
            : null;
        var scopes = new JsonArray(
            new JsonObject
            {
                ["name"] = "Locals",
                ["presentationHint"] = "locals",
                ["variablesReference"] = AddInspectionHandle(
                    new InspectionContainer(Locals: locals ?? [])
                ),
                ["expensive"] = false,
            },
            new JsonObject
            {
                ["name"] = "Global",
                ["presentationHint"] = "globals",
                ["variablesReference"] = GetObjectHandle(agent.MainRealm.GlobalObject),
                ["expensive"] = true,
            }
        );
        return new JsonObject { ["scopes"] = scopes };
    }

    private int AddInspectionHandle(InspectionContainer container)
    {
        if (nextInspectionHandle == int.MaxValue || inspectionHandles.Count >= 100_000)
            throw new InvalidOperationException("Too many debugger object references.");
        int handle = nextInspectionHandle++;
        inspectionHandles.Add(handle, container);
        return handle;
    }

    private int GetObjectHandle(JsObject obj)
    {
        if (IsInspectionProxy(obj))
            return 0; // Proxy descriptors/ownKeys can execute arbitrary JavaScript.
        if (objectHandles.TryGetValue(obj, out var handle))
            return handle;
        handle = AddInspectionHandle(new InspectionContainer(Object: obj));
        objectHandles.Add(obj, handle);
        return handle;
    }

    private JsonObject InspectVariables(JsonObject args)
    {
        RequirePaused();
        int handle = args["variablesReference"]?.GetValue<int>() ?? 0;
        if (!inspectionHandles.TryGetValue(handle, out var container))
            throw new ArgumentException("Unknown or expired variables reference.");
        int start = args["start"]?.GetValue<int>() ?? 0;
        int count = args["count"]?.GetValue<int>() ?? 0;
        string? filter = args["filter"]?.GetValue<string>();
        if (start < 0 || count < 0 || filter is not (null or "named" or "indexed"))
            throw new ArgumentException("Invalid variables paging arguments.");
        // DAP count=0 means all. Paging is applied BEFORE reading values.
        long end = count == 0 ? long.MaxValue : (long)start + count;
        var variableNodes = new List<JsonNode?>();
        if (container.Locals is { } locals)
        {
            if (filter != "indexed")
                for (int index = start; index < locals.Count && index < end; index++)
                    variableNodes.Add(
                        InspectValue(locals[index].Name, locals[index].Value, locals[index].Name)
                    );
        }
        else if (container.Object is { } obj)
        {
            var keys = new List<(string Name, uint? Index, int Atom)>();
            if (filter != "named")
            {
                var indices = new List<uint>();
                obj.CollectOwnElementIndices(indices, false);
                indices.Sort();
                keys.AddRange(
                    indices.Select(index =>
                        (index.ToString(CultureInfo.InvariantCulture), (uint?)index, 0)
                    )
                );
            }
            if (filter != "indexed")
            {
                var atoms = new List<int>();
                obj.CollectOwnNamedPropertyAtoms(obj.Realm, atoms, false);
                keys.AddRange(
                    atoms.Select(atom => (obj.Realm.Atoms.AtomToString(atom), (uint?)null, atom))
                );
            }
            for (int index = start; index < keys.Count && index < end; index++)
            {
                var key = keys[index];
                PropertyDescriptor descriptor;
                bool found = key.Index is { } elementIndex
                    ? obj.TryGetOwnElementDescriptor(elementIndex, out descriptor)
                    : obj.TryGetOwnNamedPropertyDescriptorAtom(obj.Realm, key.Atom, out descriptor);
                if (!found)
                    continue;
                variableNodes.Add(
                    descriptor.IsAccessor
                        ? new JsonObject
                        {
                            ["name"] = key.Name,
                            ["value"] = "<accessor>",
                            ["variablesReference"] = 0,
                            ["presentationHint"] = new JsonObject
                            {
                                ["attributes"] = new JsonArray("readOnly"),
                            },
                        }
                        : InspectValue(key.Name, descriptor.Value)
                );
            }
        }
        return new JsonObject { ["variables"] = new JsonArray(variableNodes.ToArray()) };
    }

    private JsonObject InspectValue(string name, JsValue value, string? evaluateName = null)
    {
        var result = new JsonObject
        {
            ["name"] = name,
            ["value"] = FormatInspectionValue(value),
            ["type"] = InspectionType(value),
            ["evaluateName"] = evaluateName,
            ["variablesReference"] = value.IsObject ? GetObjectHandle(value.AsObject()) : 0,
        };
        if (value.IsObject && value.AsObject() is JsArray array && array.Length <= int.MaxValue)
            result["indexedVariables"] = (int)array.Length;
        return result;
    }

    private static string InspectionType(JsValue value) =>
        value.IsObject
            ? value.AsObject() is JsFunction
                ? "function"
                : "object"
            : value.IsString
                ? "string"
                : value.IsNumber
                    ? "number"
                    : value.IsBool
                        ? "boolean"
                        : value.IsNull
                            ? "null"
                            : value.IsBigInt
                                ? "bigint"
                                : value.IsSymbol
                                    ? "symbol"
                                    : "undefined";

    private static bool IsInspectionProxy(JsObject obj) => obj is JsProxyObject or JsProxyFunction;

    private static string FormatInspectionValue(JsValue value)
    {
        if (value.IsTheHole)
            return "<uninitialized>";
        if (value.IsObject)
        {
            var obj = value.AsObject();
            if (IsInspectionProxy(obj))
                return "<proxy>";
            return obj switch
            {
                JsArray array => $"Array({array.Length})",
                JsFunction function => $"Function({function.Name ?? "<anonymous>"})",
                _ => obj.GetType().Name,
            };
        }
        if (value.IsBigInt)
            return value.AsBigInt().Value.ToString(CultureInfo.InvariantCulture) + "n";
        if (value.IsSymbol)
            return value.AsSymbol().ToString();
        // Unlike object ToString/ToDisplayString this never traverses a graph or
        // invokes a getter. Preserve the CLI's familiar Number/String rendering.
        return JsValueDebugString.FormatValue(value);
    }

    private static bool TryReadInspectionProperty(JsObject obj, string name, out JsValue value)
    {
        var seen = new HashSet<JsObject>(ReferenceEqualityComparer.Instance);
        for (
            JsObject? cursor = obj;
            cursor is not null && seen.Add(cursor);
            cursor = cursor.Prototype
        )
        {
            if (IsInspectionProxy(cursor))
                throw new InvalidOperationException(
                    "Proxy evaluation is disabled to avoid side effects."
                );
            bool isIndex =
                uint.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out uint index)
                && index < uint.MaxValue
                && name == index.ToString(CultureInfo.InvariantCulture);
            PropertyDescriptor descriptor;
            bool found = isIndex
                ? cursor.TryGetOwnElementDescriptor(index, out descriptor)
                : cursor.TryGetOwnNamedPropertyDescriptorAtom(
                    cursor.Realm,
                    cursor.Realm.Atoms.InternNoCheck(name),
                    out descriptor
                );
            if (!found)
                continue;
            if (descriptor.IsAccessor)
                throw new InvalidOperationException(
                    "Getter evaluation is disabled to avoid side effects."
                );
            value = descriptor.Value;
            return true;
        }
        value = default;
        return false;
    }
}
