using Okojo.JavaScript.Embedding;

namespace Okojo.Tests;

public class JsonPropertySpanTests
{
    [TestCase(
        """
            const a=JSON.parse('{"4294967294":1,"4294967295":2,"0":3,"00":4,"01":5,"-0":6,"1e0":7}');
            return Object.keys(a).join()==='0,4294967294,4294967295,00,01,-0,1e0' && a['4294967295']===2;
            """
    )]
    [TestCase(
        """
            for(let c=0;c<32;c++) {
                let threw=false;
                try { JSON.parse('{"a'+String.fromCharCode(c)+'b":1}'); } catch(e) {threw=e instanceof SyntaxError;}
                if(!threw) return false;
                const key='a'+String.fromCharCode(c)+'b';
                if(JSON.parse('{'+JSON.stringify(key)+':1}')[key]!==1) return false;
            }
            return true;
            """
    )]
    [TestCase(
        """
            const key='long'.repeat(100)+'é';
            const text='{'+JSON.stringify(key)+':1,'+JSON.stringify(key)+':2,"a\\u0062":3,"ab":4,"\\u0030":5,"":6}';
            const a=JSON.parse(text);
            return a[key]===2 && a.ab===4 && a[0]===5 && a['']===6 && Object.keys(a).length===4;
            """
    )]
    [TestCase(
        """
            const child={}; for(let i=0;i<4096;i++) child['newChild'+i]=i;
            const outer='spanOuterKey';
            const a=JSON.parse('{'+JSON.stringify(outer)+':'+JSON.stringify(child)+'}');
            return a[outer].newChild0===0 && a[outer].newChild4095===4095;
            """
    )]
    [TestCase(
        """
            const keys=['\ud800','\udfff','a"b','a\\b',''];
            for(const key of keys) {
                if(JSON.parse('{'+JSON.stringify(key)+':"value"}')[key]!=='value')return false;
            }
            for(const text of ['{"a','{"a\\','{"a\\u0":1}','{"a":','{"a" 1}']) {
                let threw=false;try{JSON.parse(text)}catch(e){threw=e instanceof SyntaxError}
                if(!threw)return false;
            }
            return true;
            """
    )]
    public void PropertyNameFormsPreserveBehavior(string body)
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        realm.Execute(realm.CompileScript("(function(){" + body + "})();"));
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
