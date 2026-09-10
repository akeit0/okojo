# C4: Root-List Completion-Sink Elision

## Scope for this iteration

C4.1: script-root completion-sink traffic whose value is provably
overwritten before observation is elided. The sink (completion register) is
only read at unit end (eval result / embedding Evaluate API); therefore a
root statement's sink traffic is dead when a LATER root statement
unconditionally writes the sink - a statement that guarantees a completion
value (`StatementGuaranteesCompletionValue`) or carries an
`AssignUndefinedBefore` reset (`StatementNeedsCompletionReset`).

Implementation: scan the root statement list backward, find the last
sink-killing statement, and suppress the sink (`CompletionSinkActive`
false) while emitting everything before it. Both the
`AssignUndefinedBefore` resets and the `CaptureCompletionValue` captures
key off `CompletionSinkActive`, so one flag suppresses all traffic.

Not in scope (future): loop-internal sink-write elision (only the final
iteration's writes are live; requires path analysis), module units (their
completion is unobservable - separate decision).

## Minimal JS repros

```js
// stopwatch-modern shape: endTest() kills all earlier sink traffic,
// including the ~5 resets + captures per inner-loop iteration
startTest("dromaeo-3d-cube", "979cd0f1");
test("Rotate 3D Cube", () => Init(20));
endTest();

// kept: last statement carries (no guarantee, no reset - var carries)
1; var x = 2; x; // result 2; the `1` write is dead but the carry is not

// kept: reset wins (V8 rewriter.cc arms-disagree rule)
1; if (false) 2; // result undefined (V8 reference: undefined)

// result: 42 - the loop's sink writes are dead (42 overwrites)
for (let i = 0; i < 2; i++) { i; }
42;
```

## Planned tests

- Focused completion-value tests (`tests/Okojo.Tests`): results must be
  IDENTICAL with and without suppression for guarantee/carry/reset shapes
  (`5; 6;` -> 6, `; 1;` -> 1, `1; ;` -> 1, `for { i; } 42;` -> 42,
  `1; if (false) 2;` -> undefined per V8).
- test262: `language` category sweep + the `cptn-*` completion-value
  assertions in particular.
- Artifact: stopwatch-modern script-unit disassembly - inner-loop
  `LdaUndefined/Star r0` traffic gone.

## Reference observations

- V8 (node 22, eval completion): `1; if (false) 2;` -> undefined (the
  rewriter's arms-disagree reset overrides the carry - Okojo mirrors this
  exactly); `1; ;` -> 1 (UpdateEmpty carry); `for { i; } 42;` -> 42.
- V8 rewriter.cc Processor/AssignUndefinedBefore is the model for the reset
  placement; the elision direction (dead-before-kill) is this engine's
  addition and is semantically neutral by construction.

## Copy vs intentional difference

- Copy: reset placement (V8 rewriter.cc), completion semantics.
- Addition: backward sink-kill elision. V8 keeps the traffic because its
  rewriter works positionally on the AST; Okojo can afford the backward
  scan (root lists only, linear).

## Perf plan

- Hot path: compiler-only; `Run` IL/asm untouched (C1/C2/C3 pattern).
- stopwatch-modern inner loop: ~10-13 dispatches/iteration removed
  (5 resets + 1-2 captures per iteration over 391k iterations).
- Risk: none to `Run` layout; compile-time cost is one backward linear
  scan of the root list.
