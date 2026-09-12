# Feature implementation workflow

Status: guide (required by `AGENTS.md` feature-kickoff rule).

1. Write the feature note first, from
   [OKOJO_FEATURE_NOTE_TEMPLATE.md](OKOJO_FEATURE_NOTE_TEMPLATE.md):
   scope, minimal JS repros, planned test targets, V8/Node reference
   observations, copy-vs-intentional-difference, perf plan.
2. Before debugging or optimizing non-trivial behavior, collect references:
   Okojo bytecode (`tools/OkojoBytecodeTool`), VM trace, V8 Ignition
   behavior (`tools/V8BytecodeTool`, `node --print-bytecode`) for
   language/compiler/VM questions, `node -e` for builtin/runtime API
   questions, optionally QuickJS for design ideas. Record copy-vs-difference
   briefly.
3. Implement parser/compiler/runtime changes, hot path first, uncommon
   semantics on explicit slow paths. Keep frame/opcode conventions stable
   unless the change explicitly renegotiates them (see
   `../proposals/README.md`).
4. Add or keep regression tests for every fixed failure. Prefer
   browser-compatible observable behavior over convenience shortcuts.
5. If the change is a substantial API, compatibility, or compliance roadmap
   shift, update the top-level anchor (`OKOJO_BROWSER_COMPATIBILITY_PLAN.md`)
   in the same work.
6. Format changed `.cs`/`.csproj` files with
   `dotnet csharpier format <changed files>`; follow the focused-first test
   workflow in `AGENTS.md`. Deferrals go to root `TODO.md`.
