using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Iced.Intel;
using Microsoft.Diagnostics.Runtime;

internal static class Program
{
    private const string DefaultTypeName = "Okojo.JavaScript.Execution.JsRealm";
    private const string DefaultMethodName = "Run";

    private static int Main(string[] args)
    {
        if (args.Contains("--help", StringComparer.OrdinalIgnoreCase))
        {
            PrintUsage();
            return 0;
        }

        try
        {
            if (GetOption(args, "--from") is { } fromPath)
            {
                var view = ViewFromReport(fromPath, args);
                WriteOutput(view, GetOption(args, "--output"));
                return 0;
            }

            var options = ParseOptions(args);
            var report = Inspect(options);
            WriteOutput(report, options.OutputPath);
            return 0;
        }
        catch (Exception ex)
            when (ex is ArgumentException or InvalidOperationException or IOException)
        {
            Console.Error.WriteLine($"VmLoopIlMap: {ex.Message}");
            return 1;
        }
    }

    private static void WriteOutput(List<string> lines, string? outputPath)
    {
        if (outputPath is null)
            Console.WriteLine(string.Join(Environment.NewLine, lines));
        else
        {
            File.WriteAllLines(outputPath, lines);
            Console.WriteLine($"Saved: {outputPath}");
        }
    }

    private static Options ParseOptions(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            throw new ArgumentException("A process ID or dump path is required.");
        }

        var target = args[0];
        var typeName = GetOption(args, "--type") ?? DefaultTypeName;
        var methodName = GetOption(args, "--method") ?? DefaultMethodName;
        var outputPath = GetOption(args, "--output");
        return new Options(target, typeName, methodName, outputPath);
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Missing value for {name}.");

