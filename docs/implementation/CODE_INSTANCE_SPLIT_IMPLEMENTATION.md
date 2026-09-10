# Code/instance split: implementation and review notes

Status: implementation delivered; .NET build, formatting, Okojo tests, Test262 and performance gates are **not verified**. The available container has no `dotnet` executable. This is a review candidate, not a claim of a green merge.

Scope: the core changes from migration steps 1–6 of `docs/proposals/OKOJO_CODE_INSTANCE_SPLIT_DESIGN.md`. Step 7 (lazy nested body compilation) is deliberately deferred. Bodies and the linked instance graph remain eager. Portable module *bodies* are supported; module resolution, import/export binding plans, cells and contexts remain owned by the existing loader.

## 1. Ownership, not a wrapper around the old product

The compilation product no longer contains JavaScript function objects. The old `JsScript` constructor, record cloning, `CloneForClosure`, mutable agent binding and object/function reset-for-clone helpers are removed.

| Object | Lifetime and ownership | Contents |
| --- | --- | --- |
| `JsCompilationUnit` | Portable, shareable in-process | Entry descriptor, source, additional module hoist descriptors; lazily materialized descriptor inventory |
| `JsFunctionDescriptor` | Portable, immutable after publication | Code, function kind, arity, strictness, constructor/method/arrow flags and static capture metadata |
| `JsFunctionCode` | Portable, immutable after publication | Pristine bytecode, numeric constants, symbolic names, portable constant descriptors, register count, resume/switch tables, declaration obligations and optional debug information |
| `JsScript` | One executable instance per descriptor per realm | Target atom handles, target literal layouts, linked child instances, inline-cache arrays, lazy template objects and optional breakpoint execution copy |
| `JsBytecodeFunction` | Fresh language-level closure identity | A linked instance, captured environment, lexical `this`/`new.target`, home/private state and closure-specific source override |

`JsScript` is retained as the executable-instance name to keep the host execution and debugger vocabulary coherent, not as an adapter retaining old ownership. Function identity is separate from both portable code and realm feedback.

### Publication and portable constants

The internal code constructor accepts an ownership transfer from the emitter. Public bytecode, numeric and symbolic-name views are `ReadOnlySpan<T>`. Internal engine/tooling code can read the underlying arrays directly; those arrays are trusted read-only except for the separate execution view.

The portable object-constant vocabulary is closed: strings, immutable `JsBigInt`, integers, integer tables, function descriptors, object-literal layouts and template-site descriptors. A JavaScript object, realm, agent or shape cannot be published through the public builder. Mutable `int[]` leaves supplied to the builder are snapshotted at publication. This is in-process code reuse, not a deserializer or a bytecode security boundary.

Compiler collection pooling is extracted into `CompileCollectionPool`. Public `CompileUnit` needs no runtime/realm. Existing realm-oriented compile facades reuse a realm-owned workspace but return the same portable product before linking. A compilation unit never retains that workspace. Symbolic names remain indexed per function, avoiding a unit-wide renumbering pass or extra lookup in VM operands.

## 2. Linking and lifetime

`JsRealm` lazily owns a `ConditionalWeakTable<JsFunctionDescriptor, WeakReference<JsScript>>` guarded by a small link lock. A live descriptor and live realm reuse one feedback instance; independently linked realms have distinct state. The unit does not contain a realm-keyed cache. Both sides are weak: the weak key lets a long-lived realm drop discarded units, and the weak value lets a discarded unit's instances (and their realm references) be collected, so retaining a unit alone never retains a realm and a live realm never roots discarded code. Stale entries are replaced on next link under the lock.

The linker interns names, reconstructs literal layouts from the target empty shape, links nested descriptors and allocates feedback arrays only for nonzero slot counts. Prototype feedback stays lazy. The complete linked graph is entered into the cache before debugger registration callbacks can observe it, avoiding reentrant duplicate publication through those callbacks.

Linking validates global declaration obligations against the target environment, including cached links. The first execution validates again because globals can change between link and execution; re-execution skips validation so an instance's own bindings from its previous run do not read as conflicts (pre-split re-execution behavior, e.g. executing the same script repeatedly under an instruction budget). Declaration atom handles are linked once and reused by subsequent checks. Parse/binding checks remain in the compiler. This separation is intended to preserve parse errors versus environment-instantiation failures; Test262 negative-phase verification is still required.

Realm execution and debugger mutations remain agent-confined. Sharing a unit between independent agents is supported by construction and covered by new, unexecuted regression tests. This does **not** authorize concurrent calls into one realm. Source-location and unit-inventory lazy publication use atomic publication because those objects can be shared across agents.

## 3. Execution and closure creation

At frame entry the VM hoists the execution byte array, numeric pool, object pool, atom array and feedback arrays. Instructions retain direct array/index access. The split adds no generic constant resolver, virtual dispatch layer or dictionary lookup to each property instruction.

