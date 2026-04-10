using Okojo.Parsing;

namespace Okojo.Tests;

public class ModuleParserShapeTests
{
    [Test]
    public void ParseModule_ExportDefaultAsyncGeneratorDeclaration_HasNamedFunctionExpression()
    {
        var program = JavaScriptParser.ParseModule("export default async function * AG() {}");
        Assert.That(program.Statements.Count, Is.EqualTo(1));
        Assert.That(program.Statements[0], Is.InstanceOf<JsExportDefaultDeclaration>());
        var export = (JsExportDefaultDeclaration)program.Statements[0];
        Assert.That(export.Expression, Is.InstanceOf<JsFunctionExpression>());
        var fn = (JsFunctionExpression)export.Expression;
        Assert.That(fn.Name, Is.EqualTo("AG"));
        Assert.That(fn.IsAsync, Is.True);
        Assert.That(fn.IsGenerator, Is.True);
    }
}
