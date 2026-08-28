using Okojo.JavaScript;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Parsing;

namespace Okojo.Tests;

/// <summary>
///     C3 block-lexical TDZ hole-initialization elision: hole-init stores in
///     loop-body blocks are elided when no read can observe the hole, and kept
///     in every case where TDZ semantics are observable.
/// </summary>
public class C3TdzElisionTests
{
    private static object Eval(string source)
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript(source));
        realm.Execute(script);
        return realm.Accumulator.TryRead<object>(out var value) ? value : realm.Accumulator;
    }

    [Test]
    public void Elision_ComputedConstInitializers_InLoopBody_KeepPerIterationValues()
    {
        var result = Eval(
            """
            function f() {
                let s = 0;
                for (let y = 0; y < 4; y++) {
                    const t = y * 10;
                    s += t;
                }
                return s;
            }
            f();
            """
        );
        Assert.That(result, Is.EqualTo(60d));
    }

    [Test]
    public void Elision_StopwatchShape_MultipleBindings_MatchHoleInitializedResult()
    {
        var result = Eval(
            """
            function f() {
                let s = "";
                for (let y = 0; y < 3; y++) {
                    const a = y + 1;
                    const b = a * 2;
                    const c = a + b;
                    s += c;
                }
                return s;
            }
            f();
            """
        );
        Assert.That(result, Is.EqualTo("369"));
    }

    [Test]
    public void Elision_ClosureCreatedAfterDeclaration_ReadsInitializedValue()
    {
        var result = Eval(
            """
            function f() {
                let out = 0;
                for (let y = 0; y < 3; y++) {
                    let t = y * 5;
                    let g = () => t;
                    out += g();
                }
                return out;
            }
            f();
            """
        );
        Assert.That(result, Is.EqualTo(15d));
    }

    [Test]
    public void Elision_WithContextSlotStorage_KeepPerIterationValues()
    {
        var result = Eval(
            """
            function f() {
                let s = 0;
                let callbacks = [];
                for (let y = 0; y < 3; y++) {
                    let t = y * 2;
                    callbacks.push(() => t);
                    s += t;
                }
                let total = 0;
                for (let i = 0; i < callbacks.length; i++) total += callbacks[i]();
                return s * 100 + total;
            }
            f();
            """
        );
        // s = 0 + 2 + 4 = 6; captured values 0, 2, 4 -> total 6
        Assert.That(result, Is.EqualTo(606d));
    }

    [Test]
    public void Kept_ReadBeforeDeclaration_InLoopBody_ThrowsReferenceError()
    {
        var ex = Assert.Throws<JsRuntimeException>(() =>
            Eval(
                """
                function f() {
                    for (let y = 0; y < 3; y++) {
                        if (y >= 0) s = t;
                        let t = y + 1;
                    }
                }
                f();
                """
            )
        );
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.ReferenceError));
    }

    [Test]
    public void Kept_InitializerSelfReference_ThrowsReferenceError()
    {
        var ex = Assert.Throws<JsRuntimeException>(() =>
            Eval(
                """
                function f() {
                    for (let y = 0; y < 3; y++) {
                        let t = t + 1;
                    }
                }
                f();
                """
            )
        );
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.ReferenceError));
    }

    [Test]
    public void Kept_IifeInInitializer_ReadsBindingBeforeInitialization_ThrowsReferenceError()
    {
        var ex = Assert.Throws<JsRuntimeException>(() =>
            Eval(
                """
                function f() {
                    for (let y = 0; y < 3; y++) {
                        let t = (function () { return t; })();
                    }
                }
                f();
                """
            )
        );
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.ReferenceError));
    }

    [Test]
    public void Kept_ClosureCreatedBeforeDeclaration_CalledBeforeInitialization_ThrowsReferenceError()
    {
        var ex = Assert.Throws<JsRuntimeException>(() =>
            Eval(
                """
                function f() {
                    for (let y = 0; y < 3; y++) {
                        let g = () => t;
                        g();
                        let t = y + 1;
                    }
                }
                f();
                """
            )
        );
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.ReferenceError));
    }

    [Test]
    public void Kept_AssignmentBeforeDeclaration_InLoopBody_ThrowsReferenceError()
    {
        var ex = Assert.Throws<JsRuntimeException>(() =>
            Eval(
                """
                function f() {
                    for (let y = 0; y < 3; y++) {
                        t = y;
                        let t = y + 1;
                    }
                }
                f();
                """
            )
        );
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.ReferenceError));
    }

    [Test]
    public void Kept_CapturedBinding_WithHoistedBlockFunctionDeclaration_ThrowsReferenceError()
    {
        var ex = Assert.Throws<JsRuntimeException>(() =>
            Eval(
                """
                function f() {
                    for (let y = 0; y < 3; y++) {
                        g();
                        let t = y + 1;
                        function g() { return t; }
                    }
                }
                f();
                """
            )
        );
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.ReferenceError));
    }

    [Test]
    public void Elision_ClosureCalledAcrossIterations_ReadsOwnIterationValue()
    {
        var result = Eval(
            """
            function f() {
                let callbacks = [];
                for (let y = 0; y < 3; y++) {
                    const t = y * 3;
                    if (y === 1) callbacks.push(() => t);
                }
                return callbacks[0]();
            }
            f();
            """
        );
        Assert.That(result, Is.EqualTo(3d));
    }
}
