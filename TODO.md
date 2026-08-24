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
- [ ] improve hot-path runtime allocation and branch behavior
- [ ] improve `Okojo.Node` compatibility against real Node-facing workloads
- [ ] attempt a real HTML/CSS renderer integration for DOM-manipulation browser compatibility testing
- [ ] add selected staging ECMA-262 support where justified, starting with candidates such as `Temporal`
- [ ] define and implement `JsRealm` structural split:
  `JsRealm` as coordinator/root, with internal `RealmIntrinsics` and `RealmShapes`

### Active supporting work

- [ ] continue module/runtime simplification without reintroducing wrapper-heavy paths
- [ ] direct-flat modules: make flat parsing/compilation the default module path
- [ ] expand `Test262Runner --planned-compiler` coverage before direct-flat default adoption
- [ ] explicit-resource-management: the current compiler/runtime seam for top-level-module `await using` is still awkward; give module async cleanup a dedicated lowering path instead of leaning on normal async-function suspension flow
- [ ] explicit-resource-management: async disposal still loses non-`Error` thrown values through the host async bridge in the remaining staging `await using` rejection case; give disposal promise completion a JS-value-preserving path instead of relying on generic task fault wrapping
- [ ] keep shape/dictionary rollout aligned with hot-path simplicity
- [ ] private-brand IDs: `JsAgent.AllocatePrivateBrandId` is a process-global monotonic counter while `InitPrivateMethod`/`GetPrivateField`/`SetPrivateField` encode `brandId` as a ushort operand — a long-lived process compiling ~65k+ private-branded classes exhausts the operand space and throws "Private field operands exceeded bytecode capacity" (reproduced by repeated planned/production compiles in one process). Fix direction: widen the brand-id operand encoding on those opcodes (bytecode is never persisted, so in-place layout change is safe); do NOT recycle ids (an id reuse would let an old instance satisfy a newly compiled class's brand check)
- [ ] keep the non-legacy, non-staging Test262 passing baseline stable during API/compiler/runtime work
- [ ] extend locals-by-name snapshots with outer-scope/context-chain value lookup helpers for paused debugger inspection
- [ ] add a compact local-name table to `JsScript` so paused frames can resolve visible locals without guessing from runtime slots
