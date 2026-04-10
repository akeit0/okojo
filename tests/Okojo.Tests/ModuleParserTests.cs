using Okojo.Parsing;

namespace Okojo.Tests;

public class ModuleParserTests
{
    [Test]
    public void ParseModule_ParsesImportAndExportStatements()
    {
        var program = JavaScriptParser.ParseModule("""
                                                   import { a as b } from "./dep.js";
                                                   export const x = 1;
                                                   export { x as y };
                                                   """);

        Assert.That(program.Statements.Count, Is.EqualTo(3));
        Assert.That(program.Statements[0], Is.TypeOf<JsImportDeclaration>());
        Assert.That(program.Statements[1], Is.TypeOf<JsExportDeclarationStatement>());
        Assert.That(program.Statements[2], Is.TypeOf<JsExportNamedDeclaration>());
        Assert.That(program.StrictDeclared, Is.True);
    }

    [Test]
    public void ParseModule_ParsesExportFromAndExportAll()
    {
        var program = JavaScriptParser.ParseModule("""
                                                   export { a as aa } from "./dep.js";
                                                   export * from "./dep2.js";
                                                   export * as ns from "./dep3.js";
                                                   """);

        Assert.That(program.Statements.Count, Is.EqualTo(3));
        Assert.That(program.Statements[0], Is.TypeOf<JsExportNamedDeclaration>());
        Assert.That(program.Statements[1], Is.TypeOf<JsExportAllDeclaration>());
        Assert.That(program.Statements[2], Is.TypeOf<JsExportAllDeclaration>());
        var exportAllAs = (JsExportAllDeclaration)program.Statements[2];
        Assert.That(exportAllAs.ExportedName, Is.EqualTo("ns"));
    }

    [Test]
    public void ParseModule_ParsesSideEffectOnlyImport()
    {
        var program = JavaScriptParser.ParseModule("""
                                                   import "./dep.js";
                                                   export const ok = 1;
                                                   """);

        Assert.That(program.Statements.Count, Is.EqualTo(2));
        Assert.That(program.Statements[0], Is.TypeOf<JsImportDeclaration>());
        var importDecl = (JsImportDeclaration)program.Statements[0];
        Assert.That(importDecl.Source, Is.EqualTo("./dep.js"));
        Assert.That(importDecl.DefaultBinding, Is.Null);
        Assert.That(importDecl.NamespaceBinding, Is.Null);
        Assert.That(importDecl.NamedBindings.Count, Is.EqualTo(0));
    }

    [Test]
    public void ParseModule_Parses_Static_Import_Attributes_On_Import_And_ExportFrom()
    {
        var program = JavaScriptParser.ParseModule("""
                                                   import x from "./dep1.js" with { type: "json" };
                                                   import "./dep2.js" with {};
                                                   export * from "./dep3.js" with { "mode": "strict", };
                                                   """);

        Assert.That(program.Statements.Count, Is.EqualTo(3));

        var importDecl = (JsImportDeclaration)program.Statements[0];
        Assert.That(importDecl.Attributes.Count, Is.EqualTo(1));
        Assert.That(importDecl.Attributes[0].Key, Is.EqualTo("type"));
        Assert.That(importDecl.Attributes[0].Value, Is.EqualTo("json"));

        var sideEffectImport = (JsImportDeclaration)program.Statements[1];
        Assert.That(sideEffectImport.Attributes, Is.Empty);

        var exportAll = (JsExportAllDeclaration)program.Statements[2];
        Assert.That(exportAll.Attributes.Count, Is.EqualTo(1));
        Assert.That(exportAll.Attributes[0].Key, Is.EqualTo("mode"));
        Assert.That(exportAll.Attributes[0].Value, Is.EqualTo("strict"));
    }

    [Test]
    public void ParseModule_Static_Import_Attributes_Reject_Duplicate_Keys()
    {
        Assert.Throws<JsParseException>(() => JavaScriptParser.ParseModule("""
                                                                           import "./dep.js" with { type: "json", type: "text" };
                                                                           """));

        Assert.Throws<JsParseException>(() => JavaScriptParser.ParseModule("""
                                                                           export * from "./dep.js" with { "type": "json", "type": "text" };
                                                                           """));
    }

    [Test]
    public void ParseModule_Parses_String_Module_Export_Name_In_Import_Specifier()
    {
        var program = JavaScriptParser.ParseModule("""
                                                   import { "☿" as Ami } from "./fixture.js";
                                                   """);

        Assert.That(program.Statements, Has.Count.EqualTo(1));
        var importDecl = program.Statements[0] as JsImportDeclaration;
        Assert.That(importDecl, Is.Not.Null);
        Assert.That(importDecl!.NamedBindings, Has.Count.EqualTo(1));
        Assert.That(importDecl.NamedBindings[0].ImportedName, Is.EqualTo("☿"));
        Assert.That(importDecl.NamedBindings[0].LocalName, Is.EqualTo("Ami"));
    }
}
