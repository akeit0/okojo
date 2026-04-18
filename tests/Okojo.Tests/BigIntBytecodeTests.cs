using System.Numerics;
using Okojo.Bytecode;
using Okojo.Compiler;
using Okojo.Diagnostics;
using Okojo.Parsing;
using Okojo.Runtime;
using Okojo.Values;

namespace Okojo.Tests;

public class BigIntBytecodeTests
{
    [Test]
    public void Compiler_Emits_LdaTypedConst_For_BigInt_Literal()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("1n;"));

        Assert.That(script.Bytecode.Contains((byte)JsOpCode.LdaTypedConst), Is.True);
        Assert.That(script.ObjectConstants.OfType<JsBigInt>().Any(b => b.Value == 1), Is.True);
    }

    [Test]
    public void Vm_Loads_BigInt_Through_LdaTypedConst()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsScript(
            [
                (byte)JsOpCode.LdaTypedConst, (byte)Tag.JsTagBigInt, 0,
                (byte)JsOpCode.Return
            ],
            Array.Empty<double>(),
            [new JsBigInt(1)],
            0,
            Array.Empty<int>()
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsBigInt, Is.True);
        Assert.That(realm.Accumulator.AsBigInt().Value, Is.EqualTo(new BigInteger(1)));
    }

    [Test]
    public void Disassembler_Formats_LdaTypedConst()
    {
        var script = new JsScript(
            [
                (byte)JsOpCode.LdaTypedConst, (byte)Tag.JsTagBigInt, 0,
                (byte)JsOpCode.Return
            ],
            Array.Empty<double>(),
            [new JsBigInt(1)],
            0,
            Array.Empty<int>()
        );

        var text = Disassembler.Dump(script);

        Assert.That(text, Does.Contain("LdaTypedConst tag:JsTagBigInt, const:0"));
        Assert.That(text, Does.Contain("1n"));
    }
}
