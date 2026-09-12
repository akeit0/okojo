using Okojo.JavaScript.Embedding;

namespace Okojo.Tests;

public class RegExpReplacementIndicesTests
{
    [TestCase(
        """
            const rx = /(?<x>a)|(?<x>b)/dg;
            const text = 'ab'.replace(rx, '$<x>!');
            const m = rx.exec('b');
            return text === 'a!b!' && m.indices[1] === undefined
                && m.indices[2][0] === 0 && m.indices.groups.x === m.indices[2];
            """
    )]
    [TestCase(
        """
            const saved = [];
            const text = 'ab a'.replace(/a(?<x>b)?/dg, function(m, c, i, s, g) {
                const nested = /(?<y>z)/d.exec('z'); saved.push(nested.indices);
                return g.x === undefined ? '-' : g.x;
            });
            return text === 'b -' && saved[0] !== saved[1]
                && saved[0].groups.y[0] === 0 && saved[1].groups.y[1] === 1;
            """
    )]
    [TestCase(
        """
            let reads = 0; const rx = /a/d;
            rx.exec = function() { const m = ['a']; m.index = 0;
                Object.defineProperty(m, 'indices', { get() { reads++; throw new Error('indices'); } });
                return m;
            };
            return 'a'.replace(rx, () => 'x') === 'x' && reads === 0
                && '😀'.replace(/(?:)/dgu, '-') === '-😀-';
            """
    )]
    public void ReplacementDoesNotExposeInternalIndices(string body)
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        realm.Execute(realm.CompileScript("(function(){" + body + "})();"));
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
