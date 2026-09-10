# Feature note template

Status: guide (required by `AGENTS.md` feature-kickoff rule).
Copy this shape into `docs/` for every new Okojo feature (syntax or runtime).
Delete sections that do not apply; keep the reference observations — they are
the point.

```markdown
# <Feature>: <one-line scope>

Status: proposed | accepted | implemented.
Scope for this iteration: <what is in, what is explicitly out>

## Minimal repros

```js
// before/after behavior, observable output only
```

## Planned tests

- `<test file target>` — <what it covers>

## Reference observations

- V8 (language/compiler/VM): <behavior or bytecode shape observed>
- Node (built-ins/runtime APIs): <behavior observed via `node -e`>
- QuickJS (optional design input): <idea, not behavior proof>

## Copy vs intentional difference

<what matches the reference and what deliberately differs, with reasons>

## Perf plan

Hot path: <...>. Slow path: <...>. Allocation risks: <...>.

## Deferred

<items moved to TODO.md with reasons>
```
