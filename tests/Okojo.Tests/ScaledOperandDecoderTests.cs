using System.Reflection;
using Okojo.JavaScript;
using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;

namespace Okojo.Tests;

public class ScaledOperandDecoderTests
{
    private delegate int Decoder(ref byte pc, ref int offset, BytecodeInfo.OperandScale scale);

    private static Decoder GetDecoder() =>
        typeof(JsRealm)
            .GetMethod("ReadScaledUnsignedOperand", BindingFlags.NonPublic | BindingFlags.Static)!
            .CreateDelegate<Decoder>();

    [Test]
    public void ExtraWide_PreservesHighBitsAndAdvancesFromNonzeroOffset()
    {
        var decode = GetDecoder();
        byte[] bytes = [0xFF, 0x78, 0x56, 0x34, 0x12, 0x21, 0x43, 0x65, 0x07, 42];
        int offset = 1;

        Assert.That(
            decode(ref bytes[0], ref offset, BytecodeInfo.OperandScale.ExtraWide),
            Is.EqualTo(0x12345678)
        );
        Assert.That(offset, Is.EqualTo(5));
        Assert.That(
            decode(ref bytes[0], ref offset, BytecodeInfo.OperandScale.ExtraWide),
            Is.EqualTo(0x07654321)
        );
        Assert.That(offset, Is.EqualTo(9));
        Assert.That(
            decode(ref bytes[0], ref offset, BytecodeInfo.OperandScale.Single),
            Is.EqualTo(42)
        );
        Assert.That(offset, Is.EqualTo(bytes.Length));
    }

    [Test]
    public void InvalidScale_ThrowsWithoutAdvancingOffset()
    {
        var decode = GetDecoder();
        byte[] bytes = [0, 1, 2, 3, 4];
        int offset = 1;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            decode(ref bytes[0], ref offset, (BytecodeInfo.OperandScale)3)
        );
        Assert.That(offset, Is.EqualTo(1));
    }

    [TestCase(2)]
    [TestCase(4)]
    public void ScaledKeyedStore_ReadsBothOperandsAndResetsScale(int width)
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        List<byte> bytes =
        [
            (byte)JsOpCode.CreateEmptyObjectLiteral,
            (byte)JsOpCode.StarWide,
            0x2C,
            0x01,
            (byte)JsOpCode.Star,
            0,
            (byte)JsOpCode.LdaSmi,
            42,
            (byte)JsOpCode.StarWide,
            0x2D,
            0x01,
            (byte)JsOpCode.LdaSmi,
            42,
            (byte)(width == 2 ? JsOpCode.Wide : JsOpCode.ExtraWide),
            (byte)JsOpCode.StaKeyedProperty,
        ];
        foreach (int register in new[] { 300, 301 })
            for (int i = 0; i < width; i++)
                bytes.Add((byte)(register >> (8 * i)));
        // A narrow property load immediately after the prefixed two-operand store
        // detects incorrect total advancement and scale leakage.
        bytes.AddRange([(byte)JsOpCode.LdaKeyedProperty, 0, (byte)JsOpCode.Return]);
        var code = new JsFunctionCode(
            bytes.ToArray(),
            Array.Empty<ulong>(),
            Array.Empty<object>(),
            302,
            []
        );
        var script = new JsCompilationUnit(new JsFunctionDescriptor(code)).Link(realm);

        realm.Execute(script);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(42));
    }
}
