using System.Text;
using Okojo.Bytecode;
using Okojo.Compiler;
using Okojo.Diagnostics;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace OkojoBytecodeTool;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || HasHelpOption(args))
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        var mode = ToolMode.Disasm;
        string? input = null;
        string? filter = null;
        var listOnly = false;
        string? savePath = null;
        string? snapshotName = null;
        string? compareLeft = null;
        string? compareRight = null;
        string? casesDir = null;
        string? snapshotsRoot = null;
        var compareOpcodes = false;
        var moduleInfo = false;
        var moduleResolved = false;
        var moduleDisasm = false;

        var start = 0;
        if (args.Length > 0 && string.Equals(args[0], "--compare", StringComparison.OrdinalIgnoreCase))
        {
            mode = ToolMode.Compare;
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Missing compare paths.");
                PrintUsage();
                return 1;
            }

            compareLeft = args[1];
            compareRight = args[2];
            start = 3;
        }
        else if (args.Length > 0 && string.Equals(args[0], "--cases-snapshot", StringComparison.OrdinalIgnoreCase))
        {
            mode = ToolMode.CasesSnapshot;
            start = 1;
        }
        else
        {
            input = args[0];
            start = 1;
        }

        for (var i = start; i < args.Length; i++)
            switch (args[i])
            {
                case "--filter" when i + 1 < args.Length:
                    if (mode != ToolMode.Disasm)
                    {
                        Console.Error.WriteLine("--filter is only supported in disasm mode.");
                        return 1;
                    }

                    filter = args[++i];
                    break;
                case "--list":
                    if (mode != ToolMode.Disasm)
                    {
                        Console.Error.WriteLine("--list is only supported in disasm mode.");
                        return 1;
                    }

                    listOnly = true;
                    break;
                case "--save" when i + 1 < args.Length:
                    savePath = args[++i];
                    break;
                case "--snapshot" when i + 1 < args.Length:
                    if (mode != ToolMode.Disasm)
                    {
                        Console.Error.WriteLine("--snapshot is only supported in disasm mode.");
                        return 1;
                    }

                    snapshotName = args[++i];
                    break;
                case "--cases-dir" when i + 1 < args.Length:
                    if (mode != ToolMode.CasesSnapshot)
                    {
                        Console.Error.WriteLine("--cases-dir is only supported with --cases-snapshot.");
                        return 1;
                    }

                    casesDir = args[++i];
                    break;
                case "--snapshots-root" when i + 1 < args.Length:
                    if (mode != ToolMode.CasesSnapshot)
                    {
                        Console.Error.WriteLine("--snapshots-root is only supported with --cases-snapshot.");
                        return 1;
                    }

                    snapshotsRoot = args[++i];
                    break;
                case "--opcodes":
                    if (mode == ToolMode.Disasm)
                    {
                        Console.Error.WriteLine("--opcodes is only supported with --compare or --cases-snapshot.");
                        return 1;
                    }

                    compareOpcodes = true;
                    break;
                case "--module-info":
                    if (mode != ToolMode.Disasm)
                    {
                        Console.Error.WriteLine("--module-info is only supported in disasm mode.");
                        return 1;
                    }

                    moduleInfo = true;
                    break;
                case "--resolved":
                    if (mode != ToolMode.Disasm)
                    {
                        Console.Error.WriteLine("--resolved is only supported in disasm mode.");
                        return 1;
                    }

                    moduleResolved = true;
                    break;
                case "--module-disasm":
                    if (mode != ToolMode.Disasm)
                    {
                        Console.Error.WriteLine("--module-disasm is only supported in disasm mode.");
                        return 1;
                    }

                    moduleDisasm = true;
                    break;
                default:
                    Console.Error.WriteLine($"Unknown option: {args[i]}");
                    PrintUsage();
                    return 1;
            }

        try
        {
            if (mode == ToolMode.Compare)
            {
                var compareText = RenderCompare(compareLeft!, compareRight!, compareOpcodes);
                Console.WriteLine(compareText);
                if (!string.IsNullOrWhiteSpace(savePath))
                {
                    File.WriteAllText(savePath, compareText, Encoding.UTF8);
                    Console.Error.WriteLine($"Saved output: {Path.GetFullPath(savePath)}");
                }

                return 0;
            }

            if (mode == ToolMode.CasesSnapshot)
            {
                var summary = RunCasesSnapshot(casesDir, snapshotsRoot, compareOpcodes);
                Console.WriteLine(summary);
                if (!string.IsNullOrWhiteSpace(savePath))
                {
                    File.WriteAllText(savePath, summary, Encoding.UTF8);
                    Console.Error.WriteLine($"Saved output: {Path.GetFullPath(savePath)}");
                }

                return 0;
            }

            var source = File.Exists(input!)
                ? File.ReadAllText(input!, Encoding.UTF8)
                : input!;

            if (moduleInfo)
            {
                if (moduleResolved && !File.Exists(input!))
                {
                    Console.Error.WriteLine("--resolved requires file input.");
                    return 1;
                }

                var moduleOutput = RenderModuleInfo(source, input!, moduleResolved);
                Console.WriteLine(moduleOutput);
                if (!string.IsNullOrWhiteSpace(savePath))
                {
                    File.WriteAllText(savePath, moduleOutput, Encoding.UTF8);
                    Console.Error.WriteLine($"Saved output: {Path.GetFullPath(savePath)}");
                }

                if (!string.IsNullOrWhiteSpace(snapshotName))
                {
                    var snapshotPath = SaveSnapshot(moduleOutput, snapshotName!);
                    Console.Error.WriteLine($"Saved snapshot: {snapshotPath}");
                }

                return 0;
            }

            if (moduleDisasm)
            {
                if (!File.Exists(input!))
                {
                    Console.Error.WriteLine("--module-disasm requires file input.");
                    return 1;
                }

                var moduleDisasmOutput = RenderModuleDisassembly(input!, filter);
                Console.WriteLine(moduleDisasmOutput);
                if (!string.IsNullOrWhiteSpace(savePath))
                {
                    File.WriteAllText(savePath, moduleDisasmOutput, Encoding.UTF8);
                    Console.Error.WriteLine($"Saved output: {Path.GetFullPath(savePath)}");
                }

                if (!string.IsNullOrWhiteSpace(snapshotName))
                {
                    var snapshotPath = SaveSnapshot(moduleDisasmOutput, snapshotName!);
                    Console.Error.WriteLine($"Saved snapshot: {snapshotPath}");
                }

                return 0;
            }

            var program = JavaScriptParser.ParseScript(source);
            using var engine = JsRuntime.Create();
            var compiler = new JsCompiler(engine.MainRealm);
            var script = compiler.Compile(program);
            var functions = CollectOkojoFunctions(script);

            var output = listOnly
                ? RenderFunctionList(functions, filter)
                : RenderDisassembly(functions, filter);

            Console.WriteLine(output);
            if (!string.IsNullOrWhiteSpace(savePath))
            {
                File.WriteAllText(savePath, output, Encoding.UTF8);
                Console.Error.WriteLine($"Saved output: {Path.GetFullPath(savePath)}");
            }

            if (!string.IsNullOrWhiteSpace(snapshotName))
            {
                var snapshotPath = SaveSnapshot(output, snapshotName!);
                Console.Error.WriteLine($"Saved snapshot: {snapshotPath}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}");
            return 2;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine(
            "  OkojoBytecodeTool <js-file-or-string> [--filter <function-name>] [--list] [--save <path>] [--snapshot <name>] [--help]");
        Console.WriteLine(
            "  OkojoBytecodeTool --compare <left-file-or-dir> <right-file-or-dir> [--save <path>] [--help]");
        Console.WriteLine(
            "  OkojoBytecodeTool --cases-snapshot [--cases-dir <path>] [--snapshots-root <path>] [--opcodes] [--save <path>] [--help]");
        Console.WriteLine("  --list           List discovered script/function units only");
        Console.WriteLine("  --module-info    Parse as ES module and print imports/exports/TLA metadata");
        Console.WriteLine(
            "  --module-disasm  Compile ES module execution and print Okojo disassembly (file input only)");
        Console.WriteLine(
            "  --resolved       With --module-info, resolve/import-walk full module graph from file input");
        Console.WriteLine("  --filter <name>  Exact or substring match for unit name");
        Console.WriteLine("  --save <path>    Save rendered output to file");
        Console.WriteLine(
            "  --snapshot <name> Save output to artifacts/okojobytecodetool/snapshots/<timestamp>/<name>.disasm.txt");
        Console.WriteLine("  --compare A B    Compare two disasm files or snapshot directories");
        Console.WriteLine("  --cases-snapshot Snapshot all case JS files and compare with latest compatible snapshot");
        Console.WriteLine("  --cases-dir      Override case JS directory (default: artifacts/okojobytecodetool/cases)");
        Console.WriteLine(
            "  --snapshots-root Override snapshots root (default: artifacts/okojobytecodetool/snapshots)");
        Console.WriteLine("  --opcodes        Include normalized opcode-sequence diff in compare outputs");
        Console.WriteLine("  --help, -h       Show this help");
    }

    private static string RenderModuleInfo(string source, string inputLabel, bool includeResolvedGraph)
    {
        var program = JavaScriptParser.ParseModule(source);
        var imports = new List<string>();
        var exports = new List<string>();
        var reexports = new List<string>();
        var starReexports = new List<string>();

        for (var i = 0; i < program.Statements.Count; i++)
            switch (program.Statements[i])
            {
                case JsImportDeclaration importDecl:
                    imports.Add(importDecl.Source);
                    break;
                case JsExportDeclarationStatement exportDecl:
                    switch (exportDecl.Declaration)
                    {
                        case JsFunctionDeclaration fn:
                            exports.Add(fn.Name);
                            break;
                        case JsClassDeclaration cls:
                            exports.Add(cls.Name);
                            break;
                        case JsVariableDeclarationStatement vars:
                            for (var d = 0; d < vars.Declarators.Count; d++)
                                exports.Add(vars.Declarators[d].Name);
                            break;
                    }

                    break;
                case JsExportDefaultDeclaration:
                    exports.Add("default");
                    break;
                case JsExportNamedDeclaration named:
                    if (named.Source is null)
                        for (var s = 0; s < named.Specifiers.Count; s++)
                            exports.Add(named.Specifiers[s].ExportedName);
                    else
                        for (var s = 0; s < named.Specifiers.Count; s++)
                        {
                            var spec = named.Specifiers[s];
                            reexports.Add($"{spec.LocalName} as {spec.ExportedName} from {named.Source}");
                        }

                    break;
                case JsExportAllDeclaration all:
                    if (string.IsNullOrEmpty(all.ExportedName))
                        starReexports.Add($"* from {all.Source}");
                    else
                        starReexports.Add($"* as {all.ExportedName} from {all.Source}");
                    break;
            }

        var sb = new StringBuilder();
        sb.AppendLine("# OkojoBytecodeTool ModuleInfo");
        sb.AppendLine();
        sb.AppendLine($"Input: {inputLabel}");
        sb.AppendLine($"TopLevelAwait: {program.HasTopLevelAwait}");
        sb.AppendLine($"Statements: {program.Statements.Count}");
        sb.AppendLine();
        sb.AppendLine("## Imports");
        if (imports.Count == 0)
            sb.AppendLine("(none)");
        else
            for (var i = 0; i < imports.Count; i++)
                sb.AppendLine($"- {imports[i]}");
        sb.AppendLine();
        sb.AppendLine("## Exports");
        if (exports.Count == 0)
            sb.AppendLine("(none)");
        else
            for (var i = 0; i < exports.Count; i++)
                sb.AppendLine($"- {exports[i]}");
        sb.AppendLine();
        sb.AppendLine("## ReExports");
        if (reexports.Count == 0)
            sb.AppendLine("(none)");
        else
            for (var i = 0; i < reexports.Count; i++)
                sb.AppendLine($"- {reexports[i]}");
        sb.AppendLine();
        sb.AppendLine("## ExportStars");
        if (starReexports.Count == 0)
            sb.AppendLine("(none)");
        else
            for (var i = 0; i < starReexports.Count; i++)
                sb.AppendLine($"- {starReexports[i]}");

        if (includeResolvedGraph)
        {
            sb.AppendLine();
            sb.AppendLine("## ResolvedGraph");
            var loader = new FileModuleSourceLoader();
            var rootResolved = loader.ResolveSpecifier(Path.GetFullPath(inputLabel), null);
            var graph = BuildResolvedModuleGraph(loader, rootResolved);
            for (var i = 0; i < graph.Count; i++)
            {
                var node = graph[i];
                sb.AppendLine($"- {node.ResolvedId}");
                if (node.Dependencies.Count == 0)
                    sb.AppendLine("  deps: (none)");
                else
                    sb.AppendLine("  deps: " + string.Join(", ", node.Dependencies));
            }
        }

        return sb.ToString();
    }

    private static string RenderModuleDisassembly(string modulePath, string? filter)
    {
        var resolvedPath = Path.GetFullPath(modulePath);
        var source = File.ReadAllText(resolvedPath, Encoding.UTF8);
        var program = JavaScriptParser.ParseModule(source);
        var loader = new FileModuleSourceLoader();
        var linker = new ModuleLinker(() => loader);
        var plan = linker.BuildPlan(resolvedPath, program);
        var compileBindings = BuildModuleVariableBindings(
            plan.ResolvedImportBindings,
            plan.ExecutionPlan.ExportLocalByName,
            plan.ExecutionPlan.PreinitializedLocalExportNames);

        using var engine = JsRuntime.CreateBuilder()
            .UseHost(host => host.UseModuleSourceLoader(loader))
            .Build();
        using var compiler = JsCompiler.CreateForModuleExecution(engine.MainRealm, compileBindings);
        var script = plan.ExecutionPlan.RequiresTopLevelAwait
            ? compiler.CompileModuleExecutionAsync(plan.ExecutionPlan, source, resolvedPath)
            : compiler.CompileModuleExecution(plan.ExecutionPlan, source, resolvedPath);
        var functions = CollectOkojoFunctions(script);
        AppendModuleHoistedFunctions(functions, compiler, program, source, resolvedPath);
        return RenderDisassembly(functions, filter);
    }

    private static Dictionary<string, ModuleVariableBinding> BuildModuleVariableBindings(
        IReadOnlyList<JsResolvedImportBinding> importBindings,
        IReadOnlyDictionary<string, string> exportLocalByName,
        IReadOnlySet<string> preinitializedLocalExportNames)
    {
        _ = preinitializedLocalExportNames;
        var map = new Dictionary<string, ModuleVariableBinding>(importBindings.Count + exportLocalByName.Count,
            StringComparer.Ordinal);
        var importCount = 0;

        for (var i = 0; i < importBindings.Count; i++)
        {
            var binding = importBindings[i];
            if (map.ContainsKey(binding.LocalName))
                continue;

            var cellIndex = unchecked((sbyte)-(importCount + 1));
            map.Add(binding.LocalName, new(cellIndex, 0, true));
            importCount++;
        }

        var exportCount = 0;
        foreach (var pair in exportLocalByName)
        {
            var localName = pair.Value;
            if (map.ContainsKey(localName))
                continue;

            var cellIndex = unchecked((sbyte)(exportCount + 1));
            map.Add(localName, new(cellIndex, 0, false));
            exportCount++;
        }

        return map;
    }

    private static void AppendModuleHoistedFunctions(
        List<(string name, JsScript script)> functions,
        JsCompiler compiler,
        JsProgram program,
        string source,
        string resolvedPath)
    {
        var seenScripts = new HashSet<JsScript>(functions.Select(static entry => entry.script));

        foreach (var statement in program.Statements)
        {
            if (statement is not JsExportDeclarationStatement exportDeclaration ||
                exportDeclaration.Declaration is not JsFunctionDeclaration functionDeclaration)
                continue;

            var hoisted = compiler.CompileHoistedFunctionTemplate(
                functionDeclaration,
                source,
                resolvedPath,
                program.IdentifierTable);
            CollectOkojoFunctions(hoisted.Script, string.IsNullOrEmpty(hoisted.Name) ? "<anonymous>" : hoisted.Name,
                functions, seenScripts);
        }
    }

    private static List<(string ResolvedId, List<string> Dependencies)> BuildResolvedModuleGraph(
        IModuleSourceLoader loader,
        string rootResolvedId)
    {
        var result = new List<(string ResolvedId, List<string> Dependencies)>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var inProgress = new HashSet<string>(StringComparer.Ordinal);

        Visit(rootResolvedId);
        return result;

        void Visit(string resolvedId)
        {
            if (!visited.Add(resolvedId))
                return;
            if (!inProgress.Add(resolvedId))
                return;

            var source = loader.LoadSource(resolvedId);
            var program = JavaScriptParser.ParseModule(source);
            var deps = new List<string>();

            for (var i = 0; i < program.Statements.Count; i++)
                switch (program.Statements[i])
                {
                    case JsImportDeclaration importDecl:
                        deps.Add(loader.ResolveSpecifier(importDecl.Source, resolvedId));
                        break;
                    case JsExportNamedDeclaration exportNamed when !string.IsNullOrEmpty(exportNamed.Source):
                        deps.Add(loader.ResolveSpecifier(exportNamed.Source!, resolvedId));
                        break;
                    case JsExportAllDeclaration exportAll:
                        deps.Add(loader.ResolveSpecifier(exportAll.Source, resolvedId));
                        break;
                }

            for (var i = 0; i < deps.Count; i++)
                Visit(deps[i]);

            _ = inProgress.Remove(resolvedId);
            result.Add((resolvedId, deps.Distinct(StringComparer.Ordinal).ToList()));
        }
    }

    private static bool HasHelpOption(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
            if (string.Equals(args[i], "--help", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[i], "-h", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static string RenderFunctionList(List<(string name, JsScript script)> functions, string? filter)
    {
        var selected = SelectFunctions(functions, filter)
            .Select(x => x.name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (selected.Count == 0)
            return $"(No Okojo units found matching: {filter})";

        return string.Join(Environment.NewLine, selected);
    }

    private static string RenderDisassembly(List<(string name, JsScript script)> functions, string? filter)
    {
        var selected = SelectFunctions(functions, filter);
        if (selected.Count == 0)
            return $"(No Okojo units found matching: {filter})";

        var sb = new StringBuilder();
        var first = true;
        foreach (var (name, script) in selected)
        {
            if (!first)
                sb.AppendLine();
            first = false;
            sb.AppendLine(new('=', 80));
            sb.Append(Disassembler.Dump(script, new()
            {
                UnitKind = name == "<script>" ? "script" : "function",
                UnitName = name
            }));
        }

        return sb.ToString();
    }

    private static List<(string name, JsScript script)> SelectFunctions(
        List<(string name, JsScript script)> functions,
        string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return functions;

        var exact = functions.Where(f => f.name.Equals(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count != 0)
            return exact;

        return functions
            .Where(f => f.name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static List<(string name, JsScript script)> CollectOkojoFunctions(JsScript root)
    {
        var result = new List<(string name, JsScript script)>();
        var seen = new HashSet<JsScript>();
        CollectOkojoFunctions(root, "<script>", result, seen);
        return result;
    }

    private static void CollectOkojoFunctions(
        JsScript root,
        string rootName,
        List<(string name, JsScript script)> result,
        HashSet<JsScript> seen)
    {
        if (!seen.Add(root))
            return;

        result.Add((rootName, root));
        foreach (var obj in root.ObjectConstants)
            if (obj is JsBytecodeFunction fn)
                CollectOkojoFunctions(fn.Script, string.IsNullOrEmpty(fn.Name) ? "<anonymous>" : fn.Name, result, seen);
    }

    private static string SaveSnapshot(string output, string snapshotName)
    {
        var safeName = MakeSafeFileName(snapshotName);
        var ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var dir = Path.Combine("artifacts", "okojobytecodetool", "snapshots", ts);
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, safeName + ".disasm.txt");
        File.WriteAllText(filePath, output, Encoding.UTF8);
        return Path.GetFullPath(filePath);
    }

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            sb.Append(invalid.Contains(ch) ? '_' : ch);
        }

        var text = sb.ToString().Trim();
        return string.IsNullOrEmpty(text) ? "snapshot" : text;
    }

    private static string RenderCompare(string left, string right, bool compareOpcodes = false)
    {
        var leftIsDir = Directory.Exists(left);
        var rightIsDir = Directory.Exists(right);
        var leftIsFile = File.Exists(left);
        var rightIsFile = File.Exists(right);

        if (leftIsFile && rightIsFile)
            return RenderFileCompare(left, right, compareOpcodes);
        if (leftIsDir && rightIsDir)
            return RenderDirectoryCompare(left, right, compareOpcodes);

        throw new InvalidOperationException("Both compare paths must be files or both must be directories.");
    }

    private static string RenderFileCompare(string leftFile, string rightFile, bool compareOpcodes)
    {
        var leftUnits = ParseUnitRegisters(File.ReadAllText(leftFile, Encoding.UTF8));
        var rightUnits = ParseUnitRegisters(File.ReadAllText(rightFile, Encoding.UTF8));
        var leftOpcodes = compareOpcodes ? ParseUnitOpcodeSequences(File.ReadAllText(leftFile, Encoding.UTF8)) : null;
        var rightOpcodes = compareOpcodes ? ParseUnitOpcodeSequences(File.ReadAllText(rightFile, Encoding.UTF8)) : null;
        var keys = leftUnits.Keys.Union(rightUnits.Keys).OrderBy(x => x, StringComparer.Ordinal).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("# OkojoBytecodeTool Compare (file)");
        sb.AppendLine();
        sb.AppendLine($"Left:  {Path.GetFullPath(leftFile)}");
        sb.AppendLine($"Right: {Path.GetFullPath(rightFile)}");
        sb.AppendLine();
        if (compareOpcodes)
        {
            sb.AppendLine("| Unit | Left | Right | Delta | Opcodes | FirstDiff |");
            sb.AppendLine("|---|---:|---:|---:|---|---|");
        }
        else
        {
            sb.AppendLine("| Unit | Left | Right | Delta |");
            sb.AppendLine("|---|---:|---:|---:|");
        }

        foreach (var key in keys)
        {
            var hasLeft = leftUnits.TryGetValue(key, out var l);
            var hasRight = rightUnits.TryGetValue(key, out var r);
            var delta = hasLeft && hasRight ? (r - l).ToString() : "-";
            sb.Append("| ").Append(key).Append(" | ")
                .Append(hasLeft ? l.ToString() : "missing").Append(" | ")
                .Append(hasRight ? r.ToString() : "missing").Append(" | ")
                .Append(delta).Append(" |");
            if (compareOpcodes)
            {
                var opcodeInfo = CompareOpcodeSequences(
                    leftOpcodes is not null && leftOpcodes.TryGetValue(key, out var lo) ? lo : null,
                    rightOpcodes is not null && rightOpcodes.TryGetValue(key, out var ro) ? ro : null);
                sb.Append(' ').Append(opcodeInfo.Status).Append(" | ")
                    .Append(opcodeInfo.FirstDifference).AppendLine(" |");
            }
            else
            {
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static string RenderDirectoryCompare(string leftDir, string rightDir, bool compareOpcodes)
    {
        var left = BuildFileMap(leftDir);
        var right = BuildFileMap(rightDir);
        var names = left.Keys.Union(right.Keys).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("# OkojoBytecodeTool Compare (directory)");
        sb.AppendLine();
        sb.AppendLine($"Left:  {Path.GetFullPath(leftDir)}");
        sb.AppendLine($"Right: {Path.GetFullPath(rightDir)}");
        sb.AppendLine();
        if (compareOpcodes)
        {
            sb.AppendLine("| Case | Left(script) | Right(script) | Delta | Opcodes | FirstDiff |");
            sb.AppendLine("|---|---:|---:|---:|---|---|");
        }
        else
        {
            sb.AppendLine("| Case | Left(script) | Right(script) | Delta |");
            sb.AppendLine("|---|---:|---:|---:|");
        }

        foreach (var name in names)
        {
            var l = left.TryGetValue(name, out var lPath) ? ExtractScriptRegisterCount(lPath) : null;
            var r = right.TryGetValue(name, out var rPath) ? ExtractScriptRegisterCount(rPath) : null;
            var delta = l.HasValue && r.HasValue ? (r.Value - l.Value).ToString() : "-";
            sb.Append("| ").Append(name).Append(" | ")
                .Append(l.HasValue ? l.Value.ToString() : "missing").Append(" | ")
                .Append(r.HasValue ? r.Value.ToString() : "missing").Append(" | ")
                .Append(delta).Append(" |");
            if (compareOpcodes)
            {
                var leftOps = lPath is null ? null : ExtractScriptOpcodeSequence(lPath);
                var rightOps = rPath is null ? null : ExtractScriptOpcodeSequence(rPath);
                var opcodeInfo = CompareOpcodeSequences(leftOps, rightOps);
                sb.Append(' ').Append(opcodeInfo.Status).Append(" | ")
                    .Append(opcodeInfo.FirstDifference).AppendLine(" |");
            }
            else
            {
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static Dictionary<string, string> BuildFileMap(string dir)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.GetFiles(dir, "*.disasm.txt"))
        {
            var fileName = Path.GetFileName(path);
            if (string.IsNullOrEmpty(fileName))
                continue;
            map[fileName] = path;
        }

        return map;
    }

    private static int? ExtractScriptRegisterCount(string disasmPath)
    {
        var units = ParseUnitRegisters(File.ReadAllText(disasmPath, Encoding.UTF8));
        foreach (var kv in units)
            if (kv.Key.StartsWith("script:", StringComparison.Ordinal))
                return kv.Value;
        return null;
    }

    private static List<string>? ExtractScriptOpcodeSequence(string disasmPath)
    {
        var units = ParseUnitOpcodeSequences(File.ReadAllText(disasmPath, Encoding.UTF8));
        foreach (var kv in units)
            if (kv.Key.StartsWith("script:", StringComparison.Ordinal))
                return kv.Value;
        return null;
    }

    private static Dictionary<string, int> ParseUnitRegisters(string text)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        string? kind = null;
        string? name = null;

        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("; unit-kind:", StringComparison.Ordinal))
            {
                kind = line["; unit-kind:".Length..].Trim();
                continue;
            }

            if (line.StartsWith("; unit-name:", StringComparison.Ordinal))
            {
                name = line["; unit-name:".Length..].Trim();
                continue;
            }

            if (line.StartsWith("; registers:", StringComparison.Ordinal))
            {
                var regText = line["; registers:".Length..].Trim();
                if (!string.IsNullOrEmpty(kind) && !string.IsNullOrEmpty(name) &&
                    int.TryParse(regText, out var reg))
                    map[$"{kind}:{name}"] = reg;
            }
        }

        return map;
    }

    private static string RunCasesSnapshot(string? casesDirArg, string? snapshotsRootArg, bool compareOpcodes)
    {
        var casesDir = string.IsNullOrWhiteSpace(casesDirArg)
            ? Path.Combine("artifacts", "okojobytecodetool", "cases")
            : casesDirArg!;
        var snapshotsRoot = string.IsNullOrWhiteSpace(snapshotsRootArg)
            ? Path.Combine("artifacts", "okojobytecodetool", "snapshots")
            : snapshotsRootArg!;

        if (!Directory.Exists(casesDir))
            throw new InvalidOperationException($"Cases directory not found: {Path.GetFullPath(casesDir)}");

        var caseFiles = Directory.GetFiles(casesDir, "*.js")
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (caseFiles.Count == 0)
            throw new InvalidOperationException($"No .js case files found in: {Path.GetFullPath(casesDir)}");

        Directory.CreateDirectory(snapshotsRoot);
        var ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var currentSnapshotDir = Path.Combine(snapshotsRoot, ts);
        Directory.CreateDirectory(currentSnapshotDir);
        var realm = JsRuntime.Create().DefaultRealm;

        for (var i = 0; i < caseFiles.Count; i++)
        {
            var casePath = caseFiles[i];
            var source = File.ReadAllText(casePath, Encoding.UTF8);
            var program = JavaScriptParser.ParseScript(source);
            var compiler = new JsCompiler(realm);
            var script = compiler.Compile(program);
            var functions = CollectOkojoFunctions(script);
            var output = RenderDisassembly(functions, null);
            var outFile = Path.Combine(currentSnapshotDir, Path.GetFileNameWithoutExtension(casePath) + ".disasm.txt");
            File.WriteAllText(outFile, output, Encoding.UTF8);
            Console.Error.WriteLine($"Saved output: {Path.GetFullPath(outFile)}");
        }

        var expectedNames = caseFiles
            .Select(x => Path.GetFileNameWithoutExtension(x) + ".disasm.txt")
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var previousCompatibleDir = FindLatestCompatibleSnapshotDirectory(snapshotsRoot, ts, expectedNames);

        var sb = new StringBuilder();
        sb.AppendLine($"Snapshot created: {Path.GetFullPath(currentSnapshotDir)}");
        if (previousCompatibleDir is null)
        {
            sb.AppendLine("No compatible previous snapshot found (same case-file set).");
            return sb.ToString();
        }

        var compareText = RenderCompare(previousCompatibleDir, currentSnapshotDir, compareOpcodes);
        var previousName = new DirectoryInfo(previousCompatibleDir).Name;
        var compareNamed = Path.Combine(currentSnapshotDir, $"compare_to_{previousName}.md");
        var comparePrev = Path.Combine(currentSnapshotDir, "compare_to_previous.md");
        File.WriteAllText(compareNamed, compareText, Encoding.UTF8);
        File.WriteAllText(comparePrev, compareText, Encoding.UTF8);

        sb.AppendLine($"Compared with:   {Path.GetFullPath(previousCompatibleDir)}");
        sb.AppendLine($"Compare report:  {Path.GetFullPath(compareNamed)}");
        sb.AppendLine();
        sb.Append(compareText);
        return sb.ToString();
    }

    private static string? FindLatestCompatibleSnapshotDirectory(
        string snapshotsRoot,
        string currentSnapshotName,
        string[] expectedNames)
    {
        var dirs = Directory.GetDirectories(snapshotsRoot)
            .Select(x => new DirectoryInfo(x))
            .Where(x => !x.Name.Equals(currentSnapshotName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var dir in dirs)
        {
            var names = Directory.GetFiles(dir.FullName, "*.disasm.txt")
                .Select(Path.GetFileName)
                .Where(x => !string.IsNullOrEmpty(x))
                .Cast<string>()
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (names.Length != expectedNames.Length)
                continue;
            var same = true;
            for (var i = 0; i < names.Length; i++)
                if (!names[i].Equals(expectedNames[i], StringComparison.OrdinalIgnoreCase))
                {
                    same = false;
                    break;
                }

            if (same)
                return dir.FullName;
        }

        return null;
    }

    private static Dictionary<string, List<string>> ParseUnitOpcodeSequences(string text)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        string? kind = null;
        string? name = null;
        var inCode = false;
        string? currentKey = null;

        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("; unit-kind:", StringComparison.Ordinal))
            {
                kind = line["; unit-kind:".Length..].Trim();
                currentKey = null;
                inCode = false;
                continue;
            }

            if (line.StartsWith("; unit-name:", StringComparison.Ordinal))
            {
                name = line["; unit-name:".Length..].Trim();
                if (!string.IsNullOrEmpty(kind) && !string.IsNullOrEmpty(name))
                {
                    currentKey = $"{kind}:{name}";
                    if (!map.ContainsKey(currentKey))
                        map[currentKey] = new();
                }

                inCode = false;
                continue;
            }

            if (line.Equals(".code", StringComparison.Ordinal))
            {
                inCode = true;
                continue;
            }

            if (line.StartsWith(".", StringComparison.Ordinal))
            {
                inCode = false;
                continue;
            }

            if (!inCode || string.IsNullOrEmpty(currentKey))
                continue;
            if (line.Length < 6 || !char.IsDigit(line[0]))
                continue;

            var opcodeStart = line.IndexOf("  ", StringComparison.Ordinal);
            if (opcodeStart < 0 || opcodeStart + 2 >= line.Length)
                continue;
            var rest = line[(opcodeStart + 2)..].Trim();
            if (rest.Length == 0)
                continue;
            var space = rest.IndexOf(' ');
            var opcode = space > 0 ? rest[..space] : rest;
            map[currentKey].Add(opcode);
        }

        return map;
    }

    private static (string Status, string FirstDifference) CompareOpcodeSequences(List<string>? left,
        List<string>? right)
    {
        if (left is null && right is null)
            return ("missing", "-");
        if (left is null || right is null)
            return ("missing", "-");

        var min = Math.Min(left.Count, right.Count);
        for (var i = 0; i < min; i++)
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                return ("diff", $"{i}:{left[i]}->{right[i]}");
        if (left.Count != right.Count)
            return ("diff", $"len:{left.Count}->{right.Count}");
        return ("same", "-");
    }

    private enum ToolMode
    {
        Disasm,
        Compare,
        CasesSnapshot
    }
}
