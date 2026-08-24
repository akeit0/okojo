using Okojo.JavaScript;
using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Compiler.Experimental;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;
using Okojo.JavaScript.Parsing;

namespace Okojo.Compiler.Tests;

public sealed class ModuleHostReentryTests
{
    private sealed class MemoryLoader(Dictionary<string, string> modules) : IModuleSourceLoader
    {
        public string ResolveSpecifier(string specifier, string? referrer) => specifier;

        public string LoadSource(string resolvedId) => modules[resolvedId];
    }

    private const string ShimSource = """
        const nodeCjsDefault = globalThis[Symbol.for("node.host.import")]("/mods/react.js");
        export default nodeCjsDefault;
        export const version = nodeCjsDefault.version;
        """;

    [Test]
    public void HostReentry_ProductionModuleTopLevel()
    {
        var loader = new MemoryLoader(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["/mods/shim.js"] = ShimSource,
            }
        );
        var options = new JsRuntimeOptions().UseModuleSourceLoader(loader);
        using var runtime = JsRuntime.Create(options);
        var realm = runtime.DefaultRealm;

        var parsed = JavaScriptParser.ParseScript(
            "(function (exports, require, module, __filename, __dirname) {\n"
                + "var captureModule = function () { return module; };\n"
                + "if (process.env.NODE_ENV === 'production') {\n"
                + "  module.exports = require('./prod');\n"
                + "} else {\n"
                + "  module.exports = require('./dev');\n"
                + "}\n"
                + "\n})",
            "/mods/react.js",
            -57,
            "var captureModule = function () { return module; };"
        );
        var expression = (JsFunctionExpression)
            ((JsExpressionStatement)parsed.Statements[0]).Expression;
        using var compiler = new JsCompiler(realm);
        var wrapper = compiler.CompileHoistedFunctionTemplate(
            expression,
            string.Empty,
            "WRAPPER",
            "/mods/react.js",
            parsed.IdentifierTable
        );

        var requireFunction = realm.Evaluate("(s) => ({ version: '19' })");
        var hostImport = new JsHostFunction(
            realm,
            (in info) =>
            {
                var hostRealm = info.Realm;
                var fn = (JsHostFunction)info.Function;
                var wrapperValue = JsValue.FromObject((JsBytecodeFunction)fn.UserData!);
                var exports = new JsPlainObject(hostRealm);
                var moduleObj = new JsPlainObject(hostRealm);
                moduleObj.DefineDataProperty(
                    "exports",
                    JsValue.FromObject(exports),
                    JsShapePropertyFlags.Open
                );
                Span<JsValue> args =
                [
                    JsValue.FromObject(exports),
                    requireFunction,
                    JsValue.FromObject(moduleObj),
                    JsValue.FromString("/mods/react.js"),
                    JsValue.FromString("/mods"),
                ];
                _ = hostRealm.Call(wrapperValue, JsValue.FromObject(exports), args);
                moduleObj.DefineDataProperty("loaded", JsValue.True, JsShapePropertyFlags.Open);
                if (!hostRealm.Atoms.TryGetInterned("exports", out var exportsAtom))
                    throw new InvalidOperationException("exports atom missing");
                moduleObj.TryGetPropertyAtom(hostRealm, exportsAtom, out var currentExports, out _);
                return currentExports;
            },
            "host.import",
            1
        )
        {
            UserData = wrapper,
        };

        realm.Evaluate("process = { env: { NODE_ENV: 'development' } };");
        realm.GlobalObject.DefineDataProperty(
            "__hostImport",
            JsValue.FromObject(hostImport),
            JsShapePropertyFlags.Open
        );
        realm.Evaluate("globalThis[Symbol.for('node.host.import')] = __hostImport;");

        var module = runtime.MainRealm.LoadModule("/mods/shim.js");

        Assert.That(module.TryGetExport("version", out var versionValue), Is.True);
        Assert.That(versionValue.IsUndefined, Is.False, "version export undefined");
        Assert.That(runtime.DefaultRealm.ToJsString(versionValue), Is.EqualTo("19"));
    }

    [Test]
    public void HostReentry_PlannedModuleTopLevel()
    {
        var loader = new MemoryLoader(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["/mods/shim.js"] = ShimSource,
            }
        );
        var options = new JsRuntimeOptions().UseModuleSourceLoader(loader);
        options.Agent.UsePlannedModuleCompiler();
        using var runtime = JsRuntime.Create(options);
        var realm = runtime.DefaultRealm;

        var parsed = JavaScriptParser.ParseScript(
            "(function (exports, require, module, __filename, __dirname) {\n"
                + "var captureModule = function () { return module; };\n"
                + "if (process.env.NODE_ENV === 'production') {\n"
                + "  module.exports = require('./prod');\n"
                + "} else {\n"
                + "  module.exports = require('./dev');\n"
                + "}\n"
                + "\n})",
            "/mods/react.js",
            -57,
            "var captureModule = function () { return module; };"
        );
        var expression = (JsFunctionExpression)
            ((JsExpressionStatement)parsed.Statements[0]).Expression;
        using var compiler = new JsCompiler(realm);
        var wrapper = compiler.CompileHoistedFunctionTemplate(
            expression,
            string.Empty,
            "WRAPPER",
            "/mods/react.js",
            parsed.IdentifierTable
        );
        var wrapperValue = JsValue.FromObject(wrapper);

        var requireFunction = realm.Evaluate("(s) => ({ version: '19' })");
        var hostImport = new JsHostFunction(
            realm,
            (in info) =>
            {
                var hostRealm = info.Realm;
                var fn = (JsHostFunction)info.Function;
                var wrapperValue = JsValue.FromObject((JsBytecodeFunction)fn.UserData!);
                var exports = new JsPlainObject(hostRealm);
                var moduleObj = new JsPlainObject(hostRealm);
                moduleObj.DefineDataProperty(
                    "exports",
                    JsValue.FromObject(exports),
                    JsShapePropertyFlags.Open
                );
                Span<JsValue> args =
                [
                    JsValue.FromObject(exports),
                    requireFunction,
                    JsValue.FromObject(moduleObj),
                    JsValue.FromString("/mods/react.js"),
                    JsValue.FromString("/mods"),
                ];
                _ = hostRealm.Call(wrapperValue, JsValue.FromObject(exports), args);
                moduleObj.DefineDataProperty("loaded", JsValue.True, JsShapePropertyFlags.Open);
                if (!hostRealm.Atoms.TryGetInterned("exports", out var exportsAtom))
                    throw new InvalidOperationException("exports atom missing");
                moduleObj.TryGetPropertyAtom(hostRealm, exportsAtom, out var currentExports, out _);
                return currentExports;
            },
            "host.import",
            1
        )
        {
            UserData = wrapper,
        };

        realm.Evaluate("process = { env: { NODE_ENV: 'development' } };");
        realm.GlobalObject.DefineDataProperty(
            "__hostImport",
            JsValue.FromObject(hostImport),
            JsShapePropertyFlags.Open
        );
        realm.Evaluate("globalThis[Symbol.for('node.host.import')] = __hostImport;");

        var module = runtime.MainRealm.LoadModule("/mods/shim.js");

        Assert.That(module.TryGetExport("version", out var versionValue), Is.True);
        Assert.That(versionValue.IsUndefined, Is.False, "version export undefined");
        Assert.That(runtime.DefaultRealm.ToJsString(versionValue), Is.EqualTo("19"));
    }
}
