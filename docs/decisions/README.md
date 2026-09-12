# Decisions

Status vocabulary: `accepted | implemented | rejected | superseded`.
A decision records the question, the evidence, and what would reopen it.

| Decision | Verdict | Reopen on |
|---|---|---|
| [OKOJO_A7_DISPATCH_DESIGN.md](OKOJO_A7_DISPATCH_DESIGN.md) — keep giant `switch`, reject function-pointer/threaded dispatch | implemented (KEEP-switch; A2 cold-split is the better table-beater) | new codegen substrate (e.g. a backend where direct threading is expressible without indirect-call spill) plus loop-shaped microbench evidence |
| No opcode-set expansion (old A8/A9 policy closing fusion work) | **superseded** — explicitly not carried into the redesign; see `../proposals/bytecode-isa-layout.md` §E | n/a (replaced, not pending) |
| Indiscriminate collection pooling / scanner experiments (allocation profile) | rejected with evidence | a corpus where collection mgmt reappears as the measured top cost |
| `EnsureCapacity(0)` hot-path trim | rejected | same as above |

Rejected stays recorded so it is not silently retried (`AGENTS.md` discipline).
