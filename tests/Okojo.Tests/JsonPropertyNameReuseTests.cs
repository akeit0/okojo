using Okojo.JavaScript.Embedding;

namespace Okojo.Tests;

public class JsonPropertyNameReuseTests
{
    [TestCase(
        """
            for(let i=0;i<3;i++) {
                const a=JSON.parse('{"id":1,"name":"x","id":2,"nested":{"id":3}}');
                if(a.id!==2 || a.nested.id!==3 || Object.keys(a).join()!=='id,name,nested') return false;
            } return true;
            """
    )]
    [TestCase(
        """
            const a=JSON.parse('{"0":1,"01":2,"4294967295":3,"":4,"__proto__":5}');
            return Object.keys(a).join() === '0,01,4294967295,,__proto__'
                && a.__proto__===5 && Object.getPrototypeOf(a)===Object.prototype;
            """
    )]
    [TestCase(
        """
            const a=JSON.parse('{"id":1,"\\u0069d":2,"b\\\"c":3}');
            return a.id===2 && a['b"c']===3 && Object.keys(a).length===2;
            """
    )]
    [TestCase(
        """
            JSON.parse('{"id":1}');
            let threw=false; try { JSON.parse('{"id\n":1}'); } catch(e) { threw=e instanceof SyntaxError; }
            return threw && JSON.parse('{"id":"id"}',(k,v)=>k==='id'?v+'!':v).id==='id!';
            """
    )]
    public void PropertyNamesPreserveJsonSemantics(string body)
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        realm.Execute(realm.CompileScript("(function(){" + body + "})();"));
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
