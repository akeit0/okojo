namespace Okojo.Node;

internal static class NodeBuiltInModuleSource
{
    private const string Importer = "globalThis[Symbol.for(\"node.host.import\")]";

    public static bool IsBuiltInSpecifier(string specifier)
    {
        return specifier is
            "node:assert" or "assert" or
            "node:module" or "module" or
            "node:path" or "path" or
            "node:os" or "os" or
            "node:fs" or "fs" or
            "node:child_process" or "child_process" or
            "node:process" or "process" or
            "node:buffer" or "buffer" or
            "node:events" or "events" or
            "node:perf_hooks" or "perf_hooks" or
            "node:stream" or "stream" or
            "node:timers" or "timers" or
            "node:timers/promises" or "timers/promises" or
            "node:tty" or "tty" or
            "node:url" or "url" or
            "node:util" or "util" or
            "node:repl" or "repl";
    }

    public static string Canonicalize(string specifier)
    {
        return specifier switch
        {
            "assert" or "node:assert" => "node:assert",
            "module" or "node:module" => "node:module",
            "path" or "node:path" => "node:path",
            "os" or "node:os" => "node:os",
            "fs" or "node:fs" => "node:fs",
            "child_process" or "node:child_process" => "node:child_process",
            "process" or "node:process" => "node:process",
            "buffer" or "node:buffer" => "node:buffer",
            "events" or "node:events" => "node:events",
            "perf_hooks" or "node:perf_hooks" => "node:perf_hooks",
            "stream" or "node:stream" => "node:stream",
            "timers" or "node:timers" => "node:timers",
            "timers/promises" or "node:timers/promises" => "node:timers/promises",
            "tty" or "node:tty" => "node:tty",
            "url" or "node:url" => "node:url",
            "util" or "node:util" => "node:util",
            "repl" or "node:repl" => "node:repl",
            _ => specifier
        };
    }

