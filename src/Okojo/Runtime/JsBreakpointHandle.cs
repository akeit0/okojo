using Okojo.Bytecode;

namespace Okojo.Runtime;

public sealed class JsBreakpointHandle : IDisposable
{
    private readonly JsBreakpointRegistry registry;

    internal JsBreakpointHandle(JsBreakpointRegistry registry, int handleId, string? sourcePath, int line,
        int programCounter = -1)
    {
        this.registry = registry;
        HandleId = handleId;
        SourcePath = sourcePath;
        Line = line;
        ProgramCounter = programCounter;
        ResolvedSourcePath = sourcePath;
        if (programCounter >= 0)
        {
            IsVerified = true;
            ResolvedLine = line;
            ResolvedProgramCounter = programCounter;
        }
    }

    public int HandleId { get; }
    public string? SourcePath { get; }
    public int Line { get; }
    public int ProgramCounter { get; }
    public bool IsDisposed { get; private set; }

    public bool IsVerified { get; private set; }

    public string? ResolvedSourcePath { get; private set; }

    public int ResolvedLine { get; private set; }

    public int ResolvedColumn { get; private set; }

    public int ResolvedProgramCounter { get; private set; } = -1;

    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        registry.ClearBreakpoint(this);
    }

    internal bool TryUpdateResolution(string? sourcePath, int line, int column, int programCounter)
    {
        var shouldReplace = !IsVerified || (ResolvedLine != Line && line == Line);
        if (!shouldReplace)
            return false;

        IsVerified = true;
        ResolvedSourcePath = sourcePath;
        ResolvedLine = line;
        ResolvedColumn = column;
        ResolvedProgramCounter = programCounter;
        return true;
    }
}

internal sealed class JsBreakpointRegistry
{
    private static readonly bool STraceBreakpoints =
        Environment.GetEnvironmentVariable("OKOJO_DEBUG_TRACE_BREAKPOINTS") == "1";

    private readonly HashSet<JsScript> dirtyScripts = new(ReferenceEqualityComparer.Instance);

    private readonly object gate = new();
    private readonly Dictionary<int, JsBreakpointHandle> handlesById = new();
    private readonly Dictionary<int, HashSet<string>> patchedSourcePathsByHandle = new();
    private readonly Dictionary<int, List<BreakpointPatch>> patchesByHandle = new();

    private readonly Dictionary<JsScript, List<BreakpointPatch>> patchesByScript =
        new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<int, PendingBreakpointRequest> pendingByHandle = new();
    private readonly Dictionary<string, HashSet<int>> pendingHandlesBySourcePath = new(SourcePathComparer.Instance);
    private int nextHandleId = 1;
    private event Action<JsBreakpointHandle>? BreakpointResolved;

    internal JsBreakpointHandle AddBreakpoint(JsAgent agent, string sourcePath, int line)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(sourcePath);
        if (sourcePath.Length == 0)
            throw new ArgumentException("Source path must not be empty.", nameof(sourcePath));
        if (line <= 0)
            throw new ArgumentOutOfRangeException(nameof(line));

        int handleId;
        JsBreakpointHandle handle;
        List<JsBreakpointHandle>? resolvedHandles = null;
        lock (gate)
        {
            handleId = nextHandleId++;
            handle = new(this, handleId, sourcePath, line);
            handlesById[handleId] = handle;
            var request = new PendingBreakpointRequest(sourcePath, line);
            pendingByHandle[handleId] = request;
            if (!pendingHandlesBySourcePath.TryGetValue(sourcePath, out var handles))
            {
                handles = new();
                pendingHandlesBySourcePath[sourcePath] = handles;
            }

            handles.Add(handleId);
            patchesByHandle[handleId] = [];
            patchedSourcePathsByHandle[handleId] = new(SourcePathComparer.Instance);
            if (STraceBreakpoints)
                Console.Error.WriteLine($"[okojo] breakpoint request {sourcePath}:{line} handle {handleId}");
            ArmRegisteredScripts(agent, sourcePath, handleId, line, ref resolvedHandles);
        }

