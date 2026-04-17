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
        Assert.That(program.HasTopLevelAwait, Is.True);
    }

    [Test]
    public void ParseModule_ForOf_Head_AwaitUsing_SetsTopLevelAwait()
    {
        var program = JavaScriptParser.ParseModule("""
                                                   for (await using value of items) {
                                                   }
                                                   """);

        Assert.That(program.HasTopLevelAwait, Is.True);
        Assert.That(program.Statements[0], Is.TypeOf<JsForInOfStatement>());
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
    public void ParseScript_Allows_ForOf_Using_Of_Ambiguity_As_Expression()
    {
        Assert.That(() => JavaScriptParser.ParseScript("for (using of of [0, 1, 2]) { }"),
            Throws.Nothing);
    }

    [Test]
    public void ParseScript_Rejects_ForIn_Using_Declaration()
    {
        Assert.That(() => JavaScriptParser.ParseScript("for (using value in obj) { }"),
            Throws.InstanceOf<JsParseException>());
    }
}
