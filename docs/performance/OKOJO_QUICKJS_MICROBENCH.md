# QuickJS microbenchmark follow-up

The comparison runs the same JavaScript in each engine on Windows x64
with default GC settings. The Okojo baseline is `bd2203e` on
.NET 10.0.14, the native QuickJS binary is `3d5e064`, Ekuma is
`41a55ec`, and V8 jitless is Node 26.12.1.
[Harness and input revisions](../../benchmarks/EngineComparison/README.md)

The QuickJS 2026-06-04 release has 71 functions in the common timer
plus its self-timed sort benchmark. Ekuma's later pinned script adds
`string_concat`, `string_array_join` and `string_template` for 74
comparable functions. The original QuickJS driver was also run with
selected cases and a 2,000-element sort. It uses its returned operation
counts and adaptive batch sizes. The cross-engine runs use the same
supplied `n` on every engine.
[QuickJS's release suite](https://github.com/bellard/quickjs/blob/3d5e064e9dd67c70f7962836505a7fa067bf0a4e/tests/microbench.js)

The initial two-round screen of Ekuma's script completed 73 of 74
workloads on all engines. The failure was Okojo's `date_parse`, which
calls the legacy `Date.prototype.toGMTString` alias. The common-case
geometric means were 1.951× Ekuma for Okojo, 0.881× for QuickJS, and
0.483× for V8 jitless. The screen used a 150 ms warm-up and a
200 ms sample target. Follow-up timings use longer warm-up.
[ECMAScript Annex B date alias](https://tc39.es/ecma262/multipage/additional-ecmascript-features-for-web-browsers.html#sec-date.prototype.togmtstring)

Okojo's dense append emitted `StaKeyedProperty`, and Ignition emitted
`SetKeyedProperty`. The baseline native profile for
`array_prop_create` had 2,206 instruction-pointer samples. The
generic keyed-store helper accounted for 38.6%, `Run` for 41.8%,
and dense capacity growth for 3.8%. After the existing own-element
probe missed, the store normalized the integer key, entered the generic
array setter, probed own elements again, and checked inherited
indexed descriptors. QuickJS's fast array path can append at the
end of dense storage when its standard prototype guard allows it.
[QuickJS keyed store](https://github.com/bellard/quickjs/blob/3d5e064e9dd67c70f7962836505a7fa067bf0a4e/quickjs.c#L9945)

The first append prototype extended the own-element probe. It
improved `array_prop_create` by 31% in three pairs and made v7
Decrypt 16.5% slower. The retained version performs the append
from the existing keyed-store fallback before key normalization.
It requires a writable length, an extensible dense receiver, and
an element-free prototype chain. Rechecking the live prototype
chain keeps inherited setters and proxy traps observable. The
existing builtin leaf-call convention now also accepts dense
`push` and `pop`. Their generic paths cover exotic receivers,
holes and other cases that need the ordinary property protocol.

Safe integral doubles use invariant integer formatting for both
the string and span APIs. Other numbers use the existing
shortest-decimal conversion. This also applies to callers such
as JSON serialization that format into a destination span.
Multi-argument `String.prototype.concat` can copy short flat
strings into a 256-character stack buffer and allocate the result
once. Coercible arguments and larger concatenations follow the
existing coercion and rope paths.

In the isolated comparison, `array_prop_create` improved from
25.33 to 18.19 µs per unit of `n`. Dense `array_push` and
`array_pop` fell from 27.38 to 16.47 µs and 26.38 to 16.42 µs.
The two text changes alone cut `string_concat` from 953 to
482 ns and `int_to_string` from 204 to 72 ns. These are
three-round medians with a 1,100 ms warm-up and a 720 ms
sample target.

In the append profile after the change, time attributed to the
keyed-store helper fell from about 1.00 ms to 0.27 ms per batch
of 100 arrays. Time attributed to the interpreter loop was about
1.1 ms in both profiles. Managed allocation per call with
`n = 100` fell from 467,912 to 34,392 bytes for `string_concat`
and from 8,856 to 216 bytes for `int_to_string`.

The two-round run of the bundled suite completed 70 of 71 functions
on all engines. The common-case geometric mean of the updated
Okojo timings was 2.30× QuickJS, 2.01× Ekuma and about 4.16×
V8 jitless. This coverage screen used a 125 ms warm-up and
165 ms sample target. The two RegExp execution cases were the
largest remaining gaps to QuickJS in that run, followed by small
BigInt arithmetic and destructuring.

The warmed control runs kept Decrypt within 1% and improved
Richards, Splay, JSON round trips and Dromaeo cube. NavierStokes
took 3.5% longer by median paired time, and the fractional
`float_toString` case took 3.9% longer.
[Warmed engine results](../../benchmarks/EngineComparison/RESULTS.md#quickjs-microbenchmark-follow-up)

The append profile also spent 10.2% of samples in the class
cast helper. The generated prototype guard tried the nonsealed
`JsPlainObject` type before checking the common array prototype.
Checking the sealed `JsArray` type first improved `array_push` by
another 4.1% and `array_prop_create` by 3.6% at the median of
three paired rounds. Decrypt and NavierStokes were each within
1%, and Splay was 1.8% faster in those rounds.

The raw samples, native lists, and allocation probes are under
`artifacts/quickjs-followup/`.

## Array destructuring

The VM trace showed two reads of `next` for a two-element pattern.
V8 Ignition captures the method when acquiring the iterator. Okojo
also captures it at acquisition in an adjacent register. The captured
method stays in use through rest collection. Elisions check completion
without reading an iterator result's `value`, and rest collects values
using own array elements. For `[, value]` on a custom iterator with
getters on both result properties, Okojo, V8 and Ekuma read `done`
twice and `value` once. The pinned QuickJS build read both twice.
[ECMAScript iterator records](https://tc39.es/ecma262/#sec-getiteratorfrommethod)

One runtime call per binding now handles the done state and returns
`undefined` on exhaustion. The same state prevents closing an exhausted
iterator or closing after an iterator step throws. In the probe with
`[a, b, , c, ...rest]`, bytecode shrank from 316 to 132 bytes and
register use fell from 13 to 11. Running the function with one
pattern took 56 dispatches, down from 128. The array portion of the
probe allocated 23,488 bytes per 100 patterns, down from 90,688 bytes.

QuickJS and Ekuma both bypass iterator result allocation for built-in
steps. Okojo does this for the array iterator when the captured callable
uses the built-in native body. Ekuma also has a plain-array path with
an index in the iterator record. Okojo keeps the iterator available
for a live `return` lookup, including methods installed during binding.
[QuickJS iteration](https://github.com/bellard/quickjs/blob/3d5e064e9dd67c70f7962836505a7fa067bf0a4e/quickjs.c#L16560)
[Ekuma iterator opcodes](https://github.com/hajimehoshi/ekuma/blob/41a55ec4e7cda95eb97417fb05a3b4dac7e06a6a/internal/engine/interp.go#L1750)

The first fused build stored the built-in function on each intrinsics
object. Decrypt took 8.9–12.5% longer in three paired rounds with
identical emitted bytecode. Using the shared host-function body as
the identity check avoided the additional field. In a separate
three-round Okojo-only screen, median Decrypt times were 85.75 ms
before the change, 90.57 ms with the additional field, and 82.63 ms
with the shared body. One Tier1-OSR capture showed the extra field
shifting intrinsic offsets and reordering the date-subtraction guards
in `Run`. The overall code size stayed the same in that capture.
The exact cause of the timing difference remains unverified.
[Warmed engine results](../../benchmarks/EngineComparison/RESULTS.md#array-destructuring)

## Synchronous iteration review

The old array `for...of` path chose indexed iteration from the source
type and length. A custom `Symbol.iterator` therefore went unused, and
exiting early skipped `return`. The regression probe returned `1,3,0`
before the fix and `5,3,1` in V8 and the shared-record build. Ignition
also captures `next` before entering the loop and looks up `return`
when closing. [ECMAScript loop evaluation](https://tc39.es/ecma262/multipage/ecmascript-language-statements-and-declarations.html#sec-runtime-semantics-forin-div-ofbodyevaluation-lhs-stmt-iteratorrecord-iterationkind-lhskind-labelset)

The compiler emits one loop body with one exception handler for the
loop. The shared runtime step records completion and step failures so
that cleanup skips exhausted iterators and preserves the original
exception. A ten-element sum went from 158 to 84 executed bytecodes.
The first shared-record build used one handler per iteration and
took about 4.4% longer in the two-round `array_for_of` screen.

The review also found that delegated generator elisions still read
`value`, array `keys()` read indexed getters, and array-like iterator
lengths wrapped at 2³². Elision now carries the skip through delegated
steps. Array iterators keep the full array-like length, read elements
only for values and entries, and define entries as own properties.

Ekuma stores the index directly in the VM's iterator record for plain
arrays and branches to the loop exit from its step opcode. Okojo
uses an indexed record when acquisition finds both built-in methods
and the iterator prototype has a data property for `next`. Steps read
the live array length and consult the ordinary property path for
holes or indexed accessors. An early close creates a real iterator at
the current index before looking up `return`. A `next` getter can
observe the iterator during acquisition, so that path creates it
immediately. QuickJS keeps a real iterator and its captured
`next` method in its loop record.
[Ekuma iterator opcodes](https://github.com/hajimehoshi/ekuma/blob/41a55ec4e7cda95eb97417fb05a3b4dac7e06a6a/internal/engine/interp.go#L1750)
[QuickJS for-of helpers](https://github.com/bellard/quickjs/blob/3d5e064e9dd67c70f7962836505a7fa067bf0a4e/quickjs.c#L16679)

With a real iterator on every path, one handler per loop cut
`array_for_of` time by 6.8%. Moving the done-state write to a CLR
exception handler cut it by 5.1% in its own three-round screen.
The indexed version retained the single loop handler and the
register marker for failed steps.

The allocation probe fell from 23,488 to 16,288 bytes per 100 array
patterns. NavierStokes took 5.0% longer by paired median with identical
emitted bytecode. Its captured Tier1-OSR `Run` listing had the same
27,656 bytes and the same instruction sequence after normalizing
addresses and generated local function names. The native cause of
the timing difference remains unverified.
[Warmed engine results](../../benchmarks/EngineComparison/RESULTS.md#synchronous-iteration)

Merge review extended iterator method lookup to generator delegation,
including async generators. The lookup passes the original receiver
to property getters and honors replacements for `Symbol.iterator`
and `Symbol.asyncIterator`. Synchronous generator methods also check
the receiver's generator kind before resuming it.
