using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Parsing;

namespace Okojo.Tests;

public class LazyPrototypeInlineCacheTests
{
    [Test]
    public void Compile_DoesNotAllocatePrototypeInlineCacheBeforeExecution()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        using var ast = JavaScriptParser.ParseScript("const obj = { x: 1 }; obj.x;");
        var script = JsCompiler.Compile(realm, ast);

        Assert.That(script.NamedPropertyIcEntries, Is.Not.Null);
        Assert.That(script.PrototypeNamedPropertyIcEntries, Is.Null);

        realm.Execute(script);

        Assert.That(script.PrototypeNamedPropertyIcEntries, Is.Null);
    }

    [Test]
    public void PrototypePropertyRead_AllocatesAndPopulatesPrototypeInlineCache()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        using var ast = JavaScriptParser.ParseScript(
            "const proto = { x: 1 }; const obj = Object.create(proto); obj.x;"
        );
        var script = JsCompiler.Compile(realm, ast);

        Assert.That(script.PrototypeNamedPropertyIcEntries, Is.Null);

        realm.Execute(script);

        var prototypeEntries = script.PrototypeNamedPropertyIcEntries;
        Assert.That(prototypeEntries, Is.Not.Null);
        Assert.That(prototypeEntries, Has.Length.EqualTo(script.NamedPropertyIcEntries!.Length));
        Assert.That(prototypeEntries!.Any(entry => entry.Holder is not null), Is.True);
    }
}
