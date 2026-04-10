using System.Text;
using Okojo.Runtime;
using Okojo.Values;

namespace Okojo.Tests;

public class JsStringTests
{
    [Test]
    public void JsString_Concat_And_Slice_Preserve_Content()
    {
        var value = JsString.Empty;
        var expected = new StringBuilder();

        for (var i = 0; i < 256; i++)
        {
            var part = i.ToString("D3");
            value = JsString.Concat(value, part);
            expected.Append(part);
        }

        var expectedText = expected.ToString();
        var slice = value.Slice(90, 120);

        Assert.That(value.Length, Is.EqualTo(expectedText.Length));
        Assert.That(value.Flatten(), Is.EqualTo(expectedText));
        Assert.That(slice.Flatten(), Is.EqualTo(expectedText.Substring(90, 120)));
    }

    [Test]
    public void JsValue_SameValue_Treats_Rope_And_Flat_String_As_Equal()
    {
        var rope = JsValue.FromString(JsString.Concat("ab", "cd"));
        var flat = JsValue.FromString("abcd");

        Assert.That(JsValue.SameValue(rope, flat), Is.True);
    }

    [Test]
    public void JsString_EnumerateRunes_Works_Without_Flattening_Whole_Value()
    {
        var value = JsString.Concat("ab", JsString.Concat("😀", "cd"));
        var runes = new List<string>();

        foreach (var rune in value.EnumerateRunes())
            runes.Add(rune.ToString());

        Assert.That(string.Join("|", runes), Is.EqualTo("a|b|😀|c|d"));
    }

    [Test]
    public void String_Addition_And_Slice_Work_For_Large_Concat_Chains()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let s = "";
                   for (let i = 0; i < 2048; i++) s = s + "ab";
                   [s.length, s.slice(1024, 1032), s.substring(0, 4), s === s.slice(0)].join("|");
                   """);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("4096|abababab|abab|true"));
    }

    [Test]
    public void String_Search_Methods_Work_On_Large_Rope_Strings()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   let s = "";
                   for (let i = 0; i < 1024; i++) s += "ab";
                   s += "XYZ";
                   for (let i = 0; i < 1024; i++) s += "cd";
                   [
                     s.indexOf("XYZ"),
                     s.lastIndexOf("XYZ"),
                     s.includes("XYZ"),
                     s.startsWith("ababab"),
                     s.endsWith("cdcdcd"),
                     s.startsWith("XYZ", 2048)
                   ].join("|");
                   """);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("2048|2048|true|true|true|true"));
    }
}
