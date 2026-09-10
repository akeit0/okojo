# Code/instance split: design inventory and migration order

Status: proposed (design — no code yet).
Replaces: the `JsScript` responsibility bundle; realm-bound
`JsFunctionCompiler` product; `MemberwiseClone` closure construction.
Implements: `proposals/code-instance-split.md` (which states the why; this
note states the verified what and the order).

## 1. Verified inventory (base `4ade560` + debug-slice commits)

### 1.1 Single construction funnel

`JsScript` has 31 constructor parameters (`Bytecode/JsScript.cs:8-40`) but
exactly one construction site: `BytecodeBuilder.ToScriptCore` (`:1066`),
reached through 4 `ToScript` call sites — script compiler
(`JsScriptCompiler.Compile.cs:106`), module body (`JsModuleCompiler.cs:68`),
async wrapper (`JsModuleCompiler.cs:121`), function compiler
(`JsFunctionCompiler.Compile.cs:188`). Any split keeps this funnel: the
link step becomes a second funnel beside it, not a fifth emission path.

`scriptSourceCode` threading already exists (`JsCompilerBase.cs:229`,
forwarded by `JsFunctionCompiler.cs:18-24`, inherited by nested compiles at
`Expressions.cs:366` / `Statements.cs:2644`) — the precedent that
compilation inputs can travel separately from the realm.

### 1.2 Post-construction writers (the mutability that must move)

| State | Writer | When |
|---|---|---|
| `NamedPropertyIcEntries[]` contents | `Vm.NamedPropertyIc.cs:139,155,183-186` | execution Get/Store miss |
| `PrototypeNamedPropertyIcEntries` (lazy alloc + contents) | `JsScript.cs:107-120`, `Vm.NamedPropertyIc.cs:154,215-220` | first prototype-cacheable Get, then misses |
| `GlobalBindingIcEntries[]` contents | `Vm.Globals.cs:93-127,218-258` | global load/store fast-miss |
| `Bytecode[]` elements | `JsBreakpointHandle.cs:596,606` (patch/restore) | breakpoint arm/hit/clear |
| `Agent` (+ `RegisterScript`) | `JsScript.cs:183-190`, 5 `BindAgent` call sites (4 compilers + `JsBytecodeFunction` ctor/clone) | compile time, closure clone |
| Everything else | — | write-once at construction |

Aliasing hazard: `Intrinsics.Objects.cs:485` clones `JsScript` via `with{}`
but shallow-shares `Bytecode`/IC arrays with the original.

### 1.3 Realm coupling inside the "immutable" product

| Coupling | Site |
|---|---|
| Builder rents all compile collections from the realm | `BytecodeBuilder.cs:48-73` |
| Names interned to atoms at emit time | `BytecodeBuilder.cs:635` (`realm.Atoms.InternNoCheck`) |
| Realm shapes baked into `ObjectConstants` | `BytecodeBuilder.cs:583-602` (`Vm.EmptyShape` + interning) |
| Global declaration validation against the live realm | `JsScriptCompiler.Compile.cs:158-243` |
| Top-level lexical atoms baked in | `JsScriptCompiler.Compile.cs:277` |
| Agent bound at compile, recursively over nested functions | `JsAgent.cs:753-792` |

Portable-by-accident precedents: `JsTemplateSiteDescriptor` (per-realm
`GetOrCreate`), RegExp literals (emitted as runtime calls, never pooled).

### 1.4 Closure construction today

`JsFunctionCompiler` returns a realm-bound `JsBytecodeFunction`;
`CloneForClosure` (`JsBytecodeFunction.cs:134-185`) does `MemberwiseClone`
plus resets (object shape/slots, flags/prototype, `BoundParentContext`,
metadata clone). Shared by reference across clones: `FunctionTemplate`
(already an immutable shareable descriptor, `:269-297`), `Script` (+ its
bytecode/consts/IC arrays), `ArgumentsMappedSlots`.

Live cross-realm defect this split fixes by construction: a clone in realm B
re-binds the SHARED `Script.Agent` to B (`JsBytecodeFunction.cs:137`),
corrupting realm A's caches and breakpoint ownership.

## 2. Ownership mapping (concrete)

