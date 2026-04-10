using Okojo.Runtime;

namespace Okojo.Tests;

public class ObjectConstructorExtensibilityTests
{
    [Test]
    public void Object_PreventExtensions_Blocks_NewProperties_But_Allows_Writes_To_Existing()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var o = { a: 1 };
                                Object.preventExtensions(o);
                                o.b = 2;
                                o.a = 3;
                                Object.isExtensible(o) === false && o.b === undefined && o.a === 3;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ModuleNamespace_IsNotExtensible()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = "export const x = 1;"
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        realm.Global["ns"] = ns;

        var result = realm.Eval("Object.isExtensible(ns) === false;");
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ModuleNamespace_ToStringTag_Descriptor_Matches_Spec()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = "export const x = 1;"
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        realm.Global["ns"] = ns;

        var result = realm.Eval("""
                                var desc = Object.getOwnPropertyDescriptor(ns, Symbol.toStringTag);
                                desc.value === "Module" &&
                                desc.writable === false &&
                                desc.enumerable === false &&
                                desc.configurable === false;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ModuleNamespace_ReflectSet_ReturnsFalse_And_StrictAssignmentThrows()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = "export const x = 1;"
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        realm.Global["ns"] = ns;

        var reflectResult = realm.Eval("""
                                       Reflect.set(ns, "x", 2) === false &&
                                       Reflect.set(ns, Symbol.toStringTag, ns[Symbol.toStringTag]) === false &&
                                       Reflect.set(ns, "x") === false;
                                       """);
        Assert.That(reflectResult.IsTrue, Is.True);

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Eval("""
                                                                    "use strict";
                                                                    ns.x = 2;
                                                                    """));
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.TypeError));
    }

    [Test]
    public void ModuleNamespace_DefineProperty_Uses_ModuleNamespace_Compatibility_Rules()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = "export const x = 1;"
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        realm.Global["ns"] = ns;

        var result = realm.Eval("""
                                Reflect.defineProperty(ns, "x", { writable: true, enumerable: true, configurable: false }) === true &&
                                Reflect.defineProperty(ns, "x", { value: 1 }) === true &&
                                Reflect.defineProperty(ns, "x", { value: 2 }) === false &&
                                Reflect.defineProperty(ns, "x", { writable: false }) === false &&
                                Reflect.defineProperty(ns, Symbol.toStringTag, {
                                  value: "Module",
                                  writable: false,
                                  enumerable: false,
                                  configurable: false
                                }) === true &&
                                Reflect.defineProperty(ns, Symbol.toStringTag, { value: "module" }) === false;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ModuleNamespace_ObjectDefineProperty_On_ToStringTag_Change_Throws_TypeError()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = "export const x = 1;"
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        realm.Global["ns"] = ns;

        var result = realm.Eval("""
                                try {
                                  Object.defineProperty(ns, Symbol.toStringTag, {
                                    value: "module",
                                    writable: false,
                                    enumerable: false,
                                    configurable: false
                                  });
                                  false;
                                } catch (e) {
                                  e && e.name === "TypeError";
                                }
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ModuleNamespace_ObjectDefineProperty_On_ToStringTag_Change_Throws_RealmTypeError()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = "export const x = 1;"
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        realm.Global["ns"] = ns;

        var result = realm.Eval("""
                                try {
                                  Object.defineProperty(ns, Symbol.toStringTag, {
                                    value: "module",
                                    writable: false,
                                    enumerable: false,
                                    configurable: false
                                  });
                                  false;
                                } catch (e) {
                                  e && e.constructor === TypeError;
                                }
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ModuleNamespace_Delete_And_OwnKeys_Follow_Exotic_Rules()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """
                                export const z = 1;
                                export const a = 2;
                                export default 3;
                                export const $ = 4;
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        realm.Global["ns"] = ns;

        var result = realm.Eval("""
                                var names = Object.getOwnPropertyNames(ns);
                                delete ns.missing === true &&
                                Reflect.deleteProperty(ns, "missing") === true &&
                                delete ns[Symbol.toStringTag] === false &&
                                Reflect.deleteProperty(ns, Symbol.toStringTag) === false &&
                                names.length === 4 &&
                                names[0] === "$" &&
                                names[1] === "a" &&
                                names[2] === "default" &&
                                names[3] === "z" &&
                                Reflect.ownKeys(ns).indexOf(Symbol.toStringTag) > 3;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ModuleNamespace_Assignment_And_Delete_Throw_From_Nested_Module_Functions()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/dep.js"] = """
                               export let local1 = 23;
                               export default null;
                               """,
            ["/mods/main.js"] = """
                                import * as ns from "./dep.js";

                                export const assignThrows = (() => {
                                  try {
                                    (function() { ns.local1 = null; })();
                                    return false;
                                  } catch (e) {
                                    return e && e.constructor === TypeError;
                                  }
                                })();

                                export const deleteExportThrows = (() => {
                                  try {
                                    (function() { delete ns.local1; })();
                                    return false;
                                  } catch (e) {
                                    return e && e.constructor === TypeError;
                                  }
                                })();

                                export const deleteToStringTagThrows = (() => {
                                  try {
                                    (function() { delete ns[Symbol.toStringTag]; })();
                                    return false;
                                  } catch (e) {
                                    return e && e.constructor === TypeError;
                                  }
                                })();
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        realm.Global["ns"] = ns;

        var result = realm.Eval("""
                                ns.assignThrows === true &&
                                ns.deleteExportThrows === true &&
                                ns.deleteToStringTagThrows === true;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ModuleNamespace_DefineProperty_Full_Sequence_Matches_Test262_Expectations()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """
                                export var local1;
                                var local2;
                                export { local2 as renamed };
                                export { local1 as indirect } from "./main.js";
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        realm.Global["ns"] = ns;

        var result = realm.Eval("""
                                var sym = Symbol('test262');
                                const exported = ['local1', 'renamed', 'indirect'];

                                var ok = true;

                                for (const key of ['local2', 0, sym, Symbol.iterator]) {
                                  ok = ok && Reflect.defineProperty(ns, key, {}) === false;
                                  try {
                                    Object.defineProperty(ns, key, {});
                                    ok = false;
                                  } catch (e) {
                                    ok = ok && e.constructor === TypeError;
                                  }
                                }

                                for (const key of ([...exported, Symbol.toStringTag])) {
                                  ok = ok && Reflect.defineProperty(ns, key, {}) === true;
                                  ok = ok && Object.defineProperty(ns, key, {}) === ns;
                                }

                                ok = ok && Reflect.defineProperty(ns, 'indirect',
                                    {writable: true, enumerable: true, configurable: false}) === true;
                                ok = ok && Object.defineProperty(ns, 'indirect',
                                    {writable: true, enumerable: true, configurable: false}) === ns;

                                ok = ok && Reflect.defineProperty(ns, Symbol.toStringTag,
                                    {value: "Module", writable: false, enumerable: false,
                                     configurable: false}) === true;
                                ok = ok && Object.defineProperty(ns, Symbol.toStringTag,
                                    {value: "Module", writable: false, enumerable: false,
                                     configurable: false}) === ns;

                                for (const key of ([...exported, Symbol.toStringTag])) {
                                  ok = ok && Reflect.defineProperty(ns, key, {value: 123}) === false;
                                  try {
                                    Object.defineProperty(ns, key, {value: 123});
                                    ok = false;
                                  } catch (e) {
                                    ok = ok && e.constructor === TypeError;
                                  }
                                }

                                ok = ok && Reflect.defineProperty(ns, 'indirect',
                                    {writable: true, enumerable: true, configurable: true}) === false;
                                try {
                                  Object.defineProperty(ns, 'indirect',
                                      {writable: true, enumerable: true, configurable: true});
                                  ok = false;
                                } catch (e) {
                                  ok = ok && e.constructor === TypeError;
                                }

                                ok = ok && Reflect.defineProperty(ns, Symbol.toStringTag,
                                    {value: "module", writable: false, enumerable: false,
                                     configurable: false}) === false;
                                try {
                                  Object.defineProperty(ns, Symbol.toStringTag,
                                      {value: "module", writable: false, enumerable: false,
                                       configurable: false});
                                  ok = false;
                                } catch (e) {
                                  ok = ok && e.constructor === TypeError;
                                }

                                ok;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Object_Seal_MarksObjectNonExtensibleAndNonConfigurable()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var o = { a: 1 };
                                var before = Object.isSealed(o) === false;
                                Object.seal(o);
                                delete o.a;
                                o.b = 2;
                                before &&
                                Object.isSealed(o) === true &&
                                Object.isExtensible(o) === false &&
                                o.a === 1 &&
                                o.b === undefined;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Object_Seal_FunctionObject_IsSealed()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var fun = function() {};
                                var preCheck = Object.isExtensible(fun);
                                Object.seal(fun);
                                preCheck &&
                                Object.isSealed(fun) === true;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Object_Freeze_MakesDataPropertyNonWritable()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var o = { a: 1 };
                                Object.freeze(o);
                                o.a = 3;
                                Object.isFrozen(o) === true && o.a === 1;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Object_Freeze_KeepsAccessorOperational()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var state = 0;
                                var o = {
                                  get x() { return state; },
                                  set x(v) { state = v; }
                                };
                                Object.freeze(o);
                                o.x = 7;
                                Object.isFrozen(o) === true && o.x === 7;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Object_Freeze_FreezesIndexedProperties()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var o = { 0: 1 };
                                Object.freeze(o);
                                o[0] = 9;
                                delete o[0];
                                Object.isFrozen(o) === true && o[0] === 1;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    private sealed class InMemoryModuleLoader(Dictionary<string, string> modules) : IModuleSourceLoader
    {
        private readonly Dictionary<string, string> modules = modules;

        public string ResolveSpecifier(string specifier, string? referrer)
        {
            if (specifier.StartsWith("/", StringComparison.Ordinal))
                return specifier;

            if (referrer is null)
                return "/" + specifier.TrimStart('/');

            var slash = referrer.LastIndexOf('/');
            var baseDir = slash >= 0 ? referrer[..(slash + 1)] : "/";
            if (specifier.StartsWith("./", StringComparison.Ordinal))
                return baseDir + specifier[2..];

            return baseDir + specifier;
        }

        public string LoadSource(string resolvedId)
        {
            if (!modules.TryGetValue(resolvedId, out var source))
                throw new InvalidOperationException("Module not found: " + resolvedId);
            return source;
        }
    }
}
