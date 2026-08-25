using System.Runtime.CompilerServices;
using Okojo.Diagnostics;
using Okojo.JavaScript;
using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler.Tests;

public class JsScriptCompilerTests
{
    [Test]
    public void Compile_ExecutesLocalOnlyLetAndAddProgram()
    {
        var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var compiler = new JsScriptCompiler(realm);

        var script = compiler.Compile(
            JavaScriptParser.ParseScript(
                """
                let x = 41;
                x + 1;
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
        Assert.That(script.Bytecode.Contains((byte)JsOpCode.AddSmi), Is.True);
    }

    [Test]
    public void Compile_UsesRealBytecodeAndRegisterMetadata()
    {
        var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var compiler = new JsScriptCompiler(realm);

        var script = compiler.Compile(
            JavaScriptParser.ParseScript(
                """
                var a = 1;
                const b = 2;
                a + b;
                """
            )
        );

        Assert.That(script.Bytecode.Length, Is.GreaterThan(0));
        Assert.That(script.RegisterCount, Is.GreaterThanOrEqualTo(1));
        Assert.That(script.TopLevelLexicalAtoms, Has.Length.EqualTo(1));
        Assert.That(script.Bytecode, Does.Contain((byte)JsOpCode.StaGlobalInit));
        Assert.That(script.Bytecode.Contains((byte)JsOpCode.Return), Is.True);
    }

    [Test]
    public void CompileModule_EmitsFinalizedModuleCellsThroughExportWrappers()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsModuleCompiler(realm).Compile(
            """
            import { source as imported } from "dependency";
            export let value = imported;
            export function read() { return value; }
            value++;
            export default value;
            """
        );

        var disassembly = Disassembler.Dump(script);
        Assert.Multiple(() =>
        {
            Assert.That(disassembly, Does.Contain("LdaModuleVariable cell_index:-1, depth:0"));
            Assert.That(disassembly, Does.Contain("StaModuleVariable cell_index:1, depth:0"));
            Assert.That(disassembly, Does.Contain("StaModuleVariable cell_index:2, depth:0"));
            Assert.That(disassembly, Does.Contain("StaModuleVariable cell_index:3, depth:0"));
        });

        var read = script.ObjectConstants.OfType<JsBytecodeFunction>().Single();
        Assert.That(
            Disassembler.Dump(read.Script),
            Does.Contain("LdaModuleVariable cell_index:3, depth:0")
        );
    }

    [Test]
    public void CompileModule_InitializesNamespaceImportFromLinkedModuleBindings()
    {
        using var runtime = JsRuntime.Create(builder =>
            builder.UseModuleSourceLoader(new TestModuleSourceLoader())
        );
        var realm = runtime.DefaultRealm;
        var script = new JsModuleCompiler(realm).Compile(
            """
            import * as namespaceValue from "./dependency" with { type: "json" };
            export default namespaceValue;
            """
        );
        var namespaceObject = new JsPlainObject(realm);
        var imports = new JsPlainObject(realm);
        imports.SetProperty("dependency\0json", JsValue.FromObject(namespaceObject));
        var exports = new[] { new ModuleVariableSlot(ModuleVariableSlotKind.Local) };
        var bindings = new ModuleExecutionBindings(
            "entry",
            JsValue.FromObject(imports),
            JsValue.Undefined,
            exports,
            [],
            JsValue.Undefined
        );
        var context = new JsContext(null, 0) { ModuleBindings = bindings };
        var root = new JsBytecodeFunction(realm, script, isStrict: true)
        {
            BoundParentContext = context,
        };

        realm.Execute(root);

        Assert.That(exports[0].LocalValue.AsObject(), Is.SameAs(namespaceObject));
        Assert.That(
            Disassembler.Dump(script),
            Does.Contain("CallRuntime runtime:GetCurrentModuleNamespace")
        );
    }

    [Test]
    public void CompileModule_ExecutesThroughLinkedModuleGraph()
    {
        var loader = new TestModuleSourceLoader(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dependency"] = """
                export let source = 40;
                source += 2;
                export const other = 1;
                """,
                ["bridge"] = """
                export { source as value } from "./dependency";
                export * as dependency from "./dependency";
                export * from "./dependency";
                """,
                ["entry"] = """
                import { value as z, other as a, dependency as forwarded } from "./bridge";
                import * as bridge from "./bridge";
                export const answer = z + a + forwarded.other + bridge.other - 3;
                export default answer;
                """,
            }
        );
        var options = new JsRuntimeOptions().UseModuleSourceLoader(loader);
        options.Agent.UseModuleCompiler();
        using var runtime = JsRuntime.Create(options);

        var module = runtime.MainRealm.LoadModule("entry");

        Assert.That(module.GetExport("answer").Int32Value, Is.EqualTo(42));
        Assert.That(module.GetExport("default").Int32Value, Is.EqualTo(42));
        Assert.That(runtime.MainAgent.ModuleGraph.TryGet("entry", out var entry), Is.True);
        Assert.That(entry.Program, Is.Null, "pooled JsAst should be released after compile");
    }

    [Test]
    public void CompileModule_ClassFieldInitializerSeesSiblingTopLevelClass()
    {
        var options = new JsRuntimeOptions().UseModuleSourceLoader(
            new TestModuleSourceLoader(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["/mods/main.js"] = """
                    class Helper {
                      tag() { return 'helper'; }
                    }
                    export class Main {
                      helper = new Helper();
                    }
                    """,
                }
            )
        );
        options.Agent.UseModuleCompiler();
        using var runtime = JsRuntime.Create(options);
        var realm = runtime.DefaultRealm;

        var module = runtime.MainRealm.LoadModule("/mods/main.js");
        realm.GlobalObject.DefineDataProperty(
            "__Main",
            module.GetExport("Main"),
            JsShapePropertyFlags.Open
        );
        realm.Evaluate("globalThis.__probeTag = new __Main().helper.tag();");

        Assert.That(realm.Evaluate("__probeTag").AsString(), Is.EqualTo("helper"));
    }

    [Test]
    public void CompileModule_DefaultClassFieldInitializerSeesSiblingTopLevelClass()
    {
        var options = new JsRuntimeOptions().UseModuleSourceLoader(
            new TestModuleSourceLoader(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["/mods/main.js"] = """
                    class OutputCaches {
                      get(key) { return 'cached:' + key; }
                    }
                    export default class Output {
                      caches = new OutputCaches();
                    }
                    """,
                }
            )
        );
        options.Agent.UseModuleCompiler();
        using var runtime = JsRuntime.Create(options);
        var realm = runtime.DefaultRealm;

        var module = runtime.MainRealm.LoadModule("/mods/main.js");
        realm.GlobalObject.DefineDataProperty(
            "__Output",
            module.GetExport("default"),
            JsShapePropertyFlags.Open
        );
        realm.Evaluate("globalThis.__probeCache = new __Output().caches.get('line');");

        Assert.That(realm.Evaluate("__probeCache").AsString(), Is.EqualTo("cached:line"));
    }

    [Test]
    public void CompileModule_NonExportedFunctionDeclarationStaysModuleScoped()
    {
        var options = new JsRuntimeOptions().UseModuleSourceLoader(
            new TestModuleSourceLoader(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["/mods/main.js"] = """
                    function buildValue() { return 41; }
                    export const value = buildValue() + 1;
                    """,
                }
            )
        );
        options.Agent.UseModuleCompiler();
        using var runtime = JsRuntime.Create(options);

        var module = runtime.MainRealm.LoadModule("/mods/main.js");

        Assert.That(module.GetExport("value").Int32Value, Is.EqualTo(42));
        Assert.That(
            runtime.DefaultRealm.Evaluate("typeof buildValue").AsString(),
            Is.EqualTo("undefined"),
            "module top-level declarations must not leak to the global object"
        );
    }

    [Test]
    public void CompileModule_ExecutesImportMetaInModuleAndClosureContexts()
    {
        var options = new JsRuntimeOptions().UseModuleSourceLoader(
            new TestModuleSourceLoader(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["/mods/main.js"] = """
                    export const meta = import.meta;
                    export const url = import.meta.url;
                    export function getMeta() { return import.meta; }
                    """,
                }
            )
        );
        options.Agent.UseModuleCompiler();
        using var runtime = JsRuntime.Create(options);

        var module = runtime.MainRealm.LoadModule("/mods/main.js");

        Assert.That(module.GetExport("url").AsString(), Is.EqualTo("/mods/main.js"));
        Assert.That(
            module.CallExport("getMeta").AsObject(),
            Is.SameAs(module.GetExport("meta").AsObject())
        );
    }

    [Test]
    public void CompileModule_ExecutesDynamicImportThroughExistingPromiseRuntime()
    {
        var options = new JsRuntimeOptions().UseModuleSourceLoader(
            new TestModuleSourceLoader(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["entry"] = """
                    export let result = 0;
                    import("./dependency").then(namespace => result = namespace.answer);
                    """,
                    ["dependency"] = "export const answer = 42;",
                }
            )
        );
        options.Agent.UseModuleCompiler();
        using var runtime = JsRuntime.Create(options);

        var module = runtime.MainRealm.LoadModule("entry");
        for (var i = 0; i < 20 && module.GetExport("result").Int32Value == 0; i++)
            runtime.MainAgent.PumpJobs();

        Assert.That(module.GetExport("result").Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void CompileModule_AwaitsAsyncDependencyBeforeParentEvaluation()
    {
        var options = new JsRuntimeOptions().UseModuleSourceLoader(
            new TestModuleSourceLoader(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["entry"] = """
                    import { value } from "./dependency";
                    export const result = value + 1;
                    """,
                    ["dependency"] = """
                    export let value = 1;
                    await Promise.resolve();
                    value = 41;
                    """,
                }
            )
        );
        options.Agent.UseModuleCompiler();
        using var runtime = JsRuntime.Create(options);

        var module = runtime.MainRealm.Import("entry").AsObject();

        Assert.That(module.TryGetProperty("result", out var result), Is.True);
        Assert.That(result.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void CompileModule_InstantiatesHoistedExportOnceAcrossCycle()
    {
        var options = new JsRuntimeOptions().UseModuleSourceLoader(
            new TestModuleSourceLoader(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["a"] = """
                    import { observed } from "./b";
                    if (false) { function mustStayBlockLocal() {} }
                    export function answer() { return 42; }
                    export { observed };
                    export const same = observed === answer;
                    """,
                    ["b"] = """
                    import { answer } from "./a";
                    export const observed = answer;
                    """,
                }
            )
        );
        options.Agent.UseModuleCompiler();
        using var runtime = JsRuntime.Create(options);

        var module = runtime.MainRealm.LoadModule("a");

        Assert.That(module.GetExport("same").IsTrue, Is.True);
        Assert.That(
            module.GetExport("observed").AsObject(),
            Is.SameAs(module.GetExport("answer").AsObject())
        );
        Assert.That(module.CallExport("answer").Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void CompileModule_InstantiatesAnonymousDefaultFunctionAcrossCycle()
    {
        var options = new JsRuntimeOptions().UseModuleSourceLoader(
            new TestModuleSourceLoader(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["a"] = """
                    export { observed } from "./b";
                    export default function() { return 42; }
                    """,
                    ["b"] = """
                    import answer from "./a";
                    export const observed = answer;
                    """,
                }
            )
        );
        options.Agent.UseModuleCompiler();
        using var runtime = JsRuntime.Create(options);

        var module = runtime.MainRealm.LoadModule("a");
        var answer = module.GetExport("default");

        Assert.That(module.GetExport("observed").AsObject(), Is.SameAs(answer.AsObject()));
        Assert.That(answer.AsObject().TryGetProperty("name", out var name), Is.True);
        Assert.That(name.AsString(), Is.EqualTo("default"));
        Assert.That(module.CallExport("default").Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void CompileModule_InstantiatesNamedDefaultFunctionWithLocalBinding()
    {
        var options = new JsRuntimeOptions().UseModuleSourceLoader(
            new TestModuleSourceLoader(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["entry"] = "export default function answer() { return answer; }",
                }
            )
        );
        options.Agent.UseModuleCompiler();
        using var runtime = JsRuntime.Create(options);

        var module = runtime.MainRealm.LoadModule("entry");
        var answer = module.GetExport("default");

        Assert.That(answer.AsObject().TryGetProperty("name", out var name), Is.True);
        Assert.That(name.AsString(), Is.EqualTo("answer"));
        Assert.That(module.CallExport("default").AsObject(), Is.SameAs(answer.AsObject()));
    }

    [Test]
    public void CompileModule_PreinitializesExportedVarAcrossCycle()
    {
        var options = new JsRuntimeOptions().UseModuleSourceLoader(
            new TestModuleSourceLoader(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["a"] = """
                    import { seen } from "./b";
                    export var value = 42;
                    export const result = seen === undefined && value;
                    """,
                    ["b"] = """
                    import { value } from "./a";
                    export const seen = value;
                    """,
                }
            )
        );
        options.Agent.UseModuleCompiler();
        using var runtime = JsRuntime.Create(options);

        var module = runtime.MainRealm.LoadModule("a");

        Assert.That(module.GetExport("result").Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void Compile_RejectsUnsupportedStatements_AtFlatParserBoundary()
    {
        var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var compiler = new JsScriptCompiler(realm);

        var ex = Assert.Throws<JsParseException>(() =>
            compiler.Compile(
                JavaScriptParser.ParseScript(
                    """
                    with ({}) {}
                    """
                )
            )
        );

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Message, Does.Contain("not supported by JavaScriptParser"));
    }

    [Test]
    public void CompileFunction_InheritsSourcePathForDynamicImportReferrer()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsScriptCompiler(realm).Compile(
            """
            globalThis.__flatRan = false;
            function run() { __flatRan = true; return 1; }
            const read = () => run();
            read();
            """,
            "/mods/main.js"
        );
        realm.Execute(script);
        Assert.That(realm.Evaluate("__flatRan").IsTrue, Is.True);

        var run = script
            .ObjectConstants.OfType<JsBytecodeFunction>()
            .Single(static function => function.Name == "run");
        Assert.That(run.Script.SourcePath, Is.EqualTo("/mods/main.js"));

        var read = script
            .ObjectConstants.OfType<JsBytecodeFunction>()
            .Single(static function => function.Name == "read");
        Assert.That(read.Script.SourcePath, Is.EqualTo("/mods/main.js"));
    }

    private sealed class TestModuleSourceLoader(IReadOnlyDictionary<string, string>? modules = null)
        : IModuleSourceLoader
    {
        public string ResolveSpecifier(string specifier, string? referrer) =>
            specifier.StartsWith("./", StringComparison.Ordinal) ? specifier[2..] : specifier;

        public string LoadSource(string resolvedId) =>
            modules is not null && modules.TryGetValue(resolvedId, out var source)
                ? source
                : string.Empty;
    }

    [Test]
    public void Compile_LowersClassAstSwitchBridge()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsScriptCompiler(realm).Compile(
            JavaScriptParser.ParseScript(
                "let result = 0; switch (2) { case 1: result = 1; break; case 2: result = 42; } result;"
            )
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void Compile_LowersClassAstTryCatchBridge()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsScriptCompiler(realm);
        var script = compiler.Compile(
            JavaScriptParser.ParseScript("try { throw 42; } catch (error) { error; }")
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void Compile_LowersClassAstObjectMethodAccessorBridge()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsScriptCompiler(realm).Compile(
            JavaScriptParser.ParseScript(
                "let object = { base: 40, method() { return this.base + 2; }, get value() { return this.method(); } }; object.value;"
            )
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void Compile_LowersClassAstRegExpBigIntBridge()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsScriptCompiler(realm).Compile(
            JavaScriptParser.ParseScript(
                "let expression = /ok/i; let amount = 40n + 2n; expression.test('OK') && amount === 42n;"
            )
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Compile_LowersClassAstTemplateBridge()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsScriptCompiler(realm).Compile(
            JavaScriptParser.ParseScript(
                "let value = 40; let prefix = `answer`; let text = `${prefix}:${` ${value + 2}`}`; text;"
            )
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("answer: 42"));
    }

    [Test]
    public void Compile_LowersClassAstArrowBridge()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsScriptCompiler(realm).Compile(
            JavaScriptParser.ParseScript(
                "function outer(value) { return (() => this.base + value + arguments[0])(); } outer.call({ base: 2 }, 3);"
            )
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(8));
    }

    [Test]
    public void Compile_ExecutesBlockScopedLexicals()
    {
        var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var compiler = new JsScriptCompiler(realm);

        var script = compiler.Compile(
            JavaScriptParser.ParseScript(
                """
                let x = 1;
                {
                    let y = 40;
                    x = y + 2;
                }
                x;
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void Compile_ExecutesIdentifierAssignmentExpression()
    {
        var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var compiler = new JsScriptCompiler(realm);

        var script = compiler.Compile(
            JavaScriptParser.ParseScript(
                """
                let x = 1;
                x = x + 41;
                x;
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void Compile_ExecutesIfWithComparison()
    {
        var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var compiler = new JsScriptCompiler(realm);

        var script = compiler.Compile(
            JavaScriptParser.ParseScript(
                """
                let x = 1;
                if (x < 2) {
                    x = 42;
                } else {
                    x = 0;
                }
                x;
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void Compile_ExecutesCompoundAssignment()
    {
        var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var compiler = new JsScriptCompiler(realm);

        var script = compiler.Compile(
            JavaScriptParser.ParseScript(
                """
                let x = 40;
                let delta = 2;
                x += delta;
                x -= delta;
                x += delta;
                x;
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void Compile_EmitsNoCaptureFunctionDeclaration()
    {
        var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var compiler = new JsScriptCompiler(realm);

        var script = compiler.Compile(
            JavaScriptParser.ParseScript(
                """
                function answer() {
                    return 42;
                }
                answer;
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsObject, Is.True);
    }

    [Test]
    public void Compile_ExecutesFunctionDeclaration_CapturingRootLexical()
    {
        var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var compiler = new JsScriptCompiler(realm);

        var script = compiler.Compile(
            JavaScriptParser.ParseScript(
                """
                let x = 41;
                function answer() {
                    return x + 1;
                }
                answer;
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.Obj, Is.AssignableTo<JsFunction>());
        var fn = (JsFunction)realm.Accumulator.Obj!;
        var result = realm.InvokeFunction(fn, JsValue.Undefined, ReadOnlySpan<JsValue>.Empty);
        Assert.That(result.Int32Value, Is.EqualTo(42));
    }
}
