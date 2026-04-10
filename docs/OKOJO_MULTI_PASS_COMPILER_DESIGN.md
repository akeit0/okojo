# Okojo Multi-Pass Compiler Design

## Purpose

This document defines the target compiler structure for Okojo.

The goal is to replace the current mixed model, where binding discovery, capture decisions, storage decisions, register allocation, and bytecode emission still overlap too much in one flow.

Primary outcomes:

- cleaner compiler invariants
- less conservative state carried into emit
- better uncaptured-local register reuse
- clearer closure/context-slot planning
- fewer ad hoc fixes for block, catch, and loop alias bindings

## Current Problem

The current compiler still does too much at once:

- local bindings are often created during predeclare and emit
- alias scopes are discovered early but storage is partly decided later
- capture information depends on currently allocated locals in some paths
- context-slot assignment mixes:
  - current local registers
  - captured flags
  - alias fallback rules
  - special-case storage visibility checks
- register allocation for many locals happens before true liveness is known

This creates three classes of problems:

1. correctness risk

- captured alias bindings can be visible to nested functions before the compiler has assigned their closure storage cleanly
- catch/block/loop aliases need conservative follow-up fixes

2. optimization limits

- uncaptured block locals are pinned too early
- register reuse is weaker than it should be

3. code clarity issues

- binding existence and register existence are conflated
- context-slot assignment depends on emit-era state that should already be planned

## Design Principle

Parser stays syntax-only.

The compiler should become explicitly multi-pass:

1. syntax and scope discovery
2. binding collection
3. capture and storage classification
4. storage planning
5. register allocation
6. bytecode emission

The compiler should emit from a plan, not discover the plan while emitting.

## V8 Reference Observations

V8 is a useful reference here because its frontend already separates the concerns Okojo is currently mixing.

Relevant observations:

- V8 models unresolved identifier references separately from later variable allocation.
- V8 scope analysis resolves identifiers and assigns storage before Ignition bytecode generation.
- Ignition bytecode generation still performs local temporary register management, but on top of a precomputed variable/storage plan.
- V8 also keeps some late decisions local to bytecode generation when they are emit-specific rather than semantic, for example some hole-check elision bookkeeping and temporary register scopes.

Concrete references:

