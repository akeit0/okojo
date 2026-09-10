# First-batch debug fixes: line starts, synthetic locals, dormant optimizer

Status: implemented (2026-09-10).
Scope for this iteration: three small, independent, review-flagged fixes
(review §§6C, 3B). No behavior change beyond the defects themselves; the full
debug-info redesign stays in `debug-info-redesign.md`.

## Fix A. Line starts recognize all spec line terminators

`SourceCode.GetOrCreateLineStarts` (`Parsing/SourceCode.cs`) counts only
`\n`. It must also handle CR, CRLF as a single terminator, U+2028, and
U+2029 (spec lexical grammar). The lexer already treats all of these as
terminators (`JsLexer.cs`, `IsLineTerminator`); only the source-location
index is defective, so reported line/column for code after such terminators
is wrong while parsing itself is right.

Repro (line/column query, not parsing):

```text
source: "a = 1;\r\nb = 2;\u2028c = 3;\rd = 4;"
offset of `b` → line 2; offset of `c` → line 3; offset of `d` → line 4.
```

Today: `b` reports line 2 only by accident of `\n` in CRLF; `c` and `d`
report wrong lines (their terminators are invisible to the index).

Planned tests: unit tests on `GetOrCreateLineStarts`/`GetLineColumn` over a
mixed-terminator source (CR-only, CRLF singles, lone CR, U+2028, U+2029);
existing debugger line-mapping tests keep passing unchanged.

## Fix B. Synthetic-local flag replaces `$` filtering

`EmitLocalDebugInfos` (`Compiler/JsCompilerBase.cs`) drops every binding
whose name starts with `$`, hiding legitimate user variables such as
`$value`. The genuinely synthetic bindings (parser-generated
`$rest_pattern_` / `$param_pattern_` / `$arrow_pattern_` parameter temps,
all carrying `JsFormalParameterBindingKind.Pattern` / `RestPattern`) get an
explicit `IsSynthetic` flag threaded collected → planned → storage, and the
emission filter uses the flag instead of the spelling. The `#` exclusion
(private-name mangling) is left untouched — out of scope for this slice.

Repro:

```js
function t() { let $value = 41; debugger; return $value; }
t();
```

Today the debugger checkpoint exposes no `$value`; after the fix
`TryGetLocalValue("$value")` returns 41.

Planned tests: debugger-checkpoint regression test for `$value` visibility
and value; companion test asserting no `$`-named synthetic temp leaks from a
destructuring-parameter function (pins the hiding direction too).

## Fix C. Delete dormant `OptimizeBytecode()`

`BytecodeBuilder.OptimizeBytecode()` has no call sites; its decoder copies
per instruction and its rewrite pass is untested dead code. Delete it and
the now-unused helpers (`DecodeInstructions`,
`CollectProtectedInstructionTargets`, `ElideDeadPureAccumulatorLoads`,
`RewriteBytecode`, `TryGetRelativeTargetPc`, the `MapPcTo*` remappers,
`RemapPc*InPlace`, `DecodedInstruction`, `PcRemapDirection`). Keep
`CopySortedIntMap` / `BuildSortedDebugNameTable` (live finalization) and the
`BytecodeInfo` load predicates (live emit-time peephole). Never "just enable"
it — the replacement is the layout stage in `bytecode-isa-layout.md`.

Planned tests: none new; full suite must stay green (deletion only).

## Reference observations

- V8/Node: no behavior references — fixes are internal index/filter/deletion
  changes with zero intended JS semantic delta. Spec reference for Fix A is
  the ECMA-262 lexical grammar line-terminator set (CR, LF, LS, PS; CRLF as
  one).
- Okojo evidence: lexer terminator handling vs index handling (Fix A);
  parser `JsFormalParameterBindingKind` vs debug filter (Fix B).

## Copy vs intentional difference

None — these align Okojo with its own lexer/spec, not with an external
engine.

## Perf plan

None. Correctness-only changes; Fix A keeps the single-pass linear scan,
Fix B adds one boolean copy per binding record, Fix C removes dead code.

## Verification (2026-09-10)

- `Okojo.Tests`: 2214 passed, 0 failed, 4 skipped (incl. 5 new line-index
  tests and 2 new debugger local-visibility tests; the `$value` test failed
  before the fix as expected).
- `Okojo.Compiler.Tests`: 362/362. `Okojo.DebugServer.Tests`: 24/24.
- `Okojo.Node.Tests`: 113 passed, 1 skipped, only the known pre-existing
  source-maps environment failure (proven pristine-failing; untouched by
  this change).
- Fix B finding: parser-generated `$..._pattern_...` temps never enter the
  binding set (prologue works off `JsParameter` by index), so the old `$`
  filter hid nothing synthetic in practice — it only hid user variables.
  The flag is set at the pattern-parameter origin and read by the filter;
  the no-leak test pins both directions.
- Fix A also covered the `string` overload of `GetLineColumn` (module and
  syntax diagnostics), which carried the same defect.

## Deferred

- Full debug-info redesign (scopes, location ranges, COW breakpoint views):
  `debug-info-redesign.md`.
- Source-map binary search: `debug-info-redesign.md` §D.
