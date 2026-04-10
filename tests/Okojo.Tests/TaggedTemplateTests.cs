using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class TaggedTemplateTests
{
    [Test]
    public void TaggedTemplate_CachesTemplateObjectBySiteInFunction()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            function tag(templateObject) {
              previousObject = templateObject;
            }

            var a = 1;
            var firstObject = null;
            var previousObject = null;

            function factory() {
              return function() {
                tag`head${a}tail`;
              }
            }

            factory()();
            firstObject = previousObject;
            previousObject = null;
            factory()();
            previousObject === firstObject;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TemplateLiteral_Untagged_Concatenates()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var a = 2;
            `x${a}y`;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("x2y"));
    }

    [Test]
    public void TaggedTemplate_PrecedesNewInvocation()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            function Constructor(x) { arg = x; }
            var tag = function(x) {
              templateObject = x;
              return Constructor;
            };
            var arg = null;
            var templateObject = null;

            var instance = new tag`first template`;
            var ok1 = instance instanceof Constructor;
            var ok2 = templateObject[0] === "first template";
            var ok3 = arg === undefined;

            instance = new tag`second template`("constructor argument");
            var ok4 = templateObject[0] === "second template";
            var ok5 = arg === "constructor argument";
            ok1 && ok2 && ok3 && ok4 && ok5;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TaggedTemplate_CookedAndRaw_CharacterEscapeSequence()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var called = 0;
            var ok = false;
            (function(s) {
              called++;
              ok = s[0] === "'" && s.raw[0] === "\\'";
            })`\'`;
            called === 1 && ok;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TaggedTemplate_InvalidEscape_ProducesUndefinedCooked()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var ok = true;
            (function(s) {
              ok = ok && s[0] === undefined && s.raw[0] === "\\1";
            })`\1`;
            (function(s) {
              ok = ok && s[0] === undefined && s.raw[0] === "\\xg";
            })`\xg`;
            ok;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TaggedTemplate_LineContinuation_CookedIsEmpty_AndRawKeepsBackslashNewline()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var source =
            "var ok = true; (function(cs){ ok = ok && cs[0] === \"\" && cs.raw[0] === \"\\\\\\n\\\\\\n\\\\\\n\"; })`\\\n\\\n\\\n`; ok;";
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript(source));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TaggedTemplate_LineTerminatorSequence_NormalizesCrAndCrLfToLf()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript(
            "(function(s){ return s[0] === \"\\n\\n\\n\" && s.raw[0] === \"\\n\\n\\n\"; })`\n\r\r\n`;"));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TaggedTemplate_TemplateObject_Has_Own_NonEnumerable_Length_Properties()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var templateObject;
            (function(s) { templateObject = s; })`${1}`;
            Object.prototype.hasOwnProperty.call(templateObject, "length") &&
            Object.prototype.hasOwnProperty.call(templateObject.raw, "length") &&
            !Object.prototype.propertyIsEnumerable.call(templateObject, "length") &&
            !Object.prototype.propertyIsEnumerable.call(templateObject.raw, "length");
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
