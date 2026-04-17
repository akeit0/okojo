using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ModuleExecutionGapTests
{
    [Test]
    public void RealmImport_DelegatesToAgentModules()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = "export const value = 7;"
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = realm.Import("/mods/main.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("value", out var value), Is.True);
        Assert.That(value.Int32Value, Is.EqualTo(7));
    }

    [Test]
    public void AgentModules_LinkAndResolve_Work()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = "export const value = 1;"
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var agent = engine.MainAgent;
        var realm = agent.MainRealm;
        var resolved = agent.Modules.Resolve("/mods/main.js");
        var linked = agent.Modules.Link(realm, "/mods/main.js");

        Assert.That(resolved, Is.EqualTo("/mods/main.js"));
        Assert.That(linked, Is.EqualTo("/mods/main.js"));
    }

    [Test]
    public void EvaluateModule_SideEffectOnlyImport_ExecutesDependency()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/dep.js"] = """
                               globalThis.sideFx = (globalThis.sideFx || 0) + 1;
                               export const tag = "dep";
                               """,
            ["/mods/main.js"] = """
                                import "./dep.js";
                                export const observed = globalThis.sideFx;
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;

        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("observed", out var observed), Is.True);
        Assert.That(observed.Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void EvaluateModule_Cycle_LiveBindings_AreVisibleAcrossModules()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/a.js"] = """
                             import { b, incB } from "./b.js";
                             export let a = 1;
                             export function incA() { a = a + 1; }
                             export function readB() { return b; }
                             export function bumpBoth() { incB(); incA(); }
                             """,
            ["/mods/b.js"] = """
                             import { a, incA } from "./a.js";
                             export let b = 10;
                             export function incB() { b = b + 1; }
                             export function readA() { return a; }
                             export function bumpBothB() { incA(); incB(); }
                             """,
            ["/mods/main.js"] = """
                                import { readB, bumpBoth } from "./a.js";
                                import { readA } from "./b.js";
                                export function snapshot() { return [readA(), readB()]; }
                                export { bumpBoth };
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);

        Assert.That(nsObj!.TryGetProperty("snapshot", out var snapshotValue), Is.True);
        Assert.That(snapshotValue.TryGetObject(out var snapshotObj), Is.True);
        Assert.That(snapshotObj, Is.InstanceOf<JsFunction>());

        Assert.That(nsObj.TryGetProperty("bumpBoth", out var bumpValue), Is.True);
        Assert.That(bumpValue.TryGetObject(out var bumpObj), Is.True);
        Assert.That(bumpObj, Is.InstanceOf<JsFunction>());

        var before = realm.InvokeFunction((JsFunction)snapshotObj!, JsValue.Undefined, ReadOnlySpan<JsValue>.Empty);
        Assert.That(before.TryGetObject(out var beforeObj), Is.True);
        Assert.That(beforeObj, Is.InstanceOf<JsArray>());
        var beforeArray = (JsArray)beforeObj!;
        Assert.That(beforeArray.TryGetElement(0, out var before0), Is.True);
        Assert.That(beforeArray.TryGetElement(1, out var before1), Is.True);
        Assert.That(before0.Int32Value, Is.EqualTo(1));
        Assert.That(before1.Int32Value, Is.EqualTo(10));

        _ = realm.InvokeFunction((JsFunction)bumpObj!, JsValue.Undefined, ReadOnlySpan<JsValue>.Empty);

        var after = realm.InvokeFunction((JsFunction)snapshotObj!, JsValue.Undefined, ReadOnlySpan<JsValue>.Empty);
        Assert.That(after.TryGetObject(out var afterObj), Is.True);
        Assert.That(afterObj, Is.InstanceOf<JsArray>());
        var afterArray = (JsArray)afterObj!;
        Assert.That(afterArray.TryGetElement(0, out var after0), Is.True);
        Assert.That(afterArray.TryGetElement(1, out var after1), Is.True);
        Assert.That(after0.Int32Value, Is.EqualTo(2));
        Assert.That(after1.Int32Value, Is.EqualTo(11));
    }

    [Test]
    public void EvaluateModule_ExportedLocal_Reads_Use_Module_Local_Not_Live_Export_Cell()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/enums.js"] = """
                                 export let Align = /*#__PURE__*/function (Align) {
                                   Align[Align["Auto"] = 0] = "Auto";
                                   return Align;
                                 }({});
                                 const constants = {
                                   ALIGN_AUTO: Align.Auto
                                 };
                                 export default constants;
                                 """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/enums.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("Align", out var alignValue), Is.True);
        Assert.That(alignValue.TryGetObject(out var alignObj), Is.True);
        Assert.That(alignObj, Is.Not.Null);
        Assert.That(alignObj!.TryGetProperty("Auto", out var autoValue), Is.True);
        Assert.That(autoValue.Int32Value, Is.EqualTo(0));
        Assert.That(nsObj.TryGetProperty("default", out var defaultValue), Is.True);
        Assert.That(defaultValue.TryGetObject(out var defaultObj), Is.True);
        Assert.That(defaultObj, Is.Not.Null);
        Assert.That(defaultObj!.TryGetProperty("ALIGN_AUTO", out var alignAutoValue), Is.True);
        Assert.That(alignAutoValue.Int32Value, Is.EqualTo(0));
    }

    [Test]
    public void EvaluateModule_WhenResolveThrows_WrapsAsJsRuntimeException()
    {
        var loader = new ThrowingResolveModuleLoader();
        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;

        var ex = Assert.Throws<JsRuntimeException>(() =>
            _ = engine.MainAgent.EvaluateModule(realm, "/mods/main.js"));

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.DetailCode, Is.EqualTo("MODULE_RESOLVE_FAILED"));
        Assert.That(ex.Message, Does.Contain("/mods/main.js"));
    }

    [Test]
    public void EvaluateModule_WhenLoadThrows_WrapsAsJsRuntimeException()
    {
        var loader = new ThrowingLoadModuleLoader();
        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;

        var ex = Assert.Throws<JsRuntimeException>(() =>
            _ = engine.MainAgent.EvaluateModule(realm, "/mods/main.js"));

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.DetailCode, Is.EqualTo("MODULE_LOAD_FAILED"));
        Assert.That(ex.Message, Does.Contain("resolved:/mods/main.js"));
    }

    [Test]
    public void EvaluateModule_WhenParseThrows_WrapsAsJsRuntimeException()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = "export { ;"
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;

        var ex = Assert.Throws<JsRuntimeException>(() =>
            _ = engine.MainAgent.EvaluateModule(realm, "/mods/main.js"));

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.DetailCode, Is.EqualTo("MODULE_PARSE_FAILED"));
        Assert.That(ex.Message, Does.Contain("/mods/main.js"));
    }

    [Test]
    public void EvaluateModule_ImportMetaUrl_UsesResolvedModuleId()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """
                                export const url = import.meta.url;
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("url", out var url), Is.True);
        Assert.That(url.IsString, Is.True);
        Assert.That(url.AsString(), Is.EqualTo("/mods/main.js"));
    }

    [Test]
    public void EvaluateModule_ImportedFunction_Uses_Its_Own_ImportMeta_Object()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/fixture.js"] = """
                                   export const meta = import.meta;
                                   export function getMeta() { return import.meta; }
                                   """,
            ["/mods/main.js"] = """
                                import { meta as fixtureMeta, getMeta } from "./fixture.js";
                                export default [
                                  import.meta !== fixtureMeta,
                                  import.meta !== getMeta(),
                                  fixtureMeta === getMeta()
                                ];
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("default", out var result), Is.True);
        Assert.That(result.TryGetObject(out var resultObj), Is.True);
        Assert.That(resultObj, Is.InstanceOf<JsArray>());
        var resultArray = (JsArray)resultObj!;
        Assert.That(resultArray.TryGetElement(0, out var first), Is.True);
        Assert.That(resultArray.TryGetElement(1, out var second), Is.True);
        Assert.That(resultArray.TryGetElement(2, out var third), Is.True);
        Assert.That(first.IsTrue, Is.True);
        Assert.That(second.IsTrue, Is.True);
        Assert.That(third.IsTrue, Is.True);
    }

    [Test]
    public void EvaluateModule_ExportStar_AmbiguousName_FailsNamedImport()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/a.js"] = """export const x = 1;""",
            ["/mods/b.js"] = """export const x = 2;""",
            ["/mods/main.js"] = """
                                export * from "./a.js";
                                export * from "./b.js";
                                """,
            ["/mods/user.js"] = """
                                import { x } from "./main.js";
                                export const y = x;
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ex = Assert.Throws<JsRuntimeException>(() =>
            _ = engine.MainAgent.EvaluateModule(realm, "/mods/user.js"));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.DetailCode, Is.EqualTo("MODULE_LINK_FAILED"));
        Assert.That(ex.InnerException, Is.TypeOf<JsRuntimeException>());
        Assert.That(((JsRuntimeException)ex.InnerException!).DetailCode, Is.EqualTo("MODULE_IMPORT_NAME_NOT_FOUND"));
    }

    [Test]
    public void EvaluateModule_ExplicitExport_PrecedesStarConflict()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/a.js"] = """export const x = 1;""",
            ["/mods/b.js"] = """export const x = 2;""",
            ["/mods/main.js"] = """
                                export { x } from "./a.js";
                                export * from "./b.js";
                                """,
            ["/mods/user.js"] = """
                                import { x } from "./main.js";
                                export const y = x;
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/user.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("y", out var y), Is.True);
        Assert.That(y.Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void EvaluateModule_SelfImport_Sees_Star_Reexported_Namespace_Binding()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/dep1.js"] = """
                                import * as foo from "./empty.js";
                                export { foo };
                                """,
            ["/mods/dep2.js"] = """
                                import * as foo from "./empty.js";
                                export { foo };
                                """,
            ["/mods/empty.js"] = string.Empty,
            ["/mods/main.js"] = """
                                export * from "./dep1.js";
                                export * from "./dep2.js";
                                import { foo } from "./main.js";
                                export const kind = typeof foo;
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("kind", out var kind), Is.True);
        Assert.That(kind.AsString(), Is.EqualTo("object"));
    }

    [Test]
    public void EvaluateModule_DynamicImport_Does_Not_Preempt_Dfs_Function_Instantiation_Order()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/a.js"] = """
                             import { evaluated, check } from "./main.js";
                             check(import("./b.js"));
                             evaluated("A");
                             """,
            ["/mods/b.js"] = """
                             import { evaluated } from "./main.js";
                             evaluated("B");
                             """,
            ["/mods/main.js"] = """
                                import "./a.js";
                                import "./b.js";

                                export function evaluated(name) {
                                  if (!evaluated.order) {
                                    evaluated.order = [];
                                  }
                                  evaluated.order.push(name);
                                }

                                export function check(promise) {
                                  promise.then(function () {
                                    globalThis.order = evaluated.order.slice();
                                  });
                                }
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;

        _ = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        realm.PumpJobs();

        var orderValue = realm.Global["order"];
        Assert.That(orderValue.TryGetObject(out var orderObject), Is.True);
        Assert.That(orderObject, Is.InstanceOf<JsArray>());
        var order = (JsArray)orderObject!;
        Assert.That(order.TryGetElement(0, out var first), Is.True);
        Assert.That(order.TryGetElement(1, out var second), Is.True);
        Assert.That(first.AsString(), Is.EqualTo("A"));
        Assert.That(second.AsString(), Is.EqualTo("B"));
    }

    [Test]
    public void EvaluateModule_Imported_Module_Locals_Do_Not_Leak_Into_Importer_Environment()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/other.js"] = """
                                 var seventh = 7;
                                 let eighth = 8;
                                 const ninth = 9;
                                 class tenth {}
                                 function eleventh() {}
                                 function* twelfth() {}
                                 """,
            ["/mods/main.js"] = """
                                import "./other.js";
                                export default [
                                  (() => { typeof seventh; try { seventh; return "visible"; } catch (e) { return e.name; } })(),
                                  (() => { typeof eighth; try { eighth; return "visible"; } catch (e) { return e.name; } })(),
                                  (() => { typeof ninth; try { ninth; return "visible"; } catch (e) { return e.name; } })(),
                                  (() => { typeof tenth; try { tenth; return "visible"; } catch (e) { return e.name; } })(),
                                  (() => { typeof eleventh; try { eleventh; return "visible"; } catch (e) { return e.name; } })(),
                                  (() => { typeof twelfth; try { twelfth; return "visible"; } catch (e) { return e.name; } })(),
                                ];
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");

        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("default", out var resultValue), Is.True);
        Assert.That(resultValue.TryGetObject(out var resultObj), Is.True);
        Assert.That(resultObj, Is.InstanceOf<JsArray>());

        var results = (JsArray)resultObj!;
        for (uint i = 0; i < 6; i++)
        {
            Assert.That(results.TryGetElement(i, out var entry), Is.True);
            Assert.That(entry.AsString(), Is.EqualTo("ReferenceError"));
        }
    }

    [Test]
    public void EvaluateModule_Anonymous_Default_Function_Export_Is_Hoisted_With_Default_Name()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """export default function() { return 23; }""",
            ["/mods/check.js"] = """
                                 import f from "./main.js";
                                 export default [f(), f.name];
                                 """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/check.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj!.TryGetProperty("default", out var resultValue), Is.True);
        Assert.That(resultValue.TryGetObject(out var resultObj), Is.True);
        var result = (JsArray)resultObj!;
        Assert.That(result.TryGetElement(0, out var value), Is.True);
        Assert.That(result.TryGetElement(1, out var name), Is.True);
        Assert.That(value.Int32Value, Is.EqualTo(23));
        Assert.That(name.AsString(), Is.EqualTo("default"));
    }

    [Test]
    public void EvaluateModule_Anonymous_Default_Generator_Export_Is_Hoisted_With_Default_Name()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """export default function* () { return 23; }""",
            ["/mods/check.js"] = """
                                 import g from "./main.js";
                                 export default [g().next().value, g.name];
                                 """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/check.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj!.TryGetProperty("default", out var resultValue), Is.True);
        Assert.That(resultValue.TryGetObject(out var resultObj), Is.True);
        var result = (JsArray)resultObj!;
        Assert.That(result.TryGetElement(0, out var value), Is.True);
        Assert.That(result.TryGetElement(1, out var name), Is.True);
        Assert.That(value.Int32Value, Is.EqualTo(23));
        Assert.That(name.AsString(), Is.EqualTo("default"));
    }

    [Test]
    public void EvaluateModule_Requested_Modules_Run_In_Source_Order_Across_Import_Forms()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/1.js"] = """globalThis.order = "1";""",
            ["/mods/2.js"] = """globalThis.order += "2";""",
            ["/mods/3.js"] = """globalThis.order += "3"; export const x = 3;""",
            ["/mods/4.js"] = """globalThis.order += "4"; export default null;""",
            ["/mods/5.js"] = """globalThis.order += "5";""",
            ["/mods/6.js"] = """globalThis.order += "6"; export default null;""",
            ["/mods/7.js"] = """globalThis.order += "7"; export const x = 7;""",
            ["/mods/8.js"] = """globalThis.order += "8"; export default null;""",
            ["/mods/9.js"] = """globalThis.order += "9"; export const x = 9;""",
            ["/mods/main.js"] = """
                                import {} from "./1.js";
                                import "./2.js";
                                import * as ns1 from "./3.js";
                                import dflt1 from "./4.js";
                                export {} from "./5.js";
                                import dflt2, {} from "./6.js";
                                export * from "./7.js";
                                import dflt3, * as ns2 from "./8.js";
                                export * as ns3 from "./9.js";
                                export default globalThis.order;
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj!.TryGetProperty("default", out var result), Is.True);
        Assert.That(result.AsString(), Is.EqualTo("123456789"));
    }

    [Test]
    public void EvaluateModule_Module_Namespace_Uses_Numeric_Export_Names_For_Element_Access()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/fixture.js"] = """
                                   var a = 0;
                                   var b = 1;
                                   export { a as "0", b as "1" };
                                   """,
            ["/mods/main.js"] = """
                                import * as ns from "./fixture.js";
                                export default [ns[0], Reflect.get(ns, 1), ns[2], 0 in ns, Reflect.has(ns, 1), 2 in ns];
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj!.TryGetProperty("default", out var resultValue), Is.True);
        Assert.That(resultValue.TryGetObject(out var resultObj), Is.True);
        var result = (JsArray)resultObj!;
        Assert.That(result.TryGetElement(0, out var first), Is.True);
        Assert.That(result.TryGetElement(1, out var second), Is.True);
        Assert.That(result.TryGetElement(2, out var third), Is.True);
        Assert.That(result.TryGetElement(3, out var has0), Is.True);
        Assert.That(result.TryGetElement(4, out var has1), Is.True);
        Assert.That(result.TryGetElement(5, out var has2), Is.True);
        Assert.That(first.Int32Value, Is.EqualTo(0));
        Assert.That(second.Int32Value, Is.EqualTo(1));
        Assert.That(third.IsUndefined, Is.True);
        Assert.That(has0.IsTrue, Is.True);
        Assert.That(has1.IsTrue, Is.True);
        Assert.That(has2.IsFalse, Is.True);
    }

    [Test]
    public void EvaluateModule_ImportMissingName_ThrowsDiagnostic()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/dep.js"] = """export const ok = 1;""",
            ["/mods/main.js"] = """
                                import { missing } from "./dep.js";
                                export const y = missing;
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ex = Assert.Throws<JsRuntimeException>(() =>
            _ = engine.MainAgent.EvaluateModule(realm, "/mods/main.js"));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.DetailCode, Is.EqualTo("MODULE_LINK_FAILED"));
        Assert.That(ex.InnerException, Is.TypeOf<JsRuntimeException>());
        Assert.That(((JsRuntimeException)ex.InnerException!).DetailCode, Is.EqualTo("MODULE_IMPORT_NAME_NOT_FOUND"));
        Assert.That(ex.Message, Does.Contain("missing"));
        Assert.That(ex.Message, Does.Contain("/mods/main.js"));
        Assert.That(ex.Message, Does.Contain("/mods/dep.js"));
    }

    [Test]
    public void EvaluateModule_ExportFromMissingName_ThrowsDiagnostic()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/dep.js"] = """export const ok = 1;""",
            ["/mods/main.js"] = """export { missing as y } from "./dep.js";"""
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ex = Assert.Throws<JsRuntimeException>(() =>
            _ = engine.MainAgent.EvaluateModule(realm, "/mods/main.js"));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.DetailCode, Is.EqualTo("MODULE_LINK_FAILED"));
        Assert.That(ex.InnerException, Is.TypeOf<JsRuntimeException>());
        Assert.That(((JsRuntimeException)ex.InnerException!).DetailCode, Is.EqualTo("MODULE_EXPORT_NAME_NOT_FOUND"));
        Assert.That(ex.Message, Does.Contain("missing"));
        Assert.That(ex.Message, Does.Contain("/mods/main.js"));
        Assert.That(ex.Message, Does.Contain("/mods/dep.js"));
    }

    [Test]
    public void EvaluateModule_CircularIndirectExport_ThrowsSyntaxErrorDiagnostic()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/a.js"] = """export { x } from "./b.js";""",
            ["/mods/b.js"] = """export { x } from "./a.js";"""
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ex = Assert.Throws<JsRuntimeException>(() =>
            _ = engine.MainAgent.EvaluateModule(realm, "/mods/a.js"));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.DetailCode, Is.EqualTo("MODULE_LINK_FAILED"));
        Assert.That(ex.InnerException, Is.TypeOf<JsRuntimeException>());
        var inner = (JsRuntimeException)ex.InnerException!;
        Assert.That(inner.DetailCode, Is.EqualTo("MODULE_EXPORT_NAME_NOT_FOUND"));
        Assert.That(inner.Kind, Is.EqualTo(JsErrorKind.SyntaxError));
        Assert.That(ex.Kind, Is.EqualTo(JsErrorKind.SyntaxError));
        Assert.That(ex.Message, Does.Contain("x"));
    }

    [Test]
    public void EvaluateModule_WhenExecutionThrowsNonOkojoException_WrapsAsModuleExecFailed()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """boom(); export const x = 1;"""
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        realm.Global["boom"] = JsValue.FromObject(new JsHostFunction(realm, "boom", 0,
            static (in info) => { throw new InvalidOperationException("boom exploded"); }));

        var ex = Assert.Throws<JsRuntimeException>(() =>
            _ = engine.MainAgent.EvaluateModule(realm, "/mods/main.js"));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.DetailCode, Is.EqualTo("MODULE_EXEC_FAILED"));
        Assert.That(ex.Message, Does.Contain("/mods/main.js"));
        Assert.That(ex.InnerException, Is.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void EvaluateModule_TopLevel_Using_And_AwaitUsing_Dispose_During_Module_Completion()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """
                                export const order = [];
                                await using a = {
                                  async [Symbol.asyncDispose]() {
                                    await 0;
                                    order.push("dispose-a");
                                  }
                                };
                                using b = {
                                  [Symbol.dispose]() {
                                    order.push("dispose-b");
                                  }
                                };
                                order.push("body");
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("order", out var orderValue), Is.True);
        Assert.That(orderValue.TryGetObject(out var orderObj), Is.True);
        Assert.That(orderObj, Is.InstanceOf<JsArray>());
        var order = (JsArray)orderObj!;
        Assert.That(order.TryGetElement(0, out var first), Is.True);
        Assert.That(order.TryGetElement(1, out var second), Is.True);
        Assert.That(order.TryGetElement(2, out var third), Is.True);
        Assert.That(first.AsString(), Is.EqualTo("body"));
        Assert.That(second.AsString(), Is.EqualTo("dispose-b"));
        Assert.That(third.AsString(), Is.EqualTo("dispose-a"));
    }

    private sealed class ThrowingResolveModuleLoader : IModuleSourceLoader
    {
        public string ResolveSpecifier(string specifier, string? referrer)
        {
            throw new InvalidOperationException("resolve exploded");
        }

        public string LoadSource(string resolvedId)
        {
            throw new InvalidOperationException("not reached");
        }
    }

    private sealed class ThrowingLoadModuleLoader : IModuleSourceLoader
    {
        public string ResolveSpecifier(string specifier, string? referrer)
        {
            return "resolved:" + specifier;
        }

        public string LoadSource(string resolvedId)
        {
            throw new InvalidOperationException("load exploded");
        }
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
