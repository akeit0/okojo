using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class CompilerFeatureSupportTests
{
    [Test]
    public void DoWhileStatement_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function t() {
                                                                       let i = 0;
                                                                       let s = 0;
                                                                       do {
                                                                           s = s + i;
                                                                           i = i + 1;
                                                                       } while (i < 4);
                                                                       return s;
                                                                   }
                                                                   t();
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsInt32, Is.True);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(6));
    }

    [Test]
    public void NestedFunction_Captures_OuterBinding_Inside_DoWhile()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function outer() {
                                                                       var limit = 25;
                                                                       function inner() {
                                                                           var count = 0;
                                                                           do {
                                                                               if (count >= limit) {
                                                                                   return limit;
                                                                               }
                                                                               count = count + 1;
                                                                           } while (count < 1);
                                                                           return count;
                                                                       }
                                                                       return inner();
                                                                   }
                                                                   outer();
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsInt32, Is.True);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void SequenceExpression_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   let x = 0;
                                                                   (x = 1, x = x + 2, x);
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsInt32, Is.True);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(3));
    }

    [Test]
    public void DebuggerStatement_IsAccepted()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   let x = 1;
                                                                   debugger;
                                                                   x + 2;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsInt32, Is.True);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(3));
    }

    [Test]
    public void ForStatement_Can_Start_With_AsyncOf_ArrowInitializer()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var i = 0;
                                                                   var counter = 0;
                                                                   for (async of => {}; i < 10; ++i) {
                                                                       ++counter;
                                                                   }
                                                                   counter;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsInt32, Is.True);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(10));
    }

    [Test]
    public void CompileHoistedFunctionTemplate_Uses_Provided_IdentifierTable_For_Parameter_Capture()
    {
        const string source = """
                              "use strict";
                              (function () {
                                exports.answer = 42;
                              })();
                              """;
        const string wrapperPrefix = "(function (exports, require, module, __filename, __dirname) {\n";
        const string wrapperSuffix = "\n})";
        var wrapperSource = wrapperPrefix + source + wrapperSuffix;

        var realm = JsRuntime.Create().DefaultRealm;
        var parsed = JavaScriptParser.ParseScript(
            wrapperSource,
            "/app/main.cjs",
            -wrapperPrefix.Length,
            source);
        var wrapperExpression = (JsFunctionExpression)((JsExpressionStatement)parsed.Statements[0]).Expression;
        var compiler = new JsCompiler(realm);
        var wrapper = compiler.CompileHoistedFunctionTemplate(
            wrapperExpression,
            string.Empty,
            wrapperSource,
            parsed.SourcePath,
            parsed.IdentifierTable);

        var exportsObject = new JsPlainObject(realm);
        _ = realm.Call(
            wrapper,
            JsValue.Undefined,
            JsValue.FromObject(exportsObject),
            JsValue.Undefined,
            JsValue.Undefined,
            JsValue.FromString("/app/main.cjs"),
            JsValue.FromString("/app"));

        Assert.That(exportsObject.TryGetProperty("answer", out var answer), Is.True);
        Assert.That(answer.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void ObjectLiteral_Method_DefaultParameter_Uses_Parameter_Binding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   const obj = {
                                                                     annotation(message, options = {}) {
                                                                       return options;
                                                                     }
                                                                   };
                                                                   obj.annotation("x");
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.TryGetObject(out var resultObject), Is.True);
        Assert.That(resultObject, Is.Not.Null);
    }

    [Test]
    public void ObjectLiteral_Arrow_DefaultParameter_Uses_Parameter_Binding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   const obj = {
                                                                     setCwd: (cwd = "x") => cwd
                                                                   };
                                                                   obj.setCwd();
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("x"));
    }

    [Test]
    public void ForOf_LoopHead_Binding_Captured_By_Nested_Function_Gets_Context_Slot()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function wrapAssembly() {
                                                                     function patch(prototype, name, fn) {
                                                                       return fn;
                                                                     }

                                                                     for (const fnName of ['setWidth']) {
                                                                       patch({}, fnName, function (original) {
                                                                         throw new Error(`Invalid value for ${fnName}`);
                                                                       });
                                                                     }

                                                                     return 1;
                                                                   }

                                                                   wrapAssembly();
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsInt32, Is.True);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void ForOf_LoopHead_Const_Captured_By_Multiple_Closures_Preserves_Per_Iteration_Value()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function wrap() {
                                                                     const fns = {};
                                                                     for (const fnName of ['setWidth', 'setGap']) {
                                                                       const methods = {
                                                                         point(...args) {
                                                                           return `${fnName}:${args.length}:${args.join('|')}`;
                                                                         }
                                                                       };
                                                                       fns[fnName] = function (...args) {
                                                                         const value = args.pop();
                                                                         return methods.point.call(this, ...args, value);
                                                                       };
                                                                     }

                                                                     return `${fns.setWidth(20)}|${fns.setGap(1, 20)}`;
                                                                   }

                                                                   wrap();
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("setWidth:1:20|setGap:2:1|20"));
    }

    [Test]
    public void ForLoop_LoopHead_Binding_Captured_By_Nested_Function_Gets_Context_Slot()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function wrap() {
                                                                     for (let i = 0; i < 1; i++) {
                                                                       const read = function () {
                                                                         return `${i}`;
                                                                       };
                                                                       return read();
                                                                     }
                                                                   }

                                                                   wrap();
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("0"));
    }

    [Test]
    public void BlockLexical_Captured_By_Nested_Arrow_Gets_Context_Slot()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function help(commands, base$0, parentCommands) {
                                                                     if (commands.length) {
                                                                       const prefix = base$0 ? `${base$0} ` : '';
                                                                       commands.forEach(command => {
                                                                         const commandString = `${prefix}${parentCommands}${command[0]}`;
                                                                         return commandString;
                                                                       });
                                                                     }
                                                                     return 1;
                                                                   }

                                                                   help([['x']], 'cli', 'sub ');
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsInt32, Is.True);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(1));
    }
}
