using Okojo.Bytecode;
using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Tests;

public class FrameAbiTests
{
    [Test]
    public void ManualConstruct_BytecodeConstructor_SeesNewTarget()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        var ctorScript = new JsScript(
            [
                (byte)JsOpCode.LdaNewTarget,
                (byte)JsOpCode.Return
            ],
            Array.Empty<double>(),
            Array.Empty<object>(),
            0,
            Array.Empty<int>()
        );
        var ctor = new JsBytecodeFunction(realm, ctorScript, "Ctor");
        realm.Global["Ctor"] = JsValue.FromObject(ctor);

        var atom = realm.Atoms.InternNoCheck("Ctor");
        var script = new JsScript(
            [
                (byte)JsOpCode.LdaGlobal, 0, 0,
                (byte)JsOpCode.Star, 0,
                (byte)JsOpCode.Construct, 0, 0, 0,
                (byte)JsOpCode.Return
            ],
            Array.Empty<double>(),
            ["Ctor"],
            1,
            [atom], GlobalBindingIcEntries: new GlobalBindingIcEntry[1]
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.TryGetObject(out var result), Is.True);
        Assert.That(ReferenceEquals(result, ctor), Is.True);
    }

    [Test]
    public void ManualConstruct_PrimitiveReturn_FallsBackToReceiverObject()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        var ctorScript = new JsScript(
            [
                (byte)JsOpCode.LdaSmi, 7,
                (byte)JsOpCode.Return
            ],
            Array.Empty<double>(),
            Array.Empty<object>(),
            0,
            Array.Empty<int>()
        );
        var ctor = new JsBytecodeFunction(realm, ctorScript, "Ctor");
        realm.Global["Ctor"] = JsValue.FromObject(ctor);

        var atom = realm.Atoms.InternNoCheck("Ctor");
        var script = new JsScript(
            [
                (byte)JsOpCode.LdaGlobal, 0, 0,
                (byte)JsOpCode.Star, 0,
                (byte)JsOpCode.Construct, 0, 0, 0,
                (byte)JsOpCode.Return
            ],
            Array.Empty<double>(),
            ["Ctor"],
            1,
            [atom], GlobalBindingIcEntries: new GlobalBindingIcEntry[1]
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsObject, Is.True);
        Assert.That(realm.Accumulator.AsObject(), Is.TypeOf<JsPlainObject>());
    }

    [Test]
    public void HostCall_PushesHostExitFrame_WithCorrectArgMetadata()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var observedKind = CallFrameKind.ScriptFrame;
        var observedArgCount = -1;
        var observedFlags = CallFrameFlag.None;
        var observedStackArg = JsValue.Undefined;
        var observedArgOffset = -1;

        var host = new JsHostFunction(realm, (in info) =>
        {
            observedKind = info.FrameKind;
            observedArgCount = info.ArgumentCount;
            observedFlags = info.Flags;
            observedArgOffset = info.ArgumentOffset;
            observedStackArg = info.Arguments[0];
            return info.Arguments[0];
        }, "h", 0);

        realm.Global["h"] = JsValue.FromObject(host);
        var atom = realm.Atoms.InternNoCheck("h");

        var script = new JsScript(
            [
                (byte)JsOpCode.LdaGlobal, 0, 0,
                (byte)JsOpCode.Star, 0,
                (byte)JsOpCode.LdaSmi, 5,
                (byte)JsOpCode.Star, 1,
                (byte)JsOpCode.CallUndefinedReceiver, 0, 1, 1,
                (byte)JsOpCode.Return
            ],
            Array.Empty<double>(),
            ["h"],
            2,
            [atom], GlobalBindingIcEntries: new GlobalBindingIcEntry[1]
        );

        realm.Execute(script);

        Assert.That(observedKind, Is.EqualTo(CallFrameKind.HostExitFrame));
        Assert.That(observedArgCount, Is.EqualTo(1));
        Assert.That(observedFlags, Is.EqualTo(CallFrameFlag.None));
        Assert.That(observedArgOffset, Is.EqualTo(JsRealm.HeaderSize + 1));
        Assert.That(observedStackArg.Int32Value, Is.EqualTo(5));
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(5));
    }

    [Test]
    public void HostConstruct_SetsConstructorFlagAndNewTarget()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var observedFlag = CallFrameFlag.None;
        var observedNewTargetIsObject = false;
        var observedKind = CallFrameKind.ScriptFrame;

        var ctor = new JsHostFunction(realm, (in info) =>
        {
            observedKind = info.FrameKind;
            observedFlag = info.Flags;
            observedNewTargetIsObject = info.NewTarget.IsObject;
            return info.ThisValue;
        }, "HostCtor", 0, true);

        realm.Global["HostCtor"] = JsValue.FromObject(ctor);
        var atom = realm.Atoms.InternNoCheck("HostCtor");

        var script = new JsScript(
            [
                (byte)JsOpCode.LdaGlobal, 0, 0,
                (byte)JsOpCode.Star, 0,
                (byte)JsOpCode.Construct, 0, 0, 0,
                (byte)JsOpCode.Return
            ],
            Array.Empty<double>(),
            ["HostCtor"],
            1,
            [atom], GlobalBindingIcEntries: new GlobalBindingIcEntry[1]
        );

        realm.Execute(script);

        Assert.That(observedKind, Is.EqualTo(CallFrameKind.HostExitFrame));
        Assert.That((observedFlag & CallFrameFlag.IsConstructorCall) != 0, Is.True);
        Assert.That(observedNewTargetIsObject, Is.True);
        Assert.That(realm.Accumulator.IsObject, Is.True);
    }

    [Test]
    public void LdaNewTarget_OutsideConstruct_IsUndefined()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsScript(
            [
                (byte)JsOpCode.LdaNewTarget,
                (byte)JsOpCode.Return
            ],
            Array.Empty<double>(),
            Array.Empty<object>(),
            0,
            Array.Empty<int>()
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsUndefined, Is.True);
    }

    [Test]
    public void DerivedConstructor_LdaThis_BeforeSuper_Throws()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        var derivedScript = new JsScript(
            [
                (byte)JsOpCode.LdaThis,
                (byte)JsOpCode.Return
            ],
            Array.Empty<double>(),
            Array.Empty<object>(),
            0,
            Array.Empty<int>()
        );
        var derived = new JsBytecodeFunction(realm, derivedScript, "Derived", isDerivedConstructor: true);
        realm.Global["Derived"] = JsValue.FromObject(derived);
        var atom = realm.Atoms.InternNoCheck("Derived");

        var script = new JsScript(
            [
                (byte)JsOpCode.LdaGlobal, 0, 0,
                (byte)JsOpCode.Star, 0,
                (byte)JsOpCode.Construct, 0, 0, 0,
                (byte)JsOpCode.Return
            ],
            Array.Empty<double>(),
            ["Derived"],
            1,
            [atom], GlobalBindingIcEntries: new GlobalBindingIcEntry[1]
        );

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex!.Message, Does.Contain("Must call super constructor"));
    }

    [Test]
    public void DerivedConstructor_ReturnWithoutSuper_Throws()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        var derivedScript = new JsScript(
            [
                (byte)JsOpCode.LdaSmi, 1,
                (byte)JsOpCode.Return
            ],
            Array.Empty<double>(),
            Array.Empty<object>(),
            0,
            Array.Empty<int>()
        );
        var derived = new JsBytecodeFunction(realm, derivedScript, "Derived", isDerivedConstructor: true);
        realm.Global["Derived"] = JsValue.FromObject(derived);
        var atom = realm.Atoms.InternNoCheck("Derived");

        var script = new JsScript(
            [
                (byte)JsOpCode.LdaGlobal, 0, 0,
                (byte)JsOpCode.Star, 0,
                (byte)JsOpCode.Construct, 0, 0, 0,
                (byte)JsOpCode.Return
            ],
            Array.Empty<double>(),
            ["Derived"],
            1,
            [atom], GlobalBindingIcEntries: new GlobalBindingIcEntry[1]
        );

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex!.Message, Does.Contain("Derived constructors may only return object or undefined"));
    }

    [Test]
    public void DerivedConstructor_ReturnPrimitiveAfterSuper_Throws()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                class Base {}
                                class Derived extends Base {
                                  constructor() {
                                    super();
                                    return 1;
                                  }
                                }

                                try {
                                  new Derived();
                                  false;
                                } catch (e) {
                                  e && e.name === "TypeError";
                                }
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void DerivedConstructor_ReturnUndefinedAfterSuper_ReturnsThis()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                class Base {
                                  constructor() { this.v = 1; }
                                }
                                class Derived extends Base {
                                  constructor() {
                                    super();
                                    return;
                                  }
                                }
                                var o = new Derived();
                                o.v === 1 && o instanceof Derived && o instanceof Base;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }
}
