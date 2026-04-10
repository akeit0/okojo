# V8 Bytecode Tool

Small CLI utility that runs `node --print-bytecode`, extracts bytecode blocks, and makes them easier to inspect when
designing the Okojo VM/compiler.

This is a reference tool, not an oracle. Use it to study lowering patterns and opcode shapes, then validate semantics
with Test262.

## Why This Exists

V8 Ignition is a production register-based interpreter. Studying its bytecode helps with:

- opcode naming and grouping
- accumulator/register usage patterns
- compiler lowering strategy (loads/stores/calls/closures)
- identifying when Okojo should copy a pattern vs simplify for maintainability

## Prerequisites

- `node` in `PATH`
- `.NET 10 SDK`

## Usage

From repo root:

```bash
dotnet run --project tools/V8BytecodeTool/V8BytecodeTool.csproj -- "function test() { return 1 + 2; }" --filter test
```

## CLI Arguments

- First argument: JavaScript source string or path to a `.js` file
- `--filter <name>`: show bytecode blocks whose function name matches/contains `<name>`
- `--filter-contains`: make `--filter` use substring matching (default is exact only)
- `--grep <text>`: show blocks whose header/content contains `<text>` (not limited to function names)
- `--list`: list discovered function names only
- `--raw`: print raw Node output (stdout + stderr)
- `--json`: emit structured JSON for tool integration
- `--save <path>`: write the rendered output (raw/text/json) to a file
- `--no-invoke`: do not auto-call the filtered function (may result in missing bytecode if V8 does not compile it)
- `--all-blocks`: disable the default user-code focus filter in unfiltered mode
- `--normalized`: normalize V8 block rendering for easier diffs/readability
- `--esm`: treat input as ESM (`.mjs`) for `import`/`export` snippets
- `--node-args "<flags>"`: pass extra flags to node before the script path
- `--compare-okojo`: also compile with Okojo and append Okojo disassembly for quick comparison
- `--compare-okojo-normalized`: compare V8 vs Okojo with paired function blocks plus normalized opcode sequences
- `-e`: evaluate the input with Okojo and print the result instead of running `node --print-bytecode`

## Examples

List functions in a file:

```bash
dotnet run --project tools/V8BytecodeTool/V8BytecodeTool.csproj -- samples/foo.js --list
```

Inspect a single function:

```bash
dotnet run --project tools/V8BytecodeTool/V8BytecodeTool.csproj -- samples/foo.js --filter parseExpr
```

Use broad substring function-name match (noisier):

```bash
dotnet run --project tools/V8BytecodeTool/V8BytecodeTool.csproj -- samples/foo.js --filter run --filter-contains
```

Inspect blocks by content text (useful when function names are anonymous/noisy):

```bash
dotnet run --project tools/V8BytecodeTool/V8BytecodeTool.csproj -- samples/foo.js --grep import
```

Normalize V8 output without Okojo compare:

```bash
dotnet run --project tools/V8BytecodeTool/V8BytecodeTool.csproj -- samples/foo.js --normalized
```

Pass extra Node/V8 flags through:

```bash
dotnet run --project tools/V8BytecodeTool/V8BytecodeTool.csproj -- samples/foo.js --node-args "--trace-warnings"
```

Inspect raw V8 output for debugging parser changes in this tool:

```bash
dotnet run --project tools/V8BytecodeTool/V8BytecodeTool.csproj -- samples/foo.js --raw
```

Save filtered output to a file:

```bash
dotnet run --project tools/V8BytecodeTool/V8BytecodeTool.csproj -- samples/foo.js --filter parseExpr --save artifacts/v8/parseExpr.txt
```

Emit JSON for scripting/integration:

```bash
dotnet run --project tools/V8BytecodeTool/V8BytecodeTool.csproj -- samples/foo.js --filter parseExpr --json --save artifacts/v8/parseExpr.json
```

Disable auto-invocation (side-effect-safe inspection attempt):

```bash
dotnet run --project tools/V8BytecodeTool/V8BytecodeTool.csproj -- samples/foo.js --filter parseExpr --no-invoke
```

Compare V8 Ignition output with Okojo disassembly for the same snippet:

```bash
dotnet run --project tools/V8BytecodeTool/V8BytecodeTool.csproj -- "function makeCounter(){ let count = 0; return function(){ count = count + 1; return count; }; } let c = makeCounter(); c();" --filter makeCounter --compare-okojo
```

Compare with normalized opcode sequences (easier lowering comparison):

```bash
dotnet run --project tools/V8BytecodeTool/V8BytecodeTool.csproj -- "function test(){ return 1 + 2; } test();" --filter test --compare-okojo-normalized
```

Show all V8 blocks instead of the default user-code-focused subset:

```bash
dotnet run --project tools/V8BytecodeTool/V8BytecodeTool.csproj -- samples/foo.js --all-blocks
```

Quick Okojo eval without Node bytecode output:

```bash
dotnet run --project tools/V8BytecodeTool/V8BytecodeTool.csproj -- "1 + 2" -e
```

## Recommended Workflow (Okojo)

1. Write a small JS snippet that isolates one construct (`let`, closure, call, property load, loop, etc.).
2. Inspect V8 bytecode with this tool (`--list`, then `--filter`).
3. Record the expected high-level lowering in Okojo docs/tests.
4. Implement or adjust Okojo compiler emission.
5. Validate behavior with targeted tests and Test262 categories.

Do not chase exact opcode parity with V8. Chase semantic correctness and a stable, optimizable internal model.

## Output Notes / Limitations

- Function name extraction is heuristic and based on current V8 text formatting.
- V8 output format can change across Node versions.
- `--filter` is applied in C# after collecting blocks (intentional; more reliable for ad-hoc snippets).
- `--filter` now defaults to exact function name match to avoid accidental huge matches.
- Unfiltered mode now applies a default "user-code focus" filter to reduce Node/bootstrap noise; use `--all-blocks` for
  full output.
- By default, `--filter` auto-appends a guarded call to encourage V8 to compile and print the target function.
- Use `--no-invoke` if running the function would have side effects.
- `--compare-okojo` uses the current Okojo parser/compiler/disassembler and may fail on unsupported syntax even if V8
  output succeeds.
- Compare modes pair functions by name and may show unmatched functions on one side.
- If compare output looks stale, rebuild `tools/V8BytecodeTool` explicitly before trusting the paired Okojo side.
- Tool output is for developer inspection and may not be stable enough yet for golden tests.

## Improvement Plan (Tooling Roadmap)

### Near Term

- [x] Add `--save <file>` to write rendered output to disk
- [x] Add `--json` structured output (function name, header, content)
- Add line-number/source mapping extraction when available
- Improve parsing robustness across Node versions

### Mid Term

- Add richer normalized compare output (operand normalization / categories)
- Add corpus mode (`--dir <path>`) to run a set of snippets and store outputs
- Add normalized diff mode for V8 vs Okojo opcode/lowering comparison
- Add diff mode (`--compare <a.js> <b.js>`) for lowering experiments
- Add normalization options to reduce noisy addresses/ids in output

### Long Term

- Integrate with Okojo docs/test assets (reference snippets + recorded observations)
- Generate a small opcode-pattern cookbook for common JS constructs
- Support automated checks that ensure the tool parser still works after Node upgrades

## Relationship to Correctness/Performance Work

- Use this tool to improve compiler design and observability.
- Use Test262 harness to prove correctness.
- Use benchmarks/profiling to prove performance.

All three are required; none is sufficient alone.
