using Okojo.Parsing;

namespace Okojo.Tests;

public class ExplicitResourceManagementParserTests
{
    [Test]
    public void ParseModule_Parses_Using_Declaration()
    {
        var program = JavaScriptParser.ParseModule("using value = null;");

        Assert.That(program.Statements, Has.Count.EqualTo(1));
        var decl = program.Statements[0] as JsVariableDeclarationStatement;
        Assert.That(decl, Is.Not.Null);
        Assert.That(decl!.Kind, Is.EqualTo(JsVariableDeclarationKind.Using));
        Assert.That(decl.Declarators[0].Name, Is.EqualTo("value"));
    }

    [Test]
    public void ParseModule_Parses_AwaitUsing_Declaration()
    {
        var program = JavaScriptParser.ParseModule("await using value = null;");

        Assert.That(program.Statements, Has.Count.EqualTo(1));
        var decl = program.Statements[0] as JsVariableDeclarationStatement;
        Assert.That(decl, Is.Not.Null);
        Assert.That(decl!.Kind, Is.EqualTo(JsVariableDeclarationKind.AwaitUsing));
        Assert.That(decl.Declarators[0].Name, Is.EqualTo("value"));
    }

    [Test]
    public void ParseScript_Rejects_TopLevel_Using_Declaration()
    {
        Assert.That(() => JavaScriptParser.ParseScript("using value = null;"),
            Throws.InstanceOf<JsParseException>());
    }

    [Test]
    public void ParseScript_Allows_Using_Declaration_In_Nested_Statement()
    {
        var program = JavaScriptParser.ParseScript("if (true) using value = null;");

        var ifStmt = program.Statements[0] as JsIfStatement;
        Assert.That(ifStmt, Is.Not.Null);
        Assert.That(ifStmt!.Consequent, Is.TypeOf<JsVariableDeclarationStatement>());
        var decl = (JsVariableDeclarationStatement)ifStmt.Consequent;
        Assert.That(decl.Kind, Is.EqualTo(JsVariableDeclarationKind.Using));
    }

    [Test]
    public void ParseScript_Parses_AwaitUsing_In_Async_Function()
    {
        var program = JavaScriptParser.ParseScript("""
                                                   async function f() {
                                                     await using value = null;
                                                   }
                                                   """);

        var function = program.Statements[0] as JsFunctionDeclaration;
        Assert.That(function, Is.Not.Null);
        Assert.That(function!.Body.Statements[0], Is.TypeOf<JsVariableDeclarationStatement>());
        var decl = (JsVariableDeclarationStatement)function.Body.Statements[0];
        Assert.That(decl.Kind, Is.EqualTo(JsVariableDeclarationKind.AwaitUsing));
    }

    [Test]
    public void ParseScript_Rejects_Using_Declaration_Without_Initializer()
    {
        Assert.That(() => JavaScriptParser.ParseScript("{ using value; }"),
            Throws.InstanceOf<JsParseException>());
    }

    [Test]
    public void ParseScript_Parses_ForOf_Using_Declaration_With_Of_Binding()
    {
        var program = JavaScriptParser.ParseScript("for (using of of []) { }");

        var forOf = program.Statements[0] as JsForInOfStatement;
        Assert.That(forOf, Is.Not.Null);
        Assert.That(forOf!.Left, Is.TypeOf<JsVariableDeclarationStatement>());
        var decl = (JsVariableDeclarationStatement)forOf.Left;
        Assert.That(decl.Kind, Is.EqualTo(JsVariableDeclarationKind.Using));
        Assert.That(decl.Declarators[0].Name, Is.EqualTo("of"));
    }

    [Test]
    public void ParseScript_Rejects_ForIn_Using_Declaration()
    {
        Assert.That(() => JavaScriptParser.ParseScript("for (using value in obj) { }"),
            Throws.InstanceOf<JsParseException>());
    }
}