`CreateClosure` reads a linked child `JsScript` from the object pool, constructs a fresh `JsBytecodeFunction`, then installs captures using the existing runtime environment machinery. Prototype identity, generator/async kind, class flags and private metadata are initialized explicitly rather than copied from a template object. Module hoisted functions and CommonJS wrappers consume descriptors and construct real closures at their existing runtime integration points.

Dynamic `Function` source-text overrides are closure-local. They no longer replace a shallow-cloned script that aliases feedback and bytecode. The portable source segment is a read-only value; materialized strings are cached on the instance or overriding closure.

Private compile IDs are allocated monotonically through an in-process atomic allocator rather than the agent. Runtime class evaluation still supplies fresh private-brand tokens. Operand width is unchanged; ID exhaustion throws rather than wrapping. Serialized code would require an explicit relocation format.

## 4. Two less obvious realm dependencies removed

Object literal prefix initialization previously embedded a target shape. It now emits an ordered symbolic layout and ordinal slots. Linking rebuilds exactly that transition sequence in the destination realm. Numeric index keys remain outside shapes, and computed keys, duplicates and accessors retain the existing slow-path boundaries.

`delete identifier` previously consulted live lexical state and could resolve through the mutable `globalThis` property. Local/captured binding cases remain compile-time false. The unresolved/global case now calls the appended `DeleteGlobalBinding` runtime helper, which checks the executing realm's lexical environment and actual global object. Existing runtime ID positions are unchanged. This uncommon operation does an atom lookup at runtime; ordinary global/property fast paths are unaffected.

Tagged template descriptors no longer own a dictionary keyed by realm. Each linked instance has a lazy site wrapper. Closures at one site in one realm share its frozen template object; a different realm receives a different object. The portable descriptor graph cannot retain a realm through a template cache.

## 5. Breakpoint copy-on-write, including live VM cursors

`JsFunctionCode.Bytecode` is always pristine. A non-debugged instance executes that same array. First patching creates one instance-owned copy; subsequent breakpoint arm/restore operations touch only that copy. Clearing all breakpoints retains the copy until instance collection, avoiding another array transition while frames are suspended. Disassembly reads the pristine code.

Copy-on-write alone is insufficient: a host callback can install the first breakpoint after the VM has hoisted a by-reference bytecode cursor. `JsAgent.CodeReload` uses a generation handshake and the existing execution-check countdown to make active VM invocations refresh their cursors. The forced refresh preserves the original policy countdown rather than treating the refresh as a new instruction-budget checkpoint. Exception handling computes PC offsets from the cursor's actual backing array, and debugger callbacks rebase before continuing or retrying the restored instruction.

Nested host reentry matters: suspended outer VM invocations must acknowledge publication too. While an outer invocation still holds the previous generation, nested execution can take the forced cold path repeatedly. That cost occurs only during this debugging transition, not ordinary dispatch. Normal execution adds per-`Run` entry/exit bookkeeping; its cost needs measurement.

An exact-script breakpoint affects that instance and rejects a foreign agent. A path/line breakpoint intentionally retains the existing agent-wide source-path behavior and can patch separate instances sharing that path. Isolation of arrays is not a promise that an agent-wide path request targets only one realm.

## 6. Public usage and migration

```csharp
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;

var unit = JsCompiler.CompileUnit(
    "function read(o) { return o.value + seed; } read({ value: 2 });",
    "shared.js"
);

using var first = JsRuntime.Create();
using var second = JsRuntime.Create();
first.DefaultRealm.Global["seed"] = 5;
second.DefaultRealm.Global["seed"] = 40;

first.DefaultRealm.Execute(unit.Link(first.DefaultRealm));
second.DefaultRealm.Execute(unit.Link(second.DefaultRealm));
// Expected completion values: 7 and 42. This example was not run under Okojo here.
```

`JsCompiler.Compile(realm, source, path)` remains compile-and-link convenience. `JsScript.CreateClosure()` creates a fresh identity on an already linked instance. `JsFunctionDescriptor.CreateClosure(realm)` links and creates an unbound identity; embedders must not expect it to reconstruct captures from another closure. `Execute(JsScript)` relinks foreign portable code to the target realm; `Execute(JsBytecodeFunction)` rejects a foreign closure rather than silently moving captured state.

Low-level breaking changes are intentional: the old raw script/function constructors and mutable bytecode/cache exposure are not preserved. Use the public builder's `ToCompilationUnit().Link(realm)` path for raw code. Lockstep ABI tests and inspection tools have been migrated; tools traverse linked instances without manufacturing template closures. Feedback and linked constants are internal, not a stable embedding surface.

`CompileModuleUnit` compiles a module body. It is **not** a self-contained module loader API: evaluating imported/exported bindings still requires the existing loader's module record and binding machinery.

## 7. Tests and reference observations

New suite: `tests/Okojo.Tests/CodeInstanceSplitTests.cs` (22 cases across 18 test methods).

