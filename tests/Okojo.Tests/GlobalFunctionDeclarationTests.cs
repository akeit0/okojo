using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class GlobalFunctionDeclarationTests
{
    private static void InstallTest262EvalScript(JsRealm realm)
    {
        var evalScript = new JsHostFunction(realm, (in info) =>
        {
            var innerRealm = info.Realm;
            var args = info.Arguments;
            var source = args.Length > 0 ? args[0].ToString() : string.Empty;
            try
            {
                var program = JavaScriptParser.ParseScript(source);
                return innerRealm.ExecuteProgramInline(program);
            }
            catch (JsParseException ex)
            {
                throw new JsRuntimeException(JsErrorKind.SyntaxError, ex.Message, "TEST262_EVALSCRIPT_PARSE");
            }
        }, "evalScript", 1);

        var test262 = new JsPlainObject(realm);
        test262.SetProperty("evalScript", JsValue.FromObject(evalScript));
        realm.Global["$262"] = JsValue.FromObject(test262);
    }

    [Test]
    public void GlobalFunctionDeclaration_CreatesOwnProperty_WithNonConfigurableDescriptor()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   Object.prototype.hasOwnProperty.call(this, "brandNew") === true &&
                                                                   Object.getOwnPropertyDescriptor(this, "brandNew").writable === true &&
                                                                   Object.getOwnPropertyDescriptor(this, "brandNew").enumerable === true &&
                                                                   Object.getOwnPropertyDescriptor(this, "brandNew").configurable === false;
                                                                   function brandNew() {}
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void GlobalFunctionDeclaration_Strict_CreatesOwnProperty_WithNonConfigurableDescriptor()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   'use strict';
                                                                   Object.prototype.hasOwnProperty.call(this, "brandNew") === true &&
                                                                   Object.getOwnPropertyDescriptor(this, "brandNew").writable === true &&
                                                                   Object.getOwnPropertyDescriptor(this, "brandNew").enumerable === true &&
                                                                   Object.getOwnPropertyDescriptor(this, "brandNew").configurable === false;
                                                                   function brandNew() {}
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void GlobalLexicalDeclaration_ShadowsConfigurableGlobalWithoutChangingDescriptor()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let Array;
                                                                   var descriptor = Object.getOwnPropertyDescriptor(this, "Array");
                                                                   descriptor.configurable === true &&
                                                                   descriptor.enumerable === false &&
                                                                   descriptor.writable === true;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void GlobalVarDeclaration_CreatesOwnProperty_BeforeStatementExecution()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var before = Object.getOwnPropertyDescriptor(this, "x");
                                                                   var x;
                                                                   before.value === undefined &&
                                                                   before.writable === true &&
                                                                   before.enumerable === true &&
                                                                   before.configurable === false;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void GlobalFunctionDeclaration_DuplicateInEval_LastDeclarationWins()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   eval("function f(){return 1;}function f(){return 2;}function f(){return 3;}");
                                                                   f() === 3;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Test262Style_EvalScript_DuplicateFunctionDeclarations_LastWins()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        InstallTest262EvalScript(realm);

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            $262.evalScript(
              'function f() { return 1; }' +
              'function f() { return 2; }' +
              'function f() { return 3; }'
            );
            f() === 3;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Test262Style_EvalScript_LexicalDeclarations_Create_GlobalLexicalBindings()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        InstallTest262EvalScript(realm);

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            Object.preventExtensions(this);
            $262.evalScript('let test262let = 1;');
            test262let = 2;
            $262.evalScript('const test262const = 3;');
            var constThrows = false;
            try {
              test262const = 4;
            } catch (e) {
              constThrows = e.constructor === TypeError;
            }
            $262.evalScript('class test262class {}');
            test262class = 5;
            test262let === 2 &&
              constThrows &&
              test262const === 3 &&
              test262class === 5 &&
              this.hasOwnProperty('test262let') === false &&
              this.hasOwnProperty('test262const') === false &&
              this.hasOwnProperty('test262class') === false;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Test262Style_EvalScript_GlobalFunctionDeclarations_Are_NonConfigurable()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        InstallTest262EvalScript(realm);

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            $262.evalScript('function brandNew() {}');
            Object.defineProperty(this, 'existingConfigurable', { configurable: true, value: 0 });
            Object.defineProperty(this, 'nonConfigurable', {
              configurable: false,
              writable: true,
              enumerable: true,
              value: 0
            });
            Object.preventExtensions(this);
            $262.evalScript('function existingConfigurable() {}');
            $262.evalScript('function nonConfigurable() {}');
            var brandNewDesc = Object.getOwnPropertyDescriptor(this, 'brandNew');
            var existingConfigurableDesc = Object.getOwnPropertyDescriptor(this, 'existingConfigurable');
            var nonConfigurableDesc = Object.getOwnPropertyDescriptor(this, 'nonConfigurable');
            typeof brandNewDesc.value === 'function' &&
              brandNewDesc.writable === true &&
              brandNewDesc.enumerable === true &&
              brandNewDesc.configurable === false &&
              typeof existingConfigurableDesc.value === 'function' &&
              existingConfigurableDesc.writable === true &&
              existingConfigurableDesc.enumerable === true &&
              existingConfigurableDesc.configurable === false &&
              typeof nonConfigurableDesc.value === 'function' &&
              nonConfigurableDesc.writable === true &&
              nonConfigurableDesc.enumerable === true &&
              nonConfigurableDesc.configurable === false;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void GlobalFunctionDeclaration_NonConfigurableProperty_CannotBeRedefinedByDefineProperty()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            function data1() {}
            try {
              Object.defineProperty(this, "data1", { configurable: false, value: 0, writable: true, enumerable: false });
              false;
            } catch (e) {
              e instanceof TypeError;
            }
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void GlobalVar_DoesNotOverrideHoistedFunctionBeforeAssignment()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var result = typeof __decl === "function";
            var __decl = 1;
            function __decl(){ return 1; }
            result;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void FunctionScopeVar_DoesNotOverrideHoistedFunctionBeforeAssignment()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            (function () {
              return typeof __decl === "function";
              var __decl = 1;
              function __decl(){ return 1; }
            })();
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
