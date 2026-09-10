# Code/instance split implementation

The source implements the core ownership split described in
`docs/proposals/OKOJO_CODE_INSTANCE_SPLIT_DESIGN.md` (migration steps 1–6).

**Validation status: not build-verified.** No .NET SDK is installed in the delivery
container. CSharpier, C# compilation, Okojo tests, Test262, Okojo bytecode snapshots
and benchmarks have not run. This is a review candidate, not a certified green
baseline. The original repository's historical test results do not certify these
changes.

## Read first

`docs/implementation/CODE_INSTANCE_SPLIT_IMPLEMENTATION.md` explains ownership,
link/cache lifetime, hot-path choices, breakpoint cursor rebasing, API migration,
test coverage, performance risks and deferred work.

## Entry points

```csharp
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;

var unit = JsCompiler.CompileUnit("function add(a, b) { return a + b; } add(20, 22);");
using var runtime = JsRuntime.Create();
var realm = runtime.DefaultRealm;
realm.Execute(unit.Link(realm));
// Expected: realm.Accumulator.NumberValue == 42.
```

Reuse `unit` for another realm/agent by linking it there. Repeated links to one
realm reuse its feedback instance. Closures remain fresh objects with local
captures; do not move live closures between realms. Use separate agents for
parallel execution.

## Validation

Requires Python 3.10+ and .NET 10. From this directory:

```text
python tools/CodeInstanceSplitValidation/validate.py --test262-root /path/to/test262/test --benchmarks
```

Without `--test262-root` or `--benchmarks`, the driver explicitly reports those
gates as not run. It stops on the first command failure. Review build warnings
and Test262 result summaries as well as process exit codes.

The new regression suite has 22 cases across 18 methods. Existing ABI, compiler,
module, closure, generator, debugger and tool consumers were migrated. Six
JavaScript expectation checks passed on local Node/V8; that is reference behavior
only. `tools/CodeInstanceSplitValidation` contains the actual reference output,
V8 bytecode sample, narrow source-structure audit and missing-SDK evidence.

## Deliberately deferred

Nested function bodies and linked instances are still eager. Lazy compilation
needs owned binding/syntax summaries and a publication policy. Portable module
bodies do not replace the loader's import/export binding plans. Persistent
serialization/private-ID relocation and measured baseline comparisons remain
separate work; see `TODO.md`.
