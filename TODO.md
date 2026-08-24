# TODO

## Okojo Priority Queue

### Current highest-priority architecture work

- [ ] finalize stable embedding API boundaries for `src/Okojo`
- [ ] split host-facing APIs from core ECMA-262 engine APIs
- [ ] redesign task queue ownership so ECMAScript jobs stay in core engine and host tasks stay host-driven
- [ ] reduce direct scheduling policy living inside `JsAgent`
- [ ] tighten the remaining intended first-class `JsRuntime` / `JsRealm` API surface against the clean API plan
- [ ] clarify `Okojo.Hosting` presets and keep environment globals separate from embedder control APIs
- [ ] complete the experimental compiler and make it usable in real execution paths
- [ ] replace the production class parser/compiler with the direct flat path after closing semantic, metadata, and workload gates
  - Node profile default flip LANDED: NodeRuntimeBuilder now applies direct-flat compilers (script + module) by default; UseLegacyCompilers() opts back out. Module parse fallback in ModuleGraph keeps decorator/auto-accessor sources on the legacy path per-node until the flat parser grows support
  - transitional scaffolding to DELETE once flat-parser gaps close (decorators, auto-accessors): (1) JsParseException fallback branches in JsPlannedCompilerOptions.CompileDirectFlatWithProductionFallback and ModuleGraph.GetOrCreate, (2) NodeRuntimeBuilder.UseLegacyCompilers + OKOJO_INK_PROD probe gate, (3) Test262Runner A/B flag, then (4) production script/module compile paths, JavaScriptParser module/script entry points still used by eval/EvaluateAsync/REPL, and finally JsCompiler+JsCompiler.* itself
- [ ] close FlatJavaScriptParser gaps that keep modules on the legacy path: decorators (`@expr` on classes/members/accessors) and auto-accessors (`accessor x`); each currently throws JsParseException and triggers per-node legacy fallback
- [ ] improve hot-path runtime allocation and branch behavior
- [ ] improve `Okojo.Node` compatibility against real Node-facing workloads
- [ ] attempt a real HTML/CSS renderer integration for DOM-manipulation browser compatibility testing
- [ ] add selected staging ECMA-262 support where justified, starting with candidates such as `Temporal`
- [ ] define and implement `JsRealm` structural split:
  `JsRealm` as coordinator/root, with internal `RealmIntrinsics` and `RealmShapes`

### Active supporting work

- [ ] continue module/runtime simplification without reintroducing wrapper-heavy paths
- [ ] direct-flat built-ins remaining: none - full non-annexB corpus passes (language 22203/0, built-ins 18067/0, intl402 1247/0). The last failure (AsyncFromSyncIteratorPrototype/next/for-await-next-rejected-promise-close) was a double IteratorClose, not a microtask-timing issue: planned's for-await try region wrapped the awaited next() call, so next-result rejections entered the loop close on top of the AsyncFromSyncIteratorContinuation onRejected close; production and V8 keep the try region body-only so next-rejections propagate without re-closing. Fixed by moving PushTry after the await (regression tests CompileString_ClosesForAwaitIteratorOnceOnNextResultRejection / OnBodyThrow). Earlier shift/pop fixes used StaGlobal for repeated global var initializers instead of StaGlobalInit, whose undefined-init no-op path silently dropped later initializers
- [ ] expand `Test262Runner --planned-compiler` coverage before direct-flat default adoption
  - full non-annexB corpus A/B sweep complete (artifacts/test262/{prod,planned}-{intl402,builtins-full,language-full}.txt): intl402 1247/0 both sides; built-ins prod 18311/0 vs planned now 18067/0; language prod 22255/4 vs planned 22203/0. The 4 production-only failures (fn-name-accessor-{get,set}) already pass under direct-flat
  - remaining flip gates: flip default with production fallback when FlatJavaScriptParser throws JsParseException (covers decorators/auto-accessors gaps); Okojo.Node/Ink soak
- [ ] explicit-resource-management: the current compiler/runtime seam for top-level-module `await using` is still awkward; give module async cleanup a dedicated lowering path instead of leaning on normal async-function suspension flow
- [ ] explicit-resource-management: async disposal still loses non-`Error` thrown values through the host async bridge in the remaining staging `await using` rejection case; give disposal promise completion a JS-value-preserving path instead of relying on generic task fault wrapping
- [x] direct-flat flip soak blocker FIXED: HandleCurrentContextSlotOp IndexOutOfRangeException. Root cause: CreateFunctionContext(WithCells) shared the active module's TopLevelContext for ANY parent-null frame during module evaluation; Okojo.Node's production-compiled CJS wrapper (FunctionFrame, null parent, called from host mid-evaluation) received the module's small context and wrote out of bounds. Under production modules this silently corrupted module slots (shims have ~44 export slots so wrapper writes at 5/6 fit); under planned shims (~2 slots) it crashed. Fix: restrict sharing to ScriptFrame and GeneratorFrame (TLA async roots resume as GeneratorFrame with null parent and legitimately rely on sharing; user closures always carry BoundParentContext). Regression tests: ModuleHostReentryTests (planned + production re-entry through a JsHostFunction calling CompileHoistedFunctionTemplate product). Post-fix sweeps: planned language 22203/0, planned built-ins 18067/0, prod language 22255/4 unchanged
- [x] direct-flat flip soak blocker #2 FIXED: ink's output.js broke under planned modules - `export default class Output` with instance field `caches = new OutputCaches()` threw ReferenceError 'OutputCaches is not defined'. Root cause: CompilerStoragePlanner classified module-root bindings by script Program-scope rules only when the root scope kind is Program, but the module collector emits kind=Module, so ClassifyStorage fell through to local classification: non-exported module top-levels got LexicalRegister/LocalRegister/GlobalBinding instead of ContextSlot/ModuleBinding. Register storages cannot cross function boundaries and BuildChildCaptureBindings only captures ContextSlot/ModuleBinding, so nested functions (including synthetic field initializers inlined into constructors) fell back to LdaGlobal at runtime. Fix: module-root bindings without an import/export cell are forced to ContextSlot in Plan(); exported names keep their cells. Regression tests: CompileModule_ClassFieldInitializerSeesSiblingTopLevelClass, CompileModule_DefaultClassFieldInitializerSeesSiblingTopLevelClass, CompileModule_NonExportedFunctionDeclarationStaysModuleScoped (+ planner expectation updated). Post-fix: planned language 22203/0, planned built-ins 18067/0, OkojoInkProbe renders the full Ink app identically to production modules
- [ ] keep shape/dictionary rollout aligned with hot-path simplicity
- [ ] private-brand IDs: `JsAgent.AllocatePrivateBrandId` is a process-global monotonic counter while `InitPrivateMethod`/`GetPrivateField`/`SetPrivateField` encode `brandId` as a ushort operand — a long-lived process compiling ~65k+ private-branded classes exhausts the operand space and throws "Private field operands exceeded bytecode capacity" (reproduced by repeated planned/production compiles in one process). Fix direction: widen the brand-id operand encoding on those opcodes (bytecode is never persisted, so in-place layout change is safe); do NOT recycle ids (an id reuse would let an old instance satisfy a newly compiled class's brand check)
- [ ] keep the non-legacy, non-staging Test262 passing baseline stable during API/compiler/runtime work
- [ ] extend locals-by-name snapshots with outer-scope/context-chain value lookup helpers for paused debugger inspection
- [ ] add a compact local-name table to `JsScript` so paused frames can resolve visible locals without guessing from runtime slots
