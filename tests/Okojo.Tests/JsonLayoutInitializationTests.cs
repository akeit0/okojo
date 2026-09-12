using Okojo.JavaScript;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Objects;

namespace Okojo.Tests;

public class JsonLayoutInitializationTests
{
    [TestCase(1)]
    [TestCase(15)]
    [TestCase(16)]
    [TestCase(64)]
    public void InitializedLayoutsRemainIndependentAndMutable(int count)
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        int[] atoms = Enumerable
            .Range(0, count)
            .Select(i => realm.Atoms.InternNoCheck("p" + i))
            .ToArray();
        JsValue[] values = Enumerable.Range(0, count).Select(JsValue.FromInt32).ToArray();
        var first = new JsPlainObject(realm, useDictionaryMode: true);
        var second = new JsPlainObject(realm, useDictionaryMode: true);
        var original = first.NamedPropertyLayout;
        first.InitializeDynamicOpenDataPropertiesNoCollision(realm, atoms, values);
        second.InitializeDynamicOpenDataPropertiesNoCollision(realm, atoms, values);
        Assert.That(first.NamedPropertyLayout, Is.SameAs(original));
        first["p0"] = JsValue.FromInt32(99);
        first["extra"] = JsValue.True;
        Assert.That(second["p0"].NumberValue, Is.Zero);
        Assert.That(second["extra"].IsUndefined, Is.True);
        for (int i = 1; i < count; i++)
            Assert.That(first["p" + i].NumberValue, Is.EqualTo(i));
    }

    [TestCase(15)]
    [TestCase(16)]
    [TestCase(64)]
    public void JsonDuplicatesReviverAndSubsequentMutationPreserveOrder(int count)
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        string body = """
            const keys=Array.from({length:COUNT},(_,i)=>'p'+i);
            const text='{'+keys.map((k,i)=>JSON.stringify(k)+':'+i).join(',')+',"p0":99,"0":7,"__proto__":8}';
            const a=JSON.parse(text,(k,v)=>k==='p1'?undefined:v);
            const b=JSON.parse(text);
            delete a.p2; a.extra=12; a.p2=22;
            Object.defineProperty(a,'p3',{value:33,writable:false});
            Object.freeze(a);
            return a.p0===99 && a.p1===undefined && b.p1===1 && b.p2===2
                && a.p2===22 && a.p3===33 && a[0]===7 && a.__proto__===8
                && Object.getPrototypeOf(a)===Object.prototype
                && Object.keys(a).slice(-2).join()==='extra,p2'
                && !Object.getOwnPropertyDescriptor(a,'p3').writable;
            """;
        realm.Execute(
            realm.CompileScript("(function(){" + body.Replace("COUNT", count.ToString()) + "})();")
        );
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
