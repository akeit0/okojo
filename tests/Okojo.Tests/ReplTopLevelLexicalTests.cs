using Okojo.Bytecode;
using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ReplTopLevelLexicalTests
{
    [Test]
    public void ReplTopLevelLet_PersistsAcrossEntries()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var lexicalNames = new HashSet<string>(StringComparer.Ordinal);
        var constNames = new HashSet<string>(StringComparer.Ordinal);
        var context = new JsCompilerContext
        {
            IsRepl = true,
            ReplTopLevelLexicalNames = lexicalNames,
            ReplTopLevelConstNames = constNames
        };

        var first = Compile(realm, context, "let x = 41;");
        realm.Execute(first);
        lexicalNames.Add("x");

        var second = Compile(realm, context, "x + 1;");
        realm.Execute(second);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void ReplTopLevelConst_AssignmentAcrossEntries_Throws()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var lexicalNames = new HashSet<string>(StringComparer.Ordinal) { "c" };
        var constNames = new HashSet<string>(StringComparer.Ordinal) { "c" };
        var context = new JsCompilerContext
        {
            IsRepl = true,
            ReplTopLevelLexicalNames = lexicalNames,
            ReplTopLevelConstNames = constNames
        };

        var first = Compile(realm, context, "const c = 1;");
        realm.Execute(first);

        var second = Compile(realm, context, "c = 2;");
        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(second));
        Assert.That(ex!.Message, Does.Contain("constant"));
    }

    [Test]
    public void ReplTopLevelFunctionDeclaration_PersistsAcrossEntries()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var lexicalNames = new HashSet<string>(StringComparer.Ordinal);
        var constNames = new HashSet<string>(StringComparer.Ordinal);
        var context = new JsCompilerContext
        {
            IsRepl = true,
            ReplTopLevelLexicalNames = lexicalNames,
            ReplTopLevelConstNames = constNames
        };

        var first = Compile(realm, context, """
                                            function f(x) { return x + x; }
                                            """);
        realm.Execute(first);

        var second = Compile(realm, context, "f(3);");
        realm.Execute(second);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(6));
    }

    [Test]
    public void ReplTopLevelVar_IsInstantiatedBeforeStatementExecution()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var lexicalNames = new HashSet<string>(StringComparer.Ordinal);
        var constNames = new HashSet<string>(StringComparer.Ordinal);
        var context = new JsCompilerContext
        {
            IsRepl = true,
            ReplTopLevelLexicalNames = lexicalNames,
            ReplTopLevelConstNames = constNames
        };

        var script = Compile(realm, context, """
                                             Object.getOwnPropertyDescriptor(this, "x");
                                             var x;
                                             """);
        realm.Execute(script);

        Assert.That(realm.Accumulator.TryGetObject(out var descriptorObj), Is.True);
        Assert.That(descriptorObj!.TryGetProperty("value", out var value), Is.True);
        Assert.That(value.IsUndefined, Is.True);
        Assert.That(descriptorObj.TryGetProperty("writable", out var writable) && writable.IsTrue, Is.True);
        Assert.That(descriptorObj.TryGetProperty("enumerable", out var enumerable) && enumerable.IsTrue, Is.True);
        Assert.That(descriptorObj.TryGetProperty("configurable", out var configurable) && configurable.IsFalse,
            Is.True);
    }

    private static JsScript Compile(JsRealm realm, JsCompilerContext context, string source)
    {
        var compiler = new JsCompiler(realm, context);
        return compiler.Compile(JavaScriptParser.ParseScript(source));
    }
}
