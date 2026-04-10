using Okojo.Parsing;

namespace Okojo.Tests;

public class ParserIdentifierTableTests
{
    [Test]
    public void IdentifierTable_TryGetIdentifierId_ForSpan_Matches_Existing_Entry()
    {
        var table = new JsIdentifierTable();

        var alphaId = table.AddIdentifierLiteral("alpha");
        var betaId = table.AddIdentifierLiteral("beta");

        Assert.Multiple(() =>
        {
            Assert.That(table.TryGetIdentifierId("alpha".AsSpan(), out var lookedUpAlphaId), Is.True);
            Assert.That(lookedUpAlphaId, Is.EqualTo(alphaId));
            Assert.That(table.TryGetIdentifierId("beta".AsSpan(), out var lookedUpBetaId), Is.True);
            Assert.That(lookedUpBetaId, Is.EqualTo(betaId));
            Assert.That(table.TryGetIdentifierId("gamma".AsSpan(), out _), Is.False);
        });
    }

    [Test]
    public void ParseScript_ContextualKeyword_Parsing_Remains_Correct_With_Span_Identifier_Lookup()
    {
        var program = JavaScriptParser.ParseModule("""
                                                   import.meta;
                                                   export { value as default } from "./dep.js";
                                                   class C extends D {}
                                                   """);

        Assert.That(program.Statements.Count, Is.EqualTo(3));
    }

    [Test]
    public void ParseScript_StrictParameter_EvalOrArguments_Rejection_Remains_Correct_With_NameIds()
    {
        Assert.That(
            () => JavaScriptParser.ParseScript("""
                                               function f(arguments) { "use strict"; }
                                               """),
            Throws.InstanceOf<JsParseException>());

        Assert.That(
            () => JavaScriptParser.ParseScript("""
                                               function g(eval) { "use strict"; }
                                               """),
            Throws.InstanceOf<JsParseException>());
    }

    [Test]
    public void ParseScript_StrictCatchParameter_EvalOrArguments_AreRejected()
    {
        Assert.That(
            () => JavaScriptParser.ParseScript("""
                                               "use strict";
                                               try {} catch (arguments) {}
                                               """),
            Throws.InstanceOf<JsParseException>());

        Assert.That(
            () => JavaScriptParser.ParseScript("""
                                               "use strict";
                                               try {} catch (eval) {}
                                               """),
            Throws.InstanceOf<JsParseException>());
    }

    [Test]
    public void ParseScript_ObjectAndClassPropertyParsing_Remains_Correct_With_Shared_PropertyKey_Helper()
    {
        var program = JavaScriptParser.ParseScript("""
                                                   const obj = {
                                                       async method() {},
                                                       get value() { return 1; },
                                                       set value(v) {},
                                                       [foo]: 1,
                                                       bar
                                                   };
                                                   class C {
                                                       static async method() {}
                                                       get value() { return 1; }
                                                       set value(v) {}
                                                       [foo] = 1;
                                                       #secret;
                                                   }
                                                   """);

        var objectDecl = (JsVariableDeclarationStatement)program.Statements[0];
        var objectExpression = (JsObjectExpression)objectDecl.Declarators[0].Initializer!;
        var classDecl = (JsClassDeclaration)program.Statements[1];

        Assert.Multiple(() =>
        {
            Assert.That(objectExpression.Properties.Count, Is.EqualTo(5));
            Assert.That(objectExpression.Properties[0].Key, Is.EqualTo("method"));
            Assert.That(objectExpression.Properties[1].Kind, Is.EqualTo(JsObjectPropertyKind.Getter));
            Assert.That(objectExpression.Properties[2].Kind, Is.EqualTo(JsObjectPropertyKind.Setter));
            Assert.That(objectExpression.Properties[3].IsComputed, Is.True);
            Assert.That(objectExpression.Properties[4].Key, Is.EqualTo("bar"));

            Assert.That(classDecl.ClassExpression.Elements.Count, Is.EqualTo(5));
            Assert.That(classDecl.ClassExpression.Elements[0].IsStatic, Is.True);
            Assert.That(classDecl.ClassExpression.Elements[0].Key, Is.EqualTo("method"));
            Assert.That(classDecl.ClassExpression.Elements[1].Kind, Is.EqualTo(JsClassElementKind.Getter));
            Assert.That(classDecl.ClassExpression.Elements[2].Kind, Is.EqualTo(JsClassElementKind.Setter));
            Assert.That(classDecl.ClassExpression.Elements[3].IsComputedKey, Is.True);
            Assert.That(classDecl.ClassExpression.Elements[4].IsPrivate, Is.True);
        });
    }

    [Test]
    public void ParseScript_Conditional_Consequent_Allows_In_Inside_For_Initializer()
    {
        var program = JavaScriptParser.ParseScript("""
                                                   var cond1Count = 0;
                                                   var cond2Count = 0;
                                                   var cond1 = function() { cond1Count += 1; return {}; };
                                                   var cond2 = function() { cond2Count += 1; };
                                                   for (true ? '' in cond1() : cond2(); false; ) ;
                                                   """);

        Assert.That(program.Statements.Count, Is.EqualTo(5));
    }

    [Test]
    public void ParseScript_AsyncSingleParameterArrow_Preserves_IdentifierId()
    {
        var program = JavaScriptParser.ParseScript("async value => value");
        var statement = (JsExpressionStatement)program.Statements[0];
        var function = (JsFunctionExpression)statement.Expression;

        Assert.Multiple(() =>
        {
            Assert.That(function.IsArrow, Is.True);
            Assert.That(function.IsAsync, Is.True);
            Assert.That(function.Parameters[0], Is.EqualTo("value"));
            Assert.That(function.ParameterIds[0], Is.GreaterThanOrEqualTo(0));
        });
    }

    //[Test]
    public void ParseScript_LazyDuplicateTrackers_Preserve_Object_And_Parameter_Semantics()
    {
        Assert.That(
            () => JavaScriptParser.ParseScript("""
                                               "use strict";
                                               ({ a: 1, get a() { return 2; } });
                                               """),
            Throws.InstanceOf<JsParseException>());

        var program = JavaScriptParser.ParseScript("""
                                                   function f(a, a) {}
                                                   ({ __proto__: 1, __proto__: 2 });
                                                   """);

        var function = (JsFunctionDeclaration)program.Statements[0];
        Assert.Multiple(() =>
        {
            Assert.That(function.HasDuplicateParameters, Is.True);
            Assert.That(program.Statements.Count, Is.EqualTo(2));
        });
    }

    [Test]
    public void ParseScript_EmptyParameterLists_Use_Stable_Empty_Metadata()
    {
        var scriptProgram = JavaScriptParser.ParseScript("function f() {}");
        var scriptFunction = (JsFunctionDeclaration)scriptProgram.Statements[0];

        var arrowProgram = JavaScriptParser.ParseScript("() => 1");
        var arrowStatement = (JsExpressionStatement)arrowProgram.Statements[0];
        var arrowFunction = (JsFunctionExpression)arrowStatement.Expression;

        Assert.Multiple(() =>
        {
            Assert.That(scriptFunction.Parameters, Is.Empty);
            Assert.That(scriptFunction.ParameterIds, Is.Empty);
            Assert.That(scriptFunction.ParameterPatterns, Is.Empty);
            Assert.That(arrowFunction.Parameters, Is.Empty);
            Assert.That(arrowFunction.ParameterIds, Is.Empty);
            Assert.That(arrowFunction.ParameterPatterns, Is.Empty);
        });
    }
}
