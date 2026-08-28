using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
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
            var options = ParseOptions(args);
            var report = Inspect(options);

            if (options.OutputPath is null)
                Console.WriteLine(string.Join(Environment.NewLine, report));
            else
            {
                File.WriteAllLines(options.OutputPath, report);
                Console.WriteLine($"Saved: {options.OutputPath}");
            }

            return 0;
        }
        catch (Exception ex)
            when (ex is ArgumentException or InvalidOperationException or IOException)
        {
            Console.Error.WriteLine($"VmLoopIlMap: {ex.Message}");
            return 1;
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
        for (var i = 1; i < args.Length; i++)
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
            }
        }
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

    private static void PrintUsage()
    {
        Console.Error.WriteLine(
            "usage: VmLoopIlMap <pid|dump-path> [--type <full-name>] [--method <name>] [--output <path>]"
        );
        Console.Error.WriteLine(
            "       inspect a paused/snapshot .NET process and print CLRMD IL-to-native ranges"
        );
    }

    private sealed record Options(
        string Target,
        string TypeName,
        string MethodName,
        string? OutputPath
    );

    private sealed record SourcePoint(int IlOffset, string Document, int Line);
}
