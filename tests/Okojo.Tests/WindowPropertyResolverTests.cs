using Okojo.JavaScript;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;

namespace Okojo.Tests;

public class WindowPropertyResolverTests
{
    private sealed class Resolver : IWindowPropertyResolver
    {
        private readonly List<JsValue> _indexed = [];
        private readonly Dictionary<string, JsValue> _named = new(StringComparer.Ordinal);

        public void AddIndexed(JsValue value) => _indexed.Add(value);

        public void AddNamed(string name, JsValue value) => _named[name] = value;

        public bool TryGetIndexed(uint index, out JsValue value)
        {
            if (index < (uint)_indexed.Count)
            {
                value = _indexed[(int)index];
                return true;
            }

            value = JsValue.Undefined;
            return false;
        }

        public bool TryGetNamed(string name, out JsValue value) =>
            _named.TryGetValue(name, out value);

        public void CollectIndices(List<uint> indices)
        {
            for (var index = 0; index < _indexed.Count; index++)
                indices.Add((uint)index);
        }

        public void CollectNames(List<string> names) => names.AddRange(_named.Keys);
    }

    private static (JsRuntime Runtime, JsRealm Realm, JsWindowProxy Proxy, Resolver Resolver)
        CreateWindow()
    {
        var runtime = JsRuntime.Create();
        var proxy = new JsWindowProxy(runtime.MainRealm);
        var realm = runtime.CreateRealm(options => options.GlobalThisObject = proxy);
        proxy.SetTarget(realm);
        var resolver = new Resolver();
        proxy.SetPropertyResolver(resolver);
        ((JsGlobalObject)realm.GlobalObject).SetPropertyResolver(resolver);
        return (runtime, realm, proxy, resolver);
    }

    [Test]
    public void WindowResolvesIndexedAndNamedProperties()
    {
        var (runtime, realm, _, resolver) = CreateWindow();
        using (runtime)
        {
            var child = new JsPlainObject(realm);
            child.SetProperty("marker", 42);
            var element = new JsPlainObject(realm);
            element.SetProperty("marker", 7);
            resolver.AddIndexed(child);
            resolver.AddNamed("frame", child);
            resolver.AddNamed("box", element);

            Assert.That(realm.Evaluate("globalThis[0].marker").NumberValue, Is.EqualTo(42));
            Assert
                .That(realm.Evaluate("globalThis['0'] === globalThis[0]").IsTrue, Is.True);
            Assert.That(realm.Evaluate("globalThis.frame.marker").NumberValue, Is.EqualTo(42));
            Assert.That(realm.Evaluate("globalThis.box.marker").NumberValue, Is.EqualTo(7));
            Assert
                .That(realm.Evaluate("'frame' in globalThis && 'box' in globalThis").IsTrue, Is.True);
            Assert
                .That(realm.Evaluate("typeof globalThis.missing === 'undefined'").IsTrue, Is.True);
            Assert
                .That(realm.Evaluate("'missing' in globalThis === false").IsTrue, Is.True);
        }
    }

    [Test]
    public void DeclaredGlobalShadowsAResolvedName()
    {
        var (runtime, realm, _, resolver) = CreateWindow();
        using (runtime)
        {
            resolver.AddNamed("frame", new JsPlainObject(realm));

            realm.Evaluate("var frame = 5;");
            Assert.That(
                realm.Evaluate("frame === 5 && globalThis.frame === 5").IsTrue,
                Is.True
            );

            realm.Evaluate("frame = 7;");
            Assert.That(realm.Evaluate("globalThis.frame === 7").IsTrue, Is.True);
        }
    }

    [Test]
    public void ResolvedNamesAppearInEnumeration()
    {
        var (runtime, realm, _, resolver) = CreateWindow();
        using (runtime)
        {
            resolver.AddNamed("frame", new JsPlainObject(realm));
            resolver.AddIndexed(new JsPlainObject(realm));

            Assert.That(
                realm.Evaluate("Object.keys(globalThis).includes('frame')").IsTrue,
                Is.True
            );
            Assert.That(
                realm
                    .Evaluate(
                        "(function(){ for (var key in globalThis) { if (key === 'frame') return true; } return false; })()"
                    )
                    .IsTrue,
                Is.True
            );
        }
    }

    [Test]
    public void GlobalObjectResolvesNamedPropertiesByIdentifier()
    {
        var (runtime, realm, _, resolver) = CreateWindow();
        using (runtime)
        {
            var element = new JsPlainObject(realm);
            element.SetProperty("marker", 42);
            resolver.AddNamed("box", element);

            Assert.That(realm.Evaluate("box.marker").NumberValue, Is.EqualTo(42));
            Assert.That(realm.Evaluate("typeof missing === 'undefined'").IsTrue, Is.True);
            Assert.That(realm.Evaluate("'box' in globalThis").IsTrue, Is.True);
        }
    }

    [Test]
    public void DeclaredGlobalShadowsTheGlobalResolver()
    {
        var (runtime, realm, _, resolver) = CreateWindow();
        using (runtime)
        {
            resolver.AddNamed("box", new JsPlainObject(realm));

            realm.Evaluate("var box = 5;");
            Assert.That(realm.Evaluate("box === 5").IsTrue, Is.True);

            realm.Evaluate("box = 7;");
            Assert.That(realm.Evaluate("box === 7").IsTrue, Is.True);
        }
    }

    [Test]
    public void ClearingTheResolverRestoresTheTarget()
    {
        var (runtime, realm, proxy, resolver) = CreateWindow();
        using (runtime)
        {
            resolver.AddIndexed(new JsPlainObject(realm));
            resolver.AddNamed("frame", new JsPlainObject(realm));

            Assert.That(realm.Evaluate("typeof globalThis[0] === 'object'").IsTrue, Is.True);
            Assert.That(realm.Evaluate("typeof globalThis.frame === 'object'").IsTrue, Is.True);
            proxy.SetPropertyResolver(null);
            ((JsGlobalObject)realm.GlobalObject).SetPropertyResolver(null);
            Assert.That(realm.Evaluate("typeof globalThis[0] === 'undefined'").IsTrue, Is.True);
            Assert.That(realm.Evaluate("typeof globalThis.frame === 'undefined'").IsTrue, Is.True);
        }
    }
}
