using Okojo.JavaScript.Embedding;

namespace Okojo.Tests;

public class RegisterStoreTests
{
    [TestCase("let a = -0; let b = a; a = 1; return Object.is(b, -0);")]
    [TestCase("let a = NaN; let b = a; a = 1; return Number.isNaN(b);")]
    [TestCase("let a = 1.25; let b = a; a = 42; return b === 1.25;")]
    [TestCase("let a = {x: 7}; let b = a; a = 42; return b.x === 7;")]
    [TestCase("let a = 42; let b = {x: 7}; a = b; return a === b;")]
    [TestCase("let a = {x: 7}; let b = a; a = a; b.x = 9; return a === b && a.x === 9;")]
    [TestCase(
        "let a = Symbol('x'); let b = a; a = 'text'; return typeof b === 'symbol' && a === 'text';"
    )]
    public void StoresPreserveValueAndReferenceIdentity(string body)
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        realm.Execute(realm.CompileScript("function storeCase() { " + body + " } storeCase();"));

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
