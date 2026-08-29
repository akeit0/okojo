using Okojo.JavaScript;
using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;
using Okojo.JavaScript.Parsing;

namespace Okojo.Tests;

public class ErrorConstructorTests
{
    [Test]
    public void ErrorConstructor_CreatesObjectWithNameAndMessage()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                let e = Error("boom");
                e.name + ":" + e.message;
                """
            )
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("Error:boom"));
    }

    [Test]
    public void NotCallable_ThrowsJsRuntimeExceptionTypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                let x = 1;
                x();
                """
            )
        );

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.TypeError));
        Assert.That(ex.DetailCode, Is.EqualTo("NOT_CALLABLE"));
        Assert.That(ex.Message, Is.EqualTo("x is not a function"));
    }

    [Test]
    public void NotCallable_Message_UsesSourceLevelCallSiteNames()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                let messages = [];
                try { let x = 1; x(); } catch (e) { messages.push(e.message); }
                try { let o = { x: 1 }; o.x(); } catch (e) { messages.push(e.message); }
                try { let o = [1]; o[0](); } catch (e) { messages.push(e.message); }
                try { let f = () => undefined; f()(); } catch (e) { messages.push(e.message); }
                try { let spread = 1; spread(...[]); } catch (e) { messages.push(e.message); }
                try { let tag = 1; tag`x`; } catch (e) { messages.push(e.message); }
                try { let C = 1; new C(); } catch (e) { messages.push(e.message); }
                messages.join("|");
                """
            )
        );

        realm.Execute(script);

        Assert.That(
            realm.Accumulator.AsString(),
            Is.EqualTo(
                "x is not a function|o.x is not a function|o[0] is not a function|"
                    + "f(...) is not a function|spread is not a function|tag is not a function|"
                    + "C is not a constructor"
            )
        );
    }

    [Test]
    public void CallSiteDebugInfo_MapsCallOpcodeToSourceExpression()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                let x = () => {};
                let o = [() => {}];
                x();
                o[0]();
                """
            )
        );

        Assert.That(script.CallSiteDebugPcs, Is.Not.Null);
        var names = script
            .CallSiteDebugPcs!.Select(pc =>
            {
                Assert.That(script.TryGetCallSiteDebugNameAtPc(pc, out var name), Is.True);
                return name;
            })
            .ToArray();

        Assert.That(names, Is.EqualTo(new[] { "x", "o[0]" }));
        Assert.That(script.TryGetCallSiteDebugNameAtPc(-1, out _), Is.False);
    }

    [Test]
    public void CallSiteDebugInfo_UsesActualOpcodePc_ForWideCalls()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var parameters = string.Join(",", Enumerable.Range(0, 260).Select(i => $"p{i}"));
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript($"function f({parameters}) {{ p259(); }} f();")
        );
        var function = script
            .ObjectConstants.OfType<JsBytecodeFunction>()
            .Single(value => value.Name == "f");

        Assert.That(function.Script.CallSiteDebugPcs, Has.Length.EqualTo(1));
        var callPc = function.Script.CallSiteDebugPcs![0];
        Assert.Multiple(() =>
        {
            Assert.That(
                function.Script.Bytecode[callPc],
                Is.EqualTo((byte)JsOpCode.CallUndefinedReceiver)
            );
            Assert.That(function.Script.Bytecode[callPc - 1], Is.EqualTo((byte)JsOpCode.Wide));
            Assert.That(function.Script.TryGetCallSiteDebugNameAtPc(callPc, out var name), Is.True);
        });
        Assert.That(
            function.Script.TryGetCallSiteDebugNameAtPc(callPc, out var debugName),
            Is.True
        );
        Assert.That(debugName, Is.EqualTo("p259"));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex!.Message, Is.EqualTo("p259 is not a function"));
    }

    [Test]
    public void TypeError_CaughtObject_HasNormalizedNameAndMessage()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                let out = "";
                try {
                    let x = 1;
                    x();
                } catch (e) {
                    out = e.name + ":" + e.message + "|" + e.toString();
                }
                out;
                """
            )
        );

        realm.Execute(script);

        Assert.That(
            realm.Accumulator.AsString(),
            Is.EqualTo("TypeError:x is not a function|TypeError: x is not a function")
        );
    }

    [Test]
    public void ReferenceError_CaughtObject_HasNormalizedNameAndMessage()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                let out = "";
                try {
                    noSuchGlobal;
                } catch (e) {
                    out = e.name + ":" + e.message + "|" + e.toString();
                }
                out;
                """
            )
        );

        realm.Execute(script);

        Assert.That(
            realm.Accumulator.AsString(),
            Is.EqualTo(
                "ReferenceError:noSuchGlobal is not defined|ReferenceError: noSuchGlobal is not defined"
            )
        );
    }

    [Test]
    public void ReferenceError_CaughtObject_HasReferenceErrorConstructor()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                let ok = false;
                try {
                    noSuchGlobal;
                } catch (e) {
                    ok = (e.constructor === ReferenceError);
                }
                ok;
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void CaughtTypeError_IsInstanceOfTypeError_AndError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                let out = 0;
                try {
                    let x = 1;
                    x();
                } catch (e) {
                    if (e instanceof TypeError) out = out + 1;
                    if (e instanceof Error) out = out + 10;
                }
                out;
                """
            )
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(11));
    }

    [Test]
    public void InstanceOf_WithNonCallableRhs_ThrowsTypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                let o = {};
                o instanceof 1;
                """
            )
        );

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.TypeError));
        Assert.That(ex.DetailCode, Is.EqualTo("INSTANCEOF_RHS_NOT_CALLABLE"));
    }

    [Test]
    public void TypeErrorConstructor_CreatesTypeErrorPrototypeInstance()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                let e = TypeError("boom");
                if (e instanceof TypeError) {
                    if (e instanceof Error) 1;
                    else 0;
                } else 0;
                """
            )
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void InstanceOf_UsesSymbolHasInstance_WhenPresent()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                const C = function() {};
                Object.defineProperty(C, Symbol.hasInstance, {
                  value: function (v) { return v === 42; }
                });
                42 instanceof C;
                """
            )
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void InstanceOf_SymbolHasInstanceNonCallable_ThrowsTypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                const C = function() {};
                Object.defineProperty(C, Symbol.hasInstance, { value: 1 });
                42 instanceof C;
                """
            )
        );

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.TypeError));
        Assert.That(ex.DetailCode, Is.EqualTo("INSTANCEOF_HASINSTANCE_NOT_CALLABLE"));
    }

    [Test]
    public void SyntaxErrorConstructor_IsInstalledAndConstructable()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                const e = SyntaxError("bad");
                (typeof SyntaxError === "function") &&
                (e instanceof SyntaxError) &&
                (e instanceof Error) &&
                (e.name === "SyntaxError") &&
                (e.message === "bad");
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void URIErrorConstructor_IsInstalled_WithExpectedPrototypeSurface()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                const ctorDesc = Object.getOwnPropertyDescriptor(URIError, "prototype");
                const proto = URIError.prototype;
                const ctorProp = Object.getOwnPropertyDescriptor(proto, "constructor");
                const nameProp = Object.getOwnPropertyDescriptor(proto, "name");
                const messageProp = Object.getOwnPropertyDescriptor(proto, "message");
                [
                  typeof URIError === "function",
                  ctorDesc.writable === false,
                  ctorDesc.enumerable === false,
                  ctorDesc.configurable === false,
                  proto.constructor === URIError,
                  ctorProp.writable === true,
                  ctorProp.enumerable === false,
                  ctorProp.configurable === true,
                  nameProp.value === "URIError",
                  nameProp.writable === true,
                  nameProp.enumerable === false,
                  nameProp.configurable === true,
                  messageProp.value === "",
                  messageProp.writable === true,
                  messageProp.enumerable === false,
                  messageProp.configurable === true
                ].every(Boolean);
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void NativeErrorInstances_InheritName_AndOnlyOwnMessageWhenProvided()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                const empty = new TypeError();
                const withMessage = new TypeError("boom");
                [
                  empty.name === "TypeError",
                  Object.prototype.hasOwnProperty.call(empty, "name") === false,
                  Object.prototype.hasOwnProperty.call(empty, "message") === false,
                  withMessage.name === "TypeError",
                  Object.prototype.hasOwnProperty.call(withMessage, "name") === false,
                  Object.prototype.hasOwnProperty.call(withMessage, "message") === true,
                  withMessage.message === "boom"
                ].every(Boolean);
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Error_Subclass_Construction_Uses_Subclass_Prototype()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                class ReturnCalledError extends Error {}
                const e = new ReturnCalledError("boom");
                [
                  e instanceof ReturnCalledError,
                  e instanceof Error,
                  e.constructor === ReturnCalledError,
                  Object.getPrototypeOf(e) === ReturnCalledError.prototype
                ].join("|");
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("true|true|true|true"));
    }

    [Test]
    public void ErrorPrototypeToString_HasNonEnumerableWritableConfigurableDescriptor()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                const desc = Object.getOwnPropertyDescriptor(Error.prototype, "toString");
                [
                  typeof Error.prototype.toString === "function",
                  desc.writable === true,
                  desc.enumerable === false,
                  desc.configurable === true
                ].every(Boolean);
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ErrorPrototypeToString_Throws_On_NonObject_Receivers()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                const values = [undefined, null, 1, true, "string", Symbol("x")];
                values.every((value) => {
                  try {
                    Error.prototype.toString.call(value);
                    return false;
                  } catch (e) {
                    return e instanceof TypeError;
                  }
                });
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void UpdateExpression_OnLiteral_IsEarlyParseError()
    {
        var ex = Assert.Throws<JsParseException>(() => JavaScriptParser.ParseScript("0++;"));
        Assert.That(ex!.Message, Does.Contain("Invalid update target"));
    }
}
