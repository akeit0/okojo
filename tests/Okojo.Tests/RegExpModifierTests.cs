using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Tests;

public class RegExpModifierTests
{
    [Test]
    public void OkojoRegExpRuntime_CompilePattern_AllowsScopedModifierGroups()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => JsRegExpRuntime.CompilePattern("(?m:es$)", ""), Throws.Nothing);
            Assert.That(() => JsRegExpRuntime.CompilePattern("(?-s:^.$)", "s"), Throws.Nothing);
            Assert.That(() => JsRegExpRuntime.CompilePattern("(?m:^(?-i:a)$)", "i"), Throws.Nothing);
        });
    }

    [Test]
    public void RegExpLiteral_ScopedModifiers_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               const re1 = /(?m:es$)/;
                               const re2 = /(?-s:^.$)/s;
                               re1.test("es\ns") && re2.test("a") && !re2.test("\n");
                               """).IsTrue, Is.True);
    }

    [Test]
    public void RegExpConstructor_ScopedModifiers_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               const re1 = new RegExp("(?m:es$)");
                               const re2 = new RegExp("(?-s:^.$)", "s");
                               re1.test("es\ns") && re2.test("a") && !re2.test("\n");
                               """).IsTrue, Is.True);
    }


    private static JsRealm CreateRealmWithAssertShim()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Global["assert"] = JsValue.FromObject(new JsHostFunction(realm, static (in info) =>
        {
            var args = info.Arguments;
            var ok = args.Length > 0 && args[0].IsTrue;
            if (!ok)
            {
                var message = args.Length > 1 ? info.Realm.ToJsStringSlowPath(args[1]) : "assertion failed";
                throw new JsRuntimeException(JsErrorKind.TypeError, message);
            }

            return JsValue.Undefined;
        }, "assert", 2));
        return realm;
    }
}
