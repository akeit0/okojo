using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ObjectLiteralTests
{
    [Test]
    public void ObjectLiteral_ComputedMethodKey_ToString_CSharpBreakdown()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   globalThis.counter = 0;
                   var key1 = {
                     toString: function() {
                       globalThis.counter = globalThis.counter + 1;
                       return 'b';
                     }
                   };
                   var key2 = {
                     toString: function() {
                       globalThis.counter = globalThis.counter + 1;
                       return 'd';
                     }
                   };
                   globalThis.object = {
                     a() { return 'A'; },
                     [key1]() { return 'B'; },
                     c() { return 'C'; },
                     [key2]() { return 'D'; },
                   };
                   """);

        Assert.That(realm.Eval("counter === 2").IsTrue, Is.True);
        Assert.That(realm.Eval("typeof object.a === 'function'").IsTrue, Is.True);
        Assert.That(realm.Eval("typeof object.b === 'function'").IsTrue, Is.True);
        Assert.That(realm.Eval("typeof object.c === 'function'").IsTrue, Is.True);
        Assert.That(realm.Eval("typeof object.d === 'function'").IsTrue, Is.True);
        Assert.That(realm.Eval("object.a() === 'A'").IsTrue, Is.True);
        Assert.That(realm.Eval("object.b() === 'B'").IsTrue, Is.True);
        Assert.That(realm.Eval("object.c() === 'C'").IsTrue, Is.True);
        Assert.That(realm.Eval("object.d() === 'D'").IsTrue, Is.True);
        Assert.That(realm.Eval("Object.getOwnPropertyNames(object).join(',')").AsString(), Is.EqualTo("a,b,c,d"));
    }

    [Test]
    public void ObjectLiteral_ComputedMethodKey_ToString_SideEffectCount_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var counter = 0;
                                                                   var key = {
                                                                     toString: function() {
                                                                       counter += 1;
                                                                       return 'b';
                                                                     }
                                                                   };
                                                                   var object = {
                                                                     [key]() { return 'B'; }
                                                                   };
                                                                   counter === 1;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ObjectLiteral_ComputedMethodKey_ToString_DefinesCallableProperty_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var key = {
                                                                     toString: function() {
                                                                       return 'b';
                                                                     }
                                                                   };
                                                                   var object = {
                                                                     [key]() { return 'B'; }
                                                                   };
                                                                   typeof object.b === 'function' && object.b() === 'B';
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ObjectLiteral_ComputedMethodKey_ToString_OwnPropertyNamesOrder_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var key = {
                                                                     toString: function() {
                                                                       return 'b';
                                                                     }
                                                                   };
                                                                   var object = {
                                                                     a() { return 'A'; },
                                                                     [key]() { return 'B'; },
                                                                     c() { return 'C'; }
                                                                   };
                                                                   Object.getOwnPropertyNames(object).join(",");
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("a,b,c"));
    }

    [Test]
    public void ObjectLiteral_ComputedGetterKey_ToString_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var key = {
                                                                     toString: function() {
                                                                       return 'b';
                                                                     }
                                                                   };
                                                                   var object = {
                                                                     get [key]() { return 'B'; }
                                                                   };
                                                                   object.b === 'B' &&
                                                                   Object.getOwnPropertyDescriptor(object, 'b').enumerable === true;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ObjectLiteral_ComputedSetterKey_ToString_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var out = 0;
                                                                   var key = {
                                                                     toString: function() {
                                                                       return 'b';
                                                                     }
                                                                   };
                                                                   var object = {
                                                                     set [key](v) { out = v; }
                                                                   };
                                                                   object.b = 7;
                                                                   out === 7 &&
                                                                   Object.getOwnPropertyDescriptor(object, 'b').enumerable === true;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ObjectLiteral_ComputedMethodKey_ToString_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var counter = 0;
                                                                   var key1 = {
                                                                     toString: function() {
                                                                       counter += 1;
                                                                       return 'b';
                                                                     }
                                                                   };
                                                                   var key2 = {
                                                                     toString: function() {
                                                                       counter += 1;
                                                                       return 'd';
                                                                     }
                                                                   };
                                                                   var object = {
                                                                     a() { return 'A'; },
                                                                     [key1]() { return 'B'; },
                                                                     c() { return 'C'; },
                                                                     [key2]() { return 'D'; },
                                                                   };
                                                                   counter === 2 &&
                                                                   object.a() === 'A' &&
                                                                   object.b() === 'B' &&
                                                                   object.c() === 'C' &&
                                                                   object.d() === 'D' &&
                                                                   Object.getOwnPropertyNames(object).join(",") === "a,b,c,d";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ObjectLiteral_ComputedDataKey_ValueOfNumber_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   globalThis.counter = 0;
                   var key1 = {
                     valueOf: function() {
                       globalThis.counter = globalThis.counter + 1;
                       return 1;
                     },
                     toString: null
                   };
                   var key2 = {
                     valueOf: function() {
                       globalThis.counter = globalThis.counter + 1;
                       return 2;
                     },
                     toString: null
                   };
                   globalThis.object = {
                     a: 'A',
                     [key1]: 'B',
                     c: 'C',
                     [key2]: 'D',
                   };
                   """);

        Assert.That(realm.Eval("counter === 2").IsTrue, Is.True);
        Assert.That(realm.Eval("object.a === 'A'").IsTrue, Is.True);
        Assert.That(realm.Eval("object[1] === 'B'").IsTrue, Is.True);
        Assert.That(realm.Eval("object.c === 'C'").IsTrue, Is.True);
        Assert.That(realm.Eval("object[2] === 'D'").IsTrue, Is.True);
        Assert.That(realm.Eval("Object.getOwnPropertyNames(object).join(',')").AsString(), Is.EqualTo("1,2,a,c"));
    }

    [Test]
    public void ObjectLiteral_Spread_Copies_Enumerable_Own_Properties()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var source = { a: 1, b: 2 };
                                var target = { z: 0, ...source };
                                target.z === 0 &&
                                target.a === 1 &&
                                target.b === 2 &&
                                Object.getOwnPropertyNames(target).join(",") === "z,a,b";
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectLiteral_Spread_Overrides_Previous_Data_And_Accessor_Properties()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var setterHits = 0;
                                var target = {
                                  set a(v) { setterHits++; },
                                  ...{ a: 2 }
                                };
                                var d = Object.getOwnPropertyDescriptor(target, "a");
                                setterHits === 0 &&
                                d.value === 2 &&
                                d.writable === true &&
                                d.enumerable === true &&
                                d.configurable === true;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectLiteral_Spread_Null_And_Undefined_Are_Ignored()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var target = { a: 1, ...null, ...undefined, b: 2 };
                                target.a === 1 &&
                                target.b === 2 &&
                                Object.getOwnPropertyNames(target).join(",") === "a,b";
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectLiteral_Spread_Unresolvable_Reference_Throws_ReferenceError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var ok = false;
                                try {
                                  [{ a: 0, ...unresolvableReference }];
                                } catch (e) {
                                  ok = e && e.name === "ReferenceError";
                                }
                                ok;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Arrow_ObjectBindingParameter_With_BlockBody_Binds_Names()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var f = ({ done, value }) => {
                                  return done === false && value === 42;
                                };
                                f({ done: false, value: 42 });
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectLiteral_Generator_Method_Parameter_Initializer_Can_Use_Super_Property_Access()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var obj = {
                                                                     *foo(a = super.toString) {
                                                                       return a;
                                                                     }
                                                                   };

                                                                   obj.toString = null;
                                                                   obj.foo().next().value === Object.prototype.toString;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ObjectLiteral_Async_Method_Parameter_Initializer_Can_Use_Super_Property_Access()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.done = false;
                                                                   globalThis.out = "";

                                                                   var sup = {
                                                                     method() {
                                                                       return "sup";
                                                                     }
                                                                   };

                                                                   var child = {
                                                                     async method(x = super.method()) {
                                                                       globalThis.out = await x;
                                                                     }
                                                                   };

                                                                   Object.setPrototypeOf(child, sup);

                                                                   child.method().then(function () {
                                                                     globalThis.done = true;
                                                                   }, function (e) {
                                                                     globalThis.out = "err:" + e.name;
                                                                     globalThis.done = true;
                                                                   });
                                                                   0;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Global["done"].IsTrue, Is.True);
        Assert.That(realm.Global["out"].AsString(), Is.EqualTo("sup"));
    }

    [Test]
    public void ObjectLiteral_Methods_Assign_Name_From_Property_Key()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var namedSym = Symbol('test262');
                                var anonSym = Symbol();
                                var o = {
                                  id() {},
                                  [anonSym]() {},
                                  [namedSym]() {}
                                };
                                var idDesc = Object.getOwnPropertyDescriptor(o.id, "name");
                                var anonDesc = Object.getOwnPropertyDescriptor(o[anonSym], "name");
                                var namedDesc = Object.getOwnPropertyDescriptor(o[namedSym], "name");
                                [
                                  idDesc.value, idDesc.writable, idDesc.enumerable, idDesc.configurable,
                                  anonDesc.value, anonDesc.writable, anonDesc.enumerable, anonDesc.configurable,
                                  namedDesc.value, namedDesc.writable, namedDesc.enumerable, namedDesc.configurable
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("id|false|false|true||false|false|true|[test262]|false|false|true"));
    }

    [Test]
    public void ObjectLiteral_Accessors_Assign_Name_From_Property_Key()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var namedSym = Symbol('test262');
                                var anonSym = Symbol();
                                var o = {
                                  get id() {},
                                  set value(v) {},
                                  get [anonSym]() {},
                                  set [namedSym](v) {}
                                };
                                var idDesc = Object.getOwnPropertyDescriptor(Object.getOwnPropertyDescriptor(o, "id").get, "name");
                                var valueDesc = Object.getOwnPropertyDescriptor(Object.getOwnPropertyDescriptor(o, "value").set, "name");
                                var anonDesc = Object.getOwnPropertyDescriptor(Object.getOwnPropertyDescriptor(o, anonSym).get, "name");
                                var namedDesc = Object.getOwnPropertyDescriptor(Object.getOwnPropertyDescriptor(o, namedSym).set, "name");
                                [
                                  idDesc.value, idDesc.writable, idDesc.enumerable, idDesc.configurable,
                                  valueDesc.value, valueDesc.writable, valueDesc.enumerable, valueDesc.configurable,
                                  anonDesc.value, anonDesc.writable, anonDesc.enumerable, anonDesc.configurable,
                                  namedDesc.value, namedDesc.writable, namedDesc.enumerable, namedDesc.configurable
                                ].join("|");
                                """);

        Assert.That(result.AsString(),
            Is.EqualTo(
                "get id|false|false|true|set value|false|false|true|get |false|false|true|set [test262]|false|false|true"));
    }

    [Test]
    public void ObjectLiteral_Computed_Numeric_Accessors_Use_JavaScript_Property_Key_String()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var stringSet;
                                var obj = {
                                  get [0.0000001]() { return 'get string'; },
                                  set [0.0000001](param) { stringSet = param; }
                                };
                                var desc = Object.getOwnPropertyDescriptor(obj, "1e-7");
                                [
                                  obj["1e-7"],
                                  desc.get.name,
                                  (obj["1e-7"] = "set string", stringSet)
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("get string|get 1e-7|set string"));
    }

    [Test]
    public void Literal_BigInt_Property_Names_Stringify_Like_JavaScript()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                let o = { 999999999999999999n: true };
                                o = {
                                  ...o,
                                  1n() { return "bar"; }
                                };
                                class C {
                                  1n() { return "baz"; }
                                }
                                let { 1n: a } = { "1": "foo" };
                                [
                                  o["999999999999999999"],
                                  o["1"](),
                                  new C()["1"](),
                                  a
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("true|bar|baz|foo"));
    }

    [Test]
    public void ObjectLiteral_Generator_Method_Defines_Prototype_Property()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var GeneratorPrototype = Object.getPrototypeOf(function* () {}).prototype;
                                var method = { *method() {} }.method;
                                var desc = Object.getOwnPropertyDescriptor(method, "prototype");
                                Object.getPrototypeOf(method.prototype) === GeneratorPrototype &&
                                desc.writable === true &&
                                desc.enumerable === false &&
                                desc.configurable === false;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectLiteral_DuplicateSetter_Definitions_Are_Allowed()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var obj = {
                                  set foo(arg) {},
                                  set foo(arg1) {}
                                };
                                typeof Object.getOwnPropertyDescriptor(obj, "foo").set === "function";
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectLiteral_Getter_Then_Data_With_Same_Name_Is_Allowed()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var obj = {
                                  get foo() { return 1; },
                                  foo: 2
                                };
                                obj.foo === 2;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectLiteral_Setter_Then_Data_With_Same_Name_Is_Allowed()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var obj = {
                                  set foo(x) {},
                                  foo: 1
                                };
                                obj.foo === 1;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectLiteral_Data_Then_Setter_With_Same_Name_Is_Allowed()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var obj = {
                                  foo: 1,
                                  set foo(x) {}
                                };
                                typeof Object.getOwnPropertyDescriptor(obj, "foo").set === "function";
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectLiteral_Computed_Data_Key_Normalizes_Before_Value_Evaluation()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var value = "bad";
                                var key = {
                                  toString() {
                                    value = "ok";
                                    return "p";
                                  }
                                };
                                var obj = {
                                  [key]: value
                                };
                                obj.p === "ok";
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectLiteral_Super_Null_Prototype_Throws_TypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var caught;
                                var obj = {
                                  method() {
                                    try {
                                      super['x'];
                                    } catch (err) {
                                      caught = err;
                                    }
                                  }
                                };
                                Object.setPrototypeOf(obj, null);
                                obj.method();
                                typeof caught === 'object' && caught.constructor === TypeError;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectLiteral_Super_Computed_Set_Uses_Super_Base_Before_ToPropertyKey()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var result;
                                var proto = {
                                  set p(v) {
                                    result = "ok";
                                  },
                                };
                                var proto2 = {
                                  set p(v) {
                                    result = "bad";
                                  },
                                };
                                var obj = {
                                  m() {
                                    super[key] = 10;
                                  }
                                };
                                Object.setPrototypeOf(obj, proto);
                                var key = {
                                  toString() {
                                    Object.setPrototypeOf(obj, proto2);
                                    return "p";
                                  }
                                };
                                obj.m();
                                result === "ok";
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectLiteral_Super_Computed_Compound_Assignment_Uses_Super_Base_Before_ToPropertyKey()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var proto = { p: 1 };
                                var proto2 = { p: -1 };
                                var obj = {
                                  m() {
                                    return super[key] += 1;
                                  }
                                };
                                Object.setPrototypeOf(obj, proto);
                                var key = {
                                  toString() {
                                    Object.setPrototypeOf(obj, proto2);
                                    return "p";
                                  }
                                };
                                obj.m() === 2;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectLiteral_Super_Set_Is_Non_Strict_Reference()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var obj = {
                                  method() {
                                    super.x = 8;
                                    Object.freeze(obj);
                                    super.y = 9;
                                  }
                                };
                                obj.method();
                                Object.prototype.hasOwnProperty.call(obj, 'x') &&
                                !Object.prototype.hasOwnProperty.call(obj, 'y');
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ClassMethod_Super_Set_Is_Strict_Reference()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var caught;
                                class Derived {
                                  method() {
                                    super.x = 8;
                                    Object.freeze(this);
                                    try {
                                      super.y = 9;
                                    } catch (err) {
                                      caught = err;
                                    }
                                  }
                                }
                                new Derived().method();
                                typeof caught === 'object' && caught.constructor === TypeError;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }
}
