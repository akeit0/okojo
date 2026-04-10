# API Feedback

This sandbox is the first "real file-backed Node app" checkpoint for `Okojo.Node`.

## What already feels reasonable

- `OkojoNodeRuntime.CreateBuilder().Build()` is enough for a default file-backed runtime.
- `RunMainModule(path)` works for both CommonJS and ESM entry modules.
- Node-style package resolution is starting to feel real once the app is laid out on disk.

## Main gaps exposed by this sandbox

1. `RunMainModule(...)` only returns the final JS value. For practical runtime inspection, a richer result with resolved entry path, module format, and maybe namespace/exports access would help.
2. There is no Node-oriented error/reporting helper yet. When module loading fails, embedders still have to inspect raw engine exceptions.
3. Built-ins are still very small. Real package attempts will quickly need more of:
    - richer `stream`
    - richer `util`
    - richer `tty`
4. `pnpm`/real package attempts will need more package resolution work:
   - broader `"exports"` support
   - package self-resolution
   - maybe symlink-aware resolution
5. Console/process I/O is still minimal. A real Ink attempt will need a more Node-like stdout/stderr story:
   - stream callback overloads
   - better terminal state handling
   - cursor/control helpers beyond direct ANSI write-through

## Likely next targets before trying Ink seriously

1. `process.nextTick`
2. better `Buffer`
3. more `"exports"` compatibility
4. broader `node:stream` behavior
5. richer stdout/tty behavior beyond minimal `write`/cursor/clear/isatty/size
