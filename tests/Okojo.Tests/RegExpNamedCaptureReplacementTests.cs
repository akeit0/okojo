using Okojo.JavaScript.Embedding;

namespace Okojo.Tests;

public class RegExpNamedCaptureReplacementTests
{
    [TestCase(
        """
            const saved = [];
            const text = 'ab a ab'.replace(/a(?<tail>b)?/g, function(m, c, i, s, g) {
                saved.push(g); return g.tail === undefined ? '-' : g.tail;
            });
            const d = Object.getOwnPropertyDescriptor(saved[1], 'tail');
            return text === 'b - b' && saved[0] !== saved[1] && saved[0].tail === 'b'
                && saved[1].tail === undefined && Object.getPrototypeOf(saved[0]) === null
                && d.writable && d.enumerable && d.configurable;
            """
    )]
    [TestCase(
        """
            const saved = [];
            const text = 'ab'.replace(/(?<x>a)|(?<x>b)/g, function() {
                const g = arguments[arguments.length - 1]; saved.push(g); return g.x;
            });
            return text === 'ab' && saved[0].x === 'a' && saved[1].x === 'b'
                && Object.keys(saved[0]).join() === 'x';
            """
    )]
    [TestCase(
        """
            const saved = [];
            const text = 'aa'.replace(/(?<x>a)/g, function(m, c, i, s, g) {
                saved.push(g);
                'b'.replace(/(?<x>b)/g, function(m, c, i, s, inner) { inner.x = 'changed'; return ''; });
                return g.x;
            });
            saved[0].x = 'mutated';
            return text === 'aa' && saved[1].x === 'a';
            """
    )]
    [TestCase(
        """
            const rx = /(?<x>a)(?<empty>)/dg;
            let saved;
            const text = 'a'.replace(rx, function(m, x, empty, i, s, g) { saved = g; return g.x + g.empty; });
            rx.lastIndex = 0;
            const m = rx.exec('a');
            return text === 'a' && saved.empty === '' && m.groups.x === 'a'
                && m.indices.groups.x[0] === 0 && m.indices.groups.x[1] === 1
                && m.indices.groups.empty[0] === 1 && m.indices.groups.empty[1] === 1
                && 'a'.replace(/(?<x>a)/g, '$<x>!') === 'a!';
            """
    )]
    [TestCase(
        """
            const g = { x: 'custom' }; const rx = /(?<x>a)/;
            rx.exec = function() { const m = ['a', 'a']; m.index = 0; m.groups = g; return m; };
            let seen;
            const text = 'a'.replace(rx, function(m, c, i, s, groups) { seen = groups; return groups.x; });
            return seen === g && text === 'custom';
            """
    )]
    [TestCase(
        """
            let saved;
            const text = 'a'.replace(/(?<z>a)(?<a>)/, function(m, z, a, i, s, g) { saved = g; return z; }.bind(null));
            return text === 'a' && saved.z === 'a' && saved.a === '' && Object.keys(saved).join() === 'z,a';
            """
    )]
    public void NamedCaptureObjectsKeepTheirSemantics(string body)
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        realm.Execute(realm.CompileScript("(function() { " + body + " })();"));
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
