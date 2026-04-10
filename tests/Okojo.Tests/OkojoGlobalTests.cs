using System.Text.RegularExpressions;
using Okojo.Compiler;
using Okojo.Diagnostics;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class OkojoGlobalTests
{
    [Test]
    public void TestGlobalThisExistsAndIsSelf()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            function t() {
                return globalThis === globalThis.globalThis;
            }
            t();
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsBool, Is.True);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TestGlobalBindingRoundtripThroughGlobalThis()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            function t() {
                x = 7;
                return globalThis.x;
            }
            t();
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsInt32, Is.True);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(7));
    }

    [Test]
    public void TestGlobalFacadeIndexerStillWorks()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Global["x"] = JsValue.FromInt32(11);
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("x;"));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsInt32, Is.True);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(11));
    }

    [Test]
    public void GlobalUriEncodeFunctions_ArePresent_OnGlobalObject()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            typeof encodeURI === "function" &&
            typeof encodeURIComponent === "function" &&
            Object.prototype.hasOwnProperty.call(globalThis, "encodeURI") &&
            Object.prototype.hasOwnProperty.call(globalThis, "encodeURIComponent");
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void GlobalUriEncodeFunctions_PreserveReservedCharacters_And_Reject_LoneSurrogates()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var reservedOk =
              encodeURI(";/?:@&=+$,#") === ";/?:@&=+$,#" &&
              encodeURIComponent(";/?:@&=+$,#") === "%3B%2F%3F%3A%40%26%3D%2B%24%2C%23";
            var loneHighEncodeUri = false;
            var loneHighEncodeUriComponent = false;
            try { encodeURI(String.fromCharCode(0xD800)); } catch (e) { loneHighEncodeUri = e instanceof URIError; }
            try { encodeURIComponent(String.fromCharCode(0xD800, 0)); } catch (e) { loneHighEncodeUriComponent = e instanceof URIError; }
            reservedOk && loneHighEncodeUri && loneHighEncodeUriComponent;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void GlobalUriDecodeFunctions_ArePresent_OnGlobalObject()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            typeof decodeURI === "function" &&
            typeof decodeURIComponent === "function" &&
            Object.prototype.hasOwnProperty.call(globalThis, "decodeURI") &&
            Object.prototype.hasOwnProperty.call(globalThis, "decodeURIComponent");
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void GlobalUriDecodeFunctions_PreserveReservedCharacters_And_Reject_MalformedUtf8()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var decodedOk =
              decodeURI("%3B%2F%3F%3A%40%26%3D%2B%24%2C%23") === "%3B%2F%3F%3A%40%26%3D%2B%24%2C%23" &&
              decodeURIComponent("%3B%2F%3F%3A%40%26%3D%2B%24%2C%23") === ";/?:@&=+$,#";
            var malformedComponent = false;
            var malformedUri = false;
            try { decodeURIComponent("%ED%BF%BF"); } catch (e) { malformedComponent = e instanceof URIError; }
            try { decodeURI("%E0%A4%A"); } catch (e) { malformedUri = e instanceof URIError; }
            decodedOk && malformedComponent && malformedUri;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TestTopLevelThisIsGlobalObjectInSloppyScript()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            this === globalThis;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsBool, Is.True);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void GlobalStore_Sloppy_UnresolvableIdentifier_CreatesGlobal()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            x = 7;
            globalThis.x === 7;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsBool, Is.True);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void GlobalStore_Strict_UnresolvableIdentifier_ThrowsReferenceError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            "use strict";
            x = 7;
            """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.ReferenceError));
    }

    [Test]
    public void GlobalStore_Sloppy_ReadOnlyGlobal_IsIgnored()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            Object.defineProperty(globalThis, "x", { value: 1, writable: false, configurable: true });
            x = 2;
            globalThis.x === 1;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsBool, Is.True);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void GlobalStore_Strict_ReadOnlyGlobal_ThrowsTypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            Object.defineProperty(globalThis, "x", { value: 1, writable: false, configurable: true });
            (function () {
              "use strict";
              x = 2;
            })();
            """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.TypeError));
    }

    [Test]
    public void GlobalStore_ForInBareIdentifier_EmitsStaGlobal()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            for (x in { a: 1 }) { }
            """));

        var disasm = Disassembler.Dump(script);
        Assert.That(disasm, Does.Contain("StaGlobal"));
        Assert.That(disasm, Does.Not.Contain("StaGlobalInit"));
    }

    [Test]
    public void GlobalVarDeclaration_ForReadOnlyBuiltinName_Is_Benign()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var NaN;
            typeof NaN === "number" && Number.isNaN(NaN);
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void GlobalStore_IcHit_Misses_After_Delete_And_Redefine_As_Accessor()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var seen = [];
            x = 1;
            function f(v) { x = v; return x; }
            f(2);
            delete globalThis.x;
            Object.defineProperty(globalThis, "x", {
              get() { return seen.length; },
              set(v) { seen.push(v); },
              configurable: true
            });
            f(3) === 1 && seen.length === 1 && seen[0] === 3;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void GlobalStore_IcHit_Misses_After_Delete_And_Recreates_Data_Global()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            x = 1;
            function store(v) { x = v; return 0; }
            store(2);
            delete globalThis.x;
            store(3);
            globalThis.x === 3;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypeofGlobal_IcHit_Misses_After_Delete()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            x = 1;
            function t() { return typeof x; }
            t();
            delete globalThis.x;
            t() === "undefined";
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void GlobalIc_Rebinds_From_Data_Global_To_Later_TopLevel_Lexical()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        realm.ExecuteProgram(JavaScriptParser.ParseScript("""
                                                          x = 1;
                                                          function readX() { return x; }
                                                          function writeX(v) { x = v; return x; }
                                                          """));

        realm.ExecuteProgram(JavaScriptParser.ParseScript("""
                                                          let x = 2;
                                                          writeX(3);
                                                          readX() === 3 && x === 3 && globalThis.x === 1;
                                                          """));

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void GlobalStore_ForInVarDeclaration_UsesGlobalInitForDeclarationAndGlobalStoreForIteration()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            for (var x in { a: 1 }) { }
            """));

        var disasm = Disassembler.Dump(script);
        Assert.That(disasm, Does.Contain("StaGlobalInit"));
        Assert.That(disasm, Does.Contain("StaGlobal "));
    }

    [Test]
    public void ForInVarDeclaration_BreakAfterTrackedBody_UsesTrimmedGenericLoopBytecode()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            function f() {
                for (var x in { a: 1 }) {
                    console.log(x);
                    if (x === 1) { break; }
                }
            }
            """));

        var f = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(fn => fn.Name == "f");
        var disasm = Disassembler.Dump(f.Script, new() { UnitKind = "function", UnitName = "f" });

        Assert.That(disasm, Does.Contain("ForInEnumerate obj:r"));
        Assert.That(disasm, Does.Contain("ForInNext enumerator:r"));
        Assert.That(disasm, Does.Contain("ForInStep enumerator:r"));
        Assert.That(disasm, Does.Not.Contain("CallRuntime runtime:ForInEnumerate"));
        Assert.That(disasm, Does.Not.Contain("CallRuntime runtime:ForInStepKey"));
        Assert.That(f.Script.RegisterCount, Is.LessThanOrEqualTo(8));
    }

    [Test]
    public void ForInVarDeclaration_FunctionBodyWithoutObservableCompletion_DoesNotEmitHoleChecks()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            function f() {
                for (var x in { a: 1 }) {
                    console.log(x);
                }
            }
            """));

        var f = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(fn => fn.Name == "f");
        var disasm = Disassembler.Dump(f.Script, new() { UnitKind = "function", UnitName = "f" });

        Assert.That(disasm, Does.Contain("ForInEnumerate obj:r"));
        Assert.That(disasm, Does.Contain("ForInNext enumerator:r"));
        Assert.That(disasm, Does.Not.Contain("TestEqualStrict"));
        Assert.That(disasm, Does.Contain("JumpIfUndefined"));
        Assert.That(disasm, Does.Not.Contain("Jump 0"));
        Assert.That(f.Script.RegisterCount, Is.LessThanOrEqualTo(6));
    }

    [Test]
    public void ForLoop_CommonBody_DoesNotEmitZeroOffsetBreakJump()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            function f() {
                for (let i = 0; i < 10; i++) {
                    console.log(x);
                }
            }
            """));

        var f = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(fn => fn.Name == "f");
        var disasm = Disassembler.Dump(f.Script, new() { UnitKind = "function", UnitName = "f" });

        Assert.That(disasm, Does.Not.Contain("Jump 0"));
        Assert.That(f.Script.RegisterCount, Is.LessThanOrEqualTo(4));
    }

    [Test]
    public void IfStatement_TopLevelTrackedCompletion_DoesNotRepeatIfStoreHoleCheck()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            if (true) { 1; } else { }
            """));

        var disasm = Disassembler.Dump(script);
        var holeCheckCount = Regex.Matches(disasm, @"TestEqualStrict").Count;

        Assert.That(holeCheckCount, Is.EqualTo(3));
    }

    [Test]
    public void ForIn_BareIdentifier_Strict_ThrowsReferenceError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            "use strict";
            for (z in { a: 1 }) { }
            """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.ReferenceError));
    }

    [Test]
    public void ForIn_BareIdentifier_Sloppy_CreatesGlobal()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            for (z in { a: 1 }) { }
            z === "a";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsBool, Is.True);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void VarInsideDoWhile_HoistsToTopLevelScope()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            do var x; while (false);
            x === undefined;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsBool, Is.True);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void GlobalVarDeclaration_IsEnumerable_Before_SourceDeclaration_Executes()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var enumed = false;
            for (var p in this) {
              if (p === "__declared__var")
                enumed = true;
            }

            var __declared__var;
            enumed;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