        NotifyResolvedHandles(resolvedHandles);
        return handle;
    }

    internal JsBreakpointHandle AddBreakpoint(JsAgent agent, JsScript script, int pc)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(agent);
        if ((uint)pc >= (uint)script.Bytecode.Length)
            throw new ArgumentOutOfRangeException(nameof(pc));

        if (!TryCreateExactPatch(script, pc, out var patch))
            throw new InvalidOperationException("No breakpoint location found for the requested pc.");

        int handleId;
        JsBreakpointHandle handle;
        lock (gate)
        {
            handleId = nextHandleId++;
            handle = new(this, handleId, script.SourcePath, TryGetLineForPc(script, pc), pc);
            handle.TryUpdateResolution(script.SourcePath, patch.Line, patch.Column, patch.Pc);
            handlesById[handleId] = handle;
            var patches = new List<BreakpointPatch>(1) { patch };
            patchesByHandle[handleId] = patches;
            patchedSourcePathsByHandle[handleId] = CreatePatchedSourcePathSet(patches);
            AddPatchesByScript(patches);
            patch.Arm();
        }

        NotifyResolvedHandles([handle]);
        return handle;
    }

    internal void ArmPendingBreakpoints(JsAgent agent, JsScript script)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(script);
        List<JsBreakpointHandle>? resolvedHandles = null;
        lock (gate)
        {
            if (patchesByScript.TryGetValue(script, out var existingPatches))
                ArmPatches(existingPatches);

            var sourcePath = script.SourcePath;
            if (sourcePath is null || !pendingHandlesBySourcePath.TryGetValue(sourcePath, out var pendingHandles))
                return;

            foreach (var handleId in pendingHandles)
            {
                if (!pendingByHandle.TryGetValue(handleId, out var request))
                    continue;

                var hasExactLineMatch = agent.GetRegisteredScripts(sourcePath).Any(registeredScript =>
                    JsScriptDebugInfo.HasExactSourceLine(registeredScript, request.Line));
                var allowRelocation = !hasExactLineMatch || JsScriptDebugInfo.HasExactSourceLine(script, request.Line);
                ArmScriptForRequest(script, handleId, request.Line, allowRelocation, ref resolvedHandles);
            }
        }

        NotifyResolvedHandles(resolvedHandles);
    }

    internal bool TryRestoreBreakpointForHit(JsScript script, int pc, out string? sourcePath, out int line,
        out int column)
    {
        lock (gate)
        {
            if (!patchesByScript.TryGetValue(script, out var patchList))
            {
                sourcePath = null;
                line = 0;
                column = 0;
                return false;
            }

            for (var i = 0; i < patchList.Count; i++)
            {
                var patch = patchList[i];
                if (patch.Pc != pc || !patch.IsArmed)
                    continue;

                patch.Restore();
                dirtyScripts.Add(script);
                sourcePath = patch.SourcePath;
                line = patch.Line;
                column = patch.Column;
                if (STraceBreakpoints)
                    Console.Error.WriteLine(
                        $"[okojo] breakpoint hit {sourcePath ?? "<anonymous>"}:{line}:{column} pc {pc}");
                return true;
            }
        }

        sourcePath = null;
        line = 0;
        column = 0;
        return false;
    }

    internal bool TryGetOriginalInstruction(JsScript script, int pc, out OriginalInstructionInfo instruction)
    {
        lock (gate)
        {
            if (patchesByScript.TryGetValue(script, out var patchList))
                for (var i = 0; i < patchList.Count; i++)
                {
                    var patch = patchList[i];
                    if (patch.Pc != pc)
                        continue;

                    var bytes = patch.GetOriginalBytesCopy();
                    var opCode = (JsOpCode)bytes[0];
                    var operands = bytes.Length > 1 ? bytes[1..] : Array.Empty<byte>();
                    instruction = new(opCode, operands);
                    return true;
                }
        }

        instruction = default;
        return false;
    }

    internal void ClearBreakpoint(JsBreakpointHandle handle)
    {
        lock (gate)
        {
            if (!patchesByHandle.Remove(handle.HandleId, out var patches))
                return;
            patchedSourcePathsByHandle.Remove(handle.HandleId);
            handlesById.Remove(handle.HandleId);

            pendingByHandle.Remove(handle.HandleId);
            if (handle.SourcePath is { Length: > 0 } sourcePath &&
                pendingHandlesBySourcePath.TryGetValue(sourcePath, out var pendingHandles))
            {
                pendingHandles.Remove(handle.HandleId);
                if (pendingHandles.Count == 0)
                    pendingHandlesBySourcePath.Remove(sourcePath);
            }

            for (var i = 0; i < patches.Count; i++)
            {
                var patch = patches[i];
                if (patch.IsArmed)
                    patch.Restore();
            }

            RemovePatchesByScript(patches);
        }
    }

    private static void ArmPatches(IEnumerable<BreakpointPatch> patches)
    {
        foreach (var patch in patches)
            patch.Arm();
    }

    private void ArmRegisteredScripts(JsAgent agent, string sourcePath, int handleId, int line,
        ref List<JsBreakpointHandle>? resolvedHandles)
    {
        var registeredScripts = agent.GetRegisteredScripts(sourcePath);
        var hasExactLineMatch = registeredScripts.Any(script => JsScriptDebugInfo.HasExactSourceLine(script, line));
        foreach (var registeredScript in registeredScripts)
        {
            var allowRelocation = !hasExactLineMatch || JsScriptDebugInfo.HasExactSourceLine(registeredScript, line);
            ArmScriptForRequest(registeredScript, handleId, line, allowRelocation, ref resolvedHandles);
        }
    }

    private void ArmScriptForRequest(JsScript script, int handleId, int line, bool allowRelocation,
        ref List<JsBreakpointHandle>? resolvedHandles)
    {
        if (!patchesByHandle.TryGetValue(handleId, out var patches))
        {
            patches = [];
            patchesByHandle[handleId] = patches;
        }

        if (!patchedSourcePathsByHandle.TryGetValue(handleId, out var patchedSourcePaths))
        {
            patchedSourcePaths = new(SourcePathComparer.Instance);
            patchedSourcePathsByHandle[handleId] = patchedSourcePaths;
        }

        for (var i = 0; i < patches.Count; i++)
        {
            var patch = patches[i];
            if (!ReferenceEquals(patch.Script, script) || patch.Line != line)
                continue;

            if (!patch.IsArmed)
                patch.Arm();

            return;
        }

        if (!TryCreateLinePatch(script, line, allowRelocation, out var newPatch))
        {
            if (STraceBreakpoints && !HasPatchedSourcePath(patchedSourcePaths, script.SourcePath))
            {
                Console.Error.WriteLine(
                    $"[okojo] breakpoint miss {script.SourcePath ?? "<anonymous>"}:{line} no-pc");
                Console.Error.WriteLine(
                    $"[okojo] breakpoint lines {script.SourcePath ?? "<anonymous>"} => [{string.Join(", ", GetExecutableLines(script))}]");
            }

            return;
        }

        patches.Add(newPatch);
        if (newPatch.SourcePath is { Length: > 0 } newPatchSourcePath)
            patchedSourcePaths.Add(newPatchSourcePath);
        AddPatchesByScript([newPatch]);
        newPatch.Arm();
        if (handlesById.TryGetValue(handleId, out var handle) &&
            handle.TryUpdateResolution(newPatch.SourcePath, newPatch.Line, newPatch.Column, newPatch.Pc))
        {
            resolvedHandles ??= new(2);
            resolvedHandles.Add(handle);
        }
    }

    private void NotifyResolvedHandles(List<JsBreakpointHandle>? handles)
    {
        if (handles is null || handles.Count == 0)
            return;

        for (var i = 0; i < handles.Count; i++)
            BreakpointResolved?.Invoke(handles[i]);
    }

    internal void SubscribeBreakpointResolved(Action<JsBreakpointHandle> handler)
    {
        BreakpointResolved += handler;
    }

    internal void UnsubscribeBreakpointResolved(Action<JsBreakpointHandle> handler)
    {
        BreakpointResolved -= handler;
    }

    private static bool TryCreateLinePatch(JsScript script, int line, bool allowRelocation, out BreakpointPatch patch)
    {
        if (allowRelocation
                ? JsScriptDebugInfo.TryFindFirstPcForSourceLine(script, line, out var pc, out var column,
                    out var actualLine)
                : JsScriptDebugInfo.TryFindFirstPcForExactSourceLine(script, line, out pc, out column, out actualLine))
        {
            var length = BytecodeInfo.GetInstructionLength(script.Bytecode, pc);
            patch = new(script, pc, length, script.SourcePath, actualLine, column);
            if (STraceBreakpoints)
            {
                var suffix = actualLine == line ? string.Empty : $" relocated-from {line}";
                Console.Error.WriteLine(
                    $"[okojo] breakpoint map {script.SourcePath ?? "<anonymous>"}:{actualLine}:{column} -> pc {pc} len {length}{suffix}");
            }

            return true;
        }

        patch = null!;
        return false;
    }

    private static bool TryCreateExactPatch(JsScript script, int pc, out BreakpointPatch patch)
    {
        if ((uint)pc >= (uint)script.Bytecode.Length)
        {
            patch = null!;
            return false;
        }

        var line = 0;
        var column = 0;
        JsScriptDebugInfo.TryGetSourceLocation(script, pc, out line, out column);
        var length = BytecodeInfo.GetInstructionLength(script.Bytecode, pc);
        patch = new(script, pc, length, script.SourcePath, line, column);
        return true;
    }

    private static int TryGetLineForPc(JsScript script, int pc)
    {
        JsScriptDebugInfo.TryGetSourceLocation(script, pc, out var line, out _);
        return line;
    }

    private static IEnumerable<int> GetExecutableLines(JsScript script)
    {
        var seen = new HashSet<int>();
        for (var pc = 0; pc < script.Bytecode.Length; pc++)
        {
            if (!JsScriptDebugInfo.TryGetSourceLocation(script, pc, out var line, out _) || line <= 0)
                continue;
            if (seen.Add(line))
                yield return line;
        }
    }

    private static bool HasPatchedSourcePath(HashSet<string> patchedSourcePaths, string? sourcePath)
    {
        if (sourcePath is null)
            return false;

        return patchedSourcePaths.Contains(sourcePath);
    }

    private static HashSet<string> CreatePatchedSourcePathSet(IEnumerable<BreakpointPatch> patches)
    {
        var sourcePaths = new HashSet<string>(SourcePathComparer.Instance);
        foreach (var patch in patches)
            if (patch.SourcePath is { Length: > 0 } sourcePath)
                sourcePaths.Add(sourcePath);

        return sourcePaths;
    }

    private void AddPatchesByScript(IEnumerable<BreakpointPatch> patches)
    {
        foreach (var patch in patches)
        {
            if (!patchesByScript.TryGetValue(patch.Script, out var scriptPatches))
            {
                scriptPatches = new(2);
                patchesByScript[patch.Script] = scriptPatches;
            }

            scriptPatches.Add(patch);
            dirtyScripts.Add(patch.Script);
        }
    }

    private void RemovePatchesByScript(IEnumerable<BreakpointPatch> patches)
    {
        foreach (var patch in patches)
        {
            if (!patchesByScript.TryGetValue(patch.Script, out var scriptPatches))
                continue;

            scriptPatches.Remove(patch);
            if (scriptPatches.Count == 0)
            {
                patchesByScript.Remove(patch.Script);
                dirtyScripts.Remove(patch.Script);
            }
        }
    }

    internal readonly record struct OriginalInstructionInfo(JsOpCode OpCode, byte[] Operands);

    private sealed class BreakpointPatch
    {
        private readonly byte[] originalBytes;

        internal BreakpointPatch(JsScript script, int pc, int length, string? sourcePath, int line,
            int column)
        {
            Script = script;
            Pc = pc;
            Length = length;
            SourcePath = sourcePath;
            Line = line;
            Column = column;
            originalBytes = new byte[length];
            Array.Copy(script.Bytecode, pc, originalBytes, 0, length);
        }

        internal JsScript Script { get; }
        internal int Pc { get; }
        internal int Length { get; }
        internal string? SourcePath { get; }
        internal int Line { get; }
        internal int Column { get; }
        internal bool IsArmed { get; private set; }

        internal byte[] GetOriginalBytesCopy()
        {
            var bytes = new byte[originalBytes.Length];
            Array.Copy(originalBytes, 0, bytes, 0, originalBytes.Length);
            return bytes;
        }

        internal void Arm()
        {
            Script.Bytecode[Pc] = (byte)JsOpCode.Debugger;
            IsArmed = true;
            if (STraceBreakpoints)
                Console.Error.WriteLine(
                    $"[okojo] patch arm {SourcePath ?? "<anonymous>"}:{Line}:{Column} pc {Pc} len {Length}");
        }

        internal void Restore()
        {
            Array.Copy(originalBytes, 0, Script.Bytecode, Pc, originalBytes.Length);
            IsArmed = false;
            if (STraceBreakpoints)
                Console.Error.WriteLine(
                    $"[okojo] patch restore {SourcePath ?? "<anonymous>"}:{Line}:{Column} pc {Pc}");
        }
    }

    private readonly record struct PendingBreakpointRequest(string SourcePath, int Line);
}
