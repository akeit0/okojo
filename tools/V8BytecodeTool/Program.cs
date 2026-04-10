using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Okojo.Bytecode;
using Okojo.Compiler;
using Okojo.Diagnostics;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace V8BytecodeTool;

internal class Program
{
    private static readonly Regex SourcePositionRegex = new(@"^\s*(\d+)\s+[SE]>", RegexOptions.Compiled);

    private static async Task Main(string[] args)
    {
        if (args.Length == 0 || args.Any(a => a is "--help" or "-h"))
        {
            PrintUsage();
            return;
        }

        var input = args[0];
        string? filter = null;
        var filterContains = false;
        string? grep = null;
        var showRaw = false;
        var listFunctions = false;
        var jsonOutput = false;
        string? savePath = null;
        var noInvoke = false;
        var compareOkojo = false;
        var compareOkojoNormalized = false;
        var normalizeOutput = false;
        var allBlocks = false;
        var esm = false;
        string? nodeArgsExtra = null;
        var eval = false;
        for (var i = 1; i < args.Length; i++)
            switch (args[i])
            {
                case "--filter" when i + 1 < args.Length:
                    filter = args[++i];
                    break;
                case "--filter-contains":
                    filterContains = true;
                    break;
                case "--grep" when i + 1 < args.Length:
                    grep = args[++i];
                    break;
                case "--raw":
                    showRaw = true;
                    break;
                case "--list":
                    listFunctions = true;
                    break;
                case "--json":
                    jsonOutput = true;
                    break;
                case "--save" when i + 1 < args.Length:
                    savePath = args[++i];
                    break;
                case "--no-invoke":
                    noInvoke = true;
                    break;
                case "--compare-okojo":
                    compareOkojo = true;
                    break;
                case "--compare-okojo-normalized":
                    compareOkojo = true;
                    compareOkojoNormalized = true;
                    break;
                case "--normalized":
                    normalizeOutput = true;
                    break;
                case "--all-blocks":
                    allBlocks = true;
                    break;
                case "--esm":
                    esm = true;
                    break;
                case "--node-args" when i + 1 < args.Length:
                    nodeArgsExtra = args[++i];
                    break;
                case "-e":
                    eval = true;
                    break;
            }


        var tempPath = Path.GetFullPath(esm ? "v8_bytecode_temp.mjs" : "v8_bytecode_temp.js");
        try
        {
            string sourceContent;
            if (File.Exists(input))
                sourceContent = File.ReadAllText(input, Encoding.UTF8);
            else
                sourceContent = input;

            if (eval)
            {
                using var okojo = JsRuntime.Create();
                Console.WriteLine(okojo.Evaluate(sourceContent));
                return;
            }


            var content = sourceContent;
            if (filter != null && !noInvoke)
                // V8/Node may lazily compile functions and omit bytecode unless they execute.
                // Always append a guarded call when filtering so the target is materialized.
                content += $"\n;if (typeof {filter} === 'function') {filter}();";

            File.WriteAllText(tempPath, content, Encoding.UTF8);

            var output = await GetV8Bytecode(tempPath, nodeArgsExtra);
            string finalOutput;

            List<BytecodeBlock>? parsedBlocks = null;
            if (string.IsNullOrWhiteSpace(output))
            {
                finalOutput = "(No output from Node)";
            }
            else if (showRaw)
            {
                finalOutput = output;
            }
            else
            {
                parsedBlocks = ParseBytecodeBlocks(output);
                var selectedBlocks = SelectBlocksForOutput(parsedBlocks, filter, filterContains, grep,
                    sourceContent.Length, allBlocks);
                finalOutput = jsonOutput
                    ? RenderJsonOutput(selectedBlocks, parsedBlocks.Count, filter, filterContains, grep, listFunctions)
                    : CleanBytecodeOutput(selectedBlocks, parsedBlocks.Count, filter, grep, listFunctions,
                        normalizeOutput);
            }

            if (compareOkojo && !showRaw && !jsonOutput)
            {
                var all = parsedBlocks ?? new List<BytecodeBlock>();
                var selectedForCompare =
                    SelectBlocksForOutput(all, filter, filterContains, grep, sourceContent.Length, allBlocks);
                finalOutput = TryRenderPairedComparison(selectedForCompare, sourceContent, filter, listFunctions,
                    compareOkojoNormalized);
            }

            if (!string.IsNullOrWhiteSpace(savePath))
            {
                var fullSavePath = Path.GetFullPath(savePath);
                var directory = Path.GetDirectoryName(fullSavePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                File.WriteAllText(fullSavePath, finalOutput, Encoding.UTF8);
                Console.Error.WriteLine($"Saved output to: {fullSavePath}");
            }

            Console.WriteLine(finalOutput);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            "Usage: V8BytecodeTool <js-file-or-string> [--filter <func-name>] [--filter-contains] [--grep <text>] [--raw] [--list] [--json] [--save <path>] [--no-invoke] [--all-blocks] [--normalized] [--esm] [--node-args \"<flags>\"] [--compare-okojo] [--compare-okojo-normalized] [-e] [--help]");
        Console.WriteLine("  <js-file-or-string>  File path or inline JavaScript source.");
        Console.WriteLine("  --filter <name>     Filter by function name (exact match by default).");
        Console.WriteLine("  --filter-contains   Make --filter use substring matching.");
        Console.WriteLine("  --grep <text>       Filter bytecode blocks by header/content text.");
        Console.WriteLine("  --raw               Print raw node --print-bytecode output.");
        Console.WriteLine("  --list              List discovered function names only.");
        Console.WriteLine("  --json              Emit structured JSON instead of text.");
        Console.WriteLine("  --save <path>       Save rendered output to a file.");
        Console.WriteLine("  --no-invoke         Do not append a guarded call for the filtered function.");
        Console.WriteLine("  --all-blocks        Disable the default user-code focus filter in unfiltered mode.");
        Console.WriteLine("  --normalized        Normalize opcode rendering for easier diffs.");
        Console.WriteLine("  --compare-okojo      Append Okojo disassembly for the same source.");
        Console.WriteLine("  --compare-okojo-normalized  Compare V8 and Okojo with normalized opcode sequences.");
        Console.WriteLine("  --esm               Treat input as ESM (temporary file uses .mjs).");
        Console.WriteLine("  --node-args <flags> Extra flags passed to node before the script path.");
        Console.WriteLine("  -e                  Evaluate with Okojo and print the result instead of using Node.");
    }

    private static async Task<string> GetV8Bytecode(string filePath, string? nodeArgsExtra)
    {
        var nodeArgs = "--print-bytecode";
        if (!string.IsNullOrWhiteSpace(nodeArgsExtra)) nodeArgs += " " + nodeArgsExtra.Trim();
        // Passing filter to node directly often suppresses too much if not called correctly.
        // We prefer to capture more and filter in C# for reliability.
        nodeArgs += $" \"{filePath}\"";

        var startInfo = new ProcessStartInfo
        {
            FileName = "node",
            Arguments = nodeArgs,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null) return "Failed to start node.";

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return await stdoutTask + await stderrTask;
    }

    private static List<BytecodeBlock> ParseBytecodeBlocks(string raw)
    {
        var lines = raw.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var currentBlock = new StringBuilder();
        var inBytecode = false;
        string? currentFunctionName = null;
        string? currentHeaderLine = null;

        List<BytecodeBlock> blocks = new();

        foreach (var line in lines)
        {
            if (line.Contains("[generated bytecode for function:"))
            {
                if (inBytecode && currentBlock.Length > 0)
                    blocks.Add(new(currentFunctionName ?? "", currentHeaderLine ?? "",
                        currentBlock.ToString()));

                currentBlock.Clear();
                inBytecode = true;
                currentHeaderLine = line;

                // Extract function name: [generated bytecode for function: name (0x...)] 
                // OR [generated bytecode for function: (0x...)]
                var match = Regex.Match(line, @"function: ([^ \(]+)?");
                currentFunctionName = match.Success && match.Groups[1].Success ? match.Groups[1].Value : "<anonymous>";
                currentBlock.AppendLine(line);
                continue;
            }

            if (inBytecode)
            {
                currentBlock.AppendLine(line);
                if (line.Contains("Source Position Table"))
                {
                    blocks.Add(new(currentFunctionName ?? "", currentHeaderLine ?? "",
                        currentBlock.ToString()));
                    currentBlock.Clear();
                    inBytecode = false;
                    currentHeaderLine = null;
                }
            }
        }

        if (inBytecode && currentBlock.Length > 0)
            blocks.Add(new(currentFunctionName ?? "", currentHeaderLine ?? "", currentBlock.ToString()));

        return blocks;
    }

    private static string CleanBytecodeOutput(List<BytecodeBlock> blocks, int totalBlocks, string? filter, string? grep,
        bool listOnly, bool normalized)
    {
        var result = new StringBuilder();

        if (listOnly)
        {
            foreach (var b in blocks.Select(x => x.Name).Distinct()) result.AppendLine(b);
            return result.ToString();
        }

        if (string.IsNullOrWhiteSpace(filter) && string.IsNullOrWhiteSpace(grep))
        {
            result.AppendLine($"(showing {blocks.Count}/{totalBlocks} blocks; use --all-blocks to show everything)");
            result.AppendLine();
        }

        foreach (var b in blocks)
        {
            result.AppendLine(new('=', 60));
            result.Append(normalized ? NormalizeV8BlockText(b.Content) : b.Content);
        }

        if (blocks.Count == 0 && !string.IsNullOrWhiteSpace(filter))
            return $"(No bytecode found matching function: {filter})";
        if (blocks.Count == 0 && !string.IsNullOrWhiteSpace(grep))
            return $"(No bytecode found matching text: {grep})";

        return result.ToString();
    }

    private static List<BytecodeBlock> SelectBlocks(List<BytecodeBlock> blocks, string filter, bool containsMode)
    {
        var exact = blocks.Where(b => b.Name.Equals(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count > 0 || !containsMode) return exact;

        return blocks.Where(b => b.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static string RenderJsonOutput(List<BytecodeBlock> blocks, string? filter, bool listOnly)
    {
        object payload;
        if (listOnly)
        {
            payload = new
            {
                mode = "list",
                functions = blocks.Select(b => b.Name).Distinct().ToArray()
            };
        }
        else if (!string.IsNullOrWhiteSpace(filter))
        {
            var matched = SelectBlocks(blocks, filter, true);
            payload = new
            {
                mode = "filtered",
                filter,
                count = matched.Count,
                blocks = matched.Select(b => new
                {
                    name = b.Name,
                    header = b.HeaderLine,
                    content = b.Content
                }).ToArray()
            };
        }
        else
        {
            payload = new
            {
                mode = "all",
                count = blocks.Count,
                blocks = blocks.Select(b => new
                {
                    name = b.Name,
                    header = b.HeaderLine,
                    content = b.Content
                }).ToArray()
            };
        }

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static string RenderJsonOutput(List<BytecodeBlock> blocks, int totalBlocks, string? filter,
        bool filterContains,
        string? grep, bool listOnly)
    {
        object payload;
        if (listOnly)
            payload = new
            {
                mode = "list",
                selectedCount = blocks.Count,
                totalCount = totalBlocks,
                functions = blocks.Select(b => b.Name).Distinct().ToArray()
            };
        else
            payload = new
            {
                mode = "blocks",
                filter,
                filterContains,
                grep,
                selectedCount = blocks.Count,
                totalCount = totalBlocks,
                blocks = blocks.Select(b => new
                {
                    name = b.Name,
                    header = b.HeaderLine,
                    content = b.Content
                }).ToArray()
            };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static List<BytecodeBlock> SelectBlocksForOutput(List<BytecodeBlock> blocks, string? filter,
        bool filterContains,
        string? grep, int sourceLength, bool allBlocks)
    {
        IEnumerable<BytecodeBlock> selected = blocks;

        if (!string.IsNullOrWhiteSpace(filter))
            selected = SelectBlocks(selected.ToList(), filter, filterContains);

        if (!string.IsNullOrWhiteSpace(grep))
            selected = selected.Where(b => BlockContainsText(b, grep!));

        var selectedList = selected.ToList();
        if (allBlocks)
            return selectedList;

        {
            var focused = SelectLikelyUserBlocks(selectedList, sourceLength);
            if (focused.Count > 0) return focused;
        }

        return selectedList;
    }

    private static List<BytecodeBlock> SelectLikelyUserBlocks(List<BytecodeBlock> blocks, int sourceLength)
    {
        var maxExpectedPosition = Math.Max(64, sourceLength + 32);
        var filtered = blocks.Where(b => IsLikelyUserBlock(b, maxExpectedPosition)).ToList();
        if (filtered.Count > 0) return filtered;

        return blocks.Where(b =>
                !(string.IsNullOrEmpty(b.Name) || b.Name.Contains("validate", StringComparison.OrdinalIgnoreCase) ||
                  b.Name.Contains("internal", StringComparison.OrdinalIgnoreCase) || b.Name.Length > 80))
            .ToList();
    }

    private static bool IsLikelyUserBlock(BytecodeBlock block, int maxExpectedPosition)
    {
        if (ContainsInternalMarker(block.Content)) return false;

        var positions = ExtractSourcePositions(block.Content);
        if (positions.Count == 0) return false;

        var min = int.MaxValue;
        var max = int.MinValue;
        foreach (var pos in positions)
        {
            if (pos < min) min = pos;
            if (pos > max) max = pos;
        }

        return min >= 0 && max <= maxExpectedPosition;
    }

    private static bool ContainsInternalMarker(string content)
    {
        return content.Contains("internal/", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("internal\\", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("node:internal", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("bootstrap", StringComparison.OrdinalIgnoreCase);
    }

    private static List<int> ExtractSourcePositions(string content)
    {
        var list = new List<int>(32);
        foreach (var line in content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
        {
            var match = SourcePositionRegex.Match(line);
            if (!match.Success) continue;
            if (int.TryParse(match.Groups[1].Value, out var value)) list.Add(value);
        }

        return list;
    }

    private static bool BlockContainsText(BytecodeBlock block, string text)
    {
        return block.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
               block.HeaderLine.Contains(text, StringComparison.OrdinalIgnoreCase) ||
               block.Content.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private static List<(string name, JsScript script)> CollectOkojoFunctions(JsScript root)
    {
        var result = new List<(string name, JsScript script)>();
        var seen = new HashSet<JsScript>();

        void Walk(JsScript script, string name)
        {
            if (!seen.Add(script)) return;
            result.Add((name, script));

            foreach (var obj in script.ObjectConstants)
                if (obj is JsBytecodeFunction fn)
                    Walk(fn.Script, fn.Name ?? "<anonymous>");
        }

        Walk(root, "<script>");
        return result;
    }

    private static string TryRenderPairedComparison(List<BytecodeBlock> allV8Blocks, string source, string? filter,
        bool listOnly, bool normalized)
    {
        try
        {
            string? okojoError = null;
            List<OkojoDumpBlock> okojoBlocks;
            try
            {
                okojoBlocks = BuildOkojoDumpBlocks(source);
            }
            catch (Exception ex)
            {
                okojoBlocks = new();
                okojoError = $"{ex.GetType().Name}: {ex.Message}";
            }

            if (listOnly)
            {
                var v8Names = SelectV8ForCompare(allV8Blocks, filter).Select(b => b.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                var okojoNames = SelectOkojoForCompare(okojoBlocks, filter).Select(b => b.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                var sbList = new StringBuilder();
                sbList.AppendLine("V8:");
                foreach (var n in v8Names) sbList.AppendLine(n);
                sbList.AppendLine("Okojo:");
                foreach (var n in okojoNames) sbList.AppendLine(n);
                if (!string.IsNullOrWhiteSpace(okojoError))
                {
                    sbList.AppendLine("Okojo compile error:");
                    sbList.AppendLine(okojoError);
                }

                return sbList.ToString();
            }

            var v8Selected = SelectV8ForCompare(allV8Blocks, filter);
            var okojoSelected = SelectOkojoForCompare(okojoBlocks, filter);
            return RenderPairedComparisonOutput(v8Selected, okojoSelected, normalized, okojoError);
        }
        catch (Exception ex)
        {
            return $"(Paired compare failed: {ex.GetType().Name}: {ex.Message})";
        }
    }

    private static List<OkojoDumpBlock> BuildOkojoDumpBlocks(string source)
    {
        var program = JavaScriptParser.ParseScript(source);
        var defaultRealm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(defaultRealm);
        var script = compiler.Compile(program);
        var functions = CollectOkojoFunctions(script);

        var result = new List<OkojoDumpBlock>(functions.Count);
        foreach (var (name, okojoScript) in functions)
        {
            var content = Disassembler.Dump(okojoScript, new()
            {
                UnitKind = name == "<script>" ? "script" : "function",
                UnitName = name
            });
            result.Add(new(name, content, ExtractOkojoOpcodes(content)));
        }

        return result;
    }

    private static List<BytecodeBlock> SelectV8ForCompare(List<BytecodeBlock> blocks, string? filter)
    {
        if (!string.IsNullOrWhiteSpace(filter)) return SelectBlocks(blocks, filter, true);

        return blocks.Where(b =>
                !(string.IsNullOrEmpty(b.Name) || b.Name.Contains("validate") || b.Name.Contains("internal") ||
                  b.Name.Length > 50))
            .ToList();
    }

    private static List<OkojoDumpBlock> SelectOkojoForCompare(List<OkojoDumpBlock> blocks, string? filter)
    {
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var exact = blocks.Where(b => b.Name.Equals(filter, StringComparison.OrdinalIgnoreCase)).ToList();
            return exact;
        }

        return blocks;
    }

    private static string RenderPairedComparisonOutput(List<BytecodeBlock> v8Blocks, List<OkojoDumpBlock> okojoBlocks,
        bool normalized, string? okojoError = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("##############################");
        sb.AppendLine("## V8 <-> Okojo Compare");
        sb.AppendLine("##############################");
        if (!string.IsNullOrWhiteSpace(okojoError)) sb.AppendLine($"## Okojo compile error: {okojoError}");

        var v8Groups = v8Blocks.GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var okojoGroups = okojoBlocks.GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var orderedNames = v8Blocks.Select(b => b.Name)
            .Concat(okojoBlocks.Select(b => b.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var name in orderedNames)
        {
            v8Groups.TryGetValue(name, out var v8List);
            okojoGroups.TryGetValue(name, out var okojoList);
            var pairCount = Math.Max(v8List?.Count ?? 0, okojoList?.Count ?? 0);
            if (pairCount == 0) continue;

            for (var i = 0; i < pairCount; i++)
            {
                var v8 = i < (v8List?.Count ?? 0) ? v8List![i] : null;
                var okojo = i < (okojoList?.Count ?? 0) ? okojoList![i] : null;

                sb.AppendLine(new('=', 80));
                sb.AppendLine($"Function: {name}  (pair {i + 1})");

                if (normalized)
                {
                    sb.AppendLine("-- V8 Opcodes (normalized) --");
                    sb.AppendLine(v8 is null ? "(missing)" : string.Join(" -> ", ExtractV8Opcodes(v8.Content)));
                    sb.AppendLine("-- Okojo Opcodes (normalized) --");
                    sb.AppendLine(okojo is null ? "(missing)" : string.Join(" -> ", okojo.Opcodes));
                }

                sb.AppendLine("-- V8 Ignition --");
                sb.AppendLine(v8 is null
                    ? "(missing)"
                    : normalized
                        ? NormalizeV8BlockText(v8.Content).TrimEnd()
                        : v8.Content.TrimEnd());
                sb.AppendLine("-- Okojo Disasm --");
                sb.AppendLine(okojo is null
                    ? "(missing)"
                    : normalized
                        ? NormalizeOkojoDisasmText(okojo.Content).TrimEnd()
                        : okojo.Content.TrimEnd());
            }
        }

        return sb.ToString();
    }

    private static IReadOnlyList<string> ExtractV8Opcodes(string content)
    {
        var list = new List<string>();
        foreach (var line in content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
        {
            // Example: "... : 0d 03             LdaSmi [3]"
            var m = Regex.Match(line, @":\s+[0-9a-fA-F ]+\s{2,}([A-Za-z][A-Za-z0-9]*)\b");
            if (m.Success) list.Add(m.Groups[1].Value);
        }

        return list;
    }

    private static IReadOnlyList<string> ExtractOkojoOpcodes(string disasm)
    {
        var list = new List<string>();
        foreach (var line in disasm.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
        {
            var m = Regex.Match(line, @"^\d{4}\s+([A-Za-z][A-Za-z0-9]*)\b");
            if (m.Success) list.Add(m.Groups[1].Value);
        }

        return list;
    }

    private static string NormalizeV8BlockText(string content)
    {
        var sb = new StringBuilder();
        foreach (var line in content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                sb.AppendLine();
                continue;
            }

            // Normalize generated-function header by removing unstable addresses.
            if (line.StartsWith("[generated bytecode for function:", StringComparison.Ordinal))
            {
                var normalizedHeader = Regex.Replace(line, @"0x[0-9a-fA-F]+", "0xADDR");
                sb.AppendLine(normalizedHeader);
                continue;
            }

            // Instruction line example:
            // "   63 S> 0000015E4098A190 @    0 : 18 02             LdaCurrentContextSlot [2]"
            var ins = Regex.Match(line, @"@\s*(\d+)\s*:\s*[0-9a-fA-F ]+\s{2,}(.*)$");
            if (ins.Success)
            {
                var pc = int.Parse(ins.Groups[1].Value);
                var tail = ins.Groups[2].Value.TrimEnd();
                sb.AppendLine($"{pc:D4}  {tail}");
                continue;
            }

            // Constant-pool/object lines often contain addresses; normalize them lightly.
            var generic = Regex.Replace(line, @"0x[0-9a-fA-F]+", "0xADDR");
            sb.AppendLine(generic);
        }

        return sb.ToString();
    }

    private static string NormalizeOkojoDisasmText(string content)
    {
        // Okojo disasm is already stable-ish; normalize only repeated spacing and trim.
        var sb = new StringBuilder();
        foreach (var line in content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
            sb.AppendLine(line.TrimEnd());

        return sb.ToString();
    }

    private sealed record BytecodeBlock(string Name, string HeaderLine, string Content);

    private sealed record OkojoDumpBlock(string Name, string Content, IReadOnlyList<string> Opcodes);
}
