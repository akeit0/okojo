using Okojo.JavaScript.Embedding;

namespace Okojo.Tests;

public class RegExpReplacementBufferTests
{
    [TestCase(
        """
            const saved = [];
            const result = 'a1 a2 a3'.replace(/a(\d)/g, function(m, c, offset, input) {
                saved.push(arguments); m = 'm' + offset; return c;
            });
            return result === '1 2 3' && saved[0][0] === 'm0' && saved[1][0] === 'm3'
                && saved[2][0] === 'm6' && saved[0][1] === '1' && saved[2][2] === 6;
            """
    )]
    [TestCase(
        """
            const saved = [];
            const result = 'ab a ab'.replace(/a(?<tail>b)?/g, function(m, c, offset, input, groups) {
                'use strict'; saved.push(arguments); return c === undefined ? 'N' : c;
            });
            return result === 'b N b' && saved[0][1] === 'b' && saved[1][1] === undefined
                && saved[0][4] !== saved[1][4] && saved[0][4].tail === 'b'
                && saved[1][4].tail === undefined && saved[2][2] === 5;
            """
    )]
    [TestCase(
        """
            const saved = [];
            const result = 'aa'.replace(/a/g, function(m, offset, input) {
                saved.push(arguments);
                const nested = 'bb'.replace(/b/g, (n, i) => String(i));
                return nested + offset;
            });
            return result === '010011' && saved[0][1] === 0 && saved[1][1] === 1
                && saved[0][2] === 'aa';
            """
    )]
    [TestCase(
        """
            const rx = /a/g;
            let step = 0;
            rx.exec = function(input) {
                if (step === 3) return null;
                const result = ['a'];
                for (let j = 0; j < step; j++) result.push(String(j));
                result.index = step++; result.input = input;
                return result;
            };
            const saved = [];
            'aaa'.replace(rx, function() { saved.push(arguments); return 'x'; });
            return saved[0].length === 3 && saved[1].length === 4 && saved[2].length === 5
                && saved[0][1] === 0 && saved[1][2] === 1 && saved[2][3] === 2;
            """
    )]
    [TestCase(
        """
            let calls = 0;
            const rx = /a/g;
            try { 'aa'.replace(rx, () => { calls++; throw new Error('stop'); }); } catch (e) {}
            const result = 'aa'.replace(rx, (m, i) => String(i));
            return calls === 1 && result === '01';
            """
    )]
    public void ReplacementArgumentsRemainIndependent(string body)
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        realm.Execute(realm.CompileScript("(function() { " + body + " })();"));
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
