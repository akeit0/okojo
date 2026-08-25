# TODO

## Okojo Priority Queue

### Current highest-priority architecture work

- [ ] finalize stable embedding API boundaries for `src/Okojo`
- [ ] split host-facing APIs from core ECMA-262 engine APIs
- [ ] redesign task queue ownership so ECMAScript jobs stay in core engine and host tasks stay host-driven
- [ ] reduce direct scheduling policy living inside `JsAgent`
- [ ] tighten the remaining intended first-class `JsRuntime` / `JsRealm` API surface against the clean API plan
- [ ] clarify `Okojo.Hosting` presets and keep environment globals separate from embedder control APIs
- [x] adopt the canonical parser/compiler in all production, module, wrapper, REPL, tool, and benchmark entry points
  - Legacy `JsCompiler` partials, compiler context, experimental namespace, and planned/legacy switches are deleted; `JsCompiler` is now the canonical public facade over the compiler.
  - CommonJS/module wrappers, top-level await interop, Test262Runner, bytecode tools, and compiler tests use the canonical path.
  - Gates: solution build is warning-free; Okojo, compiler, Node, REPL, module, and auxiliary test suites pass.
- [ ] improve hot-path runtime allocation and branch behavior
- [ ] improve `Okojo.Node` compatibility against real Node-facing workloads
- [ ] attempt a real HTML/CSS renderer integration for DOM-manipulation browser compatibility testing
- [ ] add selected staging ECMA-262 support where justified, starting with candidates such as `Temporal`
- [ ] define and implement `JsRealm` structural split:
  `JsRealm` as coordinator/root, with internal `RealmIntrinsics` and `RealmShapes`

### Active supporting work

- [ ] continue module/runtime simplification without reintroducing wrapper-heavy paths
- [x] canonical compiler coverage baseline: language, built-ins, and intl402 non-annexB sweeps passed in the pre-cutover gates; keep those caches stable while optimizing.
- [x] Test262Runner uses one canonical compiler path; the obsolete compiler switch and split pass caches are removed.
- [ ] explicit-resource-management: the current compiler/runtime seam for top-level-module `await using` is still awkward; give module async cleanup a dedicated lowering path instead of leaning on normal async-function suspension flow
- [ ] explicit-resource-management: async disposal still loses non-`Error` thrown values through the host async bridge in the remaining staging `await using` rejection case; give disposal promise completion a JS-value-preserving path instead of relying on generic task fault wrapping
- [x] direct flip soak blocker FIXED: HandleCurrentContextSlotOp IndexOutOfRangeException. Root cause: CreateFunctionContext(WithCells) shared the active module's TopLevelContext for ANY parent-null frame during module evaluation; Okojo.Node's production-compiled CJS wrapper (FunctionFrame, null parent, called from host mid-evaluation) received the module's small context and wrote out of bounds. Under production modules this silently corrupted module slots (shims have ~44 export slots so wrapper writes at 5/6 fit); under planned shims (~2 slots) it crashed. Fix: restrict sharing to ScriptFrame and GeneratorFrame (TLA async roots resume as GeneratorFrame with null parent and legitimately rely on sharing; user closures always carry BoundParentContext). Regression tests: ModuleHostReentryTests (planned + production re-entry through a JsHostFunction calling CompileHoistedFunctionTemplate product). Post-fix sweeps: planned language 22203/0, planned built-ins 18067/0, prod language 22255/4 unchanged
- [x] direct flip soak blocker #2 FIXED: ink's output.js broke under planned modules - `export default class Output` with instance field `caches = new OutputCaches()` threw ReferenceError 'OutputCaches is not defined'. Root cause: CompilerStoragePlanner classified module-root bindings by script Program-scope rules only when the root scope kind is Program, but the module collector emits kind=Module, so ClassifyStorage fell through to local classification: non-exported module top-levels got LexicalRegister/LocalRegister/GlobalBinding instead of ContextSlot/ModuleBinding. Register storages cannot cross function boundaries and BuildChildCaptureBindings only captures ContextSlot/ModuleBinding, so nested functions (including synthetic field initializers inlined into constructors) fell back to LdaGlobal at runtime. Fix: module-root bindings without an import/export cell are forced to ContextSlot in Plan(); exported names keep their cells. Regression tests: CompileModule_ClassFieldInitializerSeesSiblingTopLevelClass, CompileModule_DefaultClassFieldInitializerSeesSiblingTopLevelClass, CompileModule_NonExportedFunctionDeclarationStaysModuleScoped (+ planner expectation updated). Post-fix: planned language 22203/0, planned built-ins 18067/0, OkojoInkProbe renders the full Ink app identically to production modules
- [ ] keep shape/dictionary rollout aligned with hot-path simplicity
- [x] private-brand IDs: widened the brand-id operand to 32 bits across private initialization/access opcodes; `JsAgent.AllocatePrivateBrandId` remains monotonic and IDs are never recycled.
- [ ] keep the non-legacy, non-staging Test262 passing baseline stable during API/compiler/runtime work
- [ ] extend locals-by-name snapshots with outer-scope/context-chain value lookup helpers for paused debugger inspection
- [ ] add a compact local-name table to `JsScript` so paused frames can resolve visible locals without guessing from runtime slots
