using System.Collections.Concurrent;
using System.Text;
using Okojo.Bytecode;
using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ClassTests
{
    [Test]
    public void Class_Static_Private_Generator_Methods_Are_Stable_Across_Parallel_Engines()
    {
        const string source = """
                              var C = class {
                                static * #$(value) { yield * value; }
                                static * #_(value) { yield * value; }
                                static * #o(value) { yield * value; }
                                static * #℘(value) { yield * value; }
                                static * #ZW_‌_NJ(value) { yield * value; }
                                static * #ZW_‍_J(value) { yield * value; }
                                static get $() { return this.#$; }
                                static get _() { return this.#_; }
                                static get o() { return this.#o; }
                                static get ℘() { return this.#℘; }
                                static get ZW_‌_NJ() { return this.#ZW_‌_NJ; }
                                static get ZW_‍_J() { return this.#ZW_‍_J; }
                              };

                              [
                                C.$([1]).next().value,
                                C._([1]).next().value,
                                C.o([1]).next().value,
                                C.℘([1]).next().value,
                                C.ZW_‌_NJ([1]).next().value,
                                C.ZW_‍_J([1]).next().value
                              ].join(",");
                              """;

        var failures = new ConcurrentQueue<string>();
        Parallel.For(0, 256, i =>
        {
            try
            {
                var realm = JsRuntime.Create().DefaultRealm;
                var compiler = new JsCompiler(realm);
                var script = compiler.Compile(JavaScriptParser.ParseScript(source));
                realm.Execute(script);
                var result = realm.Accumulator.AsString();
                if (result != "1,1,1,1,1,1")
                    failures.Enqueue($"iter={i} result={result}");
            }
            catch (Exception ex)
            {
                failures.Enqueue($"iter={i} ex={ex.GetType().Name}: {ex.Message}");
            }
        });

        Assert.That(failures, Is.Empty, failures.TryPeek(out var firstFailure) ? firstFailure : string.Empty);
    }

    [Test]
    public void DecoratedClassExpression_ParenthesizedIdentifierReference_Parses_And_Compiles()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function $() {}
                                                                   var C = @($) class {};
                                                                   typeof C === "function";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void DecoratedClassDeclaration_IdentifierReference_Parses_And_Compiles()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function dec() {}
                                                                   @dec class C {}
                                                                   C.name === "C";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Class_Static_Generator_Named_Constructor_Is_Allowed()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var C = class {
                                                                     static * constructor() { yield 1; }
                                                                     constructor() {}
                                                                   };

                                                                   typeof C.constructor === "function" && typeof C.prototype.constructor === "function";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Class_Field_And_Method_String_Literal_Names_Parse_On_Same_Line()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var C = class {
                                                                     'a'; "b" = 2; m() { return this["b"]; }
                                                                   };

                                                                   var c = new C();
                                                                   c.a === undefined && c.m() === 2;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassExpression_NameBinding_Is_Immutable_Inside_Methods()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var cls = class C {
                                                                     probe() { return C; }
                                                                     modify() { C = null; }
                                                                   };

                                                                   var out = [];
                                                                   out.push(cls.prototype.probe() === cls);
                                                                   try {
                                                                     cls.prototype.modify();
                                                                     out.push("no-throw");
                                                                   } catch (e) {
                                                                     out.push(e.name);
                                                                   }
                                                                   out.push(cls.prototype.probe() === cls);
                                                                   out.join(",");
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("true,TypeError,true"));
    }

    [Test]
    public void ClassExpression_NameBinding_In_Heritage_Closures_Uses_Inner_Immutable_Binding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var probeBefore = function() { return C; };
                                                                   var probeHeritage, setHeritage;
                                                                   var C = "outside";

                                                                   var cls = class C extends (
                                                                       probeHeritage = function() { return C; },
                                                                       setHeritage = function() { C = null; }
                                                                     ) {
                                                                     method() {
                                                                       return C;
                                                                     }
                                                                   };

                                                                   var out = [];
                                                                   out.push(probeBefore());
                                                                   out.push(probeHeritage() === cls);
                                                                   try {
                                                                     setHeritage();
                                                                     out.push("no-throw");
                                                                   } catch (e) {
                                                                     out.push(e.name);
                                                                   }
                                                                   out.push(probeHeritage() === cls);
                                                                   out.push(cls.prototype.method() === cls);
                                                                   out.join(",");
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("outside,true,TypeError,true,true"));
    }

    [Test]
    public void ClassDeclaration_ConstructorAndMethod_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class C {
                                                                     constructor(x) { this.x = x; }
                                                                     m() { return this.x; }
                                                                   }
                                                                   new C(2).m() === 2;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassDeclaration_Static_Field_Initializer_Can_Read_Class_Name_Binding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class C {
                                                                     static self = C;
                                                                   }
                                                                   C.self === C;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassExpression_Assignment_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var D = class {
                                                                     m() { return 7; }
                                                                   };
                                                                   new D().m() === 7;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassStaticBlock_Executes_And_FunctionExpressions_Treat_Await_As_Identifier()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var await = 0;
                                                                   var fromParam, fromBody;

                                                                   class C {
                                                                     static {
                                                                       (function (x = fromParam = await) {
                                                                         fromBody = await;
                                                                       })();
                                                                     }
                                                                   }

                                                                   fromParam === 0 && fromBody === 0;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassStaticBlock_Allows_FunctionExpression_Await_Binding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class C {
                                                                     static {
                                                                       (function await(await) {});
                                                                     }
                                                                   }
                                                                   true;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassStaticBlock_Can_Read_Super_Property()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function Parent() {}
                                                                   Parent.test262 = "test262";
                                                                   var value;

                                                                   class C extends Parent {
                                                                     static {
                                                                       value = super.test262;
                                                                     }
                                                                   }

                                                                   value;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("test262"));
    }

    [Test]
    public void ClassStaticBlock_Nested_Methods_Keep_Their_Own_Arguments_Binding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var instance;
                                                                   var method, methodParam;
                                                                   var getter;
                                                                   var setter, setterParam;
                                                                   var genMethod, genMethodParam;
                                                                   var asyncMethod, asyncMethodParam;

                                                                   class C {
                                                                     static {
                                                                       instance = new class {
                                                                         method({test262 = methodParam = arguments}) {
                                                                           method = arguments;
                                                                         }
                                                                         get accessor() {
                                                                           getter = arguments;
                                                                         }
                                                                         set accessor({test262 = setterParam = arguments}) {
                                                                           setter = arguments;
                                                                         }
                                                                         *gen({test262 = genMethodParam = arguments}) {
                                                                           genMethod = arguments;
                                                                         }
                                                                         async async({test262 = asyncMethodParam = arguments}) {
                                                                           asyncMethod = arguments;
                                                                         }
                                                                       }();
                                                                     }
                                                                   }

                                                                   instance.method("method");
                                                                   instance.accessor;
                                                                   instance.accessor = "setter";
                                                                   instance.gen("generator method").next();
                                                                   instance.async("async method");

                                                                   [
                                                                     !!method && method.length === 1 && method[0] === "method",
                                                                     !!methodParam && methodParam.length === 1 && methodParam[0] === "method",
                                                                     !!getter && getter.length === 0,
                                                                     !!setter && setter.length === 1 && setter[0] === "setter",
                                                                     !!setterParam && setterParam.length === 1 && setterParam[0] === "setter",
                                                                     !!genMethod && genMethod.length === 1 && genMethod[0] === "generator method",
                                                                     !!genMethodParam && genMethodParam.length === 1 && genMethodParam[0] === "generator method",
                                                                     !!asyncMethod && asyncMethod.length === 1 && asyncMethod[0] === "async method",
                                                                     !!asyncMethodParam && asyncMethodParam.length === 1 && asyncMethodParam[0] === "async method"
                                                                   ];
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.TryGetObject(out var resultObj), Is.True);
        Assert.That(resultObj, Is.Not.Null);
        for (var i = 0; i < 9; i++)
        {
            Assert.That(resultObj!.TryGetProperty(i.ToString(), out var value), Is.True);
            Assert.That(value.IsTrue, Is.True, $"static block nested method arguments check #{i} failed");
        }
    }

    [Test]
    public void ClassConstructor_CallWithoutNew_ThrowsTypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class E {}
                                                                   try { E(); false; }
                                                                   catch (e) { e instanceof TypeError; }
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassMethod_IsNonEnumerable()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class C { m() {} }
                                                                   Object.prototype.propertyIsEnumerable.call(C.prototype, "m") === false;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassPrivateField_InitializerAndRead_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class C {
                                                                     #x = 1;
                                                                     get() { return this.#x; }
                                                                   }
                                                                   new C().get() === 1;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassPrivateField_AssignmentInConstructor_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class C {
                                                                     #x = 0;
                                                                     constructor(v) { this.#x = v; }
                                                                     get() { return this.#x; }
                                                                   }
                                                                   new C(7).get() === 7;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassPrivateField_SlotIndex_Above_ByteMaxValue_Works()
    {
        const int privateFieldCount = 300;
        var sourceBuilder = new StringBuilder();
        sourceBuilder.AppendLine("class C {");
        for (var i = 0; i < privateFieldCount; i++)
            sourceBuilder.Append("  #f").Append(i).Append(" = ").Append(i).AppendLine(";");
        sourceBuilder.Append("  read() { return this.#f").Append(privateFieldCount - 1).AppendLine("; }");
        sourceBuilder.Append("  write(v) { this.#f").Append(privateFieldCount - 1)
            .Append(" = v; return this.#f").Append(privateFieldCount - 1).AppendLine(" === v; }");
        sourceBuilder.AppendLine("}");
        sourceBuilder.AppendLine("var c = new C();");
        sourceBuilder.AppendLine("c.read() === 299 && c.write(777);");

        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript(sourceBuilder.ToString()));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassPrivateField_BrandCheck_ThrowsOnForeignReceiver()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class A { #x = 1; get() { return this.#x; } }
                                                                   class B { #x = 2; }
                                                                   var g = A.prototype.get;
                                                                   g.call(new B());
                                                                   """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.TypeError));
        Assert.That(ex.DetailCode, Is.EqualTo("PRIVATE_FIELD_BRAND"));
        Assert.That(ex.Message, Does.Contain("private member #x"));
    }

    [Test]
    public void ClassPrivateField_ClassLocalNames_AreIsolated()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class A { #x = 1; get() { return this.#x; } }
                                                                   class B { #x = 2; get() { return this.#x; } }
                                                                   new A().get() === 1 && new B().get() === 2;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassPublicInstanceField_InitializerAndRead_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class C {
                                                                     x = 1;
                                                                     get() { return this.x; }
                                                                   }
                                                                   new C().get() === 1;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassPublicInstanceField_DerivedConstructor_RunsAfterSuper()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class A {
                                                                     constructor() { this.base = 2; }
                                                                   }
                                                                   class B extends A {
                                                                     x = this.base + 1;
                                                                     get() { return this.x; }
                                                                   }
                                                                   new B().get() === 3;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassPublicInstanceField_ComputedName_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var key = "xy";
                                                                   class C {
                                                                     [key] = 4;
                                                                   }
                                                                   var o = new C();
                                                                   o.xy === 4;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassPublicInstanceField_CapturesOuterBinding_InComputedKey()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function make() {
                                                                     let x = "p";
                                                                     return class {
                                                                       [x] = 1;
                                                                       get() { return this.p; }
                                                                     };
                                                                   }
                                                                   new (make())().get() === 1;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassPublicInstanceField_CapturesTopLevelLexical_InComputedKey()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x = 1;
                                                                   let C = class {
                                                                     [x] = "v";
                                                                   };
                                                                   new C()[1] === "v";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassPublicInstanceField_WithStaticComputedField_CapturesTopLevelLexical()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x = 1;
                                                                   let C = class {
                                                                     [x] = "v";
                                                                     static [x] = "v";
                                                                   };
                                                                   let c = new C();
                                                                   c[x] === "v" && C[x] === "v";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassPublicInstanceField_ComputedKey_Indexing_Ignores_Preceding_NonComputed_Fields()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var computed = "h";
                                                                   var C = class {
                                                                     f = "test262";
                                                                     "g";
                                                                     0 = "bar";
                                                                     [computed];
                                                                   };

                                                                   var c = new C();
                                                                   c.f === "test262" && c.g === undefined && c[0] === "bar" && c.h === undefined;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassPublicInstanceField_WithStaticComputedField_CapturesUninitializedTopLevelLexical()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x;
                                                                   let C = class {
                                                                     [x ?? 1] = 2;
                                                                     static [x ?? 1] = 2;
                                                                   };
                                                                   let c = new C();
                                                                   c[x ?? 1] === 2 && C[x ?? 1] === 2;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassPublicInstanceField_ComputedName_AssignmentExpression_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x = 0;
                                                                   let C = class {
                                                                     [x = 1] = 2;
                                                                     static [x = 1] = 2;
                                                                   };
                                                                   let c = new C();
                                                                   c[x = 1] === 2 &&
                                                                   C[x = 1] === 2 &&
                                                                   c[String(x = 1)] === 2 &&
                                                                   C[String(x = 1)] === 2 &&
                                                                   x === 1;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassPublicInstanceField_ComputedName_Coalesce_StrictScript_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   'use strict';
                                                                   let x;
                                                                   let C = class {
                                                                     [x ?? 1] = 2;
                                                                     static [x ?? 1] = 2;
                                                                   };
                                                                   let c = new C();
                                                                   c[x ?? 1] === 2 &&
                                                                   C[x ?? 1] === 2 &&
                                                                   c[String(x ?? 1)] === 2 &&
                                                                   C[String(x ?? 1)] === 2 &&
                                                                   x === undefined;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassPublicInstanceField_ComputedName_Coalesce_WithTopLevelLetWithoutInitializer_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x;
                                                                   let C = class {
                                                                     [x ?? 1] = 2;
                                                                     static [x ?? 1] = 2;
                                                                   };
                                                                   let c = new C();
                                                                   c[x ?? 1] === 2 &&
                                                                   C[x ?? 1] === 2 &&
                                                                   x === undefined;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassGeneratorMethod_ParameterNestedEmptyArrayDefault_Accepts_GeneratorObject()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var initCount = 0;
                                                                   var iterCount = 0;
                                                                   var iter = function*() { iterCount += 1; }();
                                                                   var callCount = 0;

                                                                   var C = class {
                                                                     *method([[] = function() { initCount += 1; return iter; }()]) {
                                                                       callCount += 1;
                                                                       return initCount === 1 && iterCount === 0;
                                                                     }
                                                                   };

                                                                   var r = new C().method([]).next();
                                                                   callCount === 1 && r.value === true;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassGeneratorMethod_ParameterNestedElisionArrayDefault_Accepts_GeneratorObject()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var first = 0;
                                                                   var second = 0;
                                                                   function* g() {
                                                                     first += 1;
                                                                     yield;
                                                                     second += 1;
                                                                   }

                                                                   var callCount = 0;
                                                                   var C = class {
                                                                     *method([[,] = g()]) {
                                                                       callCount += 1;
                                                                       return first === 1 && second === 0;
                                                                     }
                                                                   };

                                                                   var r = new C().method([]).next();
                                                                   callCount === 1 && r.value === true;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassPrivateGeneratorMethod_Is_Reused_Across_Instances_In_One_Class_Evaluation()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var C = class {
                                                                     * #m() { return 42; }
                                                                     get ref() { return this.#m; }
                                                                   };

                                                                   var a = new C();
                                                                   var b = new C();
                                                                   a.ref === b.ref && a.ref().next().value === 42;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassPrivateMethod_Is_Initialized_Before_Field_Initializers_Run()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var C = class {
                                                                     a = this.#m();
                                                                     #m() { return 42; }
                                                                     #b = this.#m();
                                                                     get b() { return this.#b; }
                                                                   };

                                                                   var c = new C();
                                                                   c.a === 42 && c.b === 42;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void NestedClass_Can_Access_Outer_Private_Method()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var C = class {
                                                                     #m() { return "test262"; }
                                                                     B = class {
                                                                       method(o) { return o.#m(); }
                                                                     }
                                                                   };

                                                                   var c = new C();
                                                                   var inner = new c.B();
                                                                   inner.method(c) === "test262";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void NestedClass_Can_Access_Outer_Static_Private_Method_Simple()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var C = class {
                                                                     static #m() { return "outer class"; }
                                                                     static B = class {
                                                                       static methodAccess(o) { return o.#m(); }
                                                                     }
                                                                   };

                                                                   var ok = C.B.methodAccess(C) === "outer class";
                                                                   try { C.B.methodAccess(C.B); ok = false; } catch (e) { ok = ok && e instanceof TypeError; }
                                                                   ok;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void NestedClass_Can_Access_Outer_Static_Private_Method_From_Exact_Test262_Fixture()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var repoRoot = TestContext.CurrentContext.TestDirectory;
        while (!File.Exists(Path.Combine(repoRoot, "Okojo.slnx")))
            repoRoot = Directory.GetParent(repoRoot)!.FullName;

        var sourcePath = Path.Combine(repoRoot, "test262", "test", "language", "expressions", "class", "elements",
            "private-static-method-usage-inside-nested-class.js");
        var source = """
                     var assert = {
                       sameValue(actual, expected) {
                         if (actual !== expected) throw new Error("sameValue");
                       },
                       throws(ctor, fn) {
                         var threw = false;
                         try { fn(); } catch (e) { threw = e instanceof ctor; }
                         if (!threw) throw new Error("throws");
                       }
                     };
                     """ + Environment.NewLine + File.ReadAllText(sourcePath);
        var program = JavaScriptParser.ParseScript(source, sourcePath);
        var script = compiler.Compile(program);

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsUndefined, Is.True);
    }

    [Test]
    public void StaticAndInstancePrivateMethod_BrandCheck_From_Exact_Test262_Fixture()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var repoRoot = TestContext.CurrentContext.TestDirectory;
        while (!File.Exists(Path.Combine(repoRoot, "Okojo.slnx")))
            repoRoot = Directory.GetParent(repoRoot)!.FullName;

        var sourcePath = Path.Combine(repoRoot, "test262", "test", "language", "expressions", "class", "elements",
            "static-private-method-and-instance-method-brand-check.js");
        var source = """
                     var assert = {
                       sameValue(actual, expected) {
                         if (actual !== expected) throw new Error("sameValue");
                       },
                       throws(ctor, fn) {
                         var threw = false;
                         try { fn(); } catch (e) { threw = e instanceof ctor; }
                         if (!threw) throw new Error("throws");
                       }
                     };
                     """ + Environment.NewLine + File.ReadAllText(sourcePath);
        var program = JavaScriptParser.ParseScript(source, sourcePath);
        var script = compiler.Compile(program);

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsUndefined, Is.True);
    }

    [Test]
    public void CrossRealm_PrivateBrandMismatch_Uses_DefiningRealm_TypeError()
    {
        var engine = JsRuntime.Create();
        var hostRealm = engine.DefaultRealm;
        var realmA = hostRealm.Agent.CreateRealm();
        var realmB = hostRealm.Agent.CreateRealm();

        hostRealm.Global["RealmATypeError"] = JsValue.FromObject(realmA.TypeErrorConstructor);
        hostRealm.Global["RealmBTypeError"] = JsValue.FromObject(realmB.TypeErrorConstructor);
        hostRealm.Global["C1"] = realmA.Eval("""
                                             class C {
                                               static #x = 1;
                                               static access(other) { return other.#x; }
                                             }
                                             C;
                                             """);
        hostRealm.Global["C2"] = realmB.Eval("""
                                             class C {
                                               static #x = 1;
                                             }
                                             C;
                                             """);

        var result = hostRealm.Eval("""
                                    var error;
                                    try {
                                      C1.access(C2);
                                    } catch (e) {
                                      error = e;
                                    }
                                    error.constructor === RealmATypeError &&
                                    error.constructor !== RealmBTypeError;
                                    """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ClassStaticPrivateMethod_Is_Accessible_From_Inner_Arrow_Function()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var C = class {
                                                                     static #f() { return 42; }
                                                                     static g() {
                                                                       var arrow = () => this.#f();
                                                                       return arrow();
                                                                     }
                                                                   };

                                                                   C.g() === 42;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassPrivateGetter_Is_Accessible_From_Inner_Function_Declaration()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var C = class {
                                                                     get #m() { return "Test262"; }
                                                                     method() {
                                                                       var self = this;
                                                                       function innerFunction() {
                                                                         return self.#m;
                                                                       }
                                                                       return innerFunction();
                                                                     }
                                                                   };

                                                                   new C().method() === "Test262";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassStaticPrivateGetter_Is_Accessible_From_Inner_Function_Declaration()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var C = class {
                                                                     static get #f() { return "Test262"; }
                                                                     static access() {
                                                                       var self = this;
                                                                       function innerFunction() {
                                                                         return self.#f;
                                                                       }
                                                                       return innerFunction();
                                                                     }
                                                                   };

                                                                   C.access() === "Test262";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void NestedClass_Can_Access_Outer_Static_Private_Method()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var C = class {
                                                                     static #m() { return "outer"; }
                                                                     static B = class {
                                                                       static methodAccess(o) { return o.#m(); }
                                                                     }
                                                                   };

                                                                   C.B.methodAccess(C) === "outer";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void DerivedClass_This_Binds_Before_PublicFieldInitializers_Run()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var probeCtorThis;
                                                                   var thisDuringField;
                                                                   var thisFromProbe;
                                                                   var thisDuringCtor;
                                                                   var baseObservedPreSuperThrow;
                                                                   var ctorObservedPreSuperThrow;

                                                                   class Base {
                                                                     constructor() {
                                                                       try {
                                                                         probeCtorThis();
                                                                         baseObservedPreSuperThrow = false;
                                                                       } catch (e) {
                                                                         baseObservedPreSuperThrow = e instanceof ReferenceError;
                                                                       }
                                                                     }
                                                                   }

                                                                   var C = class extends Base {
                                                                     field = (thisDuringField = this, thisFromProbe = probeCtorThis());
                                                                     constructor() {
                                                                       probeCtorThis = () => this;
                                                                       try {
                                                                         probeCtorThis();
                                                                         ctorObservedPreSuperThrow = false;
                                                                       } catch (e) {
                                                                         ctorObservedPreSuperThrow = e instanceof ReferenceError;
                                                                       }
                                                                       super();
                                                                       thisDuringCtor = this;
                                                                     }
                                                                   };

                                                                   var instance = new C();

                                                                   baseObservedPreSuperThrow === true &&
                                                                   ctorObservedPreSuperThrow === true &&
                                                                   thisDuringField === instance &&
                                                                   thisFromProbe === instance &&
                                                                   thisDuringCtor === instance;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassMethod_EscapedStaticIdentifierName_IsNotStaticModifier()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var C = class {
                                                                     st\u0061tic() { return 42; }
                                                                   };

                                                                   var obj = new C();
                                                                   obj["static"]() === 42;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassAccessor_StringUnicodeEscape_Name_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var stringSet;

                                                                   var C = class {
                                                                     get 'unicod\u{000065}Escape'() { return 'get string'; }
                                                                     set 'unicod\u{000065}Escape'(param) { stringSet = param; }
                                                                     get ''() { return 'empty'; }
                                                                   };

                                                                   C.prototype['unicodeEscape'] === 'get string' &&
                                                                   (C.prototype['unicodeEscape'] = 'set string', stringSet === 'set string') &&
                                                                   C.prototype[''] === 'empty';
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassExtends_InheritsPrototypeMethods()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class A { m() { return 3; } }
                                                                   class B extends A { constructor() { super(); } }
                                                                   new B().m() === 3;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassExtends_ImplicitConstructor_CallsSuper()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class A { constructor() { this.v = 9; } m() { return this.v; } }
                                                                   class B extends A {}
                                                                   new B().m() === 9;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassExtends_ImplicitConstructor_ForwardsArgumentsToSuper()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class A { constructor(v) { this.v = v; } m() { return this.v; } }
                                                                   class B extends A {}
                                                                   new B(7).m() === 7;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassExtends_ImplicitConstructor_ForwardsAllArgumentsToSuper()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class A {
                                                                     constructor(a, b, c) { this.sum = a + b + c; }
                                                                     get() { return this.sum; }
                                                                   }
                                                                   class B extends A {}
                                                                   new B(2, 3, 4).get() === 9;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassExtends_NonConstructor_ThrowsTypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var ok = false;
                                                                   try { class B extends 1 {} }
                                                                   catch (e) { ok = e instanceof TypeError; }
                                                                   ok;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassExtends_SuperCall_InitializesThis()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class A {
                                                                     constructor(v) { this.v = v; }
                                                                     get() { return this.v; }
                                                                   }
                                                                   class B extends A {
                                                                     constructor(v) { super(v + 1); }
                                                                   }
                                                                   new B(6).get() === 7;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassExtends_MissingSuperCall_Throws()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class A {}
                                                                   class B extends A { constructor() {} }
                                                                   new B();
                                                                   """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.ReferenceError));
    }

    [Test]
    public void ClassExtends_SuperCallTwice_ThrowsReferenceError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class A {}
                                                                   class B extends A {
                                                                     constructor() {
                                                                       super();
                                                                       super();
                                                                     }
                                                                   }
                                                                   new B();
                                                                   """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.ReferenceError));
        Assert.That(ex.DetailCode, Is.EqualTo("SUPER_CALL_TWICE"));
    }

    [Test]
    public void ClassExtends_SuperCallInsideArrow_After_FieldInitialization_StillThrowsReferenceError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var baseCtorCalled = 0;
                                                                   var fieldInitCalled = 0;
                                                                   class Base {
                                                                     constructor() {
                                                                       ++baseCtorCalled;
                                                                     }
                                                                   }

                                                                   var C = class extends Base {
                                                                     field = ++fieldInitCalled;
                                                                     constructor() {
                                                                       super();
                                                                       (() => super())();
                                                                     }
                                                                   };

                                                                   new C();
                                                                   """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.ReferenceError));
        Assert.That(ex.DetailCode, Is.EqualTo("SUPER_CALL_TWICE"));
    }

    [Test]
    public void ClassPrivateField_BrandError_UsesDeclaringPrivateName()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class A { #x = 1; get() { return this.#x; } }
                                                                   class B { #y = 2; }
                                                                   var g = A.prototype.get;
                                                                   g.call(new B());
                                                                   """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.TypeError));
        Assert.That(ex.DetailCode, Is.EqualTo("PRIVATE_FIELD_BRAND"));
        Assert.That(ex.Message, Does.Contain("private member #x"));
        Assert.That(ex.Message, Does.Not.Contain("private member #y"));
    }

    [Test]
    public void Class_Instance_Field_Initializers_Run_In_Source_Order_For_Private_Field_Read_And_Write()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var ok = true;

                                                                   try {
                                                                     new class {
                                                                       y = this.#x;
                                                                       #x;
                                                                     }();
                                                                     ok = false;
                                                                   } catch (e) {
                                                                     ok = ok && e instanceof TypeError;
                                                                   }

                                                                   try {
                                                                     new class {
                                                                       y = this.#x = 1;
                                                                       #x;
                                                                     }();
                                                                     ok = false;
                                                                   } catch (e) {
                                                                     ok = ok && e instanceof TypeError;
                                                                   }

                                                                   ok;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Class_Private_Brand_Is_Distinct_Per_Class_Evaluation_Even_With_Same_Source_Name()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function classfactory() {
                                                                     return class {
                                                                       #x;
                                                                       get() { return this.#x; }
                                                                       set(v) { this.#x = v; }
                                                                     };
                                                                   }

                                                                   var C1 = classfactory();
                                                                   var C2 = classfactory();
                                                                   var c1 = new C1();
                                                                   var c2 = new C2();
                                                                   var ok = true;

                                                                   try { C1.prototype.get.call(c2); ok = false; } catch (e) { ok = ok && e instanceof TypeError; }
                                                                   try { C1.prototype.set.call(c2, 1); ok = false; } catch (e) { ok = ok && e instanceof TypeError; }

                                                                   ok;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Class_Private_Brand_Is_Distinct_Per_Class_Evaluation_For_Regular_Methods()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function classfactory() {
                                                                     return class {
                                                                       #x;
                                                                       f() { this.#x; }
                                                                     };
                                                                   }

                                                                   var C1 = classfactory();
                                                                   var C2 = classfactory();
                                                                   var c1 = new C1();
                                                                   var c2 = new C2();
                                                                   var ok = false;

                                                                   try { c1.f.call(c2); } catch (e) { ok = e instanceof TypeError; }
                                                                   ok;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Class_Private_Method_Function_Objects_Are_Distinct_Per_Class_Evaluation()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function classfactory() {
                                                                     return class {
                                                                       #x;
                                                                       f() { this.#x; }
                                                                     };
                                                                   }

                                                                   function check(expectedErrorConstructor, fn) {
                                                                     try {
                                                                       fn();
                                                                       return [1];
                                                                     } catch (e) {
                                                                       return [2, e.constructor === expectedErrorConstructor, e && e.constructor && e.constructor.name];
                                                                     }
                                                                   }

                                                                   var C1 = classfactory();
                                                                   var C2 = classfactory();
                                                                   [C1.prototype.f, C2.prototype.f];
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.TryGetObject(out var resultObj), Is.True);
        Assert.That(resultObj, Is.Not.Null);
        Assert.That(resultObj!.TryGetProperty("0", out var marker), Is.True);
        Assert.That(marker.TryGetObject(out var firstMethod), Is.True);
        Assert.That(resultObj.TryGetProperty("1", out var secondValue), Is.True);
        Assert.That(secondValue.TryGetObject(out var secondMethod), Is.True);
        Assert.That(firstMethod, Is.Not.SameAs(secondMethod));
    }

    [Test]
    public void JavaScript_Wrapper_In_Same_Script_Catches_Private_Brand_TypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function classfactory() {
                                                                     return class {
                                                                       #x;
                                                                       f() { this.#x; }
                                                                     };
                                                                   }

                                                                   function check(expectedErrorConstructor, fn) {
                                                                     try {
                                                                       fn();
                                                                       return [1];
                                                                     } catch (e) {
                                                                       return [2, e.constructor === expectedErrorConstructor, e && e.constructor && e.constructor.name];
                                                                     }
                                                                   }

                                                                   var C1 = classfactory();
                                                                   var C2 = classfactory();
                                                                   var c1 = new C1();
                                                                   var c2 = new C2();
                                                                   check(TypeError, function() {
                                                                     c1.f.call(c2);
                                                                   });
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.TryGetObject(out var resultObj), Is.True);
        Assert.That(resultObj, Is.Not.Null);
        Assert.That(resultObj!.TryGetProperty("0", out var marker), Is.True);
        Assert.That(marker.IsInt32, Is.True);
        Assert.That(marker.Int32Value, Is.EqualTo(2));
        Assert.That(resultObj.TryGetProperty("1", out var ctorMatches), Is.True);
        Assert.That(ctorMatches.IsTrue, Is.True);
        Assert.That(resultObj.TryGetProperty("2", out var ctorName), Is.True);
        Assert.That(ctorName.IsString, Is.True);
        Assert.That(ctorName.AsString(), Is.EqualTo("TypeError"));
    }

    [Test]
    public void ClassExtends_WithPrivateFields_GetterSetterMethods_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class A {
                                                                     #a = 0;
                                                                     constructor(v) { this.#a = v; }
                                                                     getA() { return this.#a; }
                                                                     setA(v) { this.#a = v; }
                                                                   }
                                                                   class B extends A {
                                                                     #b = 0;
                                                                     constructor(a, b) { super(a); this.#b = b; }
                                                                     getB() { return this.#b; }
                                                                     setB(v) { this.#b = v; }
                                                                     sum() { return this.getA() + this.getB(); }
                                                                   }
                                                                   var x = new B(2, 3);
                                                                   x.setA(5);
                                                                   x.setB(7);
                                                                   x.sum() === 12;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassExtends_WithPrivateFields_BrandCheckFailure_ThrowsTypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class A {
                                                                     #a = 1;
                                                                     getA() { return this.#a; }
                                                                   }
                                                                   class B extends A {
                                                                     #b = 2;
                                                                     getB() { return this.#b; }
                                                                   }
                                                                   var ga = A.prototype.getA;
                                                                   var gb = B.prototype.getB;
                                                                   ga.call({});
                                                                   """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.TypeError));
        Assert.That(ex.DetailCode, Is.EqualTo("PRIVATE_FIELD_BRAND"));
        Assert.That(ex.Message, Does.Contain("private member #a"));
    }

    [Test]
    public void ClassExtends_SuperMethodCall_UsesDerivedThis()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class A {
                                                                     m() { return this.v; }
                                                                   }
                                                                   class B extends A {
                                                                     constructor() { super(); this.v = 5; }
                                                                     n() { return super.m(); }
                                                                   }
                                                                   new B().n() === 5;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassExtends_SuperGetterRead_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class A {}
                                                                   A.prototype.x = 9;
                                                                   class B extends A {
                                                                     constructor() { super(); }
                                                                     read() { return super.x; }
                                                                   }
                                                                   new B().read() === 9;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassExtends_SuperSetterWrite_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class A {}
                                                                   A.prototype.x = 1;
                                                                   class B extends A {
                                                                     constructor() { super(); }
                                                                     write(v) { super.x = v; return this.x === v; }
                                                                   }
                                                                   var b = new B();
                                                                   b.write(11) === true && A.prototype.x === 1;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassExtends_SuperUpdateNamed_PrefixPostfix_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class A {
                                                                     get x() { return this._x; }
                                                                     set x(v) { this._x = v; }
                                                                   }
                                                                   class B extends A {
                                                                     constructor() { super(); this._x = 1; }
                                                                     run() {
                                                                       var a = super.x++;
                                                                       var b = ++super.x;
                                                                       return a === 1 && b === 3 && this._x === 3;
                                                                     }
                                                                   }
                                                                   new B().run();
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassExtends_SuperUpdateComputed_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class A {
                                                                     get x() { return this._x; }
                                                                     set x(v) { this._x = v; }
                                                                   }
                                                                   class B extends A {
                                                                     constructor() { super(); this._x = 4; }
                                                                     run() {
                                                                       var k = "x";
                                                                       var a = super[k]--;
                                                                       return a === 4 && this._x === 3;
                                                                     }
                                                                   }
                                                                   new B().run();
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassExtends_SuperCompoundAssignment_Named_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class A {
                                                                     get x() { return this._x; }
                                                                     set x(v) { this._x = v; }
                                                                   }
                                                                   class B extends A {
                                                                     constructor() { super(); this._x = 2; }
                                                                     run() {
                                                                       var v = (super.x += 5);
                                                                       return v === 7 && this._x === 7;
                                                                     }
                                                                   }
                                                                   new B().run();
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassExtends_SuperCompoundAssignment_Computed_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class A {
                                                                     get x() { return this._x; }
                                                                     set x(v) { this._x = v; }
                                                                   }
                                                                   class B extends A {
                                                                     constructor() { super(); this._x = 3; }
                                                                     run() {
                                                                       var c = 0;
                                                                       var k = { toString() { c = c + 1; return "x"; } };
                                                                       var v = (super[k] *= 2);
                                                                       return v === 6 && this._x === 6 && c === 2;
                                                                     }
                                                                   }
                                                                   new B().run();
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassExtends_SuperPrivateUpdate_ThrowsSyntaxError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var ex = Assert.Throws<JsParseException>(() =>
            compiler.Compile(JavaScriptParser.ParseScript("""
                                                          class A { #x = 1; }
                                                          class B extends A {
                                                            run() { return super.#x++; }
                                                          }
                                                          new B().run();
                                                          """)));

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Message, Does.Contain("Unexpected private field"));
    }

    [Test]
    public void ClassAccessor_GetterSetter_Syntax_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class A {
                                                                     get x() { return this._x; }
                                                                     set x(v) { this._x = v; }
                                                                   }
                                                                   var a = new A();
                                                                   a.x = 7;
                                                                   a.x === 7;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassExtends_SuperAccessorReadWrite_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class A {
                                                                     get x() { return this._x; }
                                                                     set x(v) { this._x = v; }
                                                                   }
                                                                   class B extends A {
                                                                     constructor() { super(); this._x = 1; }
                                                                     read() { return super.x; }
                                                                     write(v) { super.x = v; return this._x; }
                                                                   }
                                                                   var b = new B();
                                                                   b.read() === 1 && b.write(9) === 9;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassStatic_MethodAccessorField_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class A {
                                                                     static v = 1;
                                                                     static inc() { this.v = this.v + 1; }
                                                                     static get x() { return this.v; }
                                                                     static set x(v) { this.v = v; }
                                                                   }
                                                                   A.inc();
                                                                   A.x = 7;
                                                                   A.x === 7 && A.v === 7;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassExtends_StaticSuperAccessorReadWrite_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class A {
                                                                     static get x() { return this._x; }
                                                                     static set x(v) { this._x = v; }
                                                                   }
                                                                   class B extends A {
                                                                     static read() { return super.x; }
                                                                     static write(v) { super.x = v; return this._x === v; }
                                                                   }
                                                                   B._x = 1;
                                                                   B.read() === 1 && B.write(9) === true && A._x === undefined;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassPrivateAccessor_GetterSetter_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class C {
                                                                     #xv = 1;
                                                                     get #x() { return this.#xv; }
                                                                     set #x(v) { this.#xv = v; }
                                                                     read() { return this.#x; }
                                                                     write(v) { this.#x = v; }
                                                                   }
                                                                   var c = new C();
                                                                   c.write(9);
                                                                   c.read() === 9;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassPrivateAccessor_BrandCheckFailure_ThrowsTypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class A {
                                                                     get #x() { return 1; }
                                                                     read() { return this.#x; }
                                                                   }
                                                                   class B {}
                                                                   var r = A.prototype.read;
                                                                   r.call(new B());
                                                                   """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.TypeError));
        Assert.That(ex.DetailCode, Is.EqualTo("PRIVATE_FIELD_BRAND"));
        Assert.That(ex.Message, Does.Contain("private member #x"));
    }

    [Test]
    public void ClassStaticPrivateField_WithConstRead_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class C {
                                                                     static #x = 1;
                                                                     static get() { const v = this.#x; return v; }
                                                                     static set(v) { this.#x = v; }
                                                                   }
                                                                   C.set(7);
                                                                   C.get() === 7;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassStaticPrivateField_BrandCheckFailure_ThrowsTypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class A {
                                                                     static #x = 1;
                                                                     static read() { return this.#x; }
                                                                   }
                                                                   class B extends A {}
                                                                   A.read.call(B);
                                                                   """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.TypeError));
        Assert.That(ex.DetailCode, Is.EqualTo("PRIVATE_FIELD_BRAND"));
        Assert.That(ex.Message, Does.Contain("private member #x"));
    }

    [Test]
    public void ClassPrivateMethod_InstanceCall_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class C {
                                                                     #m(v) { return v + 1; }
                                                                     run(v) { return this.#m(v); }
                                                                   }
                                                                   new C().run(6) === 7;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassPrivateMethod_StaticCall_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class C {
                                                                     static #m(v) { return v + 2; }
                                                                     static run(v) { return this.#m(v); }
                                                                   }
                                                                   C.run(5) === 7;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassPrivateMethod_BrandCheckFailure_ThrowsTypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class A {
                                                                     #m() { return 1; }
                                                                     run() { return this.#m(); }
                                                                   }
                                                                   class B {}
                                                                   var r = A.prototype.run;
                                                                   r.call(new B());
                                                                   """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.TypeError));
        Assert.That(ex.DetailCode, Is.EqualTo("PRIVATE_FIELD_BRAND"));
        Assert.That(ex.Message, Does.Contain("private member #m"));
    }

    [Test]
    public void ClassComputed_InstanceMethodKey_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var k = "m";
                                                                   class C {
                                                                     [k](v) { return v + 1; }
                                                                   }
                                                                   new C().m(3) === 4;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassComputed_StaticMethodAndFieldKey_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var km = "run";
                                                                   var kf = "value";
                                                                   class C {
                                                                     static [km]() { return this.value; }
                                                                     static [kf] = 9;
                                                                   }
                                                                   C.run() === 9 && C.value === 9;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassComputed_InstanceAccessorKey_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var k = "x";
                                                                   class C {
                                                                     get [k]() { return this._x; }
                                                                     set [k](v) { this._x = v; }
                                                                   }
                                                                   var c = new C();
                                                                   c.x = 11;
                                                                   c.x === 11;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassComputed_MethodObjectKey_ToString_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var counter = 0;
                                                                   var key1ToString = [];
                                                                   var key2ToString = [];
                                                                   var key1 = {
                                                                     toString: function() {
                                                                       key1ToString.push(counter);
                                                                       counter += 1;
                                                                       return 'b';
                                                                     }
                                                                   };
                                                                   var key2 = {
                                                                     toString: function() {
                                                                       key2ToString.push(counter);
                                                                       counter += 1;
                                                                       return 'd';
                                                                     }
                                                                   };
                                                                   class C {
                                                                     a() { return 'A'; }
                                                                     [key1]() { return 'B'; }
                                                                     c() { return 'C'; }
                                                                     [key2]() { return 'D'; }
                                                                   }
                                                                   typeof new C().b === "function" &&
                                                                   new C().b() === "B" &&
                                                                   typeof new C().d === "function" &&
                                                                   new C().d() === "D";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassComputed_MethodObjectKey_ToString_SideEffectOrder_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   globalThis.counter = 0;
                   globalThis.key1ToString = [];
                   globalThis.key2ToString = [];
                   var key1 = {
                     toString: function() {
                       key1ToString.push(counter);
                       counter = counter + 1;
                       return 'b';
                     }
                   };
                   var key2 = {
                     toString: function() {
                       key2ToString.push(counter);
                       counter = counter + 1;
                       return 'd';
                     }
                   };
                   class C {
                     a() { return 'A'; }
                     [key1]() { return 'B'; }
                     c() { return 'C'; }
                     [key2]() { return 'D'; }
                   }
                   globalThis.C = C;
                   """);

        Assert.That(realm.Eval("key1ToString.join(',')").AsString(), Is.EqualTo("0"));
        Assert.That(realm.Eval("key2ToString.join(',')").AsString(), Is.EqualTo("1"));
        Assert.That(realm.Eval("counter === 2").IsTrue, Is.True);
        Assert.That(realm.Eval("new C().b() === 'B'").IsTrue, Is.True);
        Assert.That(realm.Eval("new C().d() === 'D'").IsTrue, Is.True);
        Assert.That(realm.Eval("Object.getOwnPropertyNames(C.prototype).join(',')").AsString(),
            Is.EqualTo("constructor,a,b,c,d"));
    }

    [Test]
    public void ClassComputed_MethodObjectKey_ValueOfNumber_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var counter = 0;
                                                                   var key1 = {
                                                                     valueOf: function() {
                                                                       counter += 1;
                                                                       return 1;
                                                                     },
                                                                     toString: null
                                                                   };
                                                                   var key2 = {
                                                                     valueOf: function() {
                                                                       counter += 1;
                                                                       return 2;
                                                                     },
                                                                     toString: null
                                                                   };
                                                                   class C {
                                                                     a() { return 'A'; }
                                                                     [key1]() { return 'B'; }
                                                                     c() { return 'C'; }
                                                                     [key2]() { return 'D'; }
                                                                   }
                                                                   counter === 2 &&
                                                                   new C()[1]() === "B" &&
                                                                   new C()[2]() === "D" &&
                                                                   Object.getOwnPropertyNames(C.prototype).join(",") === "1,2,constructor,a,c";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassMethod_ParameterInitializerScope_DoesNotCaptureBodyVar()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var x = 'outside';
                                                                   var probeParams, probeBody;

                                                                   var C = class {
                                                                     m(_ = probeParams = function() { return x; }) {
                                                                       var x = 'inside';
                                                                       probeBody = function() { return x; };
                                                                     }
                                                                   };
                                                                   C.prototype.m();

                                                                   probeParams() === 'outside' && probeBody() === 'inside';
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassSetter_DefaultParameter_ParsesAndKeepsOuterVar()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var probe;

                                                                   var C = class {
                                                                     set a(_ = null) {
                                                                       var x = 'inside';
                                                                       probe = function() { return x; };
                                                                     }
                                                                   };
                                                                   C.prototype.a = null;

                                                                   var x = 'outside';
                                                                   probe() === 'inside' && x === 'outside';
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassExpression_NameLexicalBinding_MethodSeesClass_NotOuterBinding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var C = 'outside';
                                                                   var cls = class C {
                                                                     method() {
                                                                       return C;
                                                                     }
                                                                   };
                                                                   cls.prototype.method() === cls && C === 'outside';
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassDeclaration_NameLexicalBinding_MethodSeesClass()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class C {
                                                                     method() {
                                                                       return C;
                                                                     }
                                                                   }
                                                                   C.prototype.method() === C;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void DerivedConstructor_ThisAccessRestriction_DoesNotCrash()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class Base {
                                                                     constructor(a, b) {
                                                                       var o = new Object();
                                                                       o.prp = a + b;
                                                                       return o;
                                                                     }
                                                                   }

                                                                   class Subclass extends Base {
                                                                     constructor(a, b) {
                                                                       var exn;
                                                                       try {
                                                                         this.prp1 = 3;
                                                                       } catch (e) {
                                                                         exn = e;
                                                                       }
                                                                       super(a, b);
                                                                       return this;
                                                                     }
                                                                   }

                                                                   class Subclass2 extends Base {
                                                                     constructor(x) {
                                                                       super(1,2);
                                                                       if (x < 0) return;
                                                                       function tmp() { return 3; }
                                                                       try { super(tmp(),4); } catch (e) {}
                                                                     }
                                                                   }

                                                                   class BadSubclass extends Base {
                                                                     constructor() {}
                                                                   }

                                                                   true;
                                                                   """));

        Assert.DoesNotThrow(() => realm.Execute(script));
    }

    [Test]
    public void ClassConstructor_Call_ThrowsTypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class Base { constructor() {} }
                                                                   class Sub extends Base { constructor() { super(); } }
                                                                   var ok = false;
                                                                   try {
                                                                     Sub.call({});
                                                                   } catch (e) {
                                                                     ok = (e.constructor === TypeError);
                                                                   }
                                                                   ok;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassExtendsNull_ThisRead_AndConstruction_ThrowReferenceError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class C extends null {
                                                                     constructor() {
                                                                       var inside = false;
                                                                       try { this; } catch (e) { inside = (e.constructor === ReferenceError); }
                                                                       if (!inside) throw new Error("inside");
                                                                     }
                                                                   }
                                                                   var outside = false;
                                                                   try { new C(); } catch (e) { outside = (e.constructor === ReferenceError); }
                                                                   outside;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassExtendsNull_ArrowThisRead_BeforeSuper_ThrowsReferenceError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class C extends null {
                                                                     constructor() {
                                                                       var ok = false;
                                                                       try {
                                                                         (() => { this; })();
                                                                       } catch (e) {
                                                                         ok = (e.constructor === ReferenceError);
                                                                       }
                                                                       if (!ok) throw new Error("no-ref");
                                                                     }
                                                                   }
                                                                   var outer = false;
                                                                   try { new C(); } catch (e) { outer = (e.constructor === ReferenceError); }
                                                                   outer;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void DerivedConstructor_ArrowThis_Tracks_This_After_Super()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class A {
                                                                     constructor() {
                                                                       this.value = 3;
                                                                     }
                                                                   }

                                                                   class B extends A {
                                                                     constructor() {
                                                                       let readThis = () => this.value;
                                                                       super();
                                                                       this.value = 7;
                                                                       this.captured = readThis();
                                                                       return;
                                                                     }
                                                                   }

                                                                   var b = new B();
                                                                   b.value === 7 && b.captured === 7;
                                                                   """));

        IEnumerable<JsBytecodeFunction> EnumerateFunctions(JsScript root)
        {
            foreach (var function in root.ObjectConstants.OfType<JsBytecodeFunction>())
            {
                yield return function;
                foreach (var nested in EnumerateFunctions(function.Script))
                    yield return nested;
            }
        }

        var readThis = EnumerateFunctions(script).Single(f => f.IsArrow);
        Assert.That(readThis.LexicalThisContextSlot, Is.GreaterThanOrEqualTo(0));
        Assert.That(readThis.LexicalThisContextDepth, Is.EqualTo(0));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void DerivedConstructor_NestedArrowThis_Tracks_This_After_Super()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class A {
                                                                     constructor() {
                                                                       this.value = 1;
                                                                     }
                                                                   }

                                                                   class B extends A {
                                                                     constructor() {
                                                                       let outer = () => () => this.value;
                                                                       super();
                                                                       this.value = 9;
                                                                       this.captured = outer()();
                                                                     }
                                                                   }

                                                                   var b = new B();
                                                                   b.value === 9 && b.captured === 9;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ObjectLiteralMethod_SuperProperty_UsesHomeObjectThis()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var viaCall;
                                                                   var viaMember;
                                                                   var parent = {
                                                                     getThis: function() { return this; },
                                                                     get This() { return this; }
                                                                   };
                                                                   var obj = {
                                                                     method() {
                                                                       viaCall = super['getThis']();
                                                                       viaMember = super['This'];
                                                                     }
                                                                   };
                                                                   Object.setPrototypeOf(obj, parent);
                                                                   obj.method();
                                                                   viaCall === obj && viaMember === obj;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassConstructor_SuperProperty_WithoutExtends_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class A {
                                                                     constructor() {
                                                                       super.toString();
                                                                     }
                                                                     dontDoThis() {
                                                                       super.makeBugs = 1;
                                                                     }
                                                                   }
                                                                   var a = new A();
                                                                   a.dontDoThis();
                                                                   a.makeBugs === 1;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassStaticAccessor_NonCanonicalNumericLiteral_Uses_Canonical_Property_Key()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var stringSet;
                                                                   var C = class {
                                                                     static get 0.0000001() { return "get string"; }
                                                                     static set 0.0000001(param) { stringSet = param; }
                                                                   };
                                                                   C["1e-7"] === "get string" && (C["1e-7"] = "set string", stringSet === "set string");
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Class_Numeric_Property_Names_Work_Through_Super_Element_Access()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class B {
                                                                     1() { return 1; }
                                                                     get 2() { return 2; }
                                                                     static 4() { return 4; }
                                                                     static get 5() { return 5; }
                                                                   }

                                                                   class C extends B {
                                                                     1() { return super[1](); }
                                                                     get 2() { return super[2]; }
                                                                     static 4() { return super[4](); }
                                                                     static get 5() { return super[5]; }
                                                                   }

                                                                   [new C()[1](), new C()[2], C[4](), C[5]].join(",");
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("1,2,4,5"));
    }

    [Test]
    public void Class_Method_Super_Set_Failed_Write_Throws_TypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var caught;
                                                                   class C {
                                                                     method() {
                                                                       super['x'] = 8;
                                                                       Object.freeze(C.prototype);
                                                                       try {
                                                                         super['y'] = 9;
                                                                       } catch (err) {
                                                                         caught = err;
                                                                       }
                                                                     }
                                                                   }

                                                                   C.prototype.method();
                                                                   typeof caught === 'object' && caught.constructor === TypeError;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Class_Field_Named_Get_Or_Set_Can_Be_Followed_By_Generator_Method_Via_ASI()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class A {
                                                                     get
                                                                     *a() {}
                                                                   }

                                                                   class B {
                                                                     static set
                                                                     *a(x) {}
                                                                   }

                                                                   [
                                                                     A.prototype.hasOwnProperty("a"),
                                                                     new A().hasOwnProperty("get"),
                                                                     B.prototype.hasOwnProperty("a"),
                                                                     B.hasOwnProperty("set")
                                                                   ].join(",");
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("true,true,true,true"));
    }

    [Test]
    public void ClassStaticComputedField_Named_Prototype_ThrowsTypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var x = "prototype";
                                                                   var ok = false;
                                                                   try {
                                                                     (0, class {
                                                                       static [x] = 42;
                                                                     });
                                                                     ok = false;
                                                                   } catch (e) {
                                                                     ok = e instanceof TypeError;
                                                                   }
                                                                   try {
                                                                     (0, class {
                                                                       static [x];
                                                                     });
                                                                     ok = false;
                                                                   } catch (e) {
                                                                     ok = ok && e instanceof TypeError;
                                                                   }
                                                                   ok;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Class_Constructor_Prototype_Property_Is_NonWritable_And_Static_Prototype_Members_Throw()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var desc = Object.getOwnPropertyDescriptor(class C {}, "prototype");
                                                                   var ok = desc.configurable === false &&
                                                                            desc.enumerable === false &&
                                                                            desc.writable === false;

                                                                   try { class A { static ["prototype"]() {} } ok = false; } catch (e) { ok = ok && e instanceof TypeError; }
                                                                   try { class B { static get ["prototype"]() {} } ok = false; } catch (e) { ok = ok && e instanceof TypeError; }
                                                                   try { class D { static set ["prototype"](_) {} } ok = false; } catch (e) { ok = ok && e instanceof TypeError; }
                                                                   try { class E { static *["prototype"]() {} } ok = false; } catch (e) { ok = ok && e instanceof TypeError; }

                                                                   ok;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Async_Class_Method_Parameter_Initializer_Can_Use_Super_Property_Access()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.done = false;
                                                                   globalThis.out = "";

                                                                   class A {
                                                                     async method() {
                                                                       return "sup";
                                                                     }
                                                                   }

                                                                   class B extends A {
                                                                     async method(x = super.method()) {
                                                                       globalThis.out = await x;
                                                                     }
                                                                   }

                                                                   new B().method().then(function () {
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
    public void Class_Public_Fields_Use_DefineProperty_On_Proxy_Instances()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var observed = [];
                                                                   var expectedTarget = null;

                                                                   function ProxyBase() {
                                                                     expectedTarget = this;
                                                                     return new Proxy(this, {
                                                                       defineProperty(target, key, descriptor) {
                                                                         observed.push(String(key));
                                                                         observed.push(descriptor.value);
                                                                         observed.push(target === expectedTarget);
                                                                         observed.push(descriptor.enumerable === true);
                                                                         observed.push(descriptor.configurable === true);
                                                                         observed.push(descriptor.writable === true);
                                                                         return Reflect.defineProperty(target, key, descriptor);
                                                                       }
                                                                     });
                                                                   }

                                                                   class ViaProxy extends ProxyBase {
                                                                     f = 3;
                                                                     g = "Test262";
                                                                   }

                                                                   new ViaProxy();
                                                                   JSON.stringify(observed) === JSON.stringify([
                                                                     "f", 3, true, true, true, true,
                                                                     "g", "Test262", true, true, true, true
                                                                   ]);
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Class_Public_Fields_Throw_On_Frozen_Instance()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var threw = false;
                                                                   try {
                                                                     new class {
                                                                       f = Object.freeze(this);
                                                                       g = "Test262";
                                                                     }();
                                                                   } catch (e) {
                                                                     threw = e instanceof TypeError;
                                                                   }
                                                                   threw;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Class_Private_Instance_Members_Throw_On_NonExtensible_Objects()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var ok = true;

                                                                   class NonExtensibleBase {
                                                                     constructor(seal) {
                                                                       if (seal) Object.preventExtensions(this);
                                                                     }
                                                                   }

                                                                   class PrivateFieldClass extends NonExtensibleBase {
                                                                     #x = 1;
                                                                     static read(obj) { return obj.#x; }
                                                                     constructor(seal) { super(seal); }
                                                                   }

                                                                   class PrivateMethodClass extends NonExtensibleBase {
                                                                     #m() { return 2; }
                                                                     static call(obj) { return obj.#m(); }
                                                                     constructor(seal) { super(seal); }
                                                                   }

                                                                   class TrojanBase {
                                                                     constructor(obj) {
                                                                       return obj;
                                                                     }
                                                                   }

                                                                   class ReturnOverridePrivateField extends TrojanBase {
                                                                     #x = 3;
                                                                     static read(obj) { return obj.#x; }
                                                                   }

                                                                   ok = ok && PrivateFieldClass.read(new PrivateFieldClass(false)) === 1;
                                                                   ok = ok && PrivateMethodClass.call(new PrivateMethodClass(false)) === 2;
                                                                   ok = ok && ReturnOverridePrivateField.read(new ReturnOverridePrivateField({})) === 3;

                                                                   try { new PrivateFieldClass(true); ok = false; } catch (e) { ok = ok && e instanceof TypeError; }
                                                                   try { new PrivateMethodClass(true); ok = false; } catch (e) { ok = ok && e instanceof TypeError; }
                                                                   try { new ReturnOverridePrivateField(Object.preventExtensions({})); ok = false; } catch (e) { ok = ok && e instanceof TypeError; }

                                                                   ok;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Class_Private_Static_Field_Throws_TypeError_When_Class_Becomes_NonExtensible_During_Initialization()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   try {
                                                                     class TestNonExtensibleStaticData {
                                                                       static #g = (Object.preventExtensions(TestNonExtensibleStaticData), "Test262");
                                                                     }
                                                                     "no-throw";
                                                                   } catch (e) {
                                                                     e && e.constructor && e.constructor.name;
                                                                   }
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("TypeError"));
    }

    [Test]
    public void ClassStaticFieldInitializer_Uses_Class_This_And_Name()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var className;
                                                                   var C = class C {
                                                                     static f = 'test';
                                                                     static g = this.f + '262';
                                                                     static h = (className = this.name);
                                                                   };

                                                                   C.g === 'test262' && className === 'C';
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassStaticFieldInitializer_Arrow_Captures_Class_This()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var C = class {
                                                                     static f = () => this;
                                                                   };

                                                                   C.f() === C;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassStaticFieldInitializer_Assigns_Anonymous_Function_Names()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var C = class {
                                                                     static #field = () => 'Test262';
                                                                     static field = function() { return 42; };

                                                                     static accessPrivateField() {
                                                                       return this.#field;
                                                                     }
                                                                   };

                                                                   C.accessPrivateField().name === '#field' && C.field.name === 'field';
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassStaticFieldInitializer_Assigns_Anonymous_Function_Names_From_Exact_Test262_Fixture()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var repoRoot = TestContext.CurrentContext.TestDirectory;
        while (!File.Exists(Path.Combine(repoRoot, "Okojo.slnx")))
            repoRoot = Directory.GetParent(repoRoot)!.FullName;

        var sourcePath = Path.Combine(repoRoot, "test262", "test", "language", "expressions", "class", "elements",
            "static-field-anonymous-function-name.js");
        var source = """
                     var assert = {
                       sameValue(actual, expected) {
                         if (actual !== expected) throw new Error(String(actual) + " !== " + String(expected));
                       }
                     };
                     """ + Environment.NewLine + File.ReadAllText(sourcePath);
        var program = JavaScriptParser.ParseScript(source, sourcePath);
        var script = compiler.Compile(program);

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsUndefined, Is.True);
    }

    [Test]
    public void AnonymousClass_StaticFieldInitializer_Sees_Inferred_Class_Name()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var className;
                                                                   var C = class {
                                                                     static f = (className = this.name);
                                                                   };

                                                                   className === "C" && C.name === "C";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassStaticFieldInitializer_Can_See_Preceding_Static_Methods()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var C = class {
                                                                     static g() { return 45; }
                                                                     static g = this.g();
                                                                   };

                                                                   C.g === 45;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassFieldInitializer_Arrow_Can_Access_Super_Property()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var Base = class {};
                                                                   var C = class extends Base {
                                                                     func = () => {
                                                                       super.prop = 'test262';
                                                                     };

                                                                     static staticFunc = () => {
                                                                       super.staticProp = 'static test262';
                                                                     };
                                                                   };

                                                                   var c = new C();
                                                                   c.func();
                                                                   C.staticFunc();
                                                                   c.prop === 'test262' && C.staticProp === 'static test262';
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassDeclaration_FieldInitializer_Arrow_Uses_Default_Super_Base()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class C {
                                                                     func = () => {
                                                                       super.prop = 'test262';
                                                                     }

                                                                     static staticFunc = () => {
                                                                       super.staticProp = 'static test262';
                                                                     }
                                                                   }

                                                                   let c = new C();
                                                                   c.func();
                                                                   C.staticFunc();
                                                                   c.prop === 'test262' && C.staticProp === 'static test262';
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Symbol_Construction_And_Subclass_Super_Call_Throw_TypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var out = [];

                                                                   try {
                                                                     new Symbol();
                                                                     out.push("new Symbol no-throw");
                                                                   } catch (e) {
                                                                     out.push(e.name);
                                                                   }

                                                                   class S1 extends Symbol {}
                                                                   try {
                                                                     new S1();
                                                                     out.push("new S1 no-throw");
                                                                   } catch (e) {
                                                                     out.push(e.name);
                                                                   }

                                                                   class S2 extends Symbol {
                                                                     constructor() {
                                                                       super();
                                                                     }
                                                                   }

                                                                   try {
                                                                     new S2();
                                                                     out.push("new S2 no-throw");
                                                                   } catch (e) {
                                                                     out.push(e.name);
                                                                   }

                                                                   out.join(",");
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("TypeError,TypeError,TypeError"));
    }

    [Test]
    public void DerivedConstructor_SuperPropertyAccess_Before_SuperCall_Throws_ReferenceError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class Base {}
                                                                   var out = [];

                                                                   try {
                                                                     class C extends Base {
                                                                       constructor() {
                                                                         super.method();
                                                                         super(this);
                                                                       }
                                                                     }
                                                                     new C();
                                                                     out.push("no-throw");
                                                                   } catch (e) {
                                                                     out.push(e.name);
                                                                   }

                                                                   out.join(",");
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("ReferenceError"));
    }

    [Test]
    public void ClassFieldInitializer_IndirectEval_NewTarget_Throws_SyntaxError_Before_SideEffects()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var executed = false;
                                                                   var C = class {
                                                                     x = (0, eval)('executed = true; new.target;');
                                                                   };

                                                                   try {
                                                                     new C();
                                                                     false;
                                                                   } catch (e) {
                                                                     e.name === "SyntaxError" && executed === false;
                                                                   }
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassExtendsArray_ConstructedInstance_Is_InstanceOf_Subclass_And_Array()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const Subclass = class extends Array {};
                                                                   const sub = new Subclass();
                                                                   sub instanceof Subclass && sub instanceof Array && sub.length === 0;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassExtendsWeakMap_DefaultDerivedConstructor_Preserves_Class_Binding_And_InstanceOf()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const Subclass = class extends WeakMap {};
                                                                   const sub = new Subclass();
                                                                   sub instanceof Subclass && sub instanceof WeakMap;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassExtendsUint32Array_DefaultDerivedConstructor_Preserves_Class_Binding_And_InstanceOf()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const Subclass = class extends Uint32Array {};
                                                                   const sub = new Subclass();
                                                                   sub instanceof Subclass && sub instanceof Uint32Array && sub.length === 0;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassAsyncMethod_AsyncArrow_Captures_Parent_Arguments()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var result = false;
                                                                   var C = class {
                                                                     async method(x) {
                                                                       let a = arguments;
                                                                       return async () => a === arguments;
                                                                     }
                                                                   };

                                                                   var c = new C();
                                                                   var asyncFn = c.method.bind(c);
                                                                   asyncFn().then(retFn => retFn()).then(v => { result = v; });
                                                                   """));

        realm.Execute(script);
        new JsAgentRunner(realm.Agent).PumpUntilIdle();
        var resultAtom = realm.Atoms.InternNoCheck("result");
        Assert.That(realm.GlobalObject.TryGetPropertyAtom(realm, resultAtom, out var resultValue, out _), Is.True);
        Assert.That(resultValue.IsTrue, Is.True);
    }

    [Test]
    public void ClassPrivateField_OptionalChain_PrivateSegment_ShortCircuits_And_Throws_On_Wrong_Brand()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var C = class {
                                                                     #m = 'test262';

                                                                     static access(obj) {
                                                                       return obj?.#m;
                                                                     }
                                                                   };

                                                                   let c = new C();
                                                                   let ok = C.access(c) === 'test262'
                                                                     && C.access(null) === undefined
                                                                     && C.access(undefined) === undefined;

                                                                   try {
                                                                     C.access({});
                                                                     ok = false;
                                                                   } catch (e) {
                                                                     ok = ok && e instanceof TypeError;
                                                                   }

                                                                   ok;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassPrivateField_After_OptionalChain_ShortCircuits_Whole_Chain()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var C = class {
                                                                     #f = 'Test262';

                                                                     method(o) {
                                                                       return o?.c.#f;
                                                                     }
                                                                   };

                                                                   let c = new C();
                                                                   let o = { c: c };
                                                                   let ok = c.method(o) === 'Test262'
                                                                     && c.method(null) === undefined
                                                                     && c.method(undefined) === undefined;

                                                                   o = { c: {} };
                                                                   try {
                                                                     c.method(o);
                                                                     ok = false;
                                                                   } catch (e) {
                                                                     ok = ok && e instanceof TypeError;
                                                                   }

                                                                   ok;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassPublicFields_Are_Enumerable_With_Stacked_ASI_Definitions()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var C = class {
                                                                     a
                                                                     b = 42;
                                                                     foo = "foobar"
                                                                     bar = "barbaz";
                                                                   };

                                                                   var c = new C();
                                                                   var foo = Object.getOwnPropertyDescriptor(c, "foo");
                                                                   var bar = Object.getOwnPropertyDescriptor(c, "bar");
                                                                   var a = Object.getOwnPropertyDescriptor(c, "a");
                                                                   var b = Object.getOwnPropertyDescriptor(c, "b");

                                                                   foo.enumerable && bar.enumerable && a.enumerable && b.enumerable;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassGeneratorMethod_Descriptor_Is_Writable_Configurable_And_NonEnumerable()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var C = class {
                                                                     #x; #y;
                                                                     *m() { return 42; }
                                                                   };

                                                                   var desc = Object.getOwnPropertyDescriptor(C.prototype, "m");
                                                                   desc.writable === true && desc.configurable === true && desc.enumerable === false;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Class_Computed_Methods_Use_Runtime_Property_Key_For_Function_Name()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var stringKey = "m";
                                                                   var namedSym = Symbol("test262");
                                                                   var anonSym = Symbol();
                                                                   class A {
                                                                     [stringKey]() {}
                                                                     [namedSym]() {}
                                                                     [anonSym]() {}
                                                                     static [stringKey]() {}
                                                                     static [namedSym]() {}
                                                                     static [anonSym]() {}
                                                                   }

                                                                   [
                                                                     A.prototype[stringKey].name,
                                                                     A.prototype[namedSym].name,
                                                                     A.prototype[anonSym].name,
                                                                     A[stringKey].name,
                                                                     A[namedSym].name,
                                                                     A[anonSym].name
                                                                   ].join("|");
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("m|[test262]||m|[test262]|"));
    }

    [Test]
    public void Class_Computed_Accessors_Use_Runtime_Property_Key_For_Function_Name()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var stringKey = "m";
                                                                   var namedSym = Symbol("test262");
                                                                   var anonSym = Symbol();
                                                                   class A {
                                                                     get [stringKey]() {}
                                                                     set [stringKey](_) {}
                                                                     get [namedSym]() {}
                                                                     set [namedSym](_) {}
                                                                     static get [anonSym]() {}
                                                                     static set [anonSym](_) {}
                                                                   }

                                                                   [
                                                                     Object.getOwnPropertyDescriptor(A.prototype, stringKey).get.name,
                                                                     Object.getOwnPropertyDescriptor(A.prototype, stringKey).set.name,
                                                                     Object.getOwnPropertyDescriptor(A.prototype, namedSym).get.name,
                                                                     Object.getOwnPropertyDescriptor(A.prototype, namedSym).set.name,
                                                                     Object.getOwnPropertyDescriptor(A, anonSym).get.name,
                                                                     Object.getOwnPropertyDescriptor(A, anonSym).set.name
                                                                   ].join("|");
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("get m|set m|get [test262]|set [test262]|get |set "));
    }

    [Test]
    public void Class_Static_Name_And_Length_Accessors_Replace_Default_Function_Properties()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var ready = false;
                                                                   class NameGet {
                                                                     static get name() {
                                                                       if (!ready) throw new Error("getter ran during definition");
                                                                       return "pass";
                                                                     }
                                                                   }
                                                                   class NameSet {
                                                                     static set name(_) {
                                                                       throw new Error("setter ran during definition");
                                                                     }
                                                                   }
                                                                   class LengthGet {
                                                                     static get length() {
                                                                       if (!ready) throw new Error("getter ran during definition");
                                                                       return 7;
                                                                     }
                                                                   }
                                                                   class LengthSet {
                                                                     static set length(_) {
                                                                       throw new Error("setter ran during definition");
                                                                     }
                                                                   }

                                                                   ready = true;
                                                                   [
                                                                     Object.getOwnPropertyNames(NameGet).join(","),
                                                                     NameGet.name,
                                                                     NameSet.name === undefined,
                                                                     Object.getOwnPropertyNames(LengthGet).join(","),
                                                                     LengthGet.length,
                                                                     LengthSet.length === undefined
                                                                   ].join("|");
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(),
            Is.EqualTo("length,name,prototype|pass|true|length,name,prototype|7|true"));
    }

    [Test]
    public void Class_Extends_AsyncGenerator_Function_Throws_Before_Prototype_Lookup()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   async function* fn() {}

                                                                   var out = [];

                                                                   try {
                                                                     class A extends fn {}
                                                                     out.push("no-throw-1");
                                                                   } catch (e) {
                                                                     out.push(e instanceof TypeError);
                                                                   }

                                                                   var bound = (async function* () {}).bind();
                                                                   Object.defineProperty(bound, "prototype", {
                                                                     get: function() {
                                                                       throw new Error("unreachable");
                                                                     },
                                                                   });

                                                                   try {
                                                                     class B extends bound {}
                                                                     out.push("no-throw-2");
                                                                   } catch (e) {
                                                                     out.push(e instanceof TypeError);
                                                                   }

                                                                   var proxy = new Proxy(async function* () {}, {});
                                                                   try {
                                                                     class C extends proxy {}
                                                                     out.push("no-throw-3");
                                                                   } catch (e) {
                                                                     out.push(e instanceof TypeError);
                                                                   }

                                                                   out.join(",");
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("true,true,true"));
    }

    [Test]
    public void Class_Private_Fields_After_SameLine_Async_Generator_Methods_Are_Visible()
    {
        var program = JavaScriptParser.ParseScript("""
                                                   var C = class {
                                                     async *m() { return 42; } #\u{6F};
                                                     #\u2118;
                                                     #ZW_\u200C_NJ;
                                                     #ZW_\u200D_J;
                                                     o(value) { this.#o = value; return this.#o; }
                                                     ℘(value) { this.#℘ = value; return this.#℘; }
                                                     ZW_‌_NJ(value) { this.#ZW_‌_NJ = value; return this.#ZW_‌_NJ; }
                                                     ZW_‍_J(value) { this.#ZW_‍_J = value; return this.#ZW_‍_J; }
                                                   };
                                                   """);

        var declaration = (JsVariableDeclarationStatement)program.Statements[0];
        var classExpr = (JsClassExpression)declaration.Declarators[0].Initializer!;
        Assert.That(classExpr.Elements[1].Key, Is.EqualTo("#o"));
        Assert.That(classExpr.Elements[2].Key, Is.EqualTo("#℘"));
        Assert.That(classExpr.Elements[3].Key, Is.EqualTo("#ZW_‌_NJ"));
        Assert.That(classExpr.Elements[4].Key, Is.EqualTo("#ZW_‍_J"));

        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var C = class {
                                                                     async *m() { return 42; } #\u{6F};
                                                                     #\u2118;
                                                                     #ZW_\u200C_NJ;
                                                                     #ZW_\u200D_J;
                                                                     o(value) { this.#o = value; return this.#o; }
                                                                     ℘(value) { this.#℘ = value; return this.#℘; }
                                                                     ZW_‌_NJ(value) { this.#ZW_‌_NJ = value; return this.#ZW_‌_NJ; }
                                                                     ZW_‍_J(value) { this.#ZW_‍_J = value; return this.#ZW_‍_J; }
                                                                   };

                                                                   var out = [];
                                                                   var c = new C();
                                                                   c.m().next().then(function(v) {
                                                                     out.push(v.value);
                                                                     out.push(v.done);
                                                                     out.push(c.o(1));
                                                                     out.push(c.℘(2));
                                                                     out.push(c.ZW_‌_NJ(3));
                                                                     out.push(c.ZW_‍_J(4));
                                                                   });
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        var outArray = (JsArray)realm.Global["out"].AsObject()!;
        Assert.That(outArray.TryGetElement(0, out var v0), Is.True);
        Assert.That(outArray.TryGetElement(1, out var v1), Is.True);
        Assert.That(outArray.TryGetElement(2, out var v2), Is.True);
        Assert.That(outArray.TryGetElement(3, out var v3), Is.True);
        Assert.That(outArray.TryGetElement(4, out var v4), Is.True);
        Assert.That(outArray.TryGetElement(5, out var v5), Is.True);
        Assert.That(v0.Int32Value, Is.EqualTo(42));
        Assert.That(v1.IsTrue, Is.True);
        Assert.That(v2.Int32Value, Is.EqualTo(1));
        Assert.That(v3.Int32Value, Is.EqualTo(2));
        Assert.That(v4.Int32Value, Is.EqualTo(3));
        Assert.That(v5.Int32Value, Is.EqualTo(4));
    }
}