Coverage includes portable constant graphs, same-realm cache reuse, sibling-realm and cross-agent feedback isolation, literal relinking, fresh closure/capture/prototype identity, private brands, `super`, generators, template identity/freezing, link-time and delayed execution-time declaration checks, global deletion, pristine disassembly, breakpoint ownership, first patches from host/debugger callbacks, nested host reentry and throws, policy-countdown preservation, parallel independent agents, weak-cache lifetime, builder snapshots and absent unused feedback.

Existing generator/frame ABI, compiler, module, Node wrapper, debugger, source-text and tooling tests have been migrated to descriptors/instances.

Integration fixes applied after the initial delivery (verified locally, .NET 11 SDK):

- `JsValue.FromBoolean` did not exist on this base; the global-delete helper uses an explicit `True`/`False` selection.
- Migrated tests referenced `JsScript` without the `Okojo.JavaScript.Bytecode` import; the missing usings were added, and the `ToolingTests` metadata test now builds portable code via `ToCode` with symbolic lexical names and asserts the linked atoms resolve back to those names.
- The link cache is weak on both sides (see §2); a strong-valued realm-owned table retained the realm through live units.
- Execution-time declaration validation runs only before the first execution; re-execution skips it (pre-split behavior).
- The `eval` intrinsic invokes its root with the global object as `this`. The pre-split eval root was always sloppy, coercing `undefined` to the global object; carrying real strictness into the descriptor without a receiver change regressed strict-eval `this` (test262 `language/function-code/10.4.3-1-18gs/20gs/20-s`). New regression test: `EvalTests.StrictEval_ThisValue_IsGlobalThis`.
- `JsObjectPathBenchmarks` uses `CreateClosure()`; `OkojoInkProbe` was granted `InternalsVisibleTo` for the now-internal source-map tables.

Verification: `Okojo.Tests` 2244 passed / 4 skipped; `Okojo.Compiler.Tests` 362; `Okojo.Node.Tests` 115 passed / 1 skipped; `Okojo.DebugServer.Tests` 24; Repl 8; DotNet.Modules 6; Globalization 69; Numerics 28; Text.Unicode 47; warning-free `Okojo.slnx` Release build; full Test262 42618 passed with zero non-staging failures (all 436 failures under `test/staging/`); overlapping bytecode snapshots byte-identical, including the new `code-instance-split` case rendering.

Six reference cases were executed successfully under the installed Node v22.16.0 / V8 12.4.254.21-node.26. The fixture and actual output are in `tools/CodeInstanceSplitValidation/reference.cjs` and `reference-results.json`. They check closure identity/captures, private brands/super/generators, template freezing/site identity, global and lexical deletion, and declaration rejection before side effects. This is reference behavior only, not evidence that Okojo passes those cases.

The saved V8 `make` bytecode uses a function context, captured parameter slot and `CreateClosure`, with a `SharedFunctionInfo` entry rather than an instantiated nested closure in its constant pool. See `v8-make.txt` and `artifacts/okojobytecodetool/cases/code-instance-split.js`. The design follows that separation of static function metadata and dynamic closure identity, without copying V8's feedback-vector layout. Okojo keeps direct per-instance arrays.

## 8. Performance plan and validation

`CodeInstanceSplitBenchmarks` separates portable compilation, compile-and-link, cached linking, closure allocation and a warmed property call. `CodeInstanceSplitColdLinkBenchmarks` isolates first linking from realm setup. Its iteration setup forces single-invocation iterations; interpret noise and allocation reporting accordingly. No timings, allocation reductions or regression-free claims are reported here.

Compare the existing compile, function-call, property, global-binding, JSON and VM-loop workloads against the uploaded baseline under identical Release builds. Report startup/linking, first call, steady state and retained memory separately. Watch the fresh portable workspace cost, linked graph allocations, CWT entries, explicit closure initialization, optional debug metadata and per-VM-entry reload bookkeeping. A new benchmark suite is instrumentation, not proof of an optimization.

Run the real gates with Python 3.10+ and .NET 10:

```text
python tools/CodeInstanceSplitValidation/validate.py --test262-root /path/to/test262/test --benchmarks
```

The script restores the pinned formatter, formats only changed C# files, runs the new focused suite followed by the full core suite, other test projects, solution build, optional full Test262, then optional benchmarks. It stops at the first failed command. Do not use the old passed-test cache to waive conformance for this patch. Inspect Test262 results and all build warnings before accepting a merge.

Actual local checks: source delimiter/ownership/dispatch-table audits, Node reference fixtures and `git diff --check`. See the JSON reports. The lexical audit is deliberately narrow: it does not establish C# syntax, accessibility, overload resolution or runtime correctness. `dotnet --info` and the validation driver report the missing executable; formatter, compilation, Okojo tests, Test262, Okojo bytecode snapshots and benchmarks remain blocked.

## 9. Deferred work

True lazy nested compilation needs an owned syntax/binding summary and a publication/error policy; it is not hidden behind an eagerly compiled “lazy” wrapper here. Full portable module binding plans, persistent bytecode serialization/private-ID relocation and baseline-to-patch performance/retention reports are also deferred. These items and outstanding validation gates are recorded in `TODO.md`.
