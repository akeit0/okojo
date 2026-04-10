using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ModuleEdgeCaseTests
{
    [Test]
    public void EvaluateModule_ExportDefaultAnonymousFunction_HasNameDefault()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/default-fn.js"] = """export default function() { return 1; }"""
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/default-fn.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("default", out var defaultValue), Is.True);
        Assert.That(defaultValue.TryGetObject(out var defaultObj), Is.True);
        Assert.That(defaultObj, Is.InstanceOf<JsFunction>());
        Assert.That(defaultObj!.TryGetProperty("name", out var nameValue), Is.True);
        Assert.That(nameValue.IsString, Is.True);
        Assert.That(nameValue.AsString(), Is.EqualTo("default"));
    }

    [Test]
    public void EvaluateModule_ExportDefaultAnonymousClass_HasNameDefault()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/default-class.js"] = """export default class { static value() { return 7; } }"""
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/default-class.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("default", out var defaultValue), Is.True);
        Assert.That(defaultValue.TryGetObject(out var defaultObj), Is.True);
        Assert.That(defaultObj, Is.InstanceOf<JsFunction>());
        Assert.That(defaultObj!.TryGetProperty("name", out var nameValue), Is.True);
        Assert.That(nameValue.IsString, Is.True);
        Assert.That(nameValue.AsString(), Is.EqualTo("default"));
    }

    [Test]
    public void EvaluateModule_ExportDefaultAnonymousClass_StaticFieldInitializer_Sees_Default_Name()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/default-class-static-name.js"] = """
                                                     var className;
                                                     export default class {
                                                       static f = (className = this.name);
                                                     }
                                                     export const observed = className;
                                                     """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/default-class-static-name.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("observed", out var observedValue), Is.True);
        Assert.That(observedValue.IsString, Is.True);
        Assert.That(observedValue.AsString(), Is.EqualTo("default"));
    }

    [Test]
    public void EvaluateModule_ExportDefaultAnonymousClass_DoesNotRequireSemicolonBeforeFollowingStatement()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/default-class-no-semi.js"] = """
                                                 export default class {} if (true) { globalThis.__m = 1; }
                                                 export const done = globalThis.__m;
                                                 """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/default-class-no-semi.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("done", out var doneValue), Is.True);
        Assert.That(doneValue.Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void EvaluateModule_ExportDefaultAnonymousClass_StaticNameMethod_IsNotOverwritten()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/dep-name-method.js"] = """export default class { static name() { return 'name method'; } }""",
            ["/mods/main-name-method.js"] = """
                                            import C from "./dep-name-method.js";
                                            export const result = C.name();
                                            """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main-name-method.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("result", out var result), Is.True);
        Assert.That(result.IsString, Is.True);
        Assert.That(result.AsString(), Is.EqualTo("name method"));
    }

    [Test]
    public void EvaluateModule_ObjectDefineProperties_Uses_MethodNamedGet_From_Spread_Descriptor_Object()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """
                                const styles = {};
                                styles.green = {
                                  get() { return 1; },
                                  enumerable: true,
                                  configurable: true
                                };

                                const spread = { ...styles };
                                const sourceDesc = Object.getOwnPropertyDescriptor(spread.green, "get");
                                const target = Object.defineProperties({}, spread);
                                const installed = Object.getOwnPropertyDescriptor(target, "green");

                                export default [
                                  typeof spread.green,
                                  typeof spread.green.get,
                                  sourceDesc && typeof sourceDesc.value,
                                  typeof installed.get,
                                  installed.get.name,
                                  target.green
                                ].join("|");
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("default", out var defaultValue), Is.True);
        Assert.That(defaultValue.IsString, Is.True);
        Assert.That(defaultValue.AsString(), Is.EqualTo("object|function|function|function|get|1"));
    }

    public void EvaluateModule_ExportNamedDefaultFrom_Works()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/dep.js"] = """export default 42;""",
            ["/mods/main.js"] = """export { default } from "./dep.js";"""
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("default", out var defaultValue), Is.True);
        Assert.That(defaultValue.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void EvaluateModule_JsonNamespaceImport_ExposesDefaultExport()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/value.json"] = "262",
            ["/mods/main.js"] = """
                                import * as ns from "./value.json" with { type: "json" };
                                export default [Object.getOwnPropertyNames(ns).length, ns.default];
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("default", out var defaultValue), Is.True);
        Assert.That(defaultValue.TryGetObject(out var resultObj), Is.True);
        Assert.That(resultObj, Is.InstanceOf<JsArray>());
        Assert.That(resultObj!.TryGetElement(0, out var keyCount), Is.True);
        Assert.That(resultObj.TryGetElement(1, out var jsonValue), Is.True);
        Assert.That(keyCount.Int32Value, Is.EqualTo(1));
        Assert.That(jsonValue.Int32Value, Is.EqualTo(262));
    }

    [Test]
    public void EvaluateModule_JsonImportOnly_ModuleWithoutExports_DoesNotRequireSharedTopLevelContext()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/value.json"] = """{"nested":{}}""",
            ["/mods/main.js"] = """
                                import value from "./value.json" with { type: "json" };
                                value.test262property = "ok";
                                globalThis.__jsonImportOnlyResult = Object.getOwnPropertyDescriptor(value, "test262property").value;
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;

        _ = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");

        Assert.That(realm.Global["__jsonImportOnlyResult"].IsString, Is.True);
        Assert.That(realm.Global["__jsonImportOnlyResult"].AsString(), Is.EqualTo("ok"));
    }

    [Test]
    public void EvaluateModule_HoistedNamedExportFunctions_Share_Mutable_Module_Local_Context()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/dep-hoisted.js"] = """
                                       export { read, bump, observed };
                                       let value = 1;
                                       function read() { return value; }
                                       function bump() { value += 1; return value; }
                                       value = 41;
                                       const observed = read();
                                       """,
            ["/mods/main-hoisted.js"] = """
                                        import { read, bump, observed } from "./dep-hoisted.js";
                                        export default [observed, read(), bump(), read()];
                                        """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main-hoisted.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("default", out var defaultValue), Is.True);
        Assert.That(defaultValue.TryGetObject(out var resultObj), Is.True);
        Assert.That(resultObj, Is.InstanceOf<JsArray>());
        Assert.That(resultObj!.TryGetElement(0, out var observed), Is.True);
        Assert.That(resultObj.TryGetElement(1, out var firstRead), Is.True);
        Assert.That(resultObj.TryGetElement(2, out var bumpResult), Is.True);
        Assert.That(resultObj.TryGetElement(3, out var secondRead), Is.True);
        Assert.That(observed.Int32Value, Is.EqualTo(41));
        Assert.That(firstRead.Int32Value, Is.EqualTo(41));
        Assert.That(bumpResult.Int32Value, Is.EqualTo(42));
        Assert.That(secondRead.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void EvaluateModule_HoistedDefaultExportFunction_Captures_LaterInitialized_Module_Local()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/dep-default-hoisted.js"] = """
                                               let value = "before";
                                               export default function () { return value; }
                                               value = "after";
                                               """,
            ["/mods/main-default-hoisted.js"] = """
                                                import read from "./dep-default-hoisted.js";
                                                export const result = read();
                                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main-default-hoisted.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("result", out var resultValue), Is.True);
        Assert.That(resultValue.IsString, Is.True);
        Assert.That(resultValue.AsString(), Is.EqualTo("after"));
    }

    [Test]
    public void EvaluateModule_Static_And_Dynamic_Json_Imports_Share_The_Same_Default_Value()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/value.json"] = """{"answer":262}""",
            ["/mods/indirect.js"] = """
                                    import value from "./value.json" with { type: "json" };
                                    globalThis.viaSecondModule = value;
                                    """,
            ["/mods/main.js"] = """
                                import viaStaticImport1 from "./value.json" with { type: "json" };
                                import { default as viaStaticImport2 } from "./value.json" with { type: "json" };
                                import "./indirect.js";

                                globalThis.done = "pending";

                                import("./value.json", { with: { type: "json" } }).then(function (viaDynamicImport) {
                                  globalThis.done =
                                    viaStaticImport1 === viaStaticImport2 &&
                                    globalThis.viaSecondModule === viaStaticImport1 &&
                                    viaDynamicImport.default === viaStaticImport1;
                                }, function (error) {
                                  globalThis.done = error && error.message;
                                });
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        _ = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");

        for (var i = 0; i < 20 && realm.Global["done"].IsString && realm.Global["done"].AsString() == "pending"; i++)
            realm.PumpJobs();

        Assert.That(realm.Global["done"].IsTrue, Is.True);
    }

    [Test]
    public void EvaluateModule_JsonImports_Preserve_Array_Length_OwnProperty()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/value.json"] = """{"nested":[],"root":[1,{}]}""",
            ["/mods/main.js"] = """
                                import value from "./value.json" with { type: "json" };
                                export default [
                                  Object.getOwnPropertyNames(value.nested).join(","),
                                  Object.getOwnPropertyNames(value.root).join(","),
                                  value.root.length
                                ];
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("default", out var defaultValue), Is.True);
        Assert.That(defaultValue.TryGetObject(out var resultObj), Is.True);
        Assert.That(resultObj, Is.InstanceOf<JsArray>());
        Assert.That(resultObj!.TryGetElement(0, out var nestedNames), Is.True);
        Assert.That(resultObj.TryGetElement(1, out var rootNames), Is.True);
        Assert.That(resultObj.TryGetElement(2, out var rootLength), Is.True);
        Assert.That(nestedNames.AsString(), Is.EqualTo("length"));
        Assert.That(rootNames.AsString(), Is.EqualTo("0,1,length"));
        Assert.That(rootLength.Int32Value, Is.EqualTo(2));
    }

    [Test]
    public void EvaluateModule_ImportNamespace_DefaultAndNamedBindings_Work()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/dep.js"] = """
                               export default 5;
                               export const a = 7;
                               """,
            ["/mods/main.js"] = """
                                import d, * as ns from "./dep.js";
                                export const sum = d + ns.a;
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("sum", out var sumValue), Is.True);
        Assert.That(sumValue.Int32Value, Is.EqualTo(12));
    }

    [Test]
    public void EvaluateModule_ImportNamespace_ReflectsLiveExportValue()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/dep.js"] = """
                               export let x = 1;
                               export function inc() { x = x + 1; }
                               """,
            ["/mods/main.js"] = """
                                import * as ns from "./dep.js";
                                export function read() { return ns.x; }
                                export { inc } from "./dep.js";
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var moduleNs = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(moduleNs.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);

        Assert.That(nsObj!.TryGetProperty("read", out var readValue), Is.True);
        Assert.That(readValue.TryGetObject(out var readObj), Is.True);
        Assert.That(readObj, Is.InstanceOf<JsFunction>());

        Assert.That(nsObj.TryGetProperty("inc", out var incValue), Is.True);
        Assert.That(incValue.TryGetObject(out var incObj), Is.True);
        Assert.That(incObj, Is.InstanceOf<JsFunction>());

        var before = realm.InvokeFunction((JsFunction)readObj!, JsValue.Undefined, ReadOnlySpan<JsValue>.Empty);
        Assert.That(before.Int32Value, Is.EqualTo(1));
        _ = realm.InvokeFunction((JsFunction)incObj!, JsValue.Undefined, ReadOnlySpan<JsValue>.Empty);
        var after = realm.InvokeFunction((JsFunction)readObj!, JsValue.Undefined, ReadOnlySpan<JsValue>.Empty);
        Assert.That(after.Int32Value, Is.EqualTo(2));
    }

    [Test]
    public void EvaluateModule_DoesNotLeakPerModuleTempGlobalNames()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/a.js"] = """export const a = 1;"""
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        _ = engine.MainAgent.EvaluateModule(realm, "/mods/a.js");

        var leaked = realm.Eval("""
                                var names = Object.getOwnPropertyNames(globalThis);
                                var leaked = false;
                                for (var i = 0; i < names.length; i++) {
                                  if (names[i].indexOf("__okojo_mod_") === 0) { leaked = true; break; }
                                }
                                leaked;
                                """);
        Assert.That(leaked.IsTrue, Is.False);
    }

    [Test]
    public void EvaluateModule_QuotedExportName_Works()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/q.js"] = """
                             const value = 3;
                             export { value as "quoted-name" };
                             """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/q.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("quoted-name", out var value), Is.True);
        Assert.That(value.Int32Value, Is.EqualTo(3));
    }

    [Test]
    public void EvaluateModule_ReExportQuotedAlias_And_DefaultAliasCombos_Work()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/dep.js"] = """
                               const local = 9;
                               export { local as default, local as "quoted-local" };
                               """,
            ["/mods/main.js"] = """
                                export { default as aliasDefault, default as "quoted-default", "quoted-local" as "again-quoted" } from "./dep.js";
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);

        Assert.That(nsObj!.TryGetProperty("aliasDefault", out var aliasDefault), Is.True);
        Assert.That(aliasDefault.Int32Value, Is.EqualTo(9));
        Assert.That(nsObj.TryGetProperty("quoted-default", out var quotedDefault), Is.True);
        Assert.That(quotedDefault.Int32Value, Is.EqualTo(9));
        Assert.That(nsObj.TryGetProperty("again-quoted", out var againQuoted), Is.True);
        Assert.That(againQuoted.Int32Value, Is.EqualTo(9));
    }

    [Test]
    public void EvaluateModule_ExportAllAsNamespace_Works()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/dep.js"] = """export const a = 11; export const b = 22;""",
            ["/mods/main.js"] = """export * as ns from "./dep.js";"""
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("ns", out var depNsValue), Is.True);
        Assert.That(depNsValue.TryGetObject(out var depNsObj), Is.True);
        Assert.That(depNsObj, Is.Not.Null);
        Assert.That(depNsObj!.TryGetProperty("a", out var a), Is.True);
        Assert.That(depNsObj.TryGetProperty("b", out var b), Is.True);
        Assert.That(a.Int32Value, Is.EqualTo(11));
        Assert.That(b.Int32Value, Is.EqualTo(22));
    }

    [Test]
    public void EvaluateModule_ImportNamespace_ThenExportLocalNamespace_Works()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/empty.js"] = "",
            ["/mods/main.js"] = """
                                import * as foo from "./empty.js";
                                export { foo };
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("foo", out var fooValue), Is.True);
        Assert.That(fooValue.IsObject, Is.True);
    }

    //[Test]
    public void EvaluateModule_ExportStar_DuplicateSameNamespaceBinding_IsNotAmbiguous()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/empty.js"] = "",
            ["/mods/a.js"] = """
                             import * as foo from "./empty.js";
                             export { foo };
                             """,
            ["/mods/b.js"] = """
                             import * as foo from "./empty.js";
                             export { foo };
                             """,
            ["/mods/main.js"] = """
                                export * from "./a.js";
                                export * from "./b.js";
                                import { foo } from "./main.js";
                                export const t = typeof foo;
                                export const v = foo;
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("foo", out var fooValue), Is.True);
        Assert.That(fooValue.IsObject, Is.True);
        Assert.That(nsObj.TryGetProperty("v", out var vValue), Is.True);
        Assert.That(vValue.IsObject, Is.True);
        Assert.That(nsObj.TryGetProperty("t", out var tValue), Is.True);
        Assert.That(tValue.IsString, Is.True);
        Assert.That(tValue.AsString(), Is.EqualTo("object"));
    }

    [Test]
    public void EvaluateModule_ImportedNamedBinding_IsLiveInModuleBody()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/dep.js"] = """
                               export let counter = 0;
                               export function inc() { counter = counter + 1; }
                               """,
            ["/mods/main.js"] = """
                                import { counter, inc } from "./dep.js";
                                export function read() { return counter; }
                                export { inc };
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var moduleNs = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(moduleNs.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);

        Assert.That(nsObj!.TryGetProperty("read", out var readValue), Is.True);
        Assert.That(readValue.TryGetObject(out var readObj), Is.True);
        Assert.That(readObj, Is.InstanceOf<JsFunction>());

        Assert.That(nsObj.TryGetProperty("inc", out var incValue), Is.True);
        Assert.That(incValue.TryGetObject(out var incObj), Is.True);
        Assert.That(incObj, Is.InstanceOf<JsFunction>());

        var before = realm.InvokeFunction((JsFunction)readObj!, JsValue.Undefined, ReadOnlySpan<JsValue>.Empty);
        Assert.That(before.Int32Value, Is.EqualTo(0));
        _ = realm.InvokeFunction((JsFunction)incObj!, JsValue.Undefined, ReadOnlySpan<JsValue>.Empty);
        var after = realm.InvokeFunction((JsFunction)readObj!, JsValue.Undefined, ReadOnlySpan<JsValue>.Empty);
        Assert.That(after.Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void EvaluateModule_ExportDefaultNamedDeclarationForms_Work()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """
                                export default function namedFn() { return 5; }
                                export const klass = class NamedClass {};
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("default", out var defaultFn), Is.True);
        Assert.That(defaultFn.TryGetObject(out var defaultFnObj), Is.True);
        Assert.That(defaultFnObj, Is.InstanceOf<JsFunction>());
        var fnResult = realm.InvokeFunction((JsFunction)defaultFnObj!, JsValue.Undefined, ReadOnlySpan<JsValue>.Empty);
        Assert.That(fnResult.Int32Value, Is.EqualTo(5));
    }

    [Test]
    public void EvaluateModule_ExportDefaultNamedFunctionBinding_IsLive()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/dep.js"] = """
                               export default function fn() {
                                   fn = 2;
                                   return 1;
                               }
                               """,
            ["/mods/main.js"] = """
                                import val from "./dep.js";
                                export const first = val();
                                export const second = val;
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("first", out var first), Is.True);
        Assert.That(first.Int32Value, Is.EqualTo(1));
        Assert.That(nsObj.TryGetProperty("second", out var second), Is.True);
        Assert.That(second.Int32Value, Is.EqualTo(2));
    }

    [Test]
    public void EvaluateModule_ExportDefaultNamedAsyncGeneratorDeclaration_BindsName()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """
                                export default async function * AG() {}
                                AG.foo = '';
                                export const t = typeof AG;
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("t", out var tValue), Is.True);
        Assert.That(tValue.IsString, Is.True);
        Assert.That(tValue.AsString(), Is.EqualTo("function"));
    }

    //[Test]
    public void EvaluateModule_NamespaceObject_Assignment_ThrowsTypeError()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """export const x = 1;"""
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        realm.Global["ns"] = ns;

        var ex = Assert.Throws<JsRuntimeException>(() => _ = realm.Eval("ns.x = 2;"));
        Assert.That(ex!.DetailCode, Is.EqualTo("MODULE_NAMESPACE_READONLY"));
    }

    //[Test]
    public void EvaluateModule_NamespaceObject_AddProperty_ThrowsTypeError()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """export const x = 1;"""
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        realm.Global["ns"] = ns;

        var ex = Assert.Throws<JsRuntimeException>(() => _ = realm.Eval("ns.extra = 10;"));
        Assert.That(ex!.DetailCode, Is.EqualTo("MODULE_NAMESPACE_READONLY"));
    }

    [Test]
    public void EvaluateModule_NamespaceObject_DeleteExport_ReturnsFalse()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """export const x = 1;"""
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        realm.Global["ns"] = ns;

        var deleted = realm.Eval("delete ns.x;");
        Assert.That(deleted.IsFalse, Is.True);
    }

    [Test]
    public void EvaluateModule_NamespaceObject_HasNullPrototype_AndNotInstanceOfObject()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """export const x = 1;"""
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        realm.Global["ns"] = ns;

        var ok = realm.Eval("""
                            (ns instanceof Object) === false &&
                            Object.getPrototypeOf(ns) === null;
                            """);
        Assert.That(ok.IsTrue, Is.True);
    }

    [Test]
    public void EvaluateModule_ClassComputedFieldNames_FromAwait_Work()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """
                                let result;
                                try {
                                  let C = class {
                                    [await 9] = () => {
                                      return 9;
                                    };

                                    static [await 9] = () => {
                                      return 9;
                                    };
                                  };

                                  let c = new C();

                                  result =
                                    c[await 9]() === 9 &&
                                    C[await 9]() === 9 &&
                                    c[String(await 9)]() === 9 &&
                                    C[String(await 9)]() === 9;
                                } catch (e) {
                                  result = e.name + ":" + e.message;
                                }

                                export { result };
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("result", out var result), Is.True);
        Assert.That(result.IsTrue, Is.True, result.IsString ? result.AsString() : result.ToString());
    }

    [Test]
    public void EvaluateModule_ClassComputedFieldNames_FromAwait_DebugPath()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """
                                globalThis.trace = "";
                                let C = class {
                                  [await 9] = () => {
                                    return 9;
                                  };

                                  static [await 9] = () => {
                                    return 9;
                                  };
                                };
                                globalThis.trace += "A";

                                let c = new C();
                                globalThis.trace += "B";
                                export const before = (globalThis.trace += "C", typeof C === "function" && typeof c === "object");
                                export const a = (globalThis.trace += "D", c[await 9]() === 9);
                                export const b = (globalThis.trace += "E", C[await 9]() === 9);
                                export const c1 = (globalThis.trace += "F", c[String(await 9)]() === 9);
                                export const d = (globalThis.trace += "G", C[String(await 9)]() === 9);
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("before", out var before), Is.True);
        Assert.That(nsObj!.TryGetProperty("a", out var a), Is.True);
        Assert.That(nsObj.TryGetProperty("b", out var b), Is.True);
        Assert.That(nsObj.TryGetProperty("c1", out var c1), Is.True);
        Assert.That(nsObj.TryGetProperty("d", out var d), Is.True);
        Assert.That(realm.Global["trace"].AsString(), Is.EqualTo("ABCDEFG"),
            $"before={before} a={a} b={b} c1={c1} d={d}");
        Assert.That(before.IsTrue, Is.True, $"before={before}");
        Assert.That(a.IsTrue, Is.True, $"a={a}");
        Assert.That(b.IsTrue, Is.True, $"b={b}");
        Assert.That(c1.IsTrue, Is.True, $"c1={c1}");
        Assert.That(d.IsTrue, Is.True, $"d={d}");
    }

    [Test]
    public void ParseModule_ClassComputedFieldNames_FromAwait_SetsTopLevelAwait()
    {
        var program = JavaScriptParser.ParseModule("""
                                                   let C = class {
                                                     [await 9] = 0;
                                                     static [await 9] = 0;
                                                   };
                                                   export { C };
                                                   """);

        Assert.That(program.HasTopLevelAwait, Is.True);
    }

    [Test]
    public void ParseModule_ForAwaitOf_Allows_TopLevelAwait_In_Header_And_Body()
    {
        var program = JavaScriptParser.ParseModule("""
                                                   var binding;

                                                   for await (binding of [await ``]) {
                                                     await ``;
                                                     break;
                                                   }
                                                   """);

        Assert.That(program.HasTopLevelAwait, Is.True);
        Assert.That(program.Statements.Count, Is.EqualTo(2));
        Assert.That(program.Statements[1], Is.TypeOf<JsForInOfStatement>());
        var forOf = (JsForInOfStatement)program.Statements[1];
        Assert.That(forOf.IsAwait, Is.True);
        Assert.That(forOf.IsOf, Is.True);
    }

    [Test]
    public void ParseModule_ExportVar_ObjectPattern_With_TopLevelAwait_SetsTopLevelAwait()
    {
        var program = JavaScriptParser.ParseModule("""
                                                   export var name1 = await null;
                                                   export var { x = await null } = {};
                                                   """);

        Assert.That(program.HasTopLevelAwait, Is.True);
        Assert.That(program.Statements.Count, Is.EqualTo(2));
        Assert.That(program.Statements[1], Is.TypeOf<JsExportDeclarationStatement>());
    }

    [Test]
    public void EvaluateModule_ExportVar_ObjectPattern_From_Awaited_Initializer_Works()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """
                                export var name1 = await null;
                                export var { x = await Promise.resolve(7) } = {};
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("name1", out var name1), Is.True);
        Assert.That(name1.IsNull, Is.True);
        Assert.That(nsObj.TryGetProperty("x", out var x), Is.True);
        Assert.That(x.NumberValue, Is.EqualTo(7));
    }

    [Test]
    public void EvaluateModule_TopLevelAwait_Dependency_DoesNot_Block_Sibling_Module_Evaluation()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/async.js"] = """
                                 globalThis.check = false;
                                 await 0;
                                 globalThis.check = true;
                                 """,
            ["/mods/sync.js"] = """
                                export const { check } = globalThis;
                                """,
            ["/mods/main.js"] = """
                                import "./async.js";
                                import { check } from "./sync.js";
                                export const observed = check;
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("observed", out var observed), Is.True);
        Assert.That(observed.IsFalse, Is.True, observed.ToString());

        realm.PumpJobs();
        Assert.That(realm.Global["check"].IsTrue, Is.True);
    }

    [Test]
    public void EvaluateModule_SyncImporter_Waits_For_Async_Dependency_Before_Running_Body()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/async.js"] = """
                                 await 1;
                                 await 2;
                                 export default await Promise.resolve(42);
                                 """,
            ["/mods/main.js"] = """
                                globalThis.phase = "start";
                                Promise.resolve().then(() => globalThis.phase = "tick");
                                import value from "./async.js";
                                export const observedValue = value;
                                export const observedPhase = globalThis.phase;
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("observedValue", out var observedValue), Is.True);
        Assert.That(observedValue.NumberValue, Is.EqualTo(42));
        Assert.That(nsObj.TryGetProperty("observedPhase", out var observedPhase), Is.True);
        Assert.That(observedPhase.AsString(), Is.EqualTo("start"));

        realm.PumpJobs();
        Assert.That(realm.Global["phase"].AsString(), Is.EqualTo("tick"));
    }

    [Test]
    public void EvaluateModule_ImporterOfAsyncCycleLeaf_Waits_For_CycleRoot_Completion()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/setup.js"] = """
                                 globalThis.logs = [];
                                 """,
            ["/mods/cycle-root.js"] = """
                                      import "./cycle-leaf.js";
                                      globalThis.logs.push("cycle root start");
                                      await 1;
                                      globalThis.logs.push("cycle root end");
                                      """,
            ["/mods/cycle-leaf.js"] = """
                                      import "./cycle-root.js";
                                      globalThis.logs.push("cycle leaf start");
                                      await 1;
                                      globalThis.logs.push("cycle leaf end");
                                      """,
            ["/mods/importer.js"] = """
                                    import "./cycle-leaf.js";
                                    globalThis.logs.push("importer of cycle leaf");
                                    """,
            ["/mods/main.js"] = """
                                import "./setup.js";
                                import "./cycle-root.js";
                                import "./importer.js";
                                export default globalThis.logs;
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("default", out var logsValue), Is.True);
        Assert.That(logsValue.TryGetObject(out var logsObject), Is.True);
        Assert.That(logsObject, Is.InstanceOf<JsArray>());
        var logs = (JsArray)logsObject!;
        Assert.That(logs.Length, Is.EqualTo(5));
        Assert.That(logs.TryGetElement(0, out var first), Is.True);
        Assert.That(logs.TryGetElement(1, out var second), Is.True);
        Assert.That(logs.TryGetElement(2, out var third), Is.True);
        Assert.That(logs.TryGetElement(3, out var fourth), Is.True);
        Assert.That(logs.TryGetElement(4, out var fifth), Is.True);
        var entries = new[]
        {
            first.AsString(),
            second.AsString(),
            third.AsString(),
            fourth.AsString(),
            fifth.AsString()
        };

        Assert.That(Array.IndexOf(entries, "cycle leaf start"), Is.GreaterThanOrEqualTo(0));
        Assert.That(Array.IndexOf(entries, "cycle leaf end"),
            Is.GreaterThan(Array.IndexOf(entries, "cycle leaf start")));
        Assert.That(Array.IndexOf(entries, "cycle root start"), Is.GreaterThanOrEqualTo(0));
        Assert.That(Array.IndexOf(entries, "cycle root end"),
            Is.GreaterThan(Array.IndexOf(entries, "cycle root start")));
        Assert.That(Array.IndexOf(entries, "cycle leaf end"),
            Is.LessThan(Array.IndexOf(entries, "importer of cycle leaf")));
        Assert.That(Array.IndexOf(entries, "cycle root end"),
            Is.LessThan(Array.IndexOf(entries, "importer of cycle leaf")));
        Assert.That(fifth.AsString(), Is.EqualTo("importer of cycle leaf"));
    }

    [Test]
    public void EvaluateModule_ImporterOfThreeNodeAsyncCycle_Waits_For_Entire_Cycle()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/setup-3cycle.js"] = """
                                        globalThis.logs = [];
                                        """,
            ["/mods/cycle-a.js"] = """
                                   import "./cycle-b.js";
                                   globalThis.logs.push("cycle a start");
                                   await 1;
                                   globalThis.logs.push("cycle a end");
                                   """,
            ["/mods/cycle-b.js"] = """
                                   import "./cycle-c.js";
                                   globalThis.logs.push("cycle b start");
                                   await 1;
                                   globalThis.logs.push("cycle b end");
                                   """,
            ["/mods/cycle-c.js"] = """
                                   import "./cycle-a.js";
                                   globalThis.logs.push("cycle c start");
                                   await 1;
                                   globalThis.logs.push("cycle c end");
                                   """,
            ["/mods/importer-3cycle.js"] = """
                                           import "./cycle-c.js";
                                           globalThis.logs.push("importer of three-node cycle");
                                           """,
            ["/mods/main-3cycle.js"] = """
                                       import "./setup-3cycle.js";
                                       import "./cycle-a.js";
                                       import "./importer-3cycle.js";
                                       export default globalThis.logs;
                                       """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main-3cycle.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("default", out var logsValue), Is.True);
        Assert.That(logsValue.TryGetObject(out var logsObject), Is.True);
        Assert.That(logsObject, Is.InstanceOf<JsArray>());
        var logs = (JsArray)logsObject!;
        Assert.That(logs.Length, Is.EqualTo(7));

        var entries = new string[7];
        for (uint i = 0; i < entries.Length; i++)
        {
            Assert.That(logs.TryGetElement(i, out var entry), Is.True);
            entries[i] = entry.AsString();
        }

        Assert.That(Array.IndexOf(entries, "cycle a start"), Is.GreaterThanOrEqualTo(0));
        Assert.That(Array.IndexOf(entries, "cycle a end"), Is.GreaterThan(Array.IndexOf(entries, "cycle a start")));
        Assert.That(Array.IndexOf(entries, "cycle b start"), Is.GreaterThanOrEqualTo(0));
        Assert.That(Array.IndexOf(entries, "cycle b end"), Is.GreaterThan(Array.IndexOf(entries, "cycle b start")));
        Assert.That(Array.IndexOf(entries, "cycle c start"), Is.GreaterThanOrEqualTo(0));
        Assert.That(Array.IndexOf(entries, "cycle c end"), Is.GreaterThan(Array.IndexOf(entries, "cycle c start")));
        Assert.That(Array.IndexOf(entries, "cycle a end"),
            Is.LessThan(Array.IndexOf(entries, "importer of three-node cycle")));
        Assert.That(Array.IndexOf(entries, "cycle b end"),
            Is.LessThan(Array.IndexOf(entries, "importer of three-node cycle")));
        Assert.That(Array.IndexOf(entries, "cycle c end"),
            Is.LessThan(Array.IndexOf(entries, "importer of three-node cycle")));
        Assert.That(entries[^1], Is.EqualTo("importer of three-node cycle"));
    }

    [Test]
    public void EvaluateModule_TopLevelAwait_Fulfillment_Order_Is_Leaf_To_Root()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/setup.js"] = """
                                 export const p1 = Promise.withResolvers();
                                 export const pA_start = Promise.withResolvers();
                                 export const pB_start = Promise.withResolvers();
                                 """,
            ["/mods/a-sentinel.js"] = """
                                      import { pA_start } from "./setup.js";
                                      pA_start.resolve();
                                      """,
            ["/mods/b-sentinel.js"] = """
                                      import { pB_start } from "./setup.js";
                                      pB_start.resolve();
                                      """,
            ["/mods/b.js"] = """
                             import "./b-sentinel.js";
                             import { p1 } from "./setup.js";
                             await p1.promise;
                             """,
            ["/mods/a.js"] = """
                             import "./a-sentinel.js";
                             import "./b.js";
                             """,
            ["/mods/main.js"] = """
                                import { p1, pA_start, pB_start } from "./setup.js";

                                globalThis.done = "pending";
                                let logs = [];

                                const importsP = Promise.all([
                                  pB_start.promise.then(() => import("./a.js").finally(() => logs.push("A"))).catch(() => {}),
                                  import("./b.js").finally(() => logs.push("B")).catch(() => {}),
                                ]);

                                Promise.all([pA_start.promise, pB_start.promise]).then(p1.resolve);

                                importsP.then(() => {
                                  globalThis.done = logs.join(",");
                                }, error => {
                                  globalThis.done = error && error.message;
                                });
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        _ = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");

        for (var i = 0; i < 40 && realm.Global["done"].AsString() == "pending"; i++)
            realm.PumpJobs();

        Assert.That(realm.Global["done"].AsString(), Is.EqualTo("B,A"));
    }

    [Test]
    public void EvaluateModule_TopLevelAwait_Rejection_Order_Is_Leaf_To_Root()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/setup.js"] = """
                                 export const p1 = Promise.withResolvers();
                                 export const pA_start = Promise.withResolvers();
                                 export const pB_start = Promise.withResolvers();
                                 """,
            ["/mods/a-sentinel.js"] = """
                                      import { pA_start } from "./setup.js";
                                      pA_start.resolve();
                                      """,
            ["/mods/b-sentinel.js"] = """
                                      import { pB_start } from "./setup.js";
                                      pB_start.resolve();
                                      """,
            ["/mods/b.js"] = """
                             import "./b-sentinel.js";
                             import { p1 } from "./setup.js";
                             await p1.promise;
                             """,
            ["/mods/a.js"] = """
                             import "./a-sentinel.js";
                             import "./b.js";
                             """,
            ["/mods/main.js"] = """
                                import { p1, pA_start, pB_start } from "./setup.js";

                                globalThis.done = "pending";
                                let logs = [];

                                const importsP = Promise.all([
                                  pB_start.promise.then(() => import("./a.js").finally(() => logs.push("A"))).catch(() => {}),
                                  import("./b.js").finally(() => logs.push("B")).catch(() => {}),
                                ]);

                                Promise.all([pA_start.promise, pB_start.promise]).then(p1.reject);

                                importsP.then(() => {
                                  globalThis.done = logs.join(",");
                                }, error => {
                                  globalThis.done = error && error.message;
                                });
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        _ = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");

        for (var i = 0; i < 40 && realm.Global["done"].AsString() == "pending"; i++)
            realm.PumpJobs();

        Assert.That(realm.Global["done"].AsString(), Is.EqualTo("B,A"));
    }

    [Test]
    public void EvaluateModule_TopLevelAwait_AsyncEvaluationOrder_Preserves_Earlier_Ancestor_Before_Later_Graph()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/setup.js"] = """
                                 export const logs = [];
                                 export const pB = Promise.withResolvers();
                                 export const pB_start = Promise.withResolvers();
                                 export const pE_start = Promise.withResolvers();
                                 """,
            ["/mods/b.js"] = """
                             import { pB, pB_start } from "./setup.js";
                             pB_start.resolve();
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
                             import { pE_start } from "./setup.js";
                             pE_start.resolve();
                             """,
            ["/mods/d.js"] = """
                             import { logs } from "./setup.js";
                             import "./e.js";
                             import "./b.js";
                             logs.push("D");
                             """,
            ["/mods/main.js"] = """
                                import { logs, pB, pB_start, pE_start } from "./setup.js";

                                globalThis.done = "pending";
                                const pA = import("./a.js");
                                let pD;

                                pB_start.promise.then(() => {
                                  return import("./c.js");
                                }).then(() => {
                                  pD = import("./d.js");
                                  return pE_start.promise;
                                }).then(() => {
                                  pB.resolve();
                                  return Promise.all([pA, pD]);
                                }).then(() => {
                                  globalThis.done = logs.join(",");
                                }, error => {
                                  globalThis.done = error && error.message;
                                });
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        _ = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");

        for (var i = 0; i < 60 && realm.Global["done"].AsString() == "pending"; i++)
            realm.PumpJobs();

        Assert.That(realm.Global["done"].AsString(), Is.EqualTo("A,D"));
    }

    [Test]
    public void EvaluateModule_ExportConst_From_Awaited_Keyed_Call_Works()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """
                                let c = { 9: () => 9 };
                                export const a = c[await 9];
                                export const b = c[await 9]();
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("a", out var a), Is.True);
        Assert.That(a.TryGetObject(out var aObj), Is.True);
        Assert.That(aObj, Is.InstanceOf<JsFunction>());
        Assert.That(nsObj.TryGetProperty("b", out var b), Is.True);
        Assert.That(b.NumberValue, Is.EqualTo(9));
    }

    [Test]
    public void EvaluateModule_TopLevelForAwait_With_DeepNestedAwait_Executes()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """
                                var binding;
                                globalThis.hit = 0;

                                for await (binding of [await await await await await await await await await await await await await await await 'await']) {
                                  await await await await await await await await await await await await await await await 'await';
                                  globalThis.hit += 1;
                                  break;
                                }

                                export const done = globalThis.hit;
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");

        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("done", out var done), Is.True);
        Assert.That(done.NumberValue, Is.EqualTo(1));
    }

    [Test]
    public void EvaluateModule_ForOfConstArrayDestructuring_Getter_Captures_Current_StyleName()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """
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

                                export const kind = typeof chalk.green;
                                export const value = chalk.green('hello');
                                export const wrong = Object.getOwnPropertyDescriptor(chalk, 'bgWhiteBright') === undefined;
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");

        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("kind", out var kind), Is.True);
        Assert.That(kind.AsString(), Is.EqualTo("function"));
        Assert.That(nsObj.TryGetProperty("value", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("<g>hello</g>"));
        Assert.That(nsObj.TryGetProperty("wrong", out var wrong), Is.True);
        Assert.That(wrong.IsTrue, Is.True);
    }

    [Test]
    public void EvaluateModule_TopLevelAwait_RejectedPromise_Catch_Preserves_Object_Identity()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """
                                const obj = {};
                                let same = false;
                                let type = "unset";
                                try {
                                  await Promise.reject(obj);
                                } catch (e) {
                                  same = e === obj;
                                  type = typeof e;
                                }

                                export { same, type };
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");

        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("same", out var same), Is.True);
        Assert.That(same.IsTrue, Is.True);
        Assert.That(nsObj.TryGetProperty("type", out var type), Is.True);
        Assert.That(type.AsString(), Is.EqualTo("object"));
    }

    [Test]
    public void
        EvaluateModule_TopLevelAwait_RejectedPromise_Undefined_After_Prior_Rejection_Does_Not_Reuse_Previous_Value()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """
                                let first;
                                let second = "sentinel";

                                try {
                                  await Promise.reject(true);
                                } catch (e) {
                                  first = e;
                                }

                                try {
                                  await Promise.reject(undefined);
                                } catch (e) {
                                  second = e;
                                }

                                export const firstIsTrue = first === true;
                                export const secondIsUndefined = second === undefined;
                                export const secondType = typeof second;
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");

        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("firstIsTrue", out var firstIsTrue), Is.True);
        Assert.That(firstIsTrue.IsTrue, Is.True);
        Assert.That(nsObj.TryGetProperty("secondIsUndefined", out var secondIsUndefined), Is.True);
        Assert.That(secondIsUndefined.IsTrue, Is.True);
        Assert.That(nsObj.TryGetProperty("secondType", out var secondType), Is.True);
        Assert.That(secondType.AsString(), Is.EqualTo("undefined"));
    }

    private sealed class InMemoryModuleLoader(Dictionary<string, string> modules) : IModuleSourceLoader
    {
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
