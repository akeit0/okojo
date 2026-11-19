using Okojo.JavaScript;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;
using Okojo.JavaScript.Parsing;

namespace Okojo.Tests;

/// <summary>
/// Regression coverage for copy-on-write array literals, inline named slots, constructor
/// slot prediction, polymorphic/transition named-property ICs and the bitwise number path.
/// </summary>
public class ObjectStorageOptimizationTests
{
    private static string Eval(string source)
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Execute(JsCompiler.Compile(realm, JavaScriptParser.ParseScript(source)));
        return realm.Accumulator.AsString();
    }

    [Test]
    public void ArrayLiteral_Instances_Do_Not_Share_Writes()
    {
        var result = Eval(
            """
            function make() { return [1, 2, 3]; }
            var a = make(), b = make();
            a[0] = 9; a.push(4); b.length = 1;
            var c = make(); c.reverse();
            var d = make(); d.sort(function (x, y) { return y - x; });
            var e = make(); Object.freeze(e);
            var f = make(); f.splice(1, 1);
            [a.join(), b.join(), c.join(), d.join(), make().join(), f.join(), Object.isFrozen(make())].join('|');
            """
        );
        Assert.That(result, Is.EqualTo("9,2,3,4|1|3,2,1|3,2,1|1,2,3|1,3|false"));
    }

    [Test]
    public void ArrayLiteral_Holes_Stay_Holes_After_Copy()
    {
        var result = Eval(
            """
            function make() { return [1, , 3]; }
            var a = make(); a[1] = 2;
            var b = make();
            [a.join(), (1 in b), b.length, (1 in make())].join('|');
            """
        );
        Assert.That(result, Is.EqualTo("1,2,3|false|3|false"));
    }

    [Test]
    public void InlineSlots_Grow_Delete_And_Redefine()
    {
        var result = Eval(
            """
            var out = [];
            for (var i = 0; i < 3; i++) {
                var o = { a: 1, b: 2 };
                o.c = 3; o.d = 4; o.e = 5; o.f = 6; o.g = 7; o.h = 8; o.i = 9; o.j = 10;
                delete o.b;
                Object.defineProperty(o, 'a', { get: function () { return 'ga'; }, configurable: true });
                o.k = 11;
                out.push(Object.keys(o).join(), o.a, o.j, JSON.stringify(o));
            }
            out.join('|');
            """
        );
        const string expected =
            "a,c,d,e,f,g,h,i,j,k|ga|10|{\"a\":\"ga\",\"c\":3,\"d\":4,\"e\":5,\"f\":6,\"g\":7,\"h\":8,\"i\":9,\"j\":10,\"k\":11}";
        Assert.That(result, Is.EqualTo(string.Join("|", expected, expected, expected)));
    }

    [Test]
    public void Constructor_Instances_Keep_Prototype_And_Learned_Slots()
    {
        var result = Eval(
            """
            function P(x) { this.x = x; }
            P.prototype.left = null;
            var out = [];
            for (var i = 0; i < 4; i++) {
                var p = new P(i);
                out.push(p.left === null, Object.getPrototypeOf(p) === P.prototype);
                p.left = i; p.right = i * 2; p.extra1 = 1; p.extra2 = 2; p.extra3 = 3;
                out.push(p.left + p.right + p.extra3, Object.keys(p).join());
            }
            P.prototype = { tag: 'replaced' };
            var q = new P(7);
            out.push(q.tag, q.left === undefined, q.x);
            out.join('|');
            """
        );
        Assert.That(
            result,
            Is.EqualTo(
                "true|true|3|x,left,right,extra1,extra2,extra3|true|true|6|x,left,right,extra1,extra2,extra3|"
                    + "true|true|9|x,left,right,extra1,extra2,extra3|true|true|12|x,left,right,extra1,extra2,extra3|"
                    + "replaced|true|7"
            )
        );
    }

    [Test]
    public void Polymorphic_Loads_Read_Own_And_Prototype_Slots()
    {
        var result = Eval(
            """
            function N(k) { this.key = k; }
            N.prototype.left = 'proto';
            var a = new N(1), b = new N(2), c = new N(3), d = new N(4), e = new N(5);
            b.left = 'b'; c.right = 0; c.left = 'c'; d.extra = 1; d.left = 'd';
            function read(n) { return n.left; }
            var out = [];
            for (var i = 0; i < 3; i++) out.push([a, b, c, d, e].map(read).join());
            N.prototype.left = 'changed';
            out.push([a, b, c, d, e].map(read).join());
            out.join('|');
            """
        );
        Assert.That(
            result,
            Is.EqualTo(
                "proto,b,c,d,proto|proto,b,c,d,proto|proto,b,c,d,proto|changed,b,c,d,changed"
            )
        );
    }

    [Test]
    public void Cached_Add_Transition_Respects_Setters_ReadOnly_And_Extensibility()
    {
        var result = Eval(
            """
            'use strict';
            function T() {}
            var log = [];
            function add(o, v) { o.value = v; return o; }
            for (var i = 0; i < 3; i++) add(new T(), i);
            Object.defineProperty(T.prototype, 'value', {
                set: function (v) { log.push('set' + v); }, configurable: true
            });
            var s = add(new T(), 5);
            log.push(Object.keys(s).length);
            delete T.prototype.value;
            Object.defineProperty(T.prototype, 'value', { value: 1, writable: false, configurable: true });
            try { add(new T(), 6); log.push('no-throw'); } catch (e) { log.push(e.constructor.name); }
            delete T.prototype.value;
            var sealed = Object.preventExtensions(new T());
            try { add(sealed, 7); log.push('no-throw'); } catch (e) { log.push(e.constructor.name); }
            log.push(add(new T(), 8).value);
            log.join('|');
            """
        );
        Assert.That(result, Is.EqualTo("set5|0|TypeError|TypeError|8"));
    }

    [Test]
    public void Cached_Add_Transition_Respects_Host_Object_Extensibility()
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        realm.Global["locked"] = new NonExtensiblePlainObject(realm);
        var result = realm.Eval(
            """
            'use strict';
            function add(o) { o.value = 1; }
            add({});
            let threw = false;
            try { add(locked); } catch (e) { threw = e instanceof TypeError; }
            threw && !Object.hasOwn(locked, 'value');
            """
        );
        Assert.That(result.IsTrue, Is.True);
    }

    private sealed class NonExtensiblePlainObject(JsRealm realm) : JsPlainObject(realm)
    {
        public override bool IsExtensible => false;
    }

    [Test]
    public void ReadOnlySpan_Observes_InPlace_Literal_Writes()
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var array = (JsArray)realm.Eval("function make() { return [1, 2]; } make();").AsObject();
        var span = array.AsReadOnlySpan();

        array[0u] = 3;

        Assert.That(span[0].Int32Value, Is.EqualTo(3));
        Assert.That(realm.Eval("make()[0];").Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void ArrayBuiltins_Read_Shared_Elements_And_Observe_Callback_Mutations()
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var result = realm.Eval(
            """
            function make() { return [1, 2, 3]; }
            var first = make(), second = make();
            [first.join(), first.includes(2), first.slice(1).join()].join('|');
            """
        );
        Assert.That(result.AsString(), Is.EqualTo("1,2,3|true|2,3"));

        var first = (JsArray)realm.Global["first"].AsObject();
        var second = (JsArray)realm.Global["second"].AsObject();
        Assert.That(first.DenseForRead, Is.SameAs(second.DenseForRead));

        var mapped = realm.Eval(
            """
            first.map((value, index) => {
                if (index === 0) first.fill(7, 1);
                return value;
            }).join();
            """
        );
        Assert.That(mapped.AsString(), Is.EqualTo("1,7,7"));
        Assert.That(second[1u].Int32Value, Is.EqualTo(2));
    }

    [Test]
    public void Bitwise_Double_Operands_Match_ToInt32()
    {
        var result = Eval(
            """
            var xs = [0xffffffff, -0xffffffff, 4294967296.5, -2147483649, 1e20, NaN, Infinity, -0, 2.9, -2.9];
            var out = [];
            for (var i = 0; i < xs.length; i++) {
                var x = xs[i];
                out.push(x & 0xffffffff, x | 0, x ^ 0xc761c23c, x << 3, x >> 1, x >>> 16, 0x7ed55d16 + x >>> 0);
            }
            out.join(',');
            """
        );
        Assert.That(
            result,
            Is.EqualTo(
                "-1,-1,949894595,-8,-1,65535,2127912213,1,1,-949894595,8,0,0,2127912215,0,0,-949894596,0,0,0,2127912214,2147483647,2147483647,-1197589053,-8,1073741823,32767,4275395861,1661992960,1661992960,-1536048580,411041792,830996480,25360,3789897728,0,0,-949894596,0,0,0,0,0,0,-949894596,0,0,0,0,0,0,-949894596,0,0,0,2127912214,2,2,-949894594,16,1,0,2127912216,-2,-2,949894594,-16,-1,65535,2127912211"
            )
        );
    }
}
