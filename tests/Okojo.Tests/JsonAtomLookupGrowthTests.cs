using Okojo.JavaScript.Embedding;

namespace Okojo.Tests;

public class JsonAtomLookupGrowthTests
{
    [TestCase(
        """
            const first = JSON.parse('{"e021first":1,"Case":2,"case":3}');
            const obj = {};
            for(let i=0;i<4096;i++) obj['e021key'+i]=i;
            const payload=JSON.stringify(obj);
            for(let k=0;k<2;k++) {
                const parsed=JSON.parse(payload);
                if(parsed.e021key0!==0 || parsed.e021key4095!==4095) return false;
            }
            const last=JSON.parse('{"e021first":4,"Case":5,"case":6}');
            return first.e021first===1 && last.e021first===4 && last.Case===5 && last.case===6;
            """
    )]
    [TestCase(
        """
            const payload='{"é":1,"é":2,"\\u0000":3,"01":4}';
            for(let i=0;i<3;i++) {
                const a=JSON.parse(payload);
                if(a['é']!==1 || a['é']!==2 || a['\u0000']!==3 || a['01']!==4) return false;
            } return true;
            """
    )]
    public void NamesRemainDistinctAndVisibleAfterGrowth(string body)
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        realm.Execute(realm.CompileScript("(function(){" + body + "})();"));
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
