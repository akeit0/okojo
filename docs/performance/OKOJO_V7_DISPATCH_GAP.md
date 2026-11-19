# V8 v7 dispatch comparison

This pass starts from Okojo `main` at `c3a5ca1`. The baseline runner
uses the same engine code built during integration at `72cae11`.
The local update instruction is committed at `ec06f6b`. The comparison
uses Ekuma `41a55ec` built with Go 1.27.4 and Node 26.12.1 with
`--jitless`. Each engine runs the same pinned v7 JavaScript.
[Workload inputs and timing harness](../../benchmarks/EngineComparison/README.md)

## Fixed-work opcode profile

These counts include setup and five calls to each workload. Ekuma's
instrumented build increments a counter at the opcode switch. Okojo
reports fetches after subtracting folded stores. Node's release build
does not expose Ignition dispatch counters, so its comparison uses the
emitted bytecode and CPU profiles.

| Workload | Okojo baseline | Ekuma | Okojo / Ekuma | Okojo with local updates | Removed |
| --- | ---: | ---: | ---: | ---: | ---: |
| Richards | 4,070,499 | 4,283,971 | 0.95× | 4,061,139 | 0.23% |
| DeltaBlue | 6,720,555 | 6,343,445 | 1.06× | 6,563,785 | 2.33% |
| Encrypt | 14,241,716 | 6,510,833 | 2.19× | 12,232,592 | 14.11% |
| Decrypt | 267,334,829 | 121,587,246 | 2.20× | 229,832,433 | 14.03% |
| RayTrace | 25,919,112 | 18,376,793 | 1.41× | 25,790,052 | 0.50% |
| Earley | 4,947,041 | 2,730,710 | 1.81× | 4,871,855 | 1.52% |
| Boyer | 93,422,567 | 61,440,423 | 1.52× | 93,407,337 | 0.02% |
| RegExp | 13,134,207 | 8,127,919 | 1.62× | 12,272,169 | 6.56% |
| Splay | 17,088,643 | 18,598,651 | 0.92× | 17,071,843 | 0.10% |
| NavierStokes | 163,680,929 | 93,853,433 | 1.74× | 130,894,809 | 20.03% |