    public static string GetModuleSource(string canonicalSpecifier)
    {
        return canonicalSpecifier switch
        {
            "node:assert" => $$"""
                               const assertValue = {{Importer}}("node:assert");
                               export default assertValue;
                               export const strictEqual = (...args) => assertValue.strictEqual(...args);
                               export const notStrictEqual = (...args) => assertValue.notStrictEqual(...args);
                               """,
            "node:path" => $$"""
                             const pathValue = {{Importer}}("node:path");
                             export default pathValue;
                             export const join = (...args) => pathValue.join(...args);
                             export const normalize = (value) => pathValue.normalize(value);
                             export const dirname = (value) => pathValue.dirname(value);
                             export const basename = (value, suffix) => pathValue.basename(value, suffix);
                             export const extname = (value) => pathValue.extname(value);
                             export const resolve = (...args) => pathValue.resolve(...args);
                             export const relative = (from, to) => pathValue.relative(from, to);
                             export const sep = pathValue.sep;
                             export const delimiter = pathValue.delimiter;
                             """,
            "node:os" => $$"""
                           const osValue = {{Importer}}("node:os");
                           export default osValue;
                           export const release = (...args) => osValue.release(...args);
                           export const platform = (...args) => osValue.platform(...args);
                           export const arch = (...args) => osValue.arch(...args);
                           export const homedir = (...args) => osValue.homedir(...args);
                           export const tmpdir = (...args) => osValue.tmpdir(...args);
                           export const EOL = osValue.EOL;
                           """,
            "node:fs" => $$"""
                           const fsValue = {{Importer}}("node:fs");
                           export default fsValue;
                           export const readFileSync = (...args) => fsValue.readFileSync(...args);
                           export const writeFile = (...args) => fsValue.writeFile(...args);
                           export const openSync = (...args) => fsValue.openSync(...args);
                           export const readdirSync = (...args) => fsValue.readdirSync(...args);
                           export const statSync = (...args) => fsValue.statSync(...args);
                           export const constants = fsValue.constants;
                           """,
            "node:child_process" => $$"""
                                      const childProcessValue = {{Importer}}("node:child_process");
                                      export default childProcessValue;
                                      export const execFileSync = (...args) => childProcessValue.execFileSync(...args);
                                      export const execSync = (...args) => childProcessValue.execSync(...args);
                                      """,
            "node:process" => """
                              const processValue = globalThis.process;
                              export default processValue;
                              export const cwd = (...args) => processValue.cwd(...args);
                              export const env = processValue.env;
                              export const argv = processValue.argv;
                              export const platform = processValue.platform;
                              export const version = processValue.version;
                              export const versions = processValue.versions;
                              export const nextTick = (...args) => processValue.nextTick(...args);
                              """,
            "node:buffer" => """
                             const BufferValue = globalThis.Buffer;
                             export default BufferValue;
                             export const Buffer = BufferValue;
                             export const kMaxLength = 2147483647;
                             export const kStringMaxLength = 536870888;
                             """,
            "node:events" => $$"""
                               const eventsValue = {{Importer}}("node:events");
                               const EventEmitterValue = eventsValue.EventEmitter;
                               export default EventEmitterValue;
                               export const EventEmitter = EventEmitterValue;
                               """,
            "node:perf_hooks" => $$"""
                                   const perfHooksValue = {{Importer}}("node:perf_hooks");
                                   export default perfHooksValue;
                                   export const performance = perfHooksValue.performance;
                                   """,
            "node:module" => $$"""
                               const moduleValue = {{Importer}}("node:module");
                               export default moduleValue;
                               export const createRequire = (...args) => moduleValue.createRequire(...args);
                               """,
            "node:stream" => $$"""
                               const streamValue = {{Importer}}("node:stream");
                               export default streamValue;
                               export const Stream = streamValue.Duplex;
                               export const Readable = streamValue.Readable;
                               export const Writable = streamValue.Writable;
                               export const Duplex = streamValue.Duplex;
                               export const Transform = streamValue.Transform;
                               export const PassThrough = streamValue.PassThrough;
                               export const pipeline = (...args) => streamValue.pipeline(...args);
                               """,
            "node:timers" => $$"""
                               const timersValue = {{Importer}}("node:timers");
                               export default timersValue;
                               export const setTimeout = (...args) => timersValue.setTimeout(...args);
                               export const clearTimeout = (value) => timersValue.clearTimeout(value);
                               export const setInterval = (...args) => timersValue.setInterval(...args);
                               export const clearInterval = (value) => timersValue.clearInterval(value);
                               export const setImmediate = (...args) => timersValue.setImmediate(...args);
                               export const clearImmediate = (value) => timersValue.clearImmediate(value);
                               """,
            "node:timers/promises" => $$"""
                                        const timersPromisesValue = {{Importer}}("node:timers/promises");
                                        export default timersPromisesValue;
                                        export const setTimeout = (...args) => timersPromisesValue.setTimeout(...args);
                                        export const setImmediate = (...args) => timersPromisesValue.setImmediate(...args);
                                        export const scheduler = timersPromisesValue.scheduler;
                                        """,
            "node:tty" => $$"""
                            const ttyValue = {{Importer}}("node:tty");
                            export default ttyValue;
                            export const isatty = (fd) => ttyValue.isatty(fd);
                            """,
            "node:url" => $$"""
                            const urlValue = {{Importer}}("node:url");
                            export default urlValue;
                            export const fileURLToPath = (...args) => urlValue.fileURLToPath(...args);
                            """,
            "node:util" => $$"""
                             const utilValue = {{Importer}}("node:util");
                             export default utilValue;
                             export const format = (...args) => utilValue.format(...args);
                             export const inspect = (value, options) => utilValue.inspect(value, options);
                             """,
            "node:repl" => $$"""
                             const replValue = {{Importer}}("node:repl");
                             export default replValue;
                             export const start = (...args) => replValue.start(...args);
                             """,
            _ => throw new InvalidOperationException("Unsupported built-in module: " + canonicalSpecifier)
        };
    }
}
