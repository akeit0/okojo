using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class DynamicImportExpressionTests
{
    [Test]
    public void DynamicImport_NestedBracelessIf_Parses_And_Executes()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.ok = false;
                                                                   if (true)
                                                                     import(import(import("./empty_FIXTURE.js")));
                                                                   globalThis.ok = true;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["ok"].IsTrue, Is.True);
    }

    [Test]
    public void DynamicImport_CoercesSpecifier_And_ReturnsPromise()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.hit = 0;
                                                                   var specifier = {
                                                                     toString: function () {
                                                                       globalThis.hit++;
                                                                       return "./mod.js";
                                                                     }
                                                                   };
                                                                   globalThis.promise = import(specifier);
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["hit"].Int32Value, Is.EqualTo(1));
        Assert.That(realm.Global["promise"].TryGetObject(out var promiseObj), Is.True);
        Assert.That(promiseObj, Is.TypeOf<JsPromiseObject>());
    }

    [Test]
    public void DynamicImport_Allows_TrailingComma_After_First_Argument()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.promise = import("./empty_FIXTURE.js",);
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["promise"].TryGetObject(out var promiseObj), Is.True);
        Assert.That(promiseObj, Is.TypeOf<JsPromiseObject>());
    }

    [Test]
    public void DynamicImport_Evaluates_Optional_Second_Argument_Expression()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.hit = 0;
                                                                   globalThis.promise = import("./empty_FIXTURE.js", { with: (globalThis.hit++, "json") });
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["hit"].Int32Value, Is.EqualTo(1));
        Assert.That(realm.Global["promise"].TryGetObject(out var promiseObj), Is.True);
        Assert.That(promiseObj, Is.TypeOf<JsPromiseObject>());
    }

    [Test]
    public void DynamicImport_Rejects_When_With_Getter_Throws()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.hit = 0;
                                                                   globalThis.caught = undefined;
                                                                   var thrown = new Error("boom");
                                                                   var options = {
                                                                     get with() {
                                                                       globalThis.hit++;
                                                                       throw thrown;
                                                                     }
                                                                   };
                                                                   import("./empty_FIXTURE.js", options).then(
                                                                     function() { globalThis.caught = "fulfilled"; },
                                                                     function(error) { globalThis.caught = error; }
                                                                   );
                                                                   0;
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["hit"].Int32Value, Is.EqualTo(1));
        Assert.That(realm.Global["caught"].TryGetObject(out var caughtObj), Is.True);
        Assert.That(caughtObj, Is.Not.Null);
        Assert.That(caughtObj!.TryGetProperty("message", out var message), Is.True);
        Assert.That(message.AsString(), Is.EqualTo("boom"));
    }

    [Test]
    public void DynamicImport_Enumerates_With_Enumerable_String_Keys_And_Rejects_NonString_Values()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.log = [];
                                                                   globalThis.caught = undefined;
                                                                   var options = {
                                                                     with: new Proxy({}, {
                                                                       ownKeys: function() { return ["type"]; },
                                                                       getOwnPropertyDescriptor: function(_, name) {
                                                                         return { configurable: true, enumerable: true, value: 123 };
                                                                       },
                                                                       get: function(_, name) {
                                                                         globalThis.log.push(name);
                                                                         return 123;
                                                                       }
                                                                     })
                                                                   };
                                                                   import("./empty_FIXTURE.js", options).then(
                                                                     function() { globalThis.caught = "fulfilled"; },
                                                                     function(error) { globalThis.caught = error; }
                                                                   );
                                                                   0;
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        var result = realm.Eval("""
                                log.length === 1 &&
                                log[0] === "type" &&
                                caught && caught.constructor === TypeError;
                                """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void DynamicImport_Uses_Legacy_Assert_Bag_For_Validation()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.hit = 0;
                                                                   globalThis.caught = undefined;
                                                                   var options = {
                                                                     assert: new Proxy({}, {
                                                                       ownKeys: function() { return ["type"]; },
                                                                       getOwnPropertyDescriptor: function(_, name) {
                                                                         return { configurable: true, enumerable: true, value: 123 };
                                                                       },
                                                                       get: function(_, name) {
                                                                         globalThis.hit++;
                                                                         return 123;
                                                                       }
                                                                     })
                                                                   };
                                                                   import("./empty_FIXTURE.js", options).then(
                                                                     function() { globalThis.caught = "fulfilled"; },
                                                                     function(error) { globalThis.caught = error; }
                                                                   );
                                                                   0;
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        var result = realm.Eval("""
                                hit === 1 &&
                                caught && caught.constructor === TypeError;
                                """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void DynamicImport_ThenCallback_ModuleNamespace_DefineOwnProperty_Allows_Only_Compatible_Changes()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/define-own-property.js"] = """
                                               export var local1;
                                               var local2;
                                               export { local2 as renamed };
                                               export { local1 as indirect } from './define-own-property.js';
                                               """
        });
        var realm = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build().MainRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.out = "pending";
                                                                   var sym = Symbol('test262');
                                                                   const exported = ['local1', 'renamed', 'indirect'];
                                                                   var assert = {
                                                                     sameValue: function (actual, expected, message) {
                                                                       if (actual === expected) {
                                                                         return;
                                                                       }
                                                                       throw new Error(message || ("Expected SameValue(" + actual + ", " + expected + ")"));
                                                                     },
                                                                     throws: function (ExpectedError, fn, message) {
                                                                       try {
                                                                         fn();
                                                                       } catch (error) {
                                                                         if (error instanceof ExpectedError) {
                                                                           return;
                                                                         }
                                                                         throw error;
                                                                       }
                                                                       throw new Error(message || "Expected throw");
                                                                     }
                                                                   };
                                                                   import("/mods/define-own-property.js").then(function (ns) {
                                                                       for (const key of ['local2', 0, sym, Symbol.iterator]) {
                                                                           assert.sameValue(
                                                                             Reflect.defineProperty(ns, key, {}),
                                                                             false,
                                                                             'Reflect.defineProperty: ' + key.toString()
                                                                           );
                                                                           assert.throws(TypeError, function() {
                                                                             Object.defineProperty(ns, key, {});
                                                                           }, 'Object.defineProperty: ' + key.toString());
                                                                       }

                                                                       for (const key of [...exported, Symbol.toStringTag]) {
                                                                           assert.sameValue(
                                                                             Reflect.defineProperty(ns, key, {}),
                                                                             true,
                                                                             'No change requested, Reflect.defineProperty: ' + key.toString()
                                                                           );
                                                                           assert.sameValue(
                                                                             Object.defineProperty(ns, key, {}),
                                                                             ns,
                                                                             'No change requested, Object.defineProperty: ' + key.toString()
                                                                           );
                                                                       }

                                                                       assert.sameValue(
                                                                         Reflect.defineProperty(ns, 'indirect', { writable: true, enumerable: true, configurable: false }),
                                                                         true,
                                                                         'Reflect.defineProperty: indirect'
                                                                       );
                                                                       assert.sameValue(
                                                                         Object.defineProperty(ns, 'indirect', { writable: true, enumerable: true, configurable: false }),
                                                                         ns,
                                                                         'Object.defineProperty: indirect'
                                                                       );

                                                                       assert.sameValue(
                                                                         Reflect.defineProperty(ns, Symbol.toStringTag, { value: "Module", writable: false, enumerable: false, configurable: false }),
                                                                         true,
                                                                         'Reflect.defineProperty: Symbol.toStringTag'
                                                                       );
                                                                       assert.sameValue(
                                                                         Object.defineProperty(ns, Symbol.toStringTag, { value: "Module", writable: false, enumerable: false, configurable: false }),
                                                                         ns,
                                                                         'Object.defineProperty: Symbol.toStringTag'
                                                                       );

                                                                       for (const key of [...exported, Symbol.toStringTag]) {
                                                                           assert.sameValue(
                                                                             Reflect.defineProperty(ns, key, { value: 123 }),
                                                                             false,
                                                                             'Change requested, Reflect.defineProperty: ' + key.toString()
                                                                           );
                                                                           assert.throws(TypeError, function() {
                                                                             Object.defineProperty(ns, key, { value: 123 });
                                                                           }, 'Change requested, Object.defineProperty: ' + key.toString());
                                                                       }

                                                                       assert.sameValue(
                                                                         Reflect.defineProperty(ns, 'indirect', { writable: true, enumerable: true, configurable: true }),
                                                                         false,
                                                                         'Reflect.defineProperty: indirect'
                                                                       );
                                                                       assert.throws(TypeError, function() {
                                                                         Object.defineProperty(ns, 'indirect', { writable: true, enumerable: true, configurable: true });
                                                                       }, 'Object.defineProperty: indirect');

                                                                       assert.sameValue(
                                                                         Reflect.defineProperty(ns, Symbol.toStringTag, { value: "module", writable: false, enumerable: false, configurable: false }),
                                                                         false,
                                                                         'Reflect.defineProperty: Symbol.toStringTag'
                                                                       );
                                                                       assert.throws(TypeError, function() {
                                                                         Object.defineProperty(ns, Symbol.toStringTag, { value: "module", writable: false, enumerable: false, configurable: false });
                                                                       }, 'Object.defineProperty: Symbol.toStringTag');

                                                                       globalThis.out = "ok";
                                                                   }, function (error) {
                                                                       globalThis.out = error && error.message;
                                                                   });
                                                                   0;
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["out"].AsString(), Is.EqualTo("ok"));
    }

    [Test]
    public void DynamicImport_Await_ModuleNamespace_DefineOwnProperty_Allows_Only_Compatible_Changes()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/define-own-property.js"] = """
                                               export var local1;
                                               var local2;
                                               export { local2 as renamed };
                                               export { local1 as indirect } from './define-own-property.js';
                                               """
        });
        var realm = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build().MainRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.out = "pending";
                                                                   var sym = Symbol('test262');
                                                                   const exported = ['local1', 'renamed', 'indirect'];
                                                                   var assert = {
                                                                     sameValue: function (actual, expected, message) {
                                                                       if (actual === expected) {
                                                                         return;
                                                                       }
                                                                       throw new Error(message || ("Expected SameValue(" + actual + ", " + expected + ")"));
                                                                     },
                                                                     throws: function (ExpectedError, fn, message) {
                                                                       try {
                                                                         fn();
                                                                       } catch (error) {
                                                                         if (error instanceof ExpectedError) {
                                                                           return;
                                                                         }
                                                                         throw error;
                                                                       }
                                                                       throw new Error(message || "Expected throw");
                                                                     }
                                                                   };
                                                                   async function fn() {
                                                                       const ns = await import("/mods/define-own-property.js");

                                                                       for (const key of ['local2', 0, sym, Symbol.iterator]) {
                                                                           assert.sameValue(
                                                                             Reflect.defineProperty(ns, key, {}),
                                                                             false,
                                                                             'Reflect.defineProperty: ' + key.toString()
                                                                           );
                                                                           assert.throws(TypeError, function() {
                                                                             Object.defineProperty(ns, key, {});
                                                                           }, 'Object.defineProperty: ' + key.toString());
                                                                       }

                                                                       for (const key of [...exported, Symbol.toStringTag]) {
                                                                           assert.sameValue(
                                                                             Reflect.defineProperty(ns, key, {}),
                                                                             true,
                                                                             'No change requested, Reflect.defineProperty: ' + key.toString()
                                                                           );
                                                                           assert.sameValue(
                                                                             Object.defineProperty(ns, key, {}),
                                                                             ns,
                                                                             'No change requested, Object.defineProperty: ' + key.toString()
                                                                           );
                                                                       }

                                                                       assert.sameValue(
                                                                         Reflect.defineProperty(ns, 'indirect', { writable: true, enumerable: true, configurable: false }),
                                                                         true,
                                                                         'Reflect.defineProperty: indirect'
                                                                       );
                                                                       assert.sameValue(
                                                                         Object.defineProperty(ns, 'indirect', { writable: true, enumerable: true, configurable: false }),
                                                                         ns,
                                                                         'Object.defineProperty: indirect'
                                                                       );

                                                                       assert.sameValue(
                                                                         Reflect.defineProperty(ns, Symbol.toStringTag, { value: "Module", writable: false, enumerable: false, configurable: false }),
                                                                         true,
                                                                         'Reflect.defineProperty: Symbol.toStringTag'
                                                                       );
                                                                       assert.sameValue(
                                                                         Object.defineProperty(ns, Symbol.toStringTag, { value: "Module", writable: false, enumerable: false, configurable: false }),
                                                                         ns,
                                                                         'Object.defineProperty: Symbol.toStringTag'
                                                                       );

                                                                       for (const key of [...exported, Symbol.toStringTag]) {
                                                                           assert.sameValue(
                                                                             Reflect.defineProperty(ns, key, { value: 123 }),
                                                                             false,
                                                                             'Change requested, Reflect.defineProperty: ' + key.toString()
                                                                           );
                                                                           assert.throws(TypeError, function() {
                                                                             Object.defineProperty(ns, key, { value: 123 });
                                                                           }, 'Change requested, Object.defineProperty: ' + key.toString());
                                                                       }

                                                                       assert.sameValue(
                                                                         Reflect.defineProperty(ns, 'indirect', { writable: true, enumerable: true, configurable: true }),
                                                                         false,
                                                                         'Reflect.defineProperty: indirect'
                                                                       );
                                                                       assert.throws(TypeError, function() {
                                                                         Object.defineProperty(ns, 'indirect', { writable: true, enumerable: true, configurable: true });
                                                                       }, 'Object.defineProperty: indirect');

                                                                       assert.sameValue(
                                                                         Reflect.defineProperty(ns, Symbol.toStringTag, { value: "module", writable: false, enumerable: false, configurable: false }),
                                                                         false,
                                                                         'Reflect.defineProperty: Symbol.toStringTag'
                                                                       );
                                                                       assert.throws(TypeError, function() {
                                                                         Object.defineProperty(ns, Symbol.toStringTag, { value: "module", writable: false, enumerable: false, configurable: false });
                                                                       }, 'Object.defineProperty: Symbol.toStringTag');

                                                                       globalThis.out = "ok";
                                                                   }
                                                                   fn().catch(function (error) {
                                                                       globalThis.out = error && error.message;
                                                                   });
                                                                   0;
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["out"].AsString(), Is.EqualTo("ok"));
    }

    [Test]
    public void DynamicImport_DefaultAnonymousClass_StaticNameMethod_IsNotOverwritten()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/dynamic-name-method.js"] = """export default class { static name() { return 'name method'; } }"""
        });
        var realm = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build().MainRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.out = "pending";
                                                                   import("/mods/dynamic-name-method.js").then(function (ns) {
                                                                       try {
                                                                           globalThis.out = ns.default.name();
                                                                       } catch (error) {
                                                                           globalThis.out = error && error.message;
                                                                       }
                                                                   }, function (error) {
                                                                       globalThis.out = error && error.message;
                                                                   });
                                                                   0;
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["out"].AsString(), Is.EqualTo("name method"));
    }

    [Test]
    public void DynamicImport_WaitingAsyncModule_SecondImport_Resolves_After_First()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/waiting.js"] = """
                                   globalThis.executionStarted();
                                   export let x = 1;
                                   await globalThis.promise;
                                   """,
            ["/mods/empty.js"] = """
                                 export {};
                                 """
        });
        var realm = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build().MainRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.done = "pending";
                                                                   globalThis.log = [];
                                                                   let continueExecution;
                                                                   globalThis.promise = new Promise((resolve) => continueExecution = resolve);
                                                                   const executionStartPromise = new Promise((resolve) => globalThis.executionStarted = resolve);
                                                                   (async function () {
                                                                     const first = import("/mods/waiting.js").then(function (ns) {
                                                                       globalThis.log.push("first:" + ns.x);
                                                                     });
                                                                     await executionStartPromise;
                                                                     const second = import("/mods/waiting.js").then(function (ns) {
                                                                       globalThis.log.push("second:" + ns.x);
                                                                     });
                                                                     await import("/mods/empty.js");
                                                                     continueExecution();
                                                                     await Promise.all([first, second]);
                                                                     globalThis.done = globalThis.log.join(",");
                                                                   })().catch(function (error) {
                                                                     globalThis.done = error && error.message;
                                                                   });
                                                                   0;
                                                                   """));

        realm.Execute(script);
        for (var i = 0; i < 20 && realm.Global["done"].AsString() == "pending"; i++)
            realm.Agent.PumpJobs();

        Assert.That(realm.Global["done"].AsString(), Is.EqualTo("first:1,second:1"));
    }

    [Test]
    public void DynamicImport_ModuleEvaluationThrow_Rejects_With_TypeError_Object()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/boom.js"] = """throw new TypeError("boom");"""
        });
        var realm = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build().MainRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.done = "pending";
                                                                   import("/mods/boom.js").catch(function (error) {
                                                                     globalThis.done = error && error.name + ":" + error.message;
                                                                   }).then(function () {
                                                                     if (globalThis.done === "pending") {
                                                                       globalThis.done = "fulfilled";
                                                                     }
                                                                   }, function (error) {
                                                                     globalThis.done = "then-rejected:" + (error && error.name);
                                                                   });
                                                                   0;
                                                                   """));

        realm.Execute(script);
        for (var i = 0; i < 20 && realm.Global["done"].AsString() == "pending"; i++)
            realm.Agent.PumpJobs();

        Assert.That(realm.Global["done"].AsString(), Is.EqualTo("TypeError:boom"));
    }

    [Test]
    public void DynamicImport_TypeJson_On_JavaScript_Module_Rejects()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/not-json.js"] = """export default 1;"""
        });
        var realm = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build().MainRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.done = "pending";
                                                                   import("/mods/not-json.js", { with: { type: "json" } }).then(
                                                                     function () { globalThis.done = "fulfilled"; },
                                                                     function (error) { globalThis.done = error && error.name + ":" + error.message; }
                                                                   );
                                                                   0;
                                                                   """));

        realm.Execute(script);
        for (var i = 0; i < 20 && realm.Global["done"].AsString() == "pending"; i++)
            realm.Agent.PumpJobs();

        Assert.That(realm.Global["done"].AsString(), Does.StartWith("TypeError:"));
    }

    [Test]
    public void DynamicImport_Unsupported_Type_Rejects()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/value.json"] = """{"ok":true}"""
        });
        var realm = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build().MainRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.done = "pending";
                                                                   import("/mods/value.json", { with: { type: "css" } }).then(
                                                                     function () { globalThis.done = "fulfilled"; },
                                                                     function (error) { globalThis.done = error && error.name + ":" + error.message; }
                                                                   );
                                                                   0;
                                                                   """));

        realm.Execute(script);
        for (var i = 0; i < 20 && realm.Global["done"].TryGetString(out var doneStr) && doneStr == "pending"; i++)
            realm.Agent.PumpJobs();

        Assert.That(realm.Global["done"].TryGetString(out var finalDoneStr) && finalDoneStr.StartsWith("TypeError:"),
            Is.True);
    }

    [Test]
    public void DynamicImport_NestedFunction_Uses_Enclosing_Script_SourcePath_As_Referrer()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/tests/module.js"] = """export default 1;"""
        });
        var realm = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build().MainRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.done = "pending";
                                                                   function f() {
                                                                     import("./module.js").then(function (ns) {
                                                                       globalThis.done = ns.default;
                                                                     }, function (error) {
                                                                       globalThis.done = "error:" + (error && error.name) + ":" + (error && error.message);
                                                                     });
                                                                   }
                                                                   f();
                                                                   0;
                                                                   """, "/tests/entry.js"));

        realm.Execute(script);
        for (var i = 0; i < 20 && realm.Global["done"].TryGetString(out var doneStr) && doneStr == "pending"; i++)
            realm.Agent.PumpJobs();

        Assert.That(loader.LastReferrer, Is.EqualTo("/tests/entry.js"));
        Assert.That(loader.LastResolvedId, Is.EqualTo("/tests/module.js"));
        Assert.That(loader.LoadCount, Is.EqualTo(1));
        Assert.That(realm.Global["done"].Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void DynamicImport_NestedAsyncFunction_Uses_Enclosing_Script_SourcePath_As_Referrer()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/tests/module.js"] = """export const value = 1;"""
        });
        var realm = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build().MainRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.done = "pending";
                                                                   async function f() {
                                                                     const ns = await import("./module.js");
                                                                     globalThis.done = ns.value;
                                                                   }
                                                                   f().catch(function (error) {
                                                                     globalThis.done = "error:" + (error && error.name) + ":" + (error && error.message);
                                                                   });
                                                                   0;
                                                                   """, "/tests/entry.js"));

        realm.Execute(script);
        for (var i = 0; i < 20 && realm.Global["done"].TryGetString(out var doneStr) && doneStr == "pending"; i++)
            realm.Agent.PumpJobs();

        Assert.That(loader.LastReferrer, Is.EqualTo("/tests/entry.js"));
        Assert.That(loader.LastResolvedId, Is.EqualTo("/tests/module.js"));
        Assert.That(loader.LoadCount, Is.EqualTo(1));
        Assert.That(realm.Global["done"].Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void DynamicImport_AsyncGeneratorReturnAwait_ModuleEvaluationThrow_Rejects_IteratorPromise()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/boom.js"] = """throw new URIError("bad");"""
        });
        var realm = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build().MainRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.done = "pending";
                                                                   async function * f() {
                                                                     return await import("/mods/boom.js");
                                                                   }
                                                                   f().next().catch(function (error) {
                                                                     globalThis.done = error && error.name + ":" + error.message;
                                                                   }).then(function () {
                                                                     if (globalThis.done === "pending") {
                                                                       globalThis.done = "fulfilled";
                                                                     }
                                                                   }, function (error) {
                                                                     globalThis.done = "then-rejected:" + (error && error.name);
                                                                   });
                                                                   0;
                                                                   """));

        realm.Execute(script);
        for (var i = 0; i < 20 && realm.Global["done"].AsString() == "pending"; i++)
            realm.Agent.PumpJobs();

        Assert.That(realm.Global["done"].AsString(), Is.EqualTo("URIError:bad"));
    }

    [Test]
    public void DynamicImport_AsyncDependencyGraphs_Fulfill_In_LeafToRoot_Order()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/setup.js"] = """
                                 export const p1 = Promise.withResolvers();
                                 export const pAStart = Promise.withResolvers();
                                 export const pBStart = Promise.withResolvers();
                                 """,
            ["/mods/a-sentinel.js"] = """
                                      import { pAStart } from "./setup.js";
                                      pAStart.resolve();
                                      """,
            ["/mods/b-sentinel.js"] = """
                                      import { pBStart } from "./setup.js";
                                      pBStart.resolve();
                                      """,
            ["/mods/b.js"] = """
                             import "./b-sentinel.js";
                             import { p1 } from "./setup.js";
                             await p1.promise;
                             """,
            ["/mods/a.js"] = """
                             import "./a-sentinel.js";
                             import "./b.js";
                             """
        });
        var realm = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build().MainRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.done = "pending";
                                                                   globalThis.logs = [];
                                                                   (async function () {
                                                                     const setup = await import("/mods/setup.js");
                                                                     const importsP = Promise.all([
                                                                       setup.pBStart.promise.then(() => import("/mods/a.js").finally(() => globalThis.logs.push("A"))).catch(() => {}),
                                                                       import("/mods/b.js").finally(() => globalThis.logs.push("B")).catch(() => {})
                                                                     ]);
                                                                     Promise.all([setup.pAStart.promise, setup.pBStart.promise]).then(setup.p1.resolve);
                                                                     await importsP;
                                                                     globalThis.done = globalThis.logs.join(",");
                                                                   })().catch(function (error) {
                                                                     globalThis.done = error && error.message;
                                                                   });
                                                                   0;
                                                                   """));

        realm.Execute(script);
        for (var i = 0; i < 40 && realm.Global["done"].AsString() == "pending"; i++)
            realm.Agent.PumpJobs();

        Assert.That(realm.Global["done"].AsString(), Is.EqualTo("B,A"));
    }

    [Test]
    public void DynamicImport_AsyncDependencyGraphs_Reject_In_LeafToRoot_Order()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/setup.js"] = """
                                 export const p1 = Promise.withResolvers();
                                 export const pAStart = Promise.withResolvers();
                                 export const pBStart = Promise.withResolvers();
                                 """,
            ["/mods/a-sentinel.js"] = """
                                      import { pAStart } from "./setup.js";
                                      pAStart.resolve();
                                      """,
            ["/mods/b-sentinel.js"] = """
                                      import { pBStart } from "./setup.js";
                                      pBStart.resolve();
                                      """,
            ["/mods/b.js"] = """
                             import "./b-sentinel.js";
                             import { p1 } from "./setup.js";
                             await p1.promise;
                             """,
            ["/mods/a.js"] = """
                             import "./a-sentinel.js";
                             import "./b.js";
                             """
        });
        var realm = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build().MainRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.done = "pending";
                                                                   globalThis.logs = [];
                                                                   (async function () {
                                                                     const setup = await import("/mods/setup.js");
                                                                     const importsP = Promise.all([
                                                                       setup.pBStart.promise.then(() => import("/mods/a.js").finally(() => globalThis.logs.push("A"))).catch(() => {}),
                                                                       import("/mods/b.js").finally(() => globalThis.logs.push("B")).catch(() => {})
                                                                     ]);
                                                                     Promise.all([setup.pAStart.promise, setup.pBStart.promise]).then(setup.p1.reject);
                                                                     await importsP;
                                                                     globalThis.done = globalThis.logs.join(",");
                                                                   })().catch(function (error) {
                                                                     globalThis.done = error && error.message;
                                                                   });
                                                                   0;
                                                                   """));

        realm.Execute(script);
        for (var i = 0; i < 40 && realm.Global["done"].AsString() == "pending"; i++)
            realm.Agent.PumpJobs();

        Assert.That(realm.Global["done"].AsString(), Is.EqualTo("B,A"));
    }

    [Test]
    public void DynamicImport_AsyncEvaluationOrder_Preserves_Earlier_Ancestor_Before_Later_Graph()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/setup.js"] = """
                                 export const logs = [];
                                 export const pB = Promise.withResolvers();
                                 export const pBStart = Promise.withResolvers();
                                 export const pEStart = Promise.withResolvers();
                                 """,
            ["/mods/b.js"] = """
                             import { pB, pBStart } from "./setup.js";
                             pBStart.resolve();
                             await pB.promise;
                             """,
            ["/mods/a.js"] = """
                             import { logs } from "./setup.js";
                             import "./b.js";
                             logs.push("A");
                             """,
            ["/mods/c.js"] = """
                             await 1;
                             """,
            ["/mods/e.js"] = """
                             import { pEStart } from "./setup.js";
                             pEStart.resolve();
                             """,
            ["/mods/d.js"] = """
                             import { logs } from "./setup.js";
                             import "./e.js";
                             import "./b.js";
                             logs.push("D");
                             """
        });
        var realm = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build().MainRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.done = "pending";
                                                                   (async function () {
                                                                     const setup = await import("/mods/setup.js");
                                                                     const pA = import("/mods/a.js");
                                                                     let pD;
                                                                     setup.pBStart.promise
                                                                       .then(() => import("/mods/c.js"))
                                                                       .then(() => {
                                                                         pD = import("/mods/d.js");
                                                                         return setup.pEStart.promise;
                                                                       })
                                                                       .then(() => {
                                                                         setup.pB.resolve();
                                                                         return Promise.all([pA, pD]);
                                                                       })
                                                                       .then(() => {
                                                                         globalThis.done = setup.logs.join(",");
                                                                       }, function (error) {
                                                                         globalThis.done = error && error.message;
                                                                       });
                                                                   })().catch(function (error) {
                                                                     globalThis.done = error && error.message;
                                                                   });
                                                                   0;
                                                                   """));

        realm.Execute(script);
        for (var i = 0; i < 60 && realm.Global["done"].AsString() == "pending"; i++)
            realm.Agent.PumpJobs();

        Assert.That(realm.Global["done"].AsString(), Is.EqualTo("A,D"));
    }

    private sealed class InMemoryModuleLoader(Dictionary<string, string> modules) : IModuleSourceLoader
    {
        public string? LastSpecifier { get; private set; }
        public string? LastReferrer { get; private set; }
        public string? LastResolvedId { get; private set; }
        public int LoadCount { get; private set; }

        public string ResolveSpecifier(string specifier, string? referrer)
        {
            LastSpecifier = specifier;
            LastReferrer = referrer;

            string resolvedId;
            if (specifier.StartsWith("/", StringComparison.Ordinal))
            {
                resolvedId = specifier;
            }
            else if (referrer is null)
            {
                resolvedId = "/" + specifier.TrimStart('/');
            }
            else
            {
                var slash = referrer.LastIndexOf('/');
                var baseDir = slash >= 0 ? referrer[..(slash + 1)] : "/";
                if (specifier.StartsWith("./", StringComparison.Ordinal))
                    resolvedId = baseDir + specifier[2..];
                else
                    resolvedId = baseDir + specifier;
            }

            LastResolvedId = resolvedId;
            return resolvedId;
        }

        public string LoadSource(string resolvedId)
        {
            LoadCount++;
            if (!modules.TryGetValue(resolvedId, out var source))
                throw new InvalidOperationException("Module not found: " + resolvedId);

            return source;
        }
    }
}
