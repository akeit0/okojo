using Okojo.Bytecode;
using Okojo.Diagnostics;
using Okojo.Runtime;

namespace Okojo.Node.Cli;

internal static class NodeCliBytecodePrinter
{
    public static void PrintCompiledScript(JsScript script, string unitName, string unitKind = "script")
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentException.ThrowIfNullOrWhiteSpace(unitName);

        Console.Out.WriteLine(Disassembler.Dump(script, new()
        {
            UnitKind = unitKind,
            UnitName = unitName,
            ContextSlots = 0
        }));
    }

    public static void PrintRegisteredScripts(JsAgent agent, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var normalizedPath = Path.GetFullPath(sourcePath);
        var scripts = agent.ScriptDebugRegistry.GetRegisteredScripts(normalizedPath).ToArray();
        if (scripts.Length == 0)
            return;

        var unitKind = string.Equals(Path.GetExtension(normalizedPath), ".mjs", StringComparison.OrdinalIgnoreCase)
            ? "module"
            : "script";

        for (var i = 0; i < scripts.Length; i++)
        {
            if (i != 0)
                Console.Out.WriteLine();

            var unitName = scripts.Length == 1
                ? normalizedPath
                : $"{normalizedPath}#{i + 1}";
            PrintCompiledScript(scripts[i], unitName, unitKind);
        }
    }
}