In NavierStokes, both engines executed about 16.39 million increments
and 17.13 million indexed reads. The main difference is the register
traffic around those operations. In Decrypt, Ekuma issued 11.15
million constant-mask and 11.14 million constant-right-shift
instructions. The Okojo baseline loads those constants separately.
Splay was already below Ekuma's dispatch count and still took longer.
Ekuma's Splay profile included substantial GC scanning, and V8
jitless's samples clustered around payload creation and GC.
[Ekuma opcode implementations](https://github.com/hajimehoshi/ekuma/blob/41a55ec4e7cda95eb97417fb05a3b4dac7e06a6a/internal/engine/interp.go)

## Native code and bytecode

- In Ekuma's amd64 object dump, the central switch scales the PC by
  16 to find a fixed-width instruction, loads the opcode byte, and
  jumps through an eight-byte entry. The numeric increment checks the
  value's object pointer, adds one in a floating-point register, and
  writes the destination register. A GC write-barrier check guards
  clearing the destination's object field.
  Ekuma's Decrypt profile assigned 1.81 of 5.59 sampled CPU seconds
  (32.4%) to the shared path from the loop join through the indirect
  jump, including stack spills and bounds checks.
- In the V8 jitless binary, `LdarHandler` loads the accumulator from a
  frame slot and jumps to the next handler through the dispatch table.
  Its measured body is 36 bytes, including padding. The reduced update
  case uses separate load, arithmetic, and store bytecodes. JS CPU
  samples attributed 82.9% of Decrypt to `am3` and 67.9% of
  NavierStokes to `lin_solve`.
  [Ignition handler source](https://github.com/v8/v8/blob/main/src/interpreter/interpreter-generator.cc)
- In the Okojo baseline's Decrypt native sample, 2,054 of 2,184
  instruction pointers landed inside the optimized `Run` loop.
  The shared opcode fetch and countdown block had 237 samples and
  the indirect jump block had 242, together 21.9% of all samples.
  The keyed get and set slow paths together had 16 samples.
  Ekuma's own `exec` had 93.7% flat CPU time in its separate
  Decrypt profile.

## Local updates

`UpdateLocal` reads and writes the same register and returns the
appropriate prefix or postfix value in the accumulator. It applies to
initialized mutable register bindings. The existing paths cover captured
bindings, TDZ and immutable writes, and registers above the single-byte
operand range. Postfix returns the original value after numeric coercion,
including BigInt and signed zero. The Int32 path writes the payload
directly to its original register. The previous Int32 tag guarantees
that slot contains a null object reference.

In the reduced loop with `--n`, `data[i++]`, and `data[++i]`, the prototype
replaces three register-transfer sequences with three updates. Ekuma
also encodes source and destination in the update. Its postfix form
uses a separate conversion into a register that remains the index.
The Okojo Tier1-OSR update emits a scalar store for the local slot and
scalar accumulator writes. The cold path calls the coercion helper.

The first version copied a full `JsValue` into the local. In three pairs
it improved NavierStokes by 3.2%, while Decrypt and Encrypt took 2.6%
and 5.7% longer. The scalar-store version uses the same bytecode and
dispatch counts. Its first three untraced pairs improved NavierStokes
by 14.5%, Decrypt by 3.0%, and Encrypt by 3.6%.

## Whole-workload measurements

Five rotating rounds on Windows 11, an Intel Core i7-13700F, and
.NET 10.0.14 in Release mode. Each process warmed up for 1,050 ms
with a 775 ms sample target. Times are median milliseconds per
invocation. The change column uses median paired time ratios.
Dromaeo cube is a separate control outside the v7 mean.

| Workload | Okojo baseline | Okojo local updates | Ekuma | V8 jitless | Okojo time change |
| --- | ---: | ---: | ---: | ---: | ---: |
| NavierStokes | 62.231 | 53.333 | 46.176 | 45.222 | −14.3% |
| Decrypt | 87.000 | 83.700 | 61.923 | 47.438 | −3.8% |
| Encrypt | 4.595 | 4.503 | 3.396 | 2.519 | −2.5% |
| Richards | 3.247 | 3.067 | 2.904 | 1.429 | −6.5% |
| Dromaeo cube | 2.219 | 2.220 | 1.502 | 1.470 | +0.0% |
| RegExp | 73.364 | 72.818 | 35.348 | 17.174 | −0.9% |
| RayTrace | 39.200 | 35.500 | 19.513 | 10.038 | −9.4% |
| DeltaBlue | 6.937 | 7.155 | 4.974 | 2.265 | +2.8% |
| Boyer | 113.429 | 113.571 | 70.182 | 26.448 | +0.2% |
| Earley | 5.815 | 5.769 | 4.062 | 1.365 | +0.0% |
| Splay | 4.790 | 4.822 | 1.474 | 0.630 | +0.3% |

The v7 geometric mean of Okojo's paired time ratios was 0.965× the
baseline. Okojo used 1.57× Ekuma's time and 2.97× V8 jitless's
time by the same calculation. NavierStokes, Decrypt, and Encrypt
improved in every pair. DeltaBlue slowed in every pair.

In a separate control, both bytecode versions ran in the same native
interpreter process with tiered compilation disabled. A compiler switch
selected the bytecode before execution. Six alternating pairs gave
median changes of −11.7% for NavierStokes, −5.3% for Decrypt, +0.5%
for DeltaBlue, and +0.3% for Richards. NavierStokes and Decrypt
improved in all six pairs. The larger Richards and DeltaBlue changes
from the cross-engine run did not reproduce in this control.

The opcode counts, native samples, and assembly listings are under
`artifacts/v7-dispatch-gap/`. The raw timing report is
`artifacts/frame-study/v7-update-bits-v7-main.json`.