| Component | Owns | Source today |
|---|---|---|
| `CompilationUnit` | `SourceCode`, symbolic (un-interned) name table, declaration plans (top-level lexical/var/function names + module plans), function descriptor array | `SourceCode`, `AtomizedStringConstants`→strings, `TopLevelLexical*`, `JsModuleCompiler` plans |
| `FunctionCode` | `Bytecode`, `NumericConstants`, portable `ObjectConstants`, `RegisterCount`, `StrictDeclared`, handler/resume tables, IC slot *counts* | `JsScript` minus the right column |
| `FunctionInstance` (per realm) | interned atoms, rebuilt literal shapes, zeroed IC arrays, `Agent` binding, realm-linked constants | link step output; today's `BindAgent` + IC arrays + `VmLoop` hoists |
| `JsClosure` | identity, `BoundParentContext`, lexical `this`, home/private state, link to descriptor | `JsBytecodeFunction` minus compilation product |
| `FunctionDebugInfo` | sequence points, scopes, variable locations, `DebugNames` tables, `FunctionSourceText` | `DebugPcOffsets/SourceOffsets`, `LocalDebugInfos`, `*DebugPcs/NameIndices`, `DebugNames` |

Boundaries, not allocations: descriptors live in unit-owned arrays;
debug/feedback storage stays absent until needed.

## 3. Link-step contract

`Link(unit, targetRealm, targetAgent) -> realm-local instance graph`:

1. Intern the unit's symbolic names into the target atom table.
2. Rebuild literal shapes from `targetRealm.EmptyShape`; re-resolve atoms.
3. Fresh-allocate zeroed IC arrays from the code's slot counts.
4. Run declaration checks (today's `SCRIPT_GLOBAL_*` validation) against
   the target `GlobalObject` — moved, not removed.
5. Bind the instance graph to the target agent (replacing recursive
   compile-time `BindAgent`).
6. Breakpoint patching moves to a copy-on-write execution view owned by the
   instance (`debug-info-redesign.md` §D) — `FunctionCode.Bytecode` is never
   written after emission.

Non-goals for the first cut: persistent bytecode caching (in-memory reuse
first); a compatibility adapter over the old constructor (would preserve
the old ownership inside the new one — review).

## 4. Migration order (each step lands green independently)

1. **Portable name table.** Emit symbolic strings alongside (then instead
   of) interned atoms; link step interns per realm. Gate: identical
   execution, atoms table per realm verified in tests.
2. **Portable literal layouts.** Replace baked shapes in `ObjectConstants`
   with descriptors rebuilt at link (template-site pattern already proves
   the shape). Gate: object-literal Test262 slices green across two realms
   sharing one unit.
3. **Instance-owned IC storage.** Move the three IC arrays off the shared
   product into the realm-local instance; VM reads instance state.
   Gate: polymorphic behavior tests + cross-realm cache isolation test
   (the current shared-Agent corruption becomes impossible).
4. **Breakpoint COW view.** Remove `Bytecode` mutation; patch the
   instance-owned execution view. Gate: debugger breakpoint tests green,
   disassembler shows pristine bytes while patched.
5. **Explicit closure construction.** Replace `MemberwiseClone` product
   with descriptor → instance → closure steps; delete the old constructor
   (no adapter). Gate: identity/environment/`toString`/hoisting suites +
   full Test262.
6. **Declaration checks at link.** Move global validation out of script
   compilation into the link step. Gate: negative-phase Test262 preserved
   (parse vs instantiation vs runtime errors distinct).
7. **Lazy nested compilation** (with `compiler-lazy-bindings.md`): needs
   descriptors (5) + binding summaries; compile entry eagerly, bodies on
   first use. Gate: startup/first-call/steady-state/retained-memory
   reported separately.

Ad-hoc roots (`Execute(script)`, `eval`, `Function` ctor,
`ExecuteProgramInline`) all route through link — audit each at step 5.
`with{}`-clone aliasing (`Intrinsics.Objects.cs:485`) is resolved by step 3
(instance arrays are never shared).

## 5. Standing gates for every step

Full `Okojo.Tests` + Compiler/Node/DebugServer suites green; full Test262
with zero non-staging failures (baseline: 42617 passed); bytecode snapshots
regenerated where encoding-adjacent; API-shape tests rewritten, not worked
around. probes extended per `conformance-gate.md` (phases, first-use cost,
allocated vs retained, suspension payload) once step 7 lands.
