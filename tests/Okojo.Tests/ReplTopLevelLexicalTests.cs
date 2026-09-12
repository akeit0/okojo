using Okojo.JavaScript;
using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Parsing;

namespace Okojo.Tests;

public class ReplTopLevelLexicalTests
{
    [Test]
    public void ReplTopLevelLet_PersistsAcrossEntries()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var lexicalNames = new HashSet<string>(StringComparer.Ordinal);

        var first = Compile(realm, "let x = 41;");
        realm.Execute(first);
        lexicalNames.Add("x");

        var second = Compile(realm, "x + 1;");
        realm.Execute(second);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void ReplTopLevelConst_AssignmentAcrossEntries_Throws()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var lexicalNames = new HashSet<string>(StringComparer.Ordinal) { "c" };

        var first = Compile(realm, "const c = 1;");
        realm.Execute(first);

        var second = Compile(realm, "c = 2;");
        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(second));
        Assert.That(ex!.Message, Does.Contain("read-only"));
    }

    [Test]
    public void ReplTopLevelFunctionDeclaration_PersistsAcrossEntries()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var lexicalNames = new HashSet<string>(StringComparer.Ordinal);

        var first = Compile(
            realm,
            """
            function f(x) { return x + x; }
            """
        );
        realm.Execute(first);

        var second = Compile(realm, "f(3);");
        realm.Execute(second);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(6));
    }

    [Test]
    public void ReplTopLevelVar_IsInstantiatedBeforeStatementExecution()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var lexicalNames = new HashSet<string>(StringComparer.Ordinal);

        var script = Compile(
            realm,
            """
            Object.getOwnPropertyDescriptor(this, "x");
            var x;
            """
        );
        realm.Execute(script);

        Assert.That(realm.Accumulator.TryGetObject(out var descriptorObj), Is.True);
        Assert.That(descriptorObj!.TryGetProperty("value", out var value), Is.True);
        Assert.That(value.IsUndefined, Is.True);
        Assert.That(
            descriptorObj.TryGetProperty("writable", out var writable) && writable.IsTrue,
            Is.True
        );
        Assert.That(
            descriptorObj.TryGetProperty("enumerable", out var enumerable) && enumerable.IsTrue,
            Is.True
        );
        Assert.That(
            descriptorObj.TryGetProperty("configurable", out var configurable)
                && configurable.IsFalse,
            Is.True
        );
    }

    private static JsScript Compile(JsRealm realm, string source)
    {
        return new JsScriptCompiler(realm).Compile(source);
    }
}