- V8 `scopes.h` states that after AST construction, most `VariableProxy` nodes are unresolved, and variable allocation later binds them and assigns locations:
  [src/ast/scopes.h](https://chromium.googlesource.com/v8/v8/%2B/refs/heads/11.9.135/src/ast/scopes.h)
- The same file exposes explicit scope-analysis and allocation steps such as `ResolveVariablesRecursively`, `MustAllocate`, `MustAllocateInContext`, and `AllocateVariablesRecursively`:
  [src/ast/scopes.h#598](https://chromium.googlesource.com/v8/v8/%2B/refs/heads/11.9.135/src/ast/scopes.h#598)
- Ignition bytecode generation uses scoped temporary register allocation during emit, for example `RegisterAllocationScope`, `VisitDeclarations`, and per-statement allocation scopes:
  [src/interpreter/bytecode-generator.cc#509](https://chromium.googlesource.com/v8/v8.git/%2B/refs/heads/12.0.78/src/interpreter/bytecode-generator.cc#509)
  [src/interpreter/bytecode-generator.cc#1599](https://chromium.googlesource.com/v8/v8.git/%2B/refs/heads/12.0.78/src/interpreter/bytecode-generator.cc#1599)
  [src/interpreter/bytecode-generator.cc#1605](https://chromium.googlesource.com/v8/v8.git/%2B/refs/heads/12.0.78/src/interpreter/bytecode-generator.cc#1605)
- V8’s preparser work also shows that early variable metadata is valuable before full compilation, but still as compiler/frontend metadata, not parser-only syntax output:
  [Blazingly fast parsing, part 2: lazy parsing](https://v8.dev/blog/preparser)
- V8’s background compilation note is another useful signal: bytecode compilation is cleaner when heap-dependent finalization is deferred and the compiler carries structured metadata through later stages:
  [Background compilation](https://v8.dev/blog/background-compilation)

What Okojo should copy from this:

- early binding and scope metadata
- explicit resolution and storage planning before bytecode emit
- scoped temporary allocation during bytecode generation

What Okojo should not copy blindly:

- exact V8 class structure
- every V8 scope/category split
- V8-specific lazy parsing and preparser machinery unless Okojo actually needs it

## What Should Not Move Into The Parser

The parser should not own:

- capture semantics
- closure/context-slot policy
- register allocation
- block alias lowering strategy
- host/runtime storage decisions

Those are compiler responsibilities, not syntax responsibilities.

The parser should continue to provide:

- AST
- source spans
- identifier table / name ids
- statement/expression shape
- simple syntax-derived flags that are purely structural

## Target Passes

## Pass 1: Scope And Binding Discovery

Input:

- AST
- identifier table

Output:

- function scope tree
- block scope tree
- binding declarations by scope
- alias-binding declarations for lowered constructs
- structural flags for later passes

Collect here:

- root/local declarations
- parameter bindings
- block `let` / `const`
- class lexical bindings
- catch bindings
- switch lexicals
- `for` / `for-in` / `for-of` head bindings
- lowered alias bindings with explicit binding kind
- whether a scope body may create nested functions
- whether a function has parameter expressions
- whether a function uses `super`, `new.target`, `arguments`, `this`, generators, async, etc.

Do not do here:

- register allocation
- context-slot assignment
- bytecode emission

### Binding metadata needed from this pass

Each binding should have stable metadata, for example:

- `SymbolId`
- source name / internal lowered name
- declaring scope id
- binding kind:
  - parameter
  - var
  - lexical
  - function name self-binding
  - block alias
  - loop-head alias
  - catch alias
  - class lexical alias
  - synthetic internal
- flags:
  - const
  - function-hoisted
  - switch lexical
  - repl/global special cases

This is the first key cleanup.

Today, some of this lives in many separate tables. It should become one coherent compiler binding model.

This is directly aligned with the V8 idea that references and variables are separate frontend objects until later resolution/allocation, rather than being fused during emit.

## Pass 2: Name Resolution And Capture Discovery

Input:

- AST
- scope tree
- binding metadata

Output:

- read/write resolution for identifiers
- captured-by-child classification
- ancestor capture edges
- unresolved/global/module resolution results

Collect here:

- for each identifier reference:
  - which binding it resolves to
  - whether it crosses a function boundary
  - whether it crosses an alias boundary
- for each binding:
  - whether it is captured by child code
  - whether it must remain visible in nested function compilation

Important rule:

Binding resolution must not depend on whether a register has already been allocated.

Binding existence and register existence are separate concepts.

That single rule removes the current class of bugs where scope-only aliases are missed because they were not preallocated in `locals`.

This is the most important practical lesson from V8’s separation of unresolved `VariableProxy` nodes and later variable allocation.

## Pass 3: Storage Classification

Input:

- binding metadata
- capture graph
- function structural flags

Output:

- binding storage class

Each binding should now be classified as one of:

- no runtime storage
- root/local register candidate
- alias-scope register candidate
- current-context-slot-backed local
- captured-context binding
- module/global binding

This pass decides:

- whether the binding needs a context slot
- whether the binding may stay register-only
- whether the binding needs both:
  - context slot as authoritative closure storage
  - temporary visible register while scope is active

Examples:

- uncaptured block alias:
  - alias-scope register candidate
- captured catch alias:
  - context slot required
  - temporary visible register while active
- parameter captured by nested function:
  - context slot required
- simple uncaptured `var` local:
  - root/local register candidate

This is where conservative helper sets like forced alias context-slot tracking should disappear.

## Pass 4: Storage Planning

Input:

- storage classification
- scope tree
- special semantics flags

Output:

- context-slot layout
- alias activation plan
- per-iteration context plan
- prologue initialization plan

Decide here:

- exact context-slot indices
- which bindings participate in per-iteration environments
- which bindings get TDZ initialization in:
  - context slots
  - visible registers
- which alias scopes allocate visible registers on entry and release them on exit

This pass should produce explicit plans such as:

- function context slot table
- alias scope activation records
- per-scope visible binding table

Then emit does not need to ask "should this binding have a slot?" because the answer is already in the plan.

## Pass 5: Register Allocation

Input:

- storage plan
- AST
- liveness-relevant use information

Output:

- pinned register assignments where truly required
- temporary register live-range plan
- scope-local register reuse plan

The important split:

- root locals that must stay stable for the full function
- scope-bound locals that can reuse registers
- expression temporaries

Recommended allocation strategy:

1. allocate pinned registers only for:
   - parameters when required
   - long-lived synthetic locals
   - special VM ABI locals
2. allocate scope-local visible registers for alias scopes on scope entry
3. reuse registers for disjoint block alias scopes
4. reuse temporary registers aggressively for expression lowering

This pass is where current register pressure should improve most.

## Pass 6: Bytecode Emission

Input:

- binding resolution plan
- storage plan
- register allocation plan

Output:

- bytecode
- debug info
- local storage debug mapping

Emit responsibilities become simpler:

- consult planned binding storage
- use planned register/slot indices
- emit alias scope entry/exit initialization
- emit bytecode without creating new locals as a side effect

Emit should not:

- discover bindings
- decide closure storage
- decide whether a binding is capturable
- allocate long-lived locals opportunistically

This matches the V8 shape well: bytecode generation still owns local temporary scopes and emit-specific optimizations, but it does not own the fundamental decision of what each variable is and where it lives.

## What Early Passes Can Collect That Helps Later Passes

The early passes can collect more than names.

Useful early-collection data:

- scope tree with stable scope ids
- binding declarations by scope
- identifier reference lists by function/scope
- body flags:
  - may create nested function
  - may capture outer names
  - uses `arguments`
  - uses `super`
  - uses `new.target`
  - uses `this`
  - has direct control-flow joins
- alias-origin metadata:
  - block alias
  - loop-head alias
  - catch alias
  - class alias
- declaration lifetime hints:
  - function-wide
  - block-local
  - per-iteration
- lexical initialization requirements:
  - TDZ
  - initialized in declaration
  - initialized in special prologue

These collections help later passes by letting them:

- assign context slots deterministically
- avoid depending on live `locals` dictionaries during capture logic
- allocate fewer pinned registers
- emit accurate debug info without reconstructing meaning late

## Recommended Core Data Structures

## ScopeInfo

Should contain:

- scope id
- parent scope id
- function id owner
- scope kind
- declared binding ids
- active alias binding ids

## BindingInfo

Should contain:

- binding id / symbol id
- declared name
- lowered internal name if any
- binding kind
- declaring scope id
- storage class
- flags
- context slot index if assigned
- pinned register index if assigned
- scope-activation register index if assigned

## ReferenceInfo

Should contain:

- reference site
- source identifier
- resolved binding id or module/global target
- read/write/update kind
- crosses function boundary or not

## AliasScopePlan

Should contain:

- scope id
- active binding ids
- entry initialization actions
- exit release actions

## Compile-Time Allocation Strategy

The multi-pass design should not regress compile-time allocation behavior.

Okojo should aim for:

- flatter compiler metadata
- fewer per-binding heap objects
- fewer transient `List<T>` / `Dictionary<TKey, TValue>` allocations
- more array-backed or pool-backed storage

## General Rules

1. Prefer `struct` or `record struct` for compact compiler metadata that is copied or stored densely.
2. Prefer indexed arrays or pooled buffers over nested object graphs for per-function compiler state.
3. Prefer one or two growable pooled arrays plus integer ids over many small dictionaries/lists.
4. Rent per-compile storage from Okojo’s existing compile pools where practical.
5. Use `ArrayPool<T>` for large temporary arrays or scratch buffers that are not naturally handled by current `RentCompile*` helpers.
6. Keep emit-time scratch storage separate from long-lived per-function analysis storage.

## Where Structs Help

Good candidates for compact value types:

- `BindingInfo`
- `ScopeInfo`
- `ReferenceInfo`
- `AliasScopePlan`
- `StoragePlanEntry`
- `RegisterPlanEntry`

These should be mostly id-based:

- `BindingId`
- `ScopeId`
- `ReferenceId`

Instead of storing many string references and many nested objects directly.

## Where Arrays Help

Prefer arrays or pooled buffers for:

- bindings by id
- scopes by id
- references by id
- context-slot maps by binding id
- register assignment maps by binding id
- per-scope binding ranges

Example shape:

- `BindingInfo[] bindings`
- `ScopeInfo[] scopes`
- `ReferenceInfo[] references`
- `int[] bindingContextSlot`
- `int[] bindingPinnedRegister`
- `int[] bindingActiveRegister`

This is usually better than:

- many small `Dictionary<int, T>`
- many `List<T>` instances per scope
- linked object graphs

## Where Dictionaries Still Make Sense

Dictionaries should stay only on lookup-heavy boundaries, for example:

- source-name/id to binding-id maps for a scope
- debug-name lookup where sparse access matters
- module/global resolution tables

Even there, the dictionary should usually point to ids in flat arrays, not to richer heap objects.

## Existing Pooling Direction

Okojo already has compile-pool helpers such as:

- `RentCompileList<T>`
- `RentCompileDictionary<TKey, TValue>`
- `RentCompileHashSet<T>`
- `RentCompileStack<T>`

The next refinement is not “allocate more pooled collections everywhere”.

It is:

- use fewer distinct collections
- make the core data denser
- pool the backing arrays for the denser model

## Recommended Storage Model

### 1. Per-function analysis arena

For each function compile, keep one main analysis bundle containing:

- pooled binding array
- pooled scope array
- pooled reference array
- pooled side arrays for flags, slot indices, register indices

This is the long-lived compile state for that function.

### 2. Emit scratch arena

Separate short-lived emit scratch state:

- temporary register scratch arrays
- argument preparation scratch buffers
- destructuring scratch buffers
- local branch merge scratch state

These should be easy to clear or return after emit.

### 3. Debug-info side tables

Keep debug-oriented metadata separate from the hot compiler core structures, so debug support does not bloat the normal compile path.

## Roadmap For Allocation Cleanup

### Stage 1: Metadata flattening

Introduce:

- `BindingId`
- `ScopeId`
- `ReferenceId`
- dense `BindingInfo[]`
- dense `ScopeInfo[]`

Keep current logic, but stop spreading meaning across many maps.

Expected benefit:

- cleaner invariants
- less dictionary churn
- easier later passes

### Stage 2: Replace binding-state dictionaries with id-indexed arrays

Target replacements:

- `localBindingInfoById`
- parts of `locals`
- captured-flag side sets
- forced alias context-slot side sets

Expected benefit:

- lower allocation count
- fewer hash lookups on hot compile paths

### Stage 3: Alias-scope plan arrays

Instead of carrying alias state as ad hoc lists/maps:

- precompute alias scope entries
- store binding ranges or binding ids in pooled arrays

Expected benefit:

- cleaner scope activation/deactivation
- less conservative runtime state

### Stage 4: Liveness and register plan arrays

Represent:

- live intervals
- temporary scope boundaries
- scope-bound visible register assignments

with compact arrays, not object-per-interval style structures.

Expected benefit:

- easier register reuse
- lower compile-time GC pressure

### Stage 5: Trim emit-time heap churn

Audit common emit paths such as:

- destructuring
- argument spreading
- object/array literal construction
- property assignment lowering
- loop lowering

Replace repeated temporary small-list allocations with:

- stackalloc where tiny and bounded
- pooled arrays where larger or data-dependent

## Clean-Code Rule For This Work

Do not trade allocation reduction for unreadable compiler code.

Preferred pattern:

- use dense storage under a small number of explicit helper types
- keep pass boundaries clear
- keep mutation localized

Avoid:

- sprinkling raw pooled arrays everywhere without ownership rules
- passing many parallel arrays through unrelated code
- hiding compiler invariants inside too-clever pooling helpers

The clean target is:

- explicit pass types
- explicit id-based metadata
- explicit pooled storage ownership

## Concrete Near-Term Implementation Plan

1. Introduce `BindingKind`, `StorageKind`, and a denser `BindingInfo`.
2. Add a first-class early binding-collection pass object.
3. Move alias declaration data into flat binding/scope metadata.
4. Add a storage-planning pass that produces context-slot and alias-activation plans.
5. Replace conservative alias side sets with planned storage.
6. Introduce id-indexed arrays for binding state.
7. Add a register-allocation pass for:
   - pinned locals
   - scope-activated locals
   - expression temporaries
8. Audit high-churn emit helpers and convert the worst offenders to pooled/stack storage.

## What Can Be Simplified Once This Exists

These current patterns should be removable or smaller:

- `GetOrCreateLocal(...)` during predeclare for alias bindings
- `HasLocalBinding(...)` as a capture proxy
- fallback-only sets for forced context slots
- ad hoc context-slot assignment from currently allocated locals
- mixed "resolve name and allocate register" flows

## Migration Plan

Recommended incremental order:

1. Introduce explicit `BindingKind` and richer `BindingInfo`.
2. Move declaration/alias collection into an explicit early binding pass.
3. Change capture discovery to work from binding metadata, not allocated locals.
4. Add explicit storage classification and context-slot planning.
5. Replace current alias fallback state with planned storage.
6. Move register allocation onto the planned binding/storage model.
7. Trim emit-time binding allocation paths.

This lets Okojo improve without a single risky compiler rewrite.

## Immediate Near-Term Goal

Short-term target after the current register-reuse slice:

- keep the scope-activated alias register model
- remove conservative forced alias context-slot handling
- replace it with binding-metadata-driven storage planning

That is the first meaningful step from the current mixed model toward a clean multi-pass compiler.

## Current Implemented Slice

Implemented now:

- `Okojo.Compiler.Experimental`
  - owns the new planning/emitting pipeline
- `CompilerBindingCollector`
  - early scope/binding collection
  - identifier reference collection
  - pooled dense metadata
  - no longer used by production `JsCompiler`
- `CompilerStoragePlanner`
  - separate storage classification pass over collected bindings
  - first capture-aware planning step
  - marks bindings referenced across function boundaries as `ContextSlot`
  - does not mutate `JsCompiler` binding state
- `JsPlannedScriptCompiler`
  - separate experimental emitter/compiler
  - lives in `Okojo.Compiler.Experimental`
  - does not patch the new planned pipeline into `JsCompiler`
  - split into partial files to keep the new emitter readable
  - emits real executable Okojo bytecode for a small but growing subset
- `JsPlannedFunctionCompiler`
  - first experimental function-scope compiler entry point
  - split into partial files to keep growth isolated
  - currently supports a small planned function-body subset

Important boundary:

- `JsCompiler` remains the production compiler
- the new planned pipeline is intentionally partial and separate
- this allows a real second compiler path to grow before any replacement attempt

## Current Experimental Compiler Subset

`JsPlannedScriptCompiler` currently supports only:

- top-level `var` / `let` / `const` declarations without patterns
- function declarations without closure capture
- block statements
- `if`
- expression statements
- literals:
  - `null`
  - booleans
  - small integers
- identifier reads of planned root bindings
- binary `+`
- comparison operators:
  - `==`
  - `===`
  - `<`
  - `>`
  - `<=`
  - `>=`
- assignment forms:
  - `=`
  - `+=`
  - `-=`

It intentionally does not yet support:

- closure capture
- context-slot emission
- globals/module bindings
- destructuring
- property access
- calls

This is intentional. The current purpose is to prove:

1. collected bindings can be planned without `JsCompiler` state mutation
2. planned storage can drive real bytecode emission
3. a new compiler path can execute real Okojo scripts independently
4. capture/context planning can advance before closure emission is implemented
