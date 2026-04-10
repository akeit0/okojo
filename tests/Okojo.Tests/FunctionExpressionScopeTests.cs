using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class FunctionExpressionScopeTests
{
    [Test]
    public void NamedFunctionExpression_NameBinding_DoesNotLeak_Outside()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var probe;
                                                                   var func = function f() {
                                                                     probe = function() { return f; };
                                                                   };
                                                                   var f = 'outside';
                                                                   func();
                                                                   f === 'outside' && probe() === func;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void NamedFunctionExpression_NameBinding_IsVisible_In_ParameterInitializers_And_Body()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var probeParams, probeBody;
                                                                   var func = function f(
                                                                     _ = (probeParams = function() { return f; })
                                                                   ) {
                                                                     probeBody = function() { return f; };
                                                                   };
                                                                   func();
                                                                   probeParams() === func && probeBody() === func;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void NamedFunctionExpression_Sloppy_Reassign_In_Nested_Arrow_Is_Ignored()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let callCount = 0;
                                                                   let refFn = function BindingIdentifier() {
                                                                     callCount++;
                                                                     (() => {
                                                                       BindingIdentifier = 1;
                                                                     })();
                                                                     return BindingIdentifier;
                                                                   };
                                                                   refFn() === refFn && callCount === 1;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void NamedGeneratorExpression_Sloppy_Reassign_In_Parameter_And_Body_Is_Ignored()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var g = 'outside';
                                                                   var probeParams, setParams, probeBody, setBody;
                                                                   var func = function* g(
                                                                     _ = (
                                                                       probeParams = function() { return g; },
                                                                       setParams = function() { g = null; }
                                                                     )
                                                                   ) {
                                                                     probeBody = function() { return g; };
                                                                     setBody = function() { g = null; };
                                                                   };
                                                                   func().next();
                                                                   setParams();
                                                                   setBody();
                                                                   probeParams() === func && probeBody() === func;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void NamedFunctionExpression_BodyVar_Shadow_Is_Open_From_Function_Start()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var n = 'outside';
                                                                   var probeBefore = function() { return n; };
                                                                   var probeBody;
                                                                   var func = function n() {
                                                                     var n;
                                                                     probeBody = function() { return n; };
                                                                   };
                                                                   func();
                                                                   probeBefore() === 'outside' && probeBody() === undefined;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
