# VM Dispatch Structure Design Note (A7)

Question: should `JsRealm.Run` replace its giant switch with an
opcode-indexed function-pointer/delegate table (or other threaded-dispatch
scheme)? This note records the deep design analysis and the measurements that
answer it.

## Constraints discovered from our own artifacts

Dasm facts (smi-sum-loop Tier1, post-A10 snapshot):

1. Dispatch is already a dense jump table:
   `cmp ecx,152; ja DEFAULT; lea rcx,[table]; mov ecx,[rcx+rax*4];
   lea rdx,BASE; add rcx,rdx; jmp rcx` - one indirect JMP site.
2. RyuJIT emits a SECOND indirect branch site for a sub-range
   (`lea edx,[rcx-0x64]` secondary table) - two BTB sites total.
3. Dispatch-critical state (opcode value at [rbp-0xDC], refs at
   [rbp-0xD80]) lives in Run's stack slots; Run's frame is large and the
   allocator spills regardless of arm structure.

## Options analyzed

### Option F: full function-pointer table (`Handlers[op](ref state)`)

- Per-opcode indirect CALL + RET instead of indirect JMP.
- State must live in a byref struct -> every field touched costs a store at
  handler exit and a load at next entry. .NET has no interprocedural register
  allocation; acc/pc cannot stay enregistered across dispatch.
- Handlers must be NoInlining (else the JIT re-inlines the monolith).

### Option H: hybrid - hot-N opcodes inline in switch, rest via table

- Keeps hot path identical to today; moves cold tail behind indirect calls.
- Only wins when the inline set covers the workload's hot opcodes; any miss
  pays the full table-call penalty. Coverage over general JS is always
  partial.

### Option T: V8/Ignition-style direct threading

- Each handler ENDS with its own fetch+jmp copy so the BTB learns
  (prevOpcode,nextOpcode) pairs individually - this is THE reason Ignition
  dispatch is fast.
- Requires computed goto / labels-as-values. **Not expressible in C#.**
  Replicated dispatch (duplicating the switch) is the closest approximation
  but doubles hot-arm code size and still uses one shared table.

### Option K: status quo (switch + A2 cold-split + A4 inline hygiene)

- Hot arms inline with zero cross-opcode spill overhead.
- Cold arms are ALREADY out-of-line direct calls - which dominate table
  calls: no table load, predictable target, same NoInlining semantics.
  A2 effectively built the useful part of Option F.

## E1 measurement (tools/VmDispatchMicrobench, this machine, Release)

Identical synthetic handler bodies; ns per dispatched opcode; median of
repeated passes; two process runs agreed within jitter.

| stream                    | S switch | F table        | H hybrid       |
| ------------------------- | -------- | -------------- | -------------- |
| cycle (8-op repeating)    | 0.50     | 1.45 (+192%)   | 1.29 (+159%)   |
| mixed (uniform, 16 kinds) | 5.52     | 5.18 (-6.3%)   | 6.20 (+12.3%)  |

Reading:

- Loop-shaped opcode streams (the ones that matter for JS perf): the switch
  wins ~3x. Call/ret plus struct round-trips cost ~4-5 cycles/op.
- Uniform-random streams: the table wins slightly via better I-cache
  locality; both styles are dominated by indirect-branch mispredicts there.
  Real JS does not look like this in hot loops.
- Hybrid inherits the table penalty for every uncovered opcode; its value
  depends entirely on coverage - and A2 already provides the equivalent
  effect with cheaper direct calls.

## Decision

REJECT options F/H/T for the engine; KEEP option K (status quo). The
post-A2 architecture (inline hot arms + NoInlining cold handlers + dense
jump table) is the CPU/JIT-friendly optimum reachable in C#:

- direct threading is unexpressible;
- table calls strictly lose to the existing direct cold-handler calls;
- the switch's jump-table lowering is already what a hand-written dispatcher
  would emit.

## Conditional revisit triggers

1. Profiler shows indirect-mispredict stalls concentrated at the dispatch
   jmp AND a workload mix resembling `mixed` (cold-heavy one-shot scripts).
2. A9 opcode renumbering lands: verify the second-level subtable disappears
   (single `cmp ecx,<max>; ja` + one table). Renumbering opcodes to remove
   the gap that splits the table is the cheapest remaining dispatch win and
   belongs to the compiler-contract attempt A9.
3. .NET gains labels-as-values / static tail-dispatch primitives (unlikely).

## Reusable artifact

`tools/VmDispatchMicrobench` - rerun whenever JIT dispatch lowering is in
question (new runtime major versions included). Streams and handler bodies
are intentionally minimal; extend with new styles before trusting old
conclusions on new runtimes.
