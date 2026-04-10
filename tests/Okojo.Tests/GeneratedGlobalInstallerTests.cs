using Okojo.Annotations;
using Okojo.DocGenerator.Annotations;
using Okojo.Runtime;

namespace Okojo.Tests;

[GenerateJsGlobals]
[DocDeclaration("globals")]
internal sealed partial class GeneratedGlobalInstallerSample
{
    public int WidthValue { get; set; } = 320;

    [JsGlobalProperty("width")] public int Width => WidthValue;

    [JsGlobalProperty("strokeWidth", Writable = true)]
    public int StrokeWidth { get; set; } = 2;

    public string LastBackground { get; private set; } = string.Empty;

    /// <summary>Sets the background color from a named color string.</summary>
    [JsGlobalFunction("background")]
    private void Background(string color)
    {
        LastBackground = $"named:{color}";
    }

    /// <summary>Sets the background color from a grayscale value.</summary>
    [JsGlobalFunction("background")]
    private void Background(byte gray)
    {
        LastBackground = $"gray:{gray}";
    }

    /// <summary>Sets the background color from RGB values.</summary>
    [JsGlobalFunction("background")]
    private void Background(byte r, byte g, byte b)
    {
        LastBackground = $"{r},{g},{b},255";
    }

    /// <summary>Sets the background color from RGBA values.</summary>
    [JsGlobalFunction("background")]
    private void Background(byte r, byte g, byte b, byte a)
    {
        LastBackground = $"{r},{g},{b},{a}";
    }

    [JsGlobalFunction("sumNumbers")]
    private int SumNumbers(ReadOnlySpan<int> values)
    {
        var sum = 0;
        foreach (var value in values)
            sum += value;
        return sum;
    }

    [JsGlobalFunction("describeAny")]
    private string DescribeAny(ReadOnlySpan<object> values)
    {
        if (values.Length == 0)
            return string.Empty;

        var parts = new string[values.Length];
        for (var i = 0; i < values.Length; i++)
            parts[i] = values[i]?.ToString() ?? "null";
        return string.Join("|", parts);
    }

    [JsGlobalFunction("pick")]
    private string Pick(string value)
    {
        return $"string:{value}";
    }

    [JsGlobalFunction("pick")]
    private string Pick(int value)
    {
        return $"number:{value}";
    }
}

public class GeneratedGlobalInstallerTests
{
    [Test]
    public void Generated_Global_Installer_Exposes_Typed_Function_And_Properties()
    {
        var sample = new GeneratedGlobalInstallerSample();
        using var runtime = JsRuntime.CreateBuilder()
            .UseGlobals(sample.InstallGeneratedGlobals)
            .Build();
        var realm = runtime.MainRealm;

        _ = realm.Eval("background('navy');");
        Assert.That(sample.LastBackground, Is.EqualTo("named:navy"));
        _ = realm.Eval("background(10);");
        Assert.That(sample.LastBackground, Is.EqualTo("gray:10"));
        _ = realm.Eval("background(10, 20, 30);");
        Assert.That(sample.LastBackground, Is.EqualTo("10,20,30,255"));
        _ = realm.Eval("background(10, 20, 30, 40);");
        Assert.That(sample.LastBackground, Is.EqualTo("10,20,30,40"));

        Assert.That(realm.Eval("width").NumberValue, Is.EqualTo(320d));
        sample.WidthValue = 512;
        Assert.That(realm.Eval("width").NumberValue, Is.EqualTo(512d));

        _ = realm.Eval("strokeWidth = 9;");
        Assert.That(sample.StrokeWidth, Is.EqualTo(9));
        Assert.That(realm.Eval("strokeWidth").NumberValue, Is.EqualTo(9d));

        Assert.That(realm.Eval("sumNumbers(1, 2, 3, 4)").NumberValue, Is.EqualTo(10d));
        Assert.That(realm.Eval("describeAny(1, 'x', true)").AsString(), Is.EqualTo("1|x|True"));
        Assert.That(realm.Eval("pick('x')").AsString(), Is.EqualTo("string:x"));
        Assert.That(realm.Eval("pick(7)").AsString(), Is.EqualTo("number:7"));
    }
}
