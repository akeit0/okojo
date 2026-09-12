using Okojo.Diagnostics;
using Okojo.JavaScript;
using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;

namespace Okojo.Tests;

public class GeneratorAbiTests
{
    [Test]
    public void Disassembler_Includes_Generator_Opcodes()
    {
        var code = new JsFunctionCode(
            [
                (byte)JsOpCode.SwitchOnGeneratorState,
                0,
                1,
                2,
                (byte)JsOpCode.SuspendGenerator,
                0,
                0,
                1,
                0,
                (byte)JsOpCode.ResumeGenerator,
                0,
                0,
                1,
                (byte)JsOpCode.Return,
            ],
            Array.Empty<ulong>(),
            Array.Empty<object>(),
            1,
            []
        );
        var script = new JsCompilationUnit(new JsFunctionDescriptor(code)).Link(
            Okojo.JavaScript.Embedding.JsRuntime.Create().DefaultRealm
        );

        var text = Disassembler.Dump(script);
        Assert.That(text, Does.Contain("SwitchOnGeneratorState"));
        Assert.That(text, Does.Contain("SuspendGenerator"));
        Assert.That(text, Does.Contain("ResumeGenerator"));
    }

    [Test]
    public void ManualGenerator_Next_Then_NextValue_Resumes_And_Completes()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        var generatorCode = new JsFunctionCode(
            [
                (byte)JsOpCode.LdaSmi,
                1,
                (byte)JsOpCode.SuspendGenerator,
                0,
                0,
                1,
                0,
                (byte)JsOpCode.ResumeGenerator,
                0,
                0,
                1,
                (byte)JsOpCode.Return,
            ],
            Array.Empty<ulong>(),
            Array.Empty<object>(),
            1,
            []
        );
        var g = new JsFunctionDescriptor(
            generatorCode,
            "G",
            kind: JsBytecodeFunctionKind.Generator
        ).CreateClosure(realm);
        realm.Global["G"] = JsValue.FromObject(g);

        var code = new JsFunctionCode(
            [
                (byte)JsOpCode.LdaGlobal,
                0,
                0,
                (byte)JsOpCode.Star,
                0,
                (byte)JsOpCode.CallUndefinedReceiver,
                0,
                0,
                0,
                (byte)JsOpCode.Star,
                1,
                (byte)JsOpCode.LdaNamedProperty,
                1,
                1,
                0,
                (byte)JsOpCode.Star,
                2,
                (byte)JsOpCode.CallProperty,
                2,
                1,
                0,
                0,
                (byte)JsOpCode.Star,
                3,
                (byte)JsOpCode.LdaNamedProperty,
                1,
                1,
                1,
                (byte)JsOpCode.Star,
                2,
                (byte)JsOpCode.LdaSmi,
                7,
                (byte)JsOpCode.Star,
                4,
                (byte)JsOpCode.CallProperty,
                2,
                1,
                4,
                1,
                (byte)JsOpCode.Return,
            ],
            Array.Empty<ulong>(),
            ["G", "next"],
            5,
            ["G", "next"],
            globalBindingSlotCount: 1
        );
        var script = new JsCompilationUnit(new JsFunctionDescriptor(code)).Link(realm);

        realm.Execute(script);

        Assert.That(realm.Accumulator.TryGetObject(out var resultObj), Is.True);
        Assert.That(
            resultObj!.TryGetPropertyAtom(realm, AtomTable.IdValue, out var value, out _),
            Is.True
        );
        var doneAtom = realm.Atoms.InternNoCheck("done");
        Assert.That(resultObj.TryGetPropertyAtom(realm, doneAtom, out var done, out _), Is.True);
        Assert.That(value.Int32Value, Is.EqualTo(7));
        Assert.That(done.IsTrue, Is.True);
    }

    [Test]
    public void CallFrameKind_Contains_GeneratorFrame()
    {
        Assert.That(Enum.IsDefined(typeof(CallFrameKind), CallFrameKind.GeneratorFrame), Is.True);
    }

    [Test]
    public void SwitchOnGeneratorState_InvalidRegister_IsSafeNoOp()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var code = new JsFunctionCode(
            [
                (byte)JsOpCode.SwitchOnGeneratorState,
                7,
                0,
                0,
                (byte)JsOpCode.LdaSmi,
                1,
                (byte)JsOpCode.Return,
            ],
            Array.Empty<ulong>(),
            Array.Empty<object>(),
            0,
            []
        );
        var script = new JsCompilationUnit(new JsFunctionDescriptor(code)).Link(realm);

        Assert.DoesNotThrow(() => realm.Execute(script));
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void SwitchOnGeneratorState_ContinuationDispatch_UsesSuspendIdTable()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        var generatorCode = new JsFunctionCode(
            [
                (byte)JsOpCode.SwitchOnGeneratorState,
                0,
                0,
                1,
                (byte)JsOpCode.LdaSmi,
                1,
                (byte)JsOpCode.SuspendGenerator,
                0xFF,
                0,
                1,
                0,
                (byte)JsOpCode.ResumeGenerator,
                0xFF,
                0,
                1,
                (byte)JsOpCode.Return,
            ],
            Array.Empty<ulong>(),
            Array.Empty<object>(),
            1,
            [],
            generatorSwitchTargets: [11] // jump target for suspend_id:0 -> ResumeGenerator
        );
        var g = new JsFunctionDescriptor(
            generatorCode,
            "G",
            kind: JsBytecodeFunctionKind.Generator
        ).CreateClosure(realm);
        realm.Global["G"] = JsValue.FromObject(g);

        var code = new JsFunctionCode(
            [
                (byte)JsOpCode.LdaGlobal,
                0,
                0,
                (byte)JsOpCode.Star,
                0,
                (byte)JsOpCode.CallUndefinedReceiver,
                0,
                0,
                0,
                (byte)JsOpCode.Star,
                1,
                (byte)JsOpCode.LdaNamedProperty,
                1,
                1,
                0,
                (byte)JsOpCode.Star,
                2,
                (byte)JsOpCode.CallProperty,
                2,
                1,
                0,
                0,
                (byte)JsOpCode.Star,
                3,
                (byte)JsOpCode.LdaNamedProperty,
                1,
                1,
                1,
                (byte)JsOpCode.Star,
                2,
                (byte)JsOpCode.LdaSmi,
                7,
                (byte)JsOpCode.Star,
                4,
                (byte)JsOpCode.CallProperty,
                2,
                1,
                4,
                1,
                (byte)JsOpCode.Return,
            ],
            Array.Empty<ulong>(),
            ["G", "next"],
            5,
            ["G", "next"],
            globalBindingSlotCount: 1
        );
        var script = new JsCompilationUnit(new JsFunctionDescriptor(code)).Link(realm);

        realm.Execute(script);

        Assert.That(realm.Accumulator.TryGetObject(out var resultObj), Is.True);
        Assert.That(
            resultObj!.TryGetPropertyAtom(realm, AtomTable.IdValue, out var value, out _),
            Is.True
        );
        var doneAtom = realm.Atoms.InternNoCheck("done");
        Assert.That(resultObj.TryGetPropertyAtom(realm, doneAtom, out var done, out _), Is.True);
        Assert.That(value.Int32Value, Is.EqualTo(7));
        Assert.That(done.IsTrue, Is.True);
    }
}
