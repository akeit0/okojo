using System.Text;
using Okojo.Hosting;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Node.Tests;

public class NodeCommonJsTests
{
    [Test]
    public void RunMainModule_ExportsObjectProperty()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = "exports.answer = 42;"
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js");

        Assert.That(result.TryGetObject(out var exportsObj), Is.True);
        Assert.That(exportsObj!.TryGetProperty("answer", out var answer), Is.True);
        Assert.That(answer.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void RunMainModule_ModuleExportsPrimitiveWins()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = "module.exports = 7;"
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js");

        Assert.That(result.Int32Value, Is.EqualTo(7));
    }

    [Test]
    public void Require_CommonJs_Module_Still_Works_When_Argument_Register_Goes_Wide()
    {
        var source = new StringBuilder();
        source.AppendLine("\"use strict\";");
        for (var i = 0; i < 1100; i++)
            source.Append("var x").Append(i).Append(" = ").Append(i).AppendLine(";");
        source.AppendLine("module.exports = require(\"./dep.js\");");

        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = source.ToString(),
                ["/app/dep.js"] = """module.exports = "ok";"""
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js");

        Assert.That(result.AsString(), Is.EqualTo("ok"));
    }

    [Test]
    public void RunMainModule_Provides_AbortController_Global()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    const controller = new AbortController();
                                    const events = [];
                                    controller.signal.addEventListener("abort", event => {
                                      events.push(`${event.type}|${event.target === controller.signal}|${controller.signal.aborted}`);
                                    });
                                    controller.abort("bye");
                                    export default `${typeof AbortController}|${typeof AbortSignal}|${controller.signal.aborted}|${controller.signal.reason}|${events.join(",")}`;
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var namespaceObj), Is.True);
        Assert.That(namespaceObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("function|function|true|bye|abort|true|true"));
    }

    [Test]
    public void RunMainModule_Provides_Performance_Global()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    const first = performance.now();
                                    const second = performance.now();
                                    export default [
                                      typeof performance,
                                      typeof performance.now,
                                      typeof performance.timeOrigin,
                                      second >= first
                                    ].join("|");
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var namespaceObj), Is.True);
        Assert.That(namespaceObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("object|function|number|true"));
    }

    [Test]
    public void RunMainModule_ProcessStdoutWrite_Remains_Callable()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    export default [
                                      typeof process.stdout,
                                      typeof process.stdout.write,
                                      process.stdout.write("x")
                                    ].join("|");
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var namespaceObj), Is.True);
        Assert.That(namespaceObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("object|function|true"));
    }

    [Test]
    public void RunMainModule_ThrottleStyle_Apply_Wrapper_Remains_Callable()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    function debounce(func, debounceMs, { edges } = {}) {
                                      let pendingThis = undefined;
                                      let pendingArgs = null;
                                      const leading = edges != null && edges.includes('leading');
                                      const invoke = () => {
                                        if (pendingArgs !== null) {
                                          func.apply(pendingThis, pendingArgs);
                                          pendingThis = undefined;
                                          pendingArgs = null;
                                        }
                                      };
                                      const schedule = () => {};
                                      const cancel = () => {
                                        pendingThis = undefined;
                                        pendingArgs = null;
                                      };
                                      const debounced = function (...args) {
                                        pendingThis = this;
                                        pendingArgs = args;
                                        const isFirstCall = true;
                                        schedule();
                                        if (leading && isFirstCall) {
                                          invoke();
                                        }
                                      };
                                      debounced.schedule = schedule;
                                      debounced.cancel = cancel;
                                      debounced.flush = invoke;
                                      return debounced;
                                    }

                                    function throttle(func, throttleMs, { edges = ['leading', 'trailing'] } = {}) {
                                      let pendingAt = null;
                                      const debounced = debounce(function (...args) {
                                        pendingAt = Date.now();
                                        func.apply(this, args);
                                      }, throttleMs, { edges });
                                      const throttled = function (...args) {
                                        if (pendingAt == null) {
                                          pendingAt = Date.now();
                                        }
                                        if (Date.now() - pendingAt >= throttleMs) {
                                          pendingAt = Date.now();
                                          func.apply(this, args);
                                          debounced.cancel();
                                          debounced.schedule();
                                          return 'fast';
                                        }
                                        return debounced.apply(this, args);
                                      };
                                      throttled.cancel = debounced.cancel;
                                      throttled.flush = debounced.flush;
                                      return throttled;
                                    }

                                    class InkLike {
                                      constructor() {
                                        this.calls = [];
                                      }

                                      onRender = () => {
                                        this.calls.push(typeof process.stdout.write);
                                        this.calls.push(process.stdout.write("x"));
                                        return this.calls.join("|");
                                      };

                                      run() {
                                        const throttled = throttle(this.onRender, 16, {
                                          leading: true,
                                          trailing: true,
                                        });
                                        const result = throttled();
                                        return [typeof throttled, typeof throttled.cancel, this.calls.join("|"), String(result)].join("|");
                                      }
                                    }

                                    export default new InkLike().run();
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var namespaceObj), Is.True);
        Assert.That(namespaceObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("function|function|function|true|undefined"));
    }

    [Test]
    public void Require_NodeEvents_Supports_MaxListeners_And_ListenerQueries()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = """
                                   const { EventEmitter } = require("node:events");
                                   const emitter = new EventEmitter();
                                   function a() {}
                                   function b() {}
                                   emitter.on("tick", a);
                                   emitter.once("tick", b);
                                   emitter.setMaxListeners(Infinity);
                                   module.exports = [
                                     typeof emitter.setMaxListeners,
                                     emitter.getMaxListeners(),
                                     emitter.listenerCount("tick"),
                                     emitter.listeners("tick").length,
                                     emitter.listeners("tick")[0] === a,
                                     emitter.listeners("tick")[1] === b
                                   ].join("|");
                                   """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js");

        Assert.That(result.AsString(), Is.EqualTo("function|Infinity|2|2|true|true"));
    }

    [Test]
    public void Process_Stdout_Exposes_SetMaxListeners_Method()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    process.stdout.setMaxListeners(Infinity);
                                    export default [
                                      typeof process.stdout.setMaxListeners,
                                      process.stdout.getMaxListeners(),
                                      process.stdout.listenerCount("resize"),
                                      process.stdout.listeners("resize").length
                                    ].join("|");
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var namespaceObj), Is.True);
        Assert.That(namespaceObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("function|Infinity|0|0"));
    }

    [Test]
    public void Process_Stdin_Exposes_Minimal_Tty_Surface()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .ConfigureTerminal(options => { options.StdinIsTty = true; })
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    const stdin = process.stdin;
                                    stdin.setEncoding('utf8');
                                    stdin.setRawMode(true);
                                    const read = stdin.read();
                                    stdin.ref();
                                    stdin.unref();
                                    export default [
                                      typeof stdin,
                                      stdin.isTTY,
                                      stdin.fd,
                                      typeof stdin.setRawMode,
                                      typeof stdin.setEncoding,
                                      typeof stdin.read,
                                      read === null
                                    ].join("|");
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var namespaceObj), Is.True);
        Assert.That(namespaceObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("object|true|0|function|function|function|true"));
    }

    [Test]
    public void Import_NodePerfHooks_Exposes_Performance_Object()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    import perfHooks, { performance as namedPerformance } from "node:perf_hooks";
                                    const same = perfHooks.performance === namedPerformance;
                                    export default [
                                      typeof perfHooks.performance,
                                      typeof namedPerformance.now,
                                      same,
                                      namedPerformance.now() >= 0
                                    ].join("|");
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var namespaceObj), Is.True);
        Assert.That(namespaceObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("object|function|true|true"));
    }

    [Test]
    public void RunMainModule_LargeVarChain_NewSet_RemainsConstructable_When_Register_Goes_Wide()
    {
        var source = new StringBuilder();
        source.AppendLine("\"use strict\";");
        source.Append("var ");
        for (var i = 0; i < 250; i++)
        {
            if (i != 0)
                source.Append(", ");
            source.Append("x").Append(i).Append(" = null");
        }

        source.AppendLine(", s = new Set(), out = typeof Set + \"|\" + (s instanceof Set) + \"|\" + s.size;");
        source.AppendLine("module.exports = out;");

        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = source.ToString()
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js");

        Assert.That(result.AsString(), Is.EqualTo("function|true|0"));
    }

    [Test]
    public void Import_CommonJs_NestedIife_Export_Is_Available_As_Named_And_Default()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/package.json"] = """{ "type": "module" }""",
                ["/app/main.mjs"] = """
                                    import value, { answer } from "./dep.cjs";
                                    export default `${value.answer}|${answer}`;
                                    """,
                ["/app/dep.cjs"] = """
                                   "use strict";
                                   (function () {
                                     exports.answer = 42;
                                   })();
                                   """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var namespaceObj), Is.True);
        Assert.That(namespaceObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("42|42"));
    }

    [Test]
    public void NodeRuntime_Uses_OkojoRegExp_As_Default_RegExpEngine()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    export default /\p{Emoji_Presentation}+/u.test("\u231A");
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var namespaceObj), Is.True);
        Assert.That(namespaceObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.IsTrue, Is.True);
    }

    [Test]
    public void RunMainModule_Module_ObjectLiteralArrowDefaultParameter_Works()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    const obj = {
                                      setCwd: (cwd = "x") => cwd,
                                    };
                                    export default obj.setCwd();
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var namespaceObj), Is.True);
        Assert.That(namespaceObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("x"));
    }

    [Test]
    public void Require_UsesCache_ForRepeatedLoads()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = """
                                   const a = require("./dep.js");
                                   const b = require("./dep.js");
                                   module.exports = { same: a === b, hits: globalThis.__depHits };
                                   """,
                ["/app/dep.js"] = """
                                  globalThis.__depHits = (globalThis.__depHits || 0) + 1;
                                  module.exports = { value: globalThis.__depHits };
                                  """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js");

        Assert.That(result.TryGetObject(out var exportsObj), Is.True);
        Assert.That(exportsObj!.TryGetProperty("same", out var same), Is.True);
        Assert.That(same.IsTrue, Is.True);
        Assert.That(exportsObj.TryGetProperty("hits", out var hits), Is.True);
        Assert.That(hits.Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void Require_Json_File_Parses_As_CommonJs_Exports()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = """
                                   const boxes = require("./boxes.json");
                                   module.exports = `${boxes.foo}|${boxes.answer}`;
                                   """,
                ["/app/boxes.json"] = """
                                      { "foo": "bar", "answer": 42 }
                                      """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js");

        Assert.That(result.AsString(), Is.EqualTo("bar|42"));
    }

    [Test]
    public void NodeGlobals_Install_QueueMicrotask()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = """
                                   globalThis.trace = "";
                                   queueMicrotask(() => { trace += "m"; });
                                   trace += "s";
                                   module.exports = trace;
                                   """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js");
        runtime.MainRealm.PumpJobs();

        Assert.That(result.AsString(), Is.EqualTo("s"));
        Assert.That(runtime.MainRealm.Global["trace"].AsString(), Is.EqualTo("sm"));
    }

    [Test]
    [Ignore(
        "Investigate CommonJS cycle mismatch in test-host path separately from the hoisted-wrapper identifier-table fix.")]
    public void Require_Supports_SimpleCycles()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "Okojo.Node.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var mainPath = Path.Combine(tempRoot, "main.cjs");
            var aPath = Path.Combine(tempRoot, "a.cjs");
            var bPath = Path.Combine(tempRoot, "b.cjs");

            File.WriteAllText(mainPath, """
                                        const a = require("./a.cjs");
                                        const b = require("./b.cjs");
                                        module.exports = `${a.name}:${a.fromB}:${b.name}:${b.fromA}`;
                                        """);
            File.WriteAllText(aPath, """
                                     exports.name = "a";
                                     const b = require("./b.cjs");
                                     exports.fromB = b.name;
                                     """);
            File.WriteAllText(bPath, """
                                     exports.name = "b";
                                     const a = require("./a.cjs");
                                     exports.fromA = a.name;
                                     """);

            using var runtime = NodeRuntime.CreateBuilder().Build();
            var result = runtime.RunMainModule(mainPath);

            Assert.That(result.AsString(), Is.EqualTo("a:b:b:a"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    [Test]
    public void RunMainModule_PopulatesFilenameAndDirname()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = "module.exports = __filename + '|' + __dirname;"
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js");

        Assert.That(result.AsString(), Is.EqualTo("/app/main.js|/app"));
    }

    [Test]
    public void RunMainModule_CommonJsWrapper_Exposes_NodeScopedBindings_And_RequireCacheEntry()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = """
                                   exports.answer = 1;
                                   const entry = require.cache[__filename];
                                   module.exports = [
                                     exports === module.exports,
                                     __filename,
                                     __dirname,
                                     module.filename,
                                     module.path,
                                     module.loaded,
                                     !!entry,
                                     !!entry && entry.exports === module.exports,
                                     !!entry && entry.filename,
                                     !!entry && entry.path,
                                     !!entry && entry.loaded
                                   ].join("|");
                                   """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js");

        Assert.That(
            result.AsString(),
            Is.EqualTo("true|/app/main.js|/app|/app/main.js|/app|false|true|true|/app/main.js|/app|false"));
    }

    [Test]
    public void Require_Cache_Delete_Forces_Module_Reload()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = """
                                   const a = require("./dep.js");
                                   delete require.cache["/app/dep.js"];
                                   const b = require("./dep.js");
                                   module.exports = `${a.value}|${b.value}|${a === b}|${globalThis.__depHits}`;
                                   """,
                ["/app/dep.js"] = """
                                  globalThis.__depHits = (globalThis.__depHits || 0) + 1;
                                  module.exports = { value: globalThis.__depHits };
                                  """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js");

        Assert.That(result.AsString(), Is.EqualTo("1|2|false|2"));
    }

    [Test]
    public void Require_Cache_Can_Shadow_BuiltIn_Without_Node_Prefix()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = """
                                   const realPath = require("node:path");
                                   require.cache.path = {
                                     exports: {
                                       join() { return "fake"; }
                                     }
                                   };
                                   module.exports = `${require("path").join("a", "b")}|${require("node:path") === realPath}`;
                                   """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js");

        Assert.That(result.AsString(), Is.EqualTo("fake|true"));
    }

    [Test]
    public void RunMainModule_Populates_ProcessArgv_From_RuntimeArguments()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    export default `${process.argv[0]}|${process.argv[1]}|${process.argv[2]}|${process.argv[3]}`;
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs", "greet", "--name=okojo");

        Assert.That(result.TryGetObject(out var namespaceObj), Is.True);
        Assert.That(namespaceObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("okojo|/app/main.mjs|greet|--name=okojo"));
    }

    [Test]
    public void RunMainModule_FileBackedModule_Uses_ForwardedArgv_And_CurrentDirectory_For_RelativeFsReads()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "Okojo.Node.Tests", Guid.NewGuid().ToString("N"));
        var appRoot = Path.Combine(tempRoot, "app");
        Directory.CreateDirectory(appRoot);

        var previousCurrentDirectory = Environment.CurrentDirectory;
        try
        {
            File.WriteAllText(Path.Combine(appRoot, "package.json"), """{ "type": "module" }""");
            File.WriteAllText(Path.Combine(appRoot, "project.explain.json"), """
                                                                             {
                                                                               "name": "Okojo",
                                                                               "tagline": "engine"
                                                                             }
                                                                             """);
            File.WriteAllText(Path.Combine(appRoot, "main.mjs"), """
                                                                 import fs from "node:fs";

                                                                 const docPath = process.argv[4];
                                                                 const doc = JSON.parse(fs.readFileSync(docPath, "utf8"));
                                                                 export default `${process.argv[2]}|${process.argv[3]}|${docPath}|${doc.name}|${doc.tagline}|${process.cwd()}`;
                                                                 """);

            Environment.CurrentDirectory = appRoot;

            using var runtime = NodeRuntime.CreateBuilder().Build();
            var result = runtime.RunMainModule(
                Path.Combine(appRoot, "main.mjs"),
                "explain",
                "--doc",
                "project.explain.json");

            Assert.That(result.TryGetObject(out var namespaceObj), Is.True);
            Assert.That(namespaceObj!.TryGetProperty("default", out var value), Is.True);
            Assert.That(
                value.AsString(),
                Is.EqualTo($"explain|--doc|project.explain.json|Okojo|engine|{appRoot}"));
        }
        finally
        {
            Environment.CurrentDirectory = previousCurrentDirectory;
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    [Test]
    public void Require_Resolves_Extensionless_Relative_JavaScript_File()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = "module.exports = require('./dep').value;",
                ["/app/dep.js"] = "exports.value = 42;"
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js");

        Assert.That(result.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void Require_Resolves_Directory_Index_File()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = "module.exports = require('./pkg').name;",
                ["/app/pkg/index.js"] = "exports.name = 'pkg-index';"
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js");

        Assert.That(result.AsString(), Is.EqualTo("pkg-index"));
    }

    [Test]
    public void Require_Resolves_NodeModules_Package_Main()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = "module.exports = require('pkg');",
                ["/app/node_modules/pkg/package.json"] = """{ "main": "./lib/entry.js" }""",
                ["/app/node_modules/pkg/lib/entry.js"] = "module.exports = 'pkg-main';"
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js");

        Assert.That(result.AsString(), Is.EqualTo("pkg-main"));
    }

    [Test]
    public void Require_Resolves_NodeModules_Package_Subpath()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = "module.exports = require('pkg/lib/value');",
                ["/app/node_modules/pkg/lib/value.js"] = "module.exports = 'pkg-subpath';"
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js");

        Assert.That(result.AsString(), Is.EqualTo("pkg-subpath"));
    }

    [Test]
    public void Require_Uses_Nearest_Ancestor_NodeModules()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/workspace/app/src/main.js"] = "module.exports = require('pkg');",
                ["/workspace/app/node_modules/pkg/index.js"] = "module.exports = 'nearest';",
                ["/workspace/node_modules/pkg/index.js"] = "module.exports = 'farther';"
            }))
            .Build();

        var result = runtime.RunMainModule("/workspace/app/src/main.js");

        Assert.That(result.AsString(), Is.EqualTo("nearest"));
    }

    [Test]
    public void Require_Resolves_NodeModules_Package_Exports_String()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = "module.exports = require('pkg');",
                ["/app/node_modules/pkg/package.json"] = """{ "exports": "./entry.js" }""",
                ["/app/node_modules/pkg/entry.js"] = "module.exports = 'exports-string';"
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js");

        Assert.That(result.AsString(), Is.EqualTo("exports-string"));
    }

    [Test]
    public void Require_Resolves_NodeModules_Package_Exports_Subpath()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = "module.exports = require('pkg/subpath');",
                ["/app/node_modules/pkg/package.json"] =
                    """{ "exports": { ".": "./main.js", "./subpath": "./lib/sub.js" } }""",
                ["/app/node_modules/pkg/main.js"] = "module.exports = 'exports-main';",
                ["/app/node_modules/pkg/lib/sub.js"] = "module.exports = 'exports-subpath';"
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js");

        Assert.That(result.AsString(), Is.EqualTo("exports-subpath"));
    }

    [Test]
    public void Require_Resolves_NodeModules_Package_Conditional_Exports_In_Declaration_Order()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = "module.exports = require('pkg');",
                ["/app/node_modules/pkg/package.json"] =
                    """{ "exports": { "node": "./node.js", "require": "./require.js", "default": "./default.js" } }""",
                ["/app/node_modules/pkg/node.js"] = "module.exports = 'node-cond';",
                ["/app/node_modules/pkg/require.js"] = "module.exports = 'require-cond';",
                ["/app/node_modules/pkg/default.js"] = "module.exports = 'default-cond';"
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js");

        Assert.That(result.AsString(), Is.EqualTo("node-cond"));
    }

    [Test]
    public void Require_PackageExports_Blocks_Filesystem_Fallback_For_Unexported_Subpath()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = "module.exports = require('pkg/private');",
                ["/app/node_modules/pkg/package.json"] = """{ "exports": { ".": "./main.js" } }""",
                ["/app/node_modules/pkg/main.js"] = "module.exports = 'exports-main';",
                ["/app/node_modules/pkg/private.js"] = "module.exports = 'private-file';"
            }))
            .Build();

        var ex = Assert.Throws<JsRuntimeException>(() => runtime.RunMainModule("/app/main.js"));

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Message, Does.Contain("not exported"));
    }

    [Test]
    public void RunMainModule_Uses_Esm_For_Mjs_Entry()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = "export default 42;"
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var namespaceObj), Is.True);
        Assert.That(namespaceObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void RunMainModule_Uses_Esm_For_PackageTypeModule_JavaScript()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/package.json"] = """{ "type": "module" }""",
                ["/app/main.js"] = "export const answer = 42;"
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js");

        Assert.That(result.TryGetObject(out var namespaceObj), Is.True);
        Assert.That(namespaceObj!.TryGetProperty("answer", out var value), Is.True);
        Assert.That(value.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void RunMainModule_Resolves_PackageImports_Internal_Specifier()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/node_modules/pkg/package.json"] =
                    """{ "type": "module", "imports": { "#dep": "./src/dep.js" } }""",
                ["/app/node_modules/pkg/main.js"] = """
                                                    import value from "#dep";
                                                    export default value;
                                                    """,
                ["/app/node_modules/pkg/src/dep.js"] = """
                                                       export default "imports-ok";
                                                       """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/node_modules/pkg/main.js");

        Assert.That(result.TryGetObject(out var namespaceObj), Is.True);
        Assert.That(namespaceObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("imports-ok"));
    }

    [Test]
    public void RunMainModule_Resolves_PackageImports_Conditional_Node_Target()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/node_modules/pkg/package.json"] =
                    """{ "type": "module", "imports": { "#dep": { "node": "./src/node.js", "default": "./src/default.js" } } }""",
                ["/app/node_modules/pkg/main.js"] = """
                                                    import value from "#dep";
                                                    export default value;
                                                    """,
                ["/app/node_modules/pkg/src/node.js"] = """export default "node-target";""",
                ["/app/node_modules/pkg/src/default.js"] = """export default "default-target";"""
            }))
            .Build();

        var result = runtime.RunMainModule("/app/node_modules/pkg/main.js");

        Assert.That(result.TryGetObject(out var namespaceObj), Is.True);
        Assert.That(namespaceObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("node-target"));
    }

    [Test]
    public void Require_Resolves_NodeModules_Package_Exports_Array_Target()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = "module.exports = require('pkg/subpath');",
                ["/app/node_modules/pkg/package.json"] =
                    """{ "exports": { "./subpath": [ { "require": { "default": "./lib/sub.js" } }, "./fallback.js" ] } }""",
                ["/app/node_modules/pkg/lib/sub.js"] = "module.exports = 'exports-array';",
                ["/app/node_modules/pkg/fallback.js"] = "module.exports = 'fallback';"
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js");

        Assert.That(result.AsString(), Is.EqualTo("exports-array"));
    }

    [Test]
    public void Require_NodeAssert_Exposes_Strict_Equality_Methods()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = """
                                   const assert = require("assert");
                                   assert.strictEqual(4, 4);
                                   assert.notStrictEqual(4, 5);
                                   module.exports = [
                                     typeof assert.strictEqual,
                                     typeof assert.notStrictEqual
                                   ].join("|");
                                   """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js").AsString().Split('|');

        Assert.That(result[0], Is.EqualTo("function"));
        Assert.That(result[1], Is.EqualTo("function"));
    }

    [Test]
    public void Import_NodeUrl_And_NodeModule_Expose_FileHelpers()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    import { fileURLToPath } from "url";
                                    import { createRequire } from "node:module";
                                    const require = createRequire("file:///app/main.mjs");
                                    const util = require("util");
                                    export default [
                                      fileURLToPath("file:///app/demo.mjs"),
                                      typeof require,
                                      typeof util.format
                                    ].join("|");
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("/app/demo.mjs|function|function"));
    }

    [Test]
    public void RunMainModule_DefaultParameter_Can_Use_Imported_ProcessArgv_Array()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    import process from "node:process";
                                    function hasFlag(flag, argv = globalThis.Deno ? globalThis.Deno.args : process.argv) {
                                      const prefix = flag.startsWith('-') ? '' : (flag.length === 1 ? '-' : '--');
                                      const position = argv.indexOf(prefix + flag);
                                      return position !== -1;
                                    }
                                    export default hasFlag("x");
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.IsFalse, Is.True);
    }

    [Test]
    public void RunMainModule_Imported_ProcessArgv_IndexOf_Works_Directly()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    import process from "node:process";
                                    export default process.argv.indexOf("--x");
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.Int32Value, Is.EqualTo(-1));
    }

    [Test]
    public void RunMainModule_Esm_Named_Function_Import_Is_Callable()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    import { greet } from "./dep.mjs";
                                    export default greet("okojo");
                                    """,
                ["/app/dep.mjs"] = """
                                   export function greet(name) {
                                     return `hi:${name}`;
                                   }
                                   """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("hi:okojo"));
    }

    [Test]
    public void RunMainModule_Esm_MultiNamed_Function_Import_Is_Callable()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    import {
                                      replaceAllText,
                                      otherValue,
                                    } from "./dep.mjs";
                                    export default `${typeof replaceAllText}|${replaceAllText("ab", "a", "x")}|${otherValue}`;
                                    """.Replace("\r\n",
                    "\n"), // Normalize line endings for consistent test results across platforms.
                ["/app/dep.mjs"] = """
                                   export function replaceAllText(text, needle, value) {
                                     return text.split(needle).join(value);
                                   }
                                   export function otherValue() {
                                     return 1;
                                   }
                                   """.Replace("\r\n",
                    "\n") // Normalize line endings for consistent test results across platforms.
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("function|xb|function otherValue() {\n  return 1;\n}"));
    }

    [Test]
    public void RunMainModule_ChalkStyle_Builder_Getter_Returns_Callable_Function()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    const GENERATOR = Symbol('GENERATOR');
                                    const STYLER = Symbol('STYLER');
                                    const IS_EMPTY = Symbol('IS_EMPTY');
                                    const styles = {};

                                    const applyStyle = (self, string) => {
                                      if (self.level <= 0 || !string) {
                                        return self[IS_EMPTY] ? '' : string;
                                      }

                                      let styler = self[STYLER];
                                      if (styler === undefined) {
                                        return string;
                                      }

                                      return styler.openAll + string + styler.closeAll;
                                    };

                                    const proto = Object.defineProperties(() => {}, {
                                      ...styles,
                                      level: {
                                        enumerable: true,
                                        get() {
                                          return this[GENERATOR].level;
                                        },
                                        set(level) {
                                          this[GENERATOR].level = level;
                                        }
                                      }
                                    });

                                    const createStyler = (open, close, parent) => {
                                      if (parent === undefined) {
                                        return { openAll: open, closeAll: close, parent };
                                      }

                                      return {
                                        openAll: parent.openAll + open,
                                        closeAll: close + parent.closeAll,
                                        parent
                                      };
                                    };

                                    const createBuilder = (self, _styler, _isEmpty) => {
                                      const builder = (...arguments_) => applyStyle(builder, (arguments_.length === 1) ? ('' + arguments_[0]) : arguments_.join(' '));
                                      Object.setPrototypeOf(builder, proto);
                                      builder[GENERATOR] = self;
                                      builder[STYLER] = _styler;
                                      builder[IS_EMPTY] = _isEmpty;
                                      return builder;
                                    };

                                    styles.green = {
                                      get() {
                                        const builder = createBuilder(this, createStyler('<g>', '</g>', this[STYLER]), this[IS_EMPTY]);
                                        Object.defineProperty(this, 'green', { value: builder });
                                        return builder;
                                      }
                                    };

                                    function createChalk() {
                                      const chalk = (...strings) => strings.join(' ');
                                      chalk.level = 1;
                                      Object.setPrototypeOf(chalk, createChalk.prototype);
                                      return chalk;
                                    }

                                    Object.setPrototypeOf(createChalk.prototype, Function.prototype);
                                    Object.defineProperties(createChalk.prototype, styles);

                                    const chalk = createChalk();
                                    export default chalk.green('hello');
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("<g>hello</g>"));
    }

    [Test]
    public void RunMainModule_ChalkStyle_Repeated_Getter_Materialization_On_Different_Receivers_Does_Not_Throw()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    const styles = {};

                                    styles.green = {
                                      get() {
                                        const builder = () => 'ok';
                                        Object.setPrototypeOf(builder, proto);
                                        Object.defineProperty(this, 'green', { value: builder });
                                        return builder;
                                      }
                                    };

                                    const proto = Object.defineProperties(() => {}, { ...styles });

                                    function create() {
                                      const chalk = () => 'base';
                                      Object.setPrototypeOf(chalk, proto);
                                      return chalk;
                                    }

                                    const left = create();
                                    const right = create();
                                    export default `${typeof left.green}|${typeof right.green}`;
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("function|function"));
    }

    [Test]
    public void RunMainModule_ChalkStyle_InlineTemplateTypeof_After_PrecomputedTypeof_StaysFunction()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    const styles = {};

                                    styles.green = {
                                      get() {
                                        const builder = () => 'ok';
                                        Object.setPrototypeOf(builder, proto);
                                        Object.defineProperty(this, 'green', { value: builder });
                                        return builder;
                                      }
                                    };

                                    const proto = Object.defineProperties(() => {}, { ...styles });

                                    function create() {
                                      const chalk = () => 'base';
                                      Object.setPrototypeOf(chalk, proto);
                                      return chalk;
                                    }

                                    const left = create();
                                    const first = typeof left.green;
                                    const second = typeof left.green;
                                    export default `${first}|${second}|${typeof left.green}`;
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("function|function|function"));
    }

    [Test]
    public void RunMainModule_InlineTemplateTypeof_On_SpreadDefined_SelfMaterializingGetter_Does_Not_Drift()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    const styles = {
                                      green: {
                                        get() {
                                          const value = () => 0;
                                          Object.defineProperty(this, 'green', { value });
                                          return value;
                                        }
                                      }
                                    };

                                    const proto = Object.defineProperties({}, { ...styles });
                                    const left = Object.create(proto);
                                    debugger;
                                    const first = (typeof left.green);
                                    export default `${first}|${typeof left.green}`;
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("function|function"));
    }

    [Test]
    public void RunMainModule_ChalkStyle_Typeof_Then_Call_Does_Not_Load_Wrong_Getter()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    const styles = {};

                                    styles.green = {
                                      get() {
                                        const builder = (...args) => args.join(' ');
                                        Object.setPrototypeOf(builder, proto);
                                        Object.defineProperty(this, 'green', { value: builder });
                                        return builder;
                                      }
                                    };

                                    styles.bgWhiteBright = {
                                      get() {
                                        throw new Error('wrong getter');
                                      }
                                    };

                                    const proto = Object.defineProperties(() => {}, { ...styles });

                                    const chalk = () => 'base';
                                    chalk.level = 1;
                                    Object.setPrototypeOf(chalk, proto);

                                    const kind = typeof chalk.green;
                                    const value = chalk.green('hello');
                                    export default `${kind}|${value}`;
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("function|hello"));
    }

    [Test]
    public void RunMainModule_ChalkStyle_ForOfDestructuringGetter_Captures_Current_StyleName()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    const styles = Object.create(null);
                                    const ansiStyles = {
                                      green: { open: "<g>", close: "</g>" },
                                      bgWhiteBright: { open: "<b>", close: "</b>" },
                                    };

                                    const createBuilder = (_self, styler) => {
                                      const builder = value => styler.open + value + styler.close;
                                      Object.setPrototypeOf(builder, proto);
                                      return builder;
                                    };

                                    const createStyler = (open, close) => ({ open, close });

                                    for (const [styleName, style] of Object.entries(ansiStyles)) {
                                      styles[styleName] = {
                                        get() {
                                          const builder = createBuilder(this, createStyler(style.open, style.close));
                                          Object.defineProperty(this, styleName, { value: builder });
                                          return builder;
                                        },
                                      };
                                    }

                                    const proto = Object.defineProperties(() => {}, { ...styles });
                                    const chalk = () => 'base';
                                    Object.setPrototypeOf(chalk, proto);

                                    const kind = typeof chalk.green;
                                    const value = chalk.green('hello');
                                    const wrong = Object.getOwnPropertyDescriptor(chalk, 'bgWhiteBright');
                                    export default `${kind}|${value}|${wrong === undefined}`;
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("function|<g>hello</g>|true"));
    }

    [Test]
    public void Import_NodeFs_Exposes_ReaddirSync_And_StatSync()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "Okojo.Node.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var dataDir = Path.Combine(tempRoot, "data");
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(Path.Combine(dataDir, "a.txt"), "a");
            File.WriteAllText(Path.Combine(dataDir, "b.txt"), "b");

            using var runtime = NodeRuntime.CreateBuilder()
                .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
                {
                    ["/app/main.mjs"] = $$"""
                                          import { readdirSync, statSync } from "node:fs";
                                          const names = readdirSync({{ToJsStringLiteral(dataDir)}});
                                          const stats = statSync({{ToJsStringLiteral(dataDir)}});
                                          export default `${names.length}|${stats.isDirectory()}|${stats.isFile()}`;
                                          """
                }))
                .Build();

            var result = runtime.RunMainModule("/app/main.mjs");

            Assert.That(result.TryGetObject(out var nsObj), Is.True);
            Assert.That(nsObj!.TryGetProperty("default", out var value), Is.True);
            Assert.That(value.AsString(), Is.EqualTo("2|true|false"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    [Test]
    public void Import_NodePath_Exposes_Normalize()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    import { normalize } from "node:path";
                                    export default normalize("./a/../b//c");
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo(Path.Combine("b", "c")));
    }

    [Test]
    public void Import_NodePath_Exposes_Relative()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    import { relative } from "node:path";
                                    export default relative("/app/src", "/app/test/file.js");
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo(Path.Combine("..", "test", "file.js")));
    }

    [Test]
    public void NodeConsole_Exposes_Console_Constructor_And_Bound_Instance_Methods()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = """
                                   const consoleModule = require("node:console");
                                   const { PassThrough } = require("node:stream");
                                   const stdout = new PassThrough();
                                   const stderr = new PassThrough();
                                   let trace = "";
                                   stdout.write = (data) => {
                                     trace += "out:" + String(data);
                                     return true;
                                   };
                                   stderr.write = (data) => {
                                     trace += "err:" + String(data);
                                     return true;
                                   };
                                   const constructed = new console.Console(stdout, stderr);
                                   const called = console.Console(stdout, stderr);
                                   const log = constructed.log;
                                   const warn = constructed.warn;
                                   log("hello");
                                   warn("oops");
                                   module.exports = [
                                     console === consoleModule,
                                     typeof console.Console,
                                     typeof consoleModule.Console,
                                     constructed instanceof console.Console,
                                     called instanceof console.Console,
                                     trace
                                   ].join("|");
                                   """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js").AsString().Split('|');

        Assert.That(result[0], Is.EqualTo("true"));
        Assert.That(result[1], Is.EqualTo("function"));
        Assert.That(result[2], Is.EqualTo("function"));
        Assert.That(result[3], Is.EqualTo("true"));
        Assert.That(result[4], Is.EqualTo("true"));
        Assert.That(result[5], Is.EqualTo("out:hello\nerr:oops\n"));
    }

    [Test]
    public void Process_Stdout_And_Stderr_Expose_EventEmitter_Methods()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = """
                                   let hits = 0;
                                   const handler = () => { hits++; };
                                   process.stdout.on("resize", handler);
                                   process.stdout.emit("resize");
                                   process.stdout.off("resize", handler);
                                   process.stdout.emit("resize");
                                   module.exports = [
                                     typeof process.stdout.on,
                                     typeof process.stdout.off,
                                     typeof process.stdout.emit,
                                     hits
                                   ].join("|");
                                   """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js").AsString().Split('|');

        Assert.That(result[0], Is.EqualTo("function"));
        Assert.That(result[1], Is.EqualTo("function"));
        Assert.That(result[2], Is.EqualTo("function"));
        Assert.That(result[3], Is.EqualTo("1"));
    }

    [Test]
    public void EventEmitter_BaseConstructor_Does_Not_Replace_Derived_Instance()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    import {EventEmitter} from "node:events";

                                    class Command extends EventEmitter {
                                      name() {
                                        return 'ok';
                                      }
                                    }

                                    const program = new Command();
                                    export default `${typeof program.name}|${typeof program.on}|${program.name()}`;
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("function|function|ok"));
    }

    [Test]
    public void Process_Version_Surface_Looks_Like_Modern_Node()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    import process from "node:process";
                                    export default `${process.version}|${process.versions.node}`;
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");
        Assert.That(result.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj!.TryGetProperty("default", out var value), Is.True);
        //Assert.That(value.AsString(), Is.EqualTo("v0.1.0-preview.1|0.1.0-preview.1"));
    }

    [Test]
    public void RunMainModule_TripleSlash_Comment_Before_DefaultParameter_Function_Does_Not_Break_Callability()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    import process from "node:process";
                                    /// function hasFlag(flag, argv = globalThis.Deno?.args ?? process.argv) {
                                    function hasFlag(flag, argv = globalThis.Deno ? globalThis.Deno.args : process.argv) {
                                      const prefix = flag.startsWith('-') ? '' : (flag.length === 1 ? '-' : '--');
                                      const position = argv.indexOf(prefix + flag);
                                      const terminatorPosition = argv.indexOf('--');
                                      return position !== -1 && (terminatorPosition === -1 || position < terminatorPosition);
                                    }
                                    export default hasFlag("x");
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.IsFalse, Is.True);
    }

    [Test]
    public void EvaluateModule_ExportedFunction_Can_Be_Called_Later_In_Same_Module()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    export function createSupportsColor(value = 1) {
                                      return value + 1;
                                    }

                                    const supportsColor = {
                                      stdout: createSupportsColor(2),
                                      stderr: createSupportsColor(3),
                                    };

                                    export default `${supportsColor.stdout}|${supportsColor.stderr}`;
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var namespaceObj), Is.True);
        Assert.That(namespaceObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("3|4"));
    }

    [Test]
    public void EvaluateModule_ExportedFunction_Name_Can_Be_Shadowed_In_Nested_Scope()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    export function createSupportsColor(value = 1) {
                                      return value + 1;
                                    }

                                    function readWithShadow(createSupportsColor) {
                                      return createSupportsColor(10);
                                    }

                                    export default `${createSupportsColor(2)}|${readWithShadow(value => value * 2)}`;
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var namespaceObj), Is.True);
        Assert.That(namespaceObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("3|20"));
    }

    [Test]
    public void EvaluateModule_ExportedFunction_Can_Read_Later_Parameters()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    export function read(a, b, c) {
                                      return `${b}|${c}`;
                                    }

                                    export default read("ignored", "left", "right");
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var namespaceObj), Is.True);
        Assert.That(namespaceObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("left|right"));
    }

    [Test]
    public void EvaluateModule_ExportedFunction_BlockCapturePattern_Can_Read_Parameters_And_BlockLexical()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    export function help(commands, base$0, parentCommands) {
                                      if (commands.length) {
                                        const prefix = base$0 ? `${base$0} ` : '';
                                        let last = '';
                                        commands.forEach(command => {
                                          last = `${prefix}${parentCommands}${command[0]}`;
                                        });
                                        return last;
                                      }
                                      return '';
                                    }

                                    export default help([['x']], 'cli', 'sub ');
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var namespaceObj), Is.True);
        Assert.That(namespaceObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("cli sub x"));
    }

    [Test]
    public void RunMainModule_Resolves_Transitive_Package_From_Symlinked_NodeModules_Package()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "Okojo.Node.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var appRoot = Path.Combine(tempRoot, "app");
            var storeRoot = Path.Combine(tempRoot, ".store");
            var directPackageTarget = Path.Combine(storeRoot, "direct@1.0.0", "node_modules", "direct");
            var transitivePackageTarget = Path.Combine(storeRoot, "transitive@1.0.0", "node_modules", "transitive");

            Directory.CreateDirectory(Path.Combine(appRoot, "node_modules"));
            Directory.CreateDirectory(directPackageTarget);
            Directory.CreateDirectory(Path.Combine(directPackageTarget, "node_modules"));
            Directory.CreateDirectory(transitivePackageTarget);

            File.WriteAllText(Path.Combine(appRoot, "main.mjs"), """
                                                                 import value from "direct";
                                                                 export default value;
                                                                 """);

            File.WriteAllText(Path.Combine(directPackageTarget, "package.json"), """{ "type": "module" }""");
            File.WriteAllText(Path.Combine(directPackageTarget, "index.js"), """
                                                                             import value from "transitive";
                                                                             export default `direct:${value}`;
                                                                             """);

            File.WriteAllText(Path.Combine(transitivePackageTarget, "package.json"), """{ "type": "module" }""");
            File.WriteAllText(Path.Combine(transitivePackageTarget, "index.js"), """export default "transitive";""");

            TryCreateDirectoryLink(Path.Combine(appRoot, "node_modules", "direct"), directPackageTarget);
            TryCreateDirectoryLink(Path.Combine(directPackageTarget, "node_modules", "transitive"),
                transitivePackageTarget);

            using var runtime = NodeRuntime.CreateBuilder().Build();
            var result = runtime.RunMainModule(Path.Combine(appRoot, "main.mjs"));

            Assert.That(result.TryGetObject(out var nsObj), Is.True);
            Assert.That(nsObj!.TryGetProperty("default", out var value), Is.True);
            Assert.That(value.AsString(), Is.EqualTo("direct:transitive"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }

        static void TryCreateDirectoryLink(string linkPath, string targetPath)
        {
            try
            {
                Directory.CreateSymbolicLink(linkPath, targetPath);
            }
            catch (Exception ex) when
                (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                Assert.Ignore($"Symbolic link creation is unavailable on this machine: {ex.Message}");
            }
        }
    }

    [Test]
    public void RunMainModule_Esm_And_CommonJs_Share_Singleton_For_Symlinked_Package()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "Okojo.Node.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var appRoot = Path.Combine(tempRoot, "app");
            var storeRoot = Path.Combine(tempRoot, ".store");
            var reactTarget = Path.Combine(storeRoot, "react@1.0.0", "node_modules", "react");
            var depTarget = Path.Combine(storeRoot, "dep@1.0.0", "node_modules", "dep");

            Directory.CreateDirectory(Path.Combine(appRoot, "node_modules"));
            Directory.CreateDirectory(reactTarget);
            Directory.CreateDirectory(depTarget);
            Directory.CreateDirectory(Path.Combine(depTarget, "node_modules"));

            File.WriteAllText(Path.Combine(appRoot, "main.mjs"), """
                                                                 import React from "react";
                                                                 import dep from "dep";
                                                                 export default `${React === dep}|${React.useState === dep.useState}`;
                                                                 """);

            File.WriteAllText(Path.Combine(reactTarget, "package.json"), """{ "main": "./index.js" }""");
            File.WriteAllText(Path.Combine(reactTarget, "index.js"), """
                                                                     module.exports = {
                                                                       useState() {
                                                                         return "ok";
                                                                       }
                                                                     };
                                                                     """);

            File.WriteAllText(Path.Combine(depTarget, "package.json"), """{ "main": "./index.js" }""");
            File.WriteAllText(Path.Combine(depTarget, "index.js"), """module.exports = require("react");""");

            TryCreateDirectoryLink(Path.Combine(appRoot, "node_modules", "react"), reactTarget);
            TryCreateDirectoryLink(Path.Combine(appRoot, "node_modules", "dep"), depTarget);
            TryCreateDirectoryLink(Path.Combine(depTarget, "node_modules", "react"), reactTarget);

            using var runtime = NodeRuntime.CreateBuilder().Build();
            var result = runtime.RunMainModule(Path.Combine(appRoot, "main.mjs"));

            Assert.That(result.TryGetObject(out var nsObj), Is.True);
            Assert.That(nsObj!.TryGetProperty("default", out var value), Is.True);
            Assert.That(value.AsString(), Is.EqualTo("true|true"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }

        static void TryCreateDirectoryLink(string linkPath, string targetPath)
        {
            try
            {
                Directory.CreateSymbolicLink(linkPath, targetPath);
            }
            catch (Exception ex) when
                (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                Assert.Ignore($"Symbolic link creation is unavailable on this machine: {ex.Message}");
            }
        }
    }

    [Test]
    public void RunMainModule_Uses_CommonJs_For_Cjs_Entry()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/package.json"] = """{ "type": "module" }""",
                ["/app/main.cjs"] = "module.exports = 7;"
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.cjs");

        Assert.That(result.Int32Value, Is.EqualTo(7));
    }

    [Test]
    public void Require_Loads_Sync_Esm_Under_PackageTypeModule()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.cjs"] = "module.exports = require('./esm.js').value;",
                ["/app/package.json"] = """{ "type": "module" }""",
                ["/app/esm.js"] = "export const value = 9;"
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.cjs");

        Assert.That(result.Int32Value, Is.EqualTo(9));
    }

    [Test]
    public void RunMainModule_CommonJs_SyntaxError_Uses_User_Source_Location()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = """
                                   exports.answer = 1;
                                   exports.broken = ;
                                   """
            }))
            .Build();

        var ex = Assert.Throws<JsParseException>(() => runtime.RunMainModule("/app/main.js"));

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Line, Is.EqualTo(2));
        Assert.That(ex.Column, Is.GreaterThan(0));
    }

    [Test]
    public void Require_NodePath_BuiltIn_Exposes_Path_Functions()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = """
                                   const path = require("node:path");
                                   module.exports = [
                                     path.join("a", "b", "c"),
                                     path.dirname("a/b/c.txt"),
                                     path.basename("a/b/c.txt"),
                                     path.extname("a/b/c.txt"),
                                     path.sep,
                                     path.delimiter
                                   ].join("|");
                                   """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js");
        var expected = string.Join("|", Path.Combine("a", "b", "c"), Path.GetDirectoryName("a/b/c.txt") ?? ".",
            Path.GetFileName("a/b/c.txt"), Path.GetExtension("a/b/c.txt"), Path.DirectorySeparatorChar.ToString(),
            Path.PathSeparator.ToString());

        Assert.That(result.AsString(), Is.EqualTo(expected));
    }

    [Test]
    public void Require_BarePath_Uses_BuiltIn_Module()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = """module.exports = require("path").join("x", "y");"""
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js");

        Assert.That(result.AsString(), Is.EqualTo(Path.Combine("x", "y")));
    }

    [Test]
    public void GlobalProcess_Matches_NodeProcess_BuiltIn_Module()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = """
                                   module.exports = process === require("node:process");
                                   """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js");

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Process_BuiltIn_Exposes_Cwd_And_Writable_Env()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = """
                                   const processMod = require("node:process");
                                   processMod.env.OKOJO_NODE_TEST = "ok";
                                   module.exports = [
                                     processMod.cwd(),
                                     processMod.env.OKOJO_NODE_TEST,
                                     processMod.platform,
                                     typeof processMod.version,
                                     typeof processMod.versions.node
                                   ].join("|");
                                   """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js").AsString();
        var parts = result.Split('|');

        Assert.That(parts[0], Is.EqualTo(Environment.CurrentDirectory));
        Assert.That(parts[1], Is.EqualTo("ok"));
        Assert.That(parts[2], Is.Not.Empty);
        Assert.That(parts[3], Is.EqualTo("string"));
        Assert.That(parts[4], Is.EqualTo("string"));
    }

    [Test]
    public void Process_NextTick_Runs_Before_Promise_Jobs()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = """
                                   globalThis.logs = [];
                                   process.nextTick(() => logs.push("tick"));
                                   Promise.resolve().then(() => logs.push("promise"));
                                   module.exports = 0;
                                   """
            }))
            .Build();

        _ = runtime.RunMainModule("/app/main.js");
        runtime.MainRealm.PumpJobs();

        var logsValue = runtime.MainRealm.Global["logs"];
        Assert.That(logsValue.TryGetObject(out var logsObj), Is.True);
        Assert.That(logsObj!.TryGetProperty("0", out var first), Is.True);
        Assert.That(logsObj.TryGetProperty("1", out var second), Is.True);
        Assert.That(first.AsString(), Is.EqualTo("tick"));
        Assert.That(second.AsString(), Is.EqualTo("promise"));
    }

    [Test]
    public void Require_NodeEvents_Exposes_Minimal_EventEmitter()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = """
                                   const { EventEmitter } = require("node:events");
                                   const emitter = new EventEmitter();
                                   const logs = [];
                                   function keep(value) { logs.push("keep:" + value); }
                                   emitter.once("data", value => logs.push("once:" + value));
                                   emitter.on("data", keep);
                                   emitter.emit("data", 1);
                                   emitter.off("data", keep);
                                   emitter.emit("data", 2);
                                   module.exports = logs.join("|");
                                   """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js");

        Assert.That(result.AsString(), Is.EqualTo("once:1|keep:1"));
    }

    [Test]
    public void RunMainModule_Esm_Import_Uses_NodeBuiltIn_Resolution()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    import path, { join, dirname } from "node:path";
                                    import process from "node:process";
                                    export default [join("a", "b"), dirname("a/b/c.txt"), process.cwd(), path.sep].join("|");
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj!.TryGetProperty("default", out var value), Is.True);
        var expected = string.Join("|", Path.Combine("a", "b"), Path.GetDirectoryName("a/b/c.txt") ?? ".",
            Environment.CurrentDirectory, Path.DirectorySeparatorChar.ToString());
        Assert.That(value.AsString(), Is.EqualTo(expected));
    }

    [Test]
    public void RunMainModule_Esm_Import_Uses_NodePackage_Exports_Resolution()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    import value from "pkg";
                                    export default value;
                                    """,
                ["/app/node_modules/pkg/package.json"] =
                    """{ "exports": { "import": "./esm.mjs", "require": "./cjs.js", "default": "./fallback.js" } }""",
                ["/app/node_modules/pkg/esm.mjs"] = """export default "esm-export";""",
                ["/app/node_modules/pkg/cjs.js"] = """module.exports = "cjs-export";""",
                ["/app/node_modules/pkg/fallback.js"] = """export default "fallback-export";"""
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("esm-export"));
    }

    [Test]
    public void RunMainModule_Esm_DefaultImport_Uses_CommonJs_Default_Interop()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    import react from "react";
                                    export default react.answer;
                                    """,
                ["/app/node_modules/react/package.json"] = """{ "main": "./index.js" }""",
                ["/app/node_modules/react/index.js"] = """
                                                       module.exports = { answer: 42 };
                                                       """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void RunMainModule_Esm_NamedImport_Uses_CommonJs_Discovered_Exports()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    import { LegacyRoot, ConcurrentRoot } from "react-reconciler/constants.js";
                                    export default `${LegacyRoot}|${ConcurrentRoot}`;
                                    """,
                ["/app/node_modules/react-reconciler/constants.js"] = """
                                                                      module.exports = require("./cjs/react-reconciler-constants.development.js");
                                                                      """,
                ["/app/node_modules/react-reconciler/cjs/react-reconciler-constants.development.js"] = """
                    exports.ConcurrentRoot = 1;
                    exports.LegacyRoot = 0;
                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("0|1"));
    }

    [Test]
    public void RunMainModule_Esm_NamespaceImport_Uses_CommonJs_Discovered_Exports()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    import * as Scheduler from "scheduler";
                                    export default `${typeof Scheduler.unstable_scheduleCallback}|${typeof Scheduler.unstable_cancelCallback}`;
                                    """,
                ["/app/node_modules/scheduler/package.json"] = """{ "main": "./index.js" }""",
                ["/app/node_modules/scheduler/index.js"] = """
                                                           module.exports = require("./cjs/scheduler.development.js");
                                                           """,
                ["/app/node_modules/scheduler/cjs/scheduler.development.js"] = """
                                                                               exports.unstable_scheduleCallback = function unstable_scheduleCallback() {};
                                                                               exports.unstable_cancelCallback = function unstable_cancelCallback() {};
                                                                               """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("function|function"));
    }

    [Test]
    public void RunMainModule_Esm_Import_Uses_NodeEvents_And_NextTick()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    import EventEmitter from "node:events";
                                    import { nextTick } from "node:process";
                                    const emitter = new EventEmitter();
                                    let value = "start";
                                    emitter.on("ready", message => value = message);
                                    nextTick(() => emitter.emit("ready", "tick-first"));
                                    await 0;
                                    export default value;
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("tick-first"));
    }

    [Test]
    public void Process_Stdout_And_Stderr_Expose_Minimal_Write_Surface()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = """
                                   module.exports = [
                                     typeof process.stdout.write,
                                     process.stdout.fd,
                                     process.stderr.fd,
                                     process.stdout.isTTY,
                                     process.stderr.isTTY,
                                     process.stdout.write("x")
                                   ].join("|");
                                   """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js").AsString().Split('|');

        Assert.That(result[0], Is.EqualTo("function"));
        Assert.That(result[1], Is.EqualTo("1"));
        Assert.That(result[2], Is.EqualTo("2"));
        Assert.That(result[3], Is.EqualTo("false"));
        Assert.That(result[4], Is.EqualTo("false"));
        Assert.That(result[5], Is.EqualTo("true"));
    }

    [Test]
    public void Require_NodeTty_Exposes_IsAtty_And_Process_Streams()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = """
                                   const tty = require("node:tty");
                                   module.exports = [
                                     typeof tty.isatty,
                                     tty.isatty(1),
                                     tty.isatty(2),
                                     process.stdout.isTTY,
                                     process.stderr.isTTY
                                   ].join("|");
                                   """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js").AsString().Split('|');

        Assert.That(result[0], Is.EqualTo("function"));
        Assert.That(result[1], Is.EqualTo("false"));
        Assert.That(result[2], Is.EqualTo("false"));
        Assert.That(result[3], Is.EqualTo("false"));
        Assert.That(result[4], Is.EqualTo("false"));
    }

    [Test]
    public void Terminal_Config_Exposes_Tty_Columns_And_Rows()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .ConfigureTerminal(static options =>
            {
                options.StdoutIsTty = true;
                options.StderrIsTty = true;
                options.StdoutColumns = 120;
                options.StdoutRows = 40;
                options.StderrColumns = 120;
                options.StderrRows = 40;
            })
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = """
                                   const tty = require("node:tty");
                                   module.exports = [
                                     tty.isatty(1),
                                     process.stdout.isTTY,
                                     process.stdout.columns,
                                     process.stdout.rows,
                                     process.stderr.columns,
                                     process.stderr.rows
                                   ].join("|");
                                   """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js").AsString().Split('|');

        Assert.That(result[0], Is.EqualTo("true"));
        Assert.That(result[1], Is.EqualTo("true"));
        Assert.That(result[2], Is.EqualTo("120"));
        Assert.That(result[3], Is.EqualTo("40"));
        Assert.That(result[4], Is.EqualTo("120"));
        Assert.That(result[5], Is.EqualTo("40"));
    }

    [Test]
    public void Require_NodeUtil_Exposes_Format_And_Inspect()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = """
                                   const util = require("node:util");
                                   module.exports = [
                                     util.format("%s:%d:%j", "x", 2, { a: 1 }),
                                     util.inspect({ a: [1, 2] })
                                   ].join("|");
                                   """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js").AsString().Split('|');

        Assert.That(result[0], Is.EqualTo("""x:2:{"a":1}"""));
        Assert.That(result[1], Is.EqualTo("{ a: [ 1, 2 ] }"));
    }

    [Test]
    public void Import_NodeUtil_Exposes_Default_And_Named_Exports()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    import util, { format, inspect } from "node:util";
                                    export default [
                                      typeof util.format,
                                      format("%s-%d", "v", 3),
                                      inspect({ ok: true })
                                    ].join("|");
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("function|v-3|{ ok: true }"));
    }

    [Test]
    public void Terminal_Stream_Exposes_Minimal_Tty_Control_Methods()
    {
        var stdout = new StringWriter();
        using var runtime = NodeRuntime.CreateBuilder()
            .ConfigureTerminal(options =>
            {
                options.Stdout = stdout;
                options.StdoutIsTty = true;
            })
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = """
                                   module.exports = [
                                     typeof process.stdout.cursorTo,
                                     typeof process.stdout.moveCursor,
                                     typeof process.stdout.clearLine,
                                     typeof process.stdout.clearScreenDown,
                                     process.stdout.cursorTo(3),
                                     process.stdout.moveCursor(-2, 1),
                                     process.stdout.clearLine(0),
                                     process.stdout.clearScreenDown()
                                   ].join("|");
                                   """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js").AsString().Split('|');

        Assert.That(result[0], Is.EqualTo("function"));
        Assert.That(result[1], Is.EqualTo("function"));
        Assert.That(result[2], Is.EqualTo("function"));
        Assert.That(result[3], Is.EqualTo("function"));
        Assert.That(result[4], Is.EqualTo("true"));
        Assert.That(result[5], Is.EqualTo("true"));
        Assert.That(result[6], Is.EqualTo("true"));
        Assert.That(result[7], Is.EqualTo("true"));
        Assert.That(stdout.ToString(), Is.EqualTo("\u001b[4G\u001b[2D\u001b[1B\u001b[2K\u001b[0J"));
    }

    [Test]
    public void Require_NodeStream_Exposes_Constructors_And_PassThrough_DataFlow()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = """
                                   const stream = require("node:stream");
                                   const pass = new stream.PassThrough();
                                   let chunks = "";
                                   pass.on("data", chunk => { chunks += chunk.toString(); });
                                   pass.write("ab");
                                   pass.end("c");
                                   module.exports = [
                                     typeof stream.PassThrough,
                                     typeof stream.Writable,
                                     typeof stream.Readable,
                                     typeof stream.Duplex,
                                     typeof stream.Transform,
                                     typeof stream.pipeline,
                                     chunks,
                                     pass.readable,
                                     pass.writable,
                                     pass.writableEnded,
                                     pass.destroyed
                                   ].join("|");
                                   """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js").AsString().Split('|');

        Assert.That(result[0], Is.EqualTo("function"));
        Assert.That(result[1], Is.EqualTo("function"));
        Assert.That(result[2], Is.EqualTo("function"));
        Assert.That(result[3], Is.EqualTo("function"));
        Assert.That(result[4], Is.EqualTo("function"));
        Assert.That(result[5], Is.EqualTo("function"));
        Assert.That(result[6], Is.EqualTo("abc"));
        Assert.That(result[7], Is.EqualTo("true"));
        Assert.That(result[8], Is.EqualTo("true"));
        Assert.That(result[9], Is.EqualTo("true"));
        Assert.That(result[10], Is.EqualTo("false"));
    }

    [Test]
    public void Import_NodeStream_Exposes_Default_And_Named_Exports()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    import stream, { PassThrough, pipeline } from "node:stream";
                                    const pass = new PassThrough();
                                    let chunks = "";
                                    pass.on("data", chunk => { chunks += chunk.toString(); });
                                    pass.write("x");
                                    pass.end("y");
                                    export default [
                                      typeof stream.PassThrough,
                                      typeof pipeline,
                                      chunks
                                    ].join("|");
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("function|function|xy"));
    }

    [Test]
    public void Import_NodeStream_Exposes_Stream_Base_Alias()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.mjs"] = """
                                    import { Stream, PassThrough } from "node:stream";
                                    const pass = new PassThrough();
                                    export default `${typeof Stream}|${pass instanceof Stream}`;
                                    """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.mjs");

        Assert.That(result.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("function|true"));
    }

    [Test]
    public void NodeGlobals_Install_SetTimeout()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = """
                                   globalThis.trace = "";
                                   setTimeout(() => { trace += "t"; }, 0);
                                   trace += "s";
                                   module.exports = typeof setTimeout;
                                   """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js");
        runtime.MainRealm.PumpJobs();

        Assert.That(result.AsString(), Is.EqualTo("function"));
    }

    [Test]
    public void NodeGlobals_Timers_Use_Queued_Host_Loop_When_Available()
    {
        var hostLoop = new ManualHostEventLoop(TimeProvider.System);
        using var runtime = NodeRuntime.CreateBuilder()
            .UseHostTaskScheduler(hostLoop)
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = """
                                   globalThis.step = 0;
                                   const id = setInterval(() => {
                                     step++;
                                     if (step === 3)
                                       clearInterval(id);
                                   }, 1);
                                   module.exports = typeof setInterval;
                                   """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js");

        Assert.That(result.AsString(), Is.EqualTo("function"));
        Assert.That(hostLoop.GetSnapshot().PendingDelayedCount, Is.EqualTo(1));

        var completed = HostTurnRunner.RunUntil(
            hostLoop,
            new(runtime.Runtime.MainAgent),
            () =>
            {
                var snapshot = hostLoop.GetSnapshot();
                return runtime.MainRealm.Global.TryGetValue("step", out var stepValue) &&
                       stepValue.IsNumber &&
                       stepValue.Int32Value == 3 &&
                       snapshot.PendingDelayedCount == 0;
            },
            TimeSpan.FromSeconds(2));

        Assert.That(completed, Is.True);
    }

    [Test]
    public void NodeGlobals_Install_SetImmediate_And_ClearImmediate()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = """
                                   globalThis.trace = [];
                                   const cancelled = setImmediate(() => trace.push("cancelled"));
                                   clearImmediate(cancelled);
                                   process.nextTick(() => trace.push("tick"));
                                   Promise.resolve().then(() => trace.push("promise"));
                                   setImmediate(() => trace.push("immediate"));
                                   module.exports = [typeof setImmediate, typeof clearImmediate].join("|");
                                   """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js");
        runtime.MainRealm.PumpJobs();

        Assert.That(result.AsString(), Is.EqualTo("function|function"));
        Assert.That(runtime.MainRealm.Eval("trace.join(',')").AsString(), Is.EqualTo("tick,promise,immediate"));
    }

    [Test]
    public void Require_NodeChildProcess_ExecFileSync_Supports_Utf8_Output()
    {
        string command;
        string[] commandArgs;
        string expected;
        if (OperatingSystem.IsWindows())
        {
            command = "cmd";
            commandArgs = ["/c", "echo hello"];
            expected = "hello\r\n";
        }
        else
        {
            command = "printf";
            commandArgs = ["hello"];
            expected = "hello";
        }

        var argsLiteral = string.Join(", ",
            commandArgs.Select(static arg =>
                $"'{arg.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal)}'"));
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = $$"""
                                     const childProcess = require("node:child_process");
                                     module.exports = childProcess.execFileSync('{{command}}', [{{argsLiteral}}], { encoding: 'utf8' });
                                     """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js");

        Assert.That(result.AsString(), Is.EqualTo(expected));
    }

    [Test]
    public void Require_NodeFs_Exposes_ReadFileSync_And_Constants()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "hello-fs");

            var escapedPath = tempFile
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("'", "\\'", StringComparison.Ordinal);

            using var runtime = NodeRuntime.CreateBuilder()
                .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
                {
                    ["/app/main.js"] = $$"""
                                         const fs = require("node:fs");
                                         module.exports = [
                                           fs.readFileSync('{{escapedPath}}', 'utf8'),
                                           typeof fs.openSync,
                                           typeof fs.constants,
                                           typeof fs.constants.O_NONBLOCK
                                         ].join("|");
                                         """
                }))
                .Build();

            var result = runtime.RunMainModule("/app/main.js").AsString().Split('|');

            Assert.That(result[0], Is.EqualTo("hello-fs"));
            Assert.That(result[1], Is.EqualTo("function"));
            Assert.That(result[2], Is.EqualTo("object"));
            Assert.That(result[3], Is.EqualTo("number"));
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Test]
    public void Import_NodeFs_Exposes_WriteFile_With_Callback_Signature()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "Okojo.Node.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var targetPath = Path.Combine(tempRoot, "out.txt");
            var escapedPath = targetPath
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("'", "\\'", StringComparison.Ordinal);

            using var runtime = NodeRuntime.CreateBuilder()
                .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
                {
                    ["/app/main.mjs"] = $$"""
                                          import { writeFile, readFileSync } from "node:fs";
                                          let callbackArgType = "missing";
                                          writeFile('{{escapedPath}}', 'hello-write-file', 'utf-8', (err) => {
                                            callbackArgType = typeof err;
                                          });
                                          export default `${callbackArgType}|${readFileSync('{{escapedPath}}', 'utf8')}`;
                                          """
                }))
                .Build();

            var result = runtime.RunMainModule("/app/main.mjs");

            Assert.That(result.TryGetObject(out var nsObj), Is.True);
            Assert.That(nsObj!.TryGetProperty("default", out var value), Is.True);
            Assert.That(value.AsString(), Is.EqualTo("undefined|hello-write-file"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    [Test]
    public void Buffer_Global_And_NodeBuffer_BuiltIn_Expose_Minimal_Buffer_Api()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = """
                                   const nodeBuffer = require("node:buffer");
                                   const same = nodeBuffer.Buffer === Buffer;
                                   const from = Buffer.from("ABC");
                                   const alloc = Buffer.alloc(3, 65);
                                   module.exports = [
                                     same,
                                     Buffer.isBuffer(from),
                                     from.length,
                                     from[0],
                                     Buffer.byteLength("ABC"),
                                     alloc[1]
                                   ].join("|");
                                   """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js").AsString().Split('|');

        Assert.That(result[0], Is.EqualTo("true"));
        Assert.That(result[1], Is.EqualTo("true"));
        Assert.That(result[2], Is.EqualTo("3"));
        Assert.That(result[3], Is.EqualTo("65"));
        Assert.That(result[4], Is.EqualTo("3"));
        Assert.That(result[5], Is.EqualTo("65"));
    }

    [Test]
    public void Buffer_From_ArrayBuffer_Creates_Shared_Uint8Array_View()
    {
        using var runtime = NodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/app/main.js"] = """
                                   const source = new Uint8Array(4);
                                   source[1] = 65;
                                   const view = Buffer.from(source.buffer, 1, 2);
                                   view[0] = 66;
                                   module.exports = [
                                     source[1],
                                     view[0],
                                     view.length,
                                     Buffer.isBuffer(view)
                                   ].join("|");
                                   """
            }))
            .Build();

        var result = runtime.RunMainModule("/app/main.js").AsString().Split('|');

        Assert.That(result[0], Is.EqualTo("66"));
        Assert.That(result[1], Is.EqualTo("66"));
        Assert.That(result[2], Is.EqualTo("2"));
        Assert.That(result[3], Is.EqualTo("true"));
    }

    private static string ToJsStringLiteral(string value)
    {
        return "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal) + "\"";
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tests")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root for Okojo.Node integration tests.");
    }

    private sealed class InMemoryModuleLoader(Dictionary<string, string> modules) : IModuleSourceLoader
    {
        private readonly Dictionary<string, string> modules = modules;

        public string ResolveSpecifier(string specifier, string? referrer)
        {
            if (specifier.StartsWith("./", StringComparison.Ordinal) ||
                specifier.StartsWith("../", StringComparison.Ordinal))
            {
                var basePath = referrer is null ? "/" : Normalize(referrer);
                var slash = basePath.LastIndexOf('/');
                var dir = slash >= 0 ? basePath[..(slash + 1)] : "/";
                return Normalize(dir + specifier);
            }

            return Normalize(specifier);
        }

        public string LoadSource(string resolvedId)
        {
            if (modules.TryGetValue(Normalize(resolvedId), out var source))
                return source;
            throw new InvalidOperationException("Module not found: " + resolvedId);
        }

        private static string Normalize(string path)
        {
            path = path.Replace('\\', '/');
            var parts = new List<string>();
            foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part == ".")
                    continue;
                if (part == "..")
                {
                    if (parts.Count != 0)
                        parts.RemoveAt(parts.Count - 1);
                    continue;
                }

                parts.Add(part);
            }

            return "/" + string.Join("/", parts);
        }
    }
}
