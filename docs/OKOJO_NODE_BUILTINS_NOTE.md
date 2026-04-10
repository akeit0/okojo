# Okojo Node Built-Ins Note

## Scope

This note covers the first practical built-in module/global slice in `Okojo.Node`.

Included in this iteration:

- built-in module resolution for:
  - `node:path`
  - `node:process`
  - `node:buffer`
  - `node:events`
  - bare aliases:
    - `path`
    - `process`
    - `buffer`
    - `events`
- global `process`
- global `Buffer`
- `process.nextTick`
- `setImmediate`
- `clearImmediate`
- minimal `EventEmitter`
- `node:tty`
- `node:util`
- `node:stream`
- `process.stdout` / `process.stderr` write streams with minimal TTY control methods
- fixed-layout built-in object creation using cached shapes
- dictionary-mode only for dynamic host data such as `process.env`

Not included in this iteration:

- `node:module`
- `node:timers`
- full Node `events` helpers beyond `EventEmitter`
- full Node `stream` compatibility
- full `util.inspect` / `util.format` compatibility
- full tty stream behavior beyond minimal write/cursor/clear methods

## Minimal JS Repros

```js
const path = require("node:path");
path.join("a", "b");
```

```js
const processMod = require("node:process");
process === processMod;
process.cwd();
process.env.MY_FLAG = "1";
process.nextTick(() => {});
```

```js
const { EventEmitter } = require("node:events");
const emitter = new EventEmitter();
emitter.on("ready", value => value);
emitter.emit("ready", 1);
```

```js
const util = require("node:util");
util.format("%s:%d", "tty", 120);
util.inspect({ a: [1, 2] });
process.stdout.cursorTo(2);
process.stdout.clearLine(0);
```

```js
const { PassThrough } = require("node:stream");
const pass = new PassThrough();
let chunks = "";
pass.on("data", chunk => { chunks += chunk.toString(); });
pass.write("ab");
pass.end("c");
```

## Planned Tests

- `tests/Okojo.Node.Tests/NodeCommonJsTests.cs`
  - `node:path` join / dirname / basename / extname
  - bare `path` alias
  - `process === require("node:process")`
  - `process.cwd()`
  - `process.env` remains writable/dynamic
  - `process.nextTick` before Promise jobs
  - minimal `node:events` EventEmitter in CommonJS and ESM
  - minimal `node:stream` constructors and `PassThrough` data flow
  - minimal `node:util` in CommonJS and ESM
  - tty stream methods on configured process streams

## Reference Observations

Primary references:

- [Node.js Process](https://nodejs.org/api/process.html)
- [Node.js Path](https://nodejs.org/api/path.html)
- [Node.js Buffer](https://nodejs.org/api/buffer.html)
- [Node.js Events](https://nodejs.org/api/events.html)
- [Node.js Util](https://nodejs.org/api/util.html)
- [Node.js Stream](https://nodejs.org/api/stream.html)
- [Node.js TTY](https://nodejs.org/api/tty.html)
- [Node.js Packages](https://nodejs.org/api/packages.html)
  - `node:` imports

Observable behavior copied in this slice:

- built-in module specifiers are resolved before package filesystem resolution
- `node:process` exports the process object
- `process` is also a global
- `Buffer` is also a global
- `process.nextTick` jobs run before Promise jobs in the same turn
- `setImmediate` runs as host-task work after the current microtask drain
- `node:events` ESM default export is the `EventEmitter` constructor
- `node:util` exposes `format` / `inspect`
- `node:stream` exposes constructors and `pipeline`
- `process.stdout` / `process.stderr` expose `write`, `fd`, `isTTY`, `columns`, `rows`
- path operations follow host-platform default semantics

## Copy vs Intentional Difference

Copied:

- built-in resolution priority for supported built-ins
- shared process object between global and built-in module export
- `Buffer.from(arrayBuffer, byteOffset, length)` creates a shared Uint8Array view
- minimal `EventEmitter` `on` / `once` / `off` / `emit`
- minimal `util.format` / `util.inspect`
- minimal `node:stream` constructors with `PassThrough` write/end/data flow
- minimal tty stream `cursorTo` / `moveCursor` / `clearLine` / `clearScreenDown`

Intentional differences for now:

- only a small subset of Node built-ins is implemented
- `process.version` / `process.versions` are Okojo-host values, not real Node release values
- `process.nextTick` is implemented using Okojo host-priority microtasks; this matches the basic
  ordering against Promise jobs, but broader Node event-loop integration is still incomplete
- `setImmediate` is implemented through the Okojo host check queue; this aligns the API surface and
  queue ownership, but full libuv phase parity is still outside this slice
- `node:events` currently only exposes the minimal `EventEmitter` constructor surface
- `node:stream` is intentionally a small compatibility layer, not a full Node streams implementation
- `util` is intentionally a very small debugging-oriented subset
- tty control methods currently write ANSI sequences directly; callback overloads and richer stream semantics are not implemented

## Perf Plan

- use cached `StaticNamedPropertyLayout` for fixed built-in objects
- set slots directly on shaped objects instead of replaying property transitions
- use dictionary mode only for dynamic host data objects like `process.env`
- keep built-in registry and object policy in `Okojo.Node`, not in core `Okojo`
