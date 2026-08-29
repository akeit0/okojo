# Okojo frontend allocation profile (2026-08-30)

## Scope

This note records how parser/compiler time, allocation, and retained memory were
attributed before changing the frontend. The acceptance constraint is unchanged
emitted bytecode and observable metadata unless an opcode improvement is called
out separately.

The two representative inputs are:

- the closure-heavy default source embedded in `tools/CompilerAllocProbe`;
- `benchmarks/Okojo.Benchmarks/scripts/linq-js.js`, a 34 KB source containing
  461 function literals and emitting 462 `JsScript` instances, including the
  root, from one source compilation.

## Measurement method

`CompilerAllocProbe` runs parse, binding collection, storage planning, and
preparsed full compilation in separate loops. It measures allocation with
`GC.GetAllocatedBytesForCurrentThread()` and elapsed time with
`Stopwatch.GetTimestamp()`. Each production-shaped compilation creates a fresh
`JsScriptCompiler`; explicit collections occur only between phase groups.

Typical bounded commands are:

```powershell
dotnet build tools/CompilerAllocProbe/CompilerAllocProbe.csproj -c Release
dotnet run --project tools/CompilerAllocProbe/CompilerAllocProbe.csproj `
  -c Release --no-build -- --warmup 100 --samples 1000
dotnet run --project tools/CompilerAllocProbe/CompilerAllocProbe.csproj `
  -c Release --no-build -- benchmarks/Okojo.Benchmarks/scripts/linq-js.js `
  --warmup 10 --samples 30