            return args[i + 1];
        }

        return null;
    }

    private static List<string> Inspect(Options options)
    {
        using var dataTarget = CreateDataTarget(options.Target);
        if (dataTarget.ClrVersions.Length == 0)
            throw new InvalidOperationException("The target has no supported CLR runtime.");

        using var runtime = dataTarget.ClrVersions[0].CreateRuntime();
        var match = runtime
            .EnumerateModules()
            .Select(module => (Module: module, Type: module.GetTypeByName(options.TypeName)))
            .FirstOrDefault(item => item.Type is not null);
        if (match.Type is null)
            throw new InvalidOperationException(
                $"Type '{options.TypeName}' was not found in the target modules."
            );

        var methods = match
            .Type.Methods.Where(method =>
                string.Equals(method.Name, options.MethodName, StringComparison.Ordinal)
            )
            .ToArray();
        if (methods.Length == 0)
            throw new InvalidOperationException(
                $"Method '{options.MethodName}' was not found on '{options.TypeName}'."
            );
        if (methods.Length > 1)
            throw new InvalidOperationException(
                $"Method '{options.MethodName}' is overloaded on '{options.TypeName}'; add a method selector."
            );

        var method = methods[0];
        var sourcePoints = ReadSourcePoints(match.Module, method.MetadataToken);
        var report = new List<string>
        {
            $"[target] {options.Target}",
            $"[runtime] version={runtime.ClrInfo.Version} architecture={dataTarget.DataReader.Architecture}",
            $"[module] name={match.Module.Name} assembly={match.Module.AssemblyName}",
            $"[method] type={options.TypeName} name={method.Name} signature={method.Signature}",
            $"[method] metadata_token=0x{method.MetadataToken:X8} compilation={method.CompilationType}",
        };

        var regions = method.HotColdInfo;
        report.Add(
            $"[native] code=0x{method.NativeCode:X} hot=0x{regions.HotStart:X}+0x{regions.HotSize:X} cold=0x{regions.ColdStart:X}+0x{regions.ColdSize:X}"
        );
        report.Add($"[map] entries={method.ILOffsetMap.Length} pdb_points={sourcePoints.Count}");

        foreach (var entry in method.ILOffsetMap)
        {
            var size =
                entry.EndAddress >= entry.StartAddress ? entry.EndAddress - entry.StartAddress : 0;
            report.Add(
                $"[map] il={entry.ILOffset} native=0x{entry.StartAddress:X}-0x{entry.EndAddress:X} size=0x{size:X} source={FindSource(sourcePoints, entry.ILOffset)}"
            );
        }

        AppendDisassembly(dataTarget.DataReader, method, sourcePoints, report);

        return report;
    }

    private static DataTarget CreateDataTarget(string target)
    {
        if (int.TryParse(target, out var processId))
            return DataTarget.CreateSnapshotAndAttach(processId);

        if (!File.Exists(target))
            throw new FileNotFoundException($"Target dump was not found: {target}");

        return DataTarget.LoadDump(target);
    }

    private static void AppendDisassembly(
        IDataReader dataReader,
        ClrMethod method,
        IReadOnlyList<SourcePoint> sourcePoints,
        List<string> report
    )
    {
        if (method.NativeCode == 0 || method.HotColdInfo.HotSize == 0)
        {
            report.Add("[asm] unavailable: method has no native code");
            return;
        }

        if (dataReader.Architecture is not (Architecture.X86 or Architecture.X64))
        {
            report.Add($"[asm] unavailable: unsupported architecture {dataReader.Architecture}");
            return;
        }

        var maps = method
            .ILOffsetMap.Where(map => map.StartAddress < map.EndAddress)
            .OrderBy(map => map.StartAddress)
            .ToArray();
        var formatter = new IntelFormatter();
        var instructions = new List<NativeInstruction>();
        foreach (var (name, start, size) in GetNativeRegions(method.HotColdInfo))
        {
            report.Add($"[asm-region] name={name} start=0x{start:X} size=0x{size:X}");
            var bytes = ReadMemory(dataReader, start, size);
            var reader = new ByteArrayCodeReader(bytes);
            var decoder = Decoder.Create(
                dataReader.PointerSize * 8,
                reader,
                start,
                DecoderOptions.None
            );
            while (reader.CanReadByte)
            {
                decoder.Decode(out var instruction);
                var output = new StringOutput();
                formatter.Format(instruction, output);
                var ilOffset = FindIlOffset(maps, instruction.IP);
                report.Add(
                    $"[asm] native=0x{instruction.IP:X} il={ilOffset?.ToString() ?? "-"} source={FindSource(sourcePoints, ilOffset ?? -1)} {output}"
                );
                if (instructions.Count > 0)
                {
                    var previous = instructions[^1];
                    instructions[^1] = previous with
                    {
                        Length = (int)(instruction.IP - previous.Address),
                    };
                }

                instructions.Add(
                    new NativeInstruction(
                        instruction.IP,
                        instruction.FlowControl,
                        instruction.MemoryBase != Register.None
                            || instruction.MemoryIndex != Register.None,
                        ExtractSourceLine(sourcePoints, ilOffset ?? -1),
                        1,
                        output.ToString()
                    )
                );
            }

            // Tail length: the last instruction extends to the region end.
            if (instructions.Count > 0)
            {
                var last = instructions[^1];
                instructions[^1] = last with
                {
                    Length = (int)(start + size - last.Address),
                };
            }
        }

        AppendLineMap(report, instructions);
        AppendSummary(report, instructions, sourcePoints);
    }

    private static void AppendLineMap(
        List<string> report,
        IReadOnlyList<NativeInstruction> instructions
    )
    {
        // Source line -> native positions: one entry per contiguous native
        // range per source line, with exact counts taken from the decoded
        // flow control and memory operands. Read this file offline instead of
        // re-attaching to the process for every view.
        report.Add("[line-map] line range=0xSTART-0xEND size instr calls loads");
        var index = 0;
        while (index < instructions.Count)
        {
            var line = instructions[index].SourceLine;
            var rangeStart = index;
            var rangeEnd = 0UL;
            var bytes = 0UL;
            var count = 0;
            var calls = 0;
            var loads = 0;
            while (
                index < instructions.Count
                && instructions[index].SourceLine.Equals(line)
                && (
                    index == rangeStart
                    || instructions[index].Address == rangeEnd
                )
            )
            {
                var item = instructions[index];
                bytes += (ulong)item.Length;
                count++;
                if (item.HasMemoryOperand)
                    loads++;
                if (item.FlowControl is FlowControl.Call or FlowControl.IndirectCall)
                    calls++;
                rangeEnd = item.Address + (ulong)item.Length;
                index++;
            }

            report.Add(
                $"[line-map] line={line?.ToString() ?? "-"} range=0x{instructions[rangeStart].Address:X}-0x{rangeEnd:X} size={bytes} instr={count} calls={calls} loads={loads}"
            );
        }
    }

    private static void AppendSummary(
        List<string> report,
        IReadOnlyList<NativeInstruction> instructions,
        IReadOnlyList<SourcePoint> sourcePoints
    )
    {
        // Attribute native code to opcode arms through the PDB source lines of
        // the `case JsOpCode.X:` labels: consecutive case labels form one
        // shared arm group; attribution uses a per-line owner table (source
        // order), because the JIT does not emit arms in address order.
        var sourcePath = sourcePoints
            .Select(point => point.Document)
            .Where(document => document != "<unknown>" && File.Exists(document))
            .GroupBy(document => document)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault();
        if (sourcePath is null)
        {
            report.Add("[summary] unavailable: no source document");
            return;
        }

        var sourceLines = File.ReadAllLines(sourcePath);
        var firstLine = sourcePoints.Min(point => point.Line);
        var lastLine = sourcePoints.Max(point => point.Line);
        var groups = new List<(int Line, string Names)>();
        var pending = new List<string>();
        for (var line = firstLine; line <= lastLine; line++)
        {
            if (line - 1 >= sourceLines.Length)
                break;
            var match = Regex.Match(
                sourceLines[line - 1],
                @"^\s*case JsOpCode\.([A-Za-z0-9_]+)\s*:"
            );
            if (match.Success)
            {
                pending.Add(match.Groups[1].Value);
                continue;
            }

            if (pending.Count > 0)
            {
                groups.Add((line, string.Join("+", pending)));
                pending.Clear();
            }
        }

        var ownerByLine = new string[lastLine + 1];
        var currentOwner = "prologue";
        var groupIndex = 0;
        for (var line = firstLine; line <= lastLine; line++)
        {
            if (groupIndex < groups.Count && groups[groupIndex].Line == line)
            {
                currentOwner = groups[groupIndex].Names;
                groupIndex++;
            }

            ownerByLine[line] = currentOwner;
        }

        var aggregates = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var instruction in instructions)
        {
            var owner =
                instruction.SourceLine is int line && line < ownerByLine.Length
                    ? ownerByLine[line]
                    : currentOwner;

            if (!aggregates.TryGetValue(owner, out var stats))
            {
                stats = new int[4]; // instr, bytes, loads, calls
                aggregates[owner] = stats;
            }

            stats[0]++;
            stats[1] += instruction.Length;
            if (instruction.HasMemoryOperand)
                stats[2]++;
            if (
                instruction.FlowControl
                is FlowControl.Call
                    or FlowControl.IndirectCall
            )
                stats[3]++;
        }

        report.Add($"[summary] arms={aggregates.Count} instructions={instructions.Count}");
        report.Add(
            "[summary-arm] opcode instr bytes loads calls (sorted by bytes; group names joined by +)"
        );
        foreach (var entry in aggregates.OrderByDescending(item => item.Value[1]))
        {
            report.Add(
                $"[summary-arm] {entry.Key} {entry.Value[0]} {entry.Value[1]} {entry.Value[2]} {entry.Value[3]}"
            );
        }
    }

    private static List<string> ViewFromReport(string fromPath, string[] args)
    {
        var lines = File.ReadAllLines(fromPath);
        var wantSourceMap = args.Contains("--source-map", StringComparer.OrdinalIgnoreCase);
        var wantSummary = args.Contains("--summary", StringComparer.OrdinalIgnoreCase);
        var lineFilter = GetOption(args, "--line");
        var filteredLines =
            lineFilter is null
                ? null
                : lineFilter
                    .Split(',')
                    .Select(static value => int.Parse(value, CultureInfo.InvariantCulture))
                    .ToArray();
        var output = new List<string> { $"[view] from={fromPath}" };

        foreach (var line in lines)
        {
            if (line.StartsWith("[asm] ", StringComparison.Ordinal))
            {
                if (filteredLines is null)
                    continue;
                var match = Regex.Match(
                    line,
                    @"^\[asm\] native=0x[0-9A-Fa-f]+ il=\S+ source=.+:(\d+) "
                );
                if (
                    match.Success
                    && int.TryParse(match.Groups[1].Value, out var sourceLine)
                    && filteredLines.Contains(sourceLine)
                )
                    output.Add(line);
                continue;
            }

            if (line.StartsWith("[line-map]", StringComparison.Ordinal))
            {
                if (wantSourceMap || (filteredLines is null && !wantSummary))
                    output.Add(line);
                continue;
            }

            if (line.StartsWith("[summary-arm]", StringComparison.Ordinal))
            {
                if (wantSummary || (filteredLines is null && !wantSourceMap))
                    output.Add(line);
                continue;
            }

            if (line.StartsWith("[summary]", StringComparison.Ordinal))
            {
                if (wantSummary || (filteredLines is null && !wantSourceMap))
                    output.Add(line);
                continue;
            }

            // headers ([target]/[method]/[native]/[map]/[asm-region]) pass through
            output.Add(line);
        }

        return output;
    }

    private static IEnumerable<(string Name, ulong Start, uint Size)> GetNativeRegions(
        HotColdRegions regions
    )
    {
        if (regions.HotStart != 0 && regions.HotSize != 0)
            yield return ("hot", regions.HotStart, regions.HotSize);
        if (regions.ColdStart != 0 && regions.ColdSize != 0)
            yield return ("cold", regions.ColdStart, regions.ColdSize);
    }

    private static byte[] ReadMemory(IDataReader dataReader, ulong start, uint size)
    {
        var bytes = new byte[checked((int)size)];
        var total = 0;
        while (total < bytes.Length)
        {
            var read = dataReader.Read(start + (ulong)total, bytes.AsSpan(total));
            if (read <= 0)
                throw new IOException($"Could not read native code at 0x{start + (ulong)total:X}.");
            total += read;
        }

        return bytes;
    }

    private static int? FindIlOffset(IReadOnlyList<ILToNativeMap> maps, ulong address)
    {
        foreach (var map in maps)
        {
            if (address < map.StartAddress)
                break;
            if (address < map.EndAddress)
                return map.ILOffset;
        }

        return null;
    }

    private static IReadOnlyList<SourcePoint> ReadSourcePoints(ClrModule module, int metadataToken)
    {
        var pdbPath = module.Pdb?.Path;
        if (string.IsNullOrWhiteSpace(pdbPath))
            pdbPath = Path.ChangeExtension(module.Name, ".pdb");
        if (!File.Exists(pdbPath))
            return [];

        try
        {
            using var stream = File.OpenRead(pdbPath);
            using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
            var reader = provider.GetMetadataReader();
            var handle = MetadataTokens.MethodDefinitionHandle(metadataToken);
            var debugInfo = reader.GetMethodDebugInformation(handle);
            var points = new List<SourcePoint>();
            foreach (var point in debugInfo.GetSequencePoints())
            {
                if (point.IsHidden)
                    continue;

                var document = point.Document.IsNil
                    ? "<unknown>"
                    : reader.GetString(reader.GetDocument(point.Document).Name);
                points.Add(new SourcePoint(point.Offset, document, point.StartLine));
            }

            return points;
        }
        catch (BadImageFormatException)
        {
            return [];
        }
        catch (ArgumentException)
        {
            return [];
        }
    }

    private static string FindSource(IReadOnlyList<SourcePoint> points, int ilOffset)
    {
        SourcePoint? selected = null;
        foreach (var point in points)
        {
            if (point.IlOffset > ilOffset)
                break;
            selected = point;
        }

        return selected is null ? "-" : $"{selected.Document}:{selected.Line}";
    }

    private static int? ExtractSourceLine(
        IReadOnlyList<SourcePoint> sourcePoints,
        int ilOffset
    )
    {
        SourcePoint? selected = null;
        foreach (var point in sourcePoints)
        {
            if (point.IlOffset > ilOffset)
                break;
            selected = point;
        }

        return selected?.Line;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine(
            "usage: VmLoopIlMap <pid|dump-path> [--type <full-name>] [--method <name>] [--output <path>]"
        );
        Console.Error.WriteLine(
            "       capture once: full IL/native map, per-instruction asm, line-map, arm summary"
        );
        Console.Error.WriteLine(
            "       VmLoopIlMap --from <report-file> [--source-map] [--summary] [--line 1200,1477] [--output <path>]"
        );
        Console.Error.WriteLine(
            "       offline views over a saved capture (no process attach, no repeated execution)"
        );
    }

    private sealed record Options(
        string Target,
        string TypeName,
        string MethodName,
        string? OutputPath
    );

    private sealed record SourcePoint(int IlOffset, string Document, int Line);

    private sealed record NativeInstruction(
        ulong Address,
        FlowControl FlowControl,
        bool HasMemoryOperand,
        int? SourceLine,
        int Length = 1,
        string Text = ""
    );
}