```

Short runs are repeated in fresh processes and compared by median. Only the
summary lines are retained; verbose benchmark progress is not useful evidence.
Allocation is the primary gate for small materialization changes because their
timing delta can be below machine noise.

The emitted-unit count and opcode sequence are checked with
`tools/OkojoBytecodeTool`. Snapshots are written only below the ignored
`artifacts/okojobytecodetool/snapshots/<timestamp>` tree. General and focused
cases compare script count, register count, constants, and opcode/operand
sequence before a candidate is accepted.

CPU attribution used a sampled .NET trace as directional evidence. A trace
captured around `CompilerAllocProbe` over-represented `Thread.PollGC` because the
probe intentionally forces collections between phase groups, and allocation
tracing itself perturbs GC. Consequently, sampled percentages below are not
treated as additive or as an acceptance gate; phase stopwatch/allocation medians
are more reliable.

## Findings

### Phase scale

| corpus / phase | parse | collect | plan | preparsed full compile |
|---|---:|---:|---:|---:|
| closures allocation | 2.30 KB | 0.22 KB | 0.06 KB | 20.01 KB |
| closures time | 17.2 us | 2.7 us | 2.5 us | 103.8 us |
| linq-js allocation | 42.83 KB | 63.60 KB | 0.06 KB | 1,957.90 KB |
| linq-js time | 2.73 ms | 0.10 ms | 0.07 ms | 3.84 ms |

Parser allocation is already small relative to compiled output. Binding
collection and planning are not the main time source, even for linq-js.

### 1. Eager nested-function compilation

`JsCompilerBase.EmitDeclarationPrologue` reaches
`JsFunctionCompiler.CompileFunctionCore` for every nested function. Each nested
unit repeats binding work, planning, emission, final-array construction, and
agent registration immediately. The closure CPU sample attributed roughly 35%
inclusive time to this declaration/function-compiler path. linq-js multiplies
the path across 462 units.

This is the largest structural opportunity, but it is not a local emitter
change. A correct lazy boundary must retain function source ranges, strictness,
parameter metadata, outer-scope capture requirements, debugger/source metadata,
and module/eval behavior until first use.

V8 reference inspected at `C:/Users/akito/RiderProjects/MQuickJs/v8`:

- `src/parsing/parser.cc` decides whether a function can be lazy, preparses it,
  and falls back to an eager full parse when the preparser cannot identify an
  error precisely;
- `src/parsing/preparse-data.h` records variable-use/assignment/context facts
  needed to allocate scopes when the lazy function is later compiled;
- `src/parsing/parse-info.*` carries the lazy/eager compilation state.

Okojo should copy the separation of syntax validation, retained scope facts,
and first-use compilation. It should not copy V8's Zone-owned AST representation;
Okojo's flat pooled AST and index-based compiler remain the suitable local
representation.

### 2. Output materialization and copying

`BytecodeBuilder.ToScript()` materializes bytecode, constants, feedback tables,
switch tables, source/debug maps, debug-name tables, and local metadata for each
unit. `Array.Copy` had substantial inclusive sampled activity. Most arrays are
durable output and cannot simply be pooled or returned. Improvements must first
distinguish exact-size durable arrays from temporary sorting/interning storage.

### 3. Redundant `JsScript` record cloning

The script, function, and module compilers previously finalized with
`builder.ToScript() with { ... }`. This constructs a complete `JsScript`, then
uses the record clone path to make a second object solely to attach source,
function-source, and top-level lexical metadata. `Object.MemberwiseClone` was
about 7% exclusive in one directional CPU sample. The allocation saving is one
record object per emitted unit and is deterministic even when the timing saving
is noisy.

The safe local change is to pass metadata into an internal `ToScript` path and
construct the final record once. Public `BytecodeBuilder.ToScript()` behavior and
the immutable output arrays remain unchanged.

Five fresh-process runs after that change reported an exact allocation drop on
every run: 20.07 to 18.58 KB/op for the closure corpus (-7.4%), and 1,966.94 to
1,855.01 KB/op for linq-js (-5.7%). Five longer closure runs (500 warmups, 3,000
samples) also favored the candidate in elapsed time, but no timing percentage is
claimed because shorter tiering-sensitive runs disagreed. Thirty-four general
case disassemblies and the complete 462-unit linq-js disassembly were byte-for-byte
identical between `fa18f68` and the candidate.

### 4. Script registry lifetime explains GB-scale growth

At the start of the investigation, `JsAgent` owned strong `HashSet<JsScript>`
collections for all scripts and for source-path lookup. `JsScript.BindAgent`
registers the script, and `JsAgent.RegisterScriptRecursive` also registers every
nested function script. No unregister/reset path existed. Repeated same-realm
linq-js compilation therefore retained 462 script instances per operation.

The initial investigation incorrectly reported 37 units by counting the output
lines from `OkojoBytecodeTool --list`. That mode intentionally listed distinct
function names, not script instances; linq-js has 37 distinct names across its
462 units. The tool now prints both counts in its header, and
`CompilerAllocProbe` independently walks each output graph using reference
identity. This correction explains the retained-memory growth more strongly and
also explains why removing one record clone per unit saved about 112 KB per
linq-js compilation.

This is durable debugger/breakpoint state, not parser scratch memory. A compiler
benchmark using one realm measures both allocated output and an ever-growing
registry. Merely creating a fresh compiler does not bound the agent lifetime.
The probe first addressed measurement safety by rotating runtimes every
`--runtime-batch` operations (25 by default), seeding compile pools outside the
measured region, and forcing GC outside timed intervals. It reports produced
units per batch and live registered units after GC separately.

The production registry now uses `ConditionalWeakTable` identity tables for the
agent-wide and per-source-path indexes. Live functions and scripts continue to
own their `JsScript`; active breakpoint patches also retain patched scripts until
the breakpoint is disposed. Unreferenced compile/eval results can be collected,
and empty source-path tables are compacted after collections. Focused tests cover
both reclamation and breakpoint retention.

With 100 measured linq-js compilations in one runtime, the strong-registry
baseline retained all 46,662 produced units after forced GC and peaked at 121.4
MiB. The weak registry retained zero unreachable units and peaked at 69.2 MiB.
Across three 10-operation batches it also reduced compilation from 1,827.35 to
1,815.16 KB/op (-0.7%); in the 100-operation batch the result was 1,826.69 to
1,797.34 KB/op (-1.6%). Timing varied with tiering and GC placement, so the
lifetime and allocation results, not a timing percentage, are the acceptance
gates.

### Shared source/debug ownership

Every eagerly compiled unit previously allocated a separate `SourceCode` wrapper
around the same source string and path. Its line-start table is lazy, but if
debugging requested locations from many nested units each wrapper could build a
separate full-source line index. The compiler tree now creates one `SourceCode`
per source compilation and shares it with the root, nested functions, and async
module wrapper.

Five bounded runs reported 18.79 to 18.64 KB/op for the closure corpus (-0.8%)
and 1,841.75 to 1,827.35 KB/op for linq-js (-0.8%). The linq-js delta is about
14.4 KB, matching 461 avoided wrappers. Timing was neutral within run noise.
Thirty-four general disassemblies and the complete 462-unit linq-js disassembly
remained byte-for-byte identical. A focused test also verifies reference-shared
source metadata across nested units.

### Compiler scope-stack reuse

Each compiler unit previously allocated two `Stack<T>` instances for active
lexical scopes and control-flow scopes. Eager compilation made this a fixed cost
per emitted unit: linq-js allocated 924 stacks before considering their backing
arrays. These collections are temporary compiler state, so they can use the
existing per-realm compile collection pool and be cleared when a unit finishes.
The module compiler now follows the same finalization path, while preserving its
planned bindings until `CompileForExecution` has materialized module cells and
hoisted-function metadata.

Five interleaved fresh-process A/B runs against `92341bd` reported 18.91 to
17.41 KB/op for the closure corpus (-7.9%) and 1,815.16 to 1,721.17 KB/op for
linq-js (-5.2%, about 94 KB per source compile). Median elapsed time was 79.7 to
80.3 us/op for closures and 2.911 to 2.973 ms/op for linq-js. The latter is a
2.1% unfavorable result, so this change is accepted as an allocation/GC-pressure
improvement and not claimed as a CPU-speed improvement. The complete 462-unit
linq-js disassembly remained byte-for-byte identical.

### 5. Identifier scanning/interning

The parser's main repeated work is `JsLexer.ReadIdentifier` followed by
`JsIdentifierTable.AddIdentifierLiteral(ReadOnlySpan<char>)`. Span lookup avoids
temporary strings for repeated identifiers; one string is retained for each
unique identifier. A direct substring-scanner experiment reduced allocation but
slowed string-heavy parsing by about 20%, so it was rejected and removed.

## Prioritized work and gates

1. Construct each `JsScript` once. Gate on metadata tests, identical bytecode,
   deterministic allocation reduction, focused/full tests, and build warnings.
2. Reduce avoidable finalization copies only where ownership can transfer safely;
   durable output arrays must remain exact and immutable to compiler pooling.
3. Reuse temporary compiler collections when the retained capacity is bounded;
   measure lookup/clearing overhead as well as allocation.
4. Prototype lazy nested functions behind an internal boundary. First establish
   source/scope metadata and first-use compile semantics; then measure startup,
   first call, repeated call, retained memory, and debugger behavior separately.

Rejected attempts and measurement caveats stay in this note so later work does
not repeat them.
