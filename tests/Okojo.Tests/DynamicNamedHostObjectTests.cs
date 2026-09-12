using Okojo.JavaScript;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;

namespace Okojo.Tests;

public sealed class DynamicNamedHostObjectTests
{
    [Test]
    public void DynamicNamedPropertiesSupportReadsWritesMissingKeysAndDescriptors()
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.MainRealm;
        var host = new DynamicCollectionHostObject(realm);
        realm.Global["host"] = JsValue.FromObject(host);

        var result = realm.Eval(
            """
            host.alpha = 1;
            host.beta = "two";
            const descriptor = Object.getOwnPropertyDescriptor(host, "alpha");
            [
              host.alpha,
              host.beta,
              host.missing === undefined,
              "alpha" in host,
              "missing" in host,
              Object.keys(host).join(","),
              descriptor.value,
              descriptor.writable,
              descriptor.enumerable,
              descriptor.configurable
            ].join("|");
            """
        );

        Assert.That(
            result.AsString(),
            Is.EqualTo("1|two|true|true|false|0,1,alpha,beta|1|true|true|false")
        );
    }

    [Test]
    public void InChecksExistenceWithoutInvokingTheDynamicGetter()
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.MainRealm;
        var host = new DynamicCollectionHostObject(realm);
        host.SetInitial("present", 7);
        realm.Global["host"] = JsValue.FromObject(host);

        var getCountBefore = host.GetCount;
        var result = realm.Eval("'present' in host && !('absent' in host)");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsTrue, Is.True);
            Assert.That(host.GetCount, Is.EqualTo(getCountBefore));
        });
    }

    [Test]
    public void OrdinaryOwnPropertiesShadowDynamicNamesDuringReadsAndEnumeration()
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.MainRealm;
        var host = new DynamicCollectionHostObject(realm);
        host.SetInitial("shadowed", 7);
        host.DefineDataProperty("shadowed", 9, JsShapePropertyFlags.None);
        realm.Global["host"] = JsValue.FromObject(host);

        var result = realm.Eval(
            "[host.shadowed, Object.keys(host).includes('shadowed')].join('|')"
        );

        Assert.That(result.AsString(), Is.EqualTo("9|false"));
    }

    [Test]
    public void DynamicNamedPropertiesPreservePrototypeAndReceiverBehavior()
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.MainRealm;
        var prototype = realm
            .Eval(
                """
                ({
                  method() { return this.value; },
                  get doubled() { return this.value * 2; },
                  set doubled(value) { this.value = value / 2; }
                })
                """
            )
            .AsObject();
        var host = new DynamicCollectionHostObject(realm, prototype);
        host.SetInitial("value", 3);
        realm.Global["host"] = JsValue.FromObject(host);

        var result = realm.Eval(
            """
            const child = Object.create(host);
            child.value = 20;
            host.doubled = 10;
            [
              host.method(),
              host.doubled,
              host.value,
              child.method(),
              child.doubled,
              child.value,
              Object.hasOwn(child, "value"),
              Object.hasOwn(host, "method")
            ].join("|");
            """
        );

        Assert.That(result.AsString(), Is.EqualTo("5|10|5|20|40|20|true|false"));
    }

    [Test]
    public void RejectedDynamicAssignmentThrowsOnlyInStrictMode()
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.MainRealm;
        var host = new DynamicCollectionHostObject(realm);
        host.SetInitial("rejected", 1);
        realm.Global["host"] = JsValue.FromObject(host);

        var result = realm.Eval(
            """
            host.rejected = 2;
            let strictError = false;
            try {
              (function() { "use strict"; host.rejected = 3; })();
            } catch (error) {
              strictError = error instanceof TypeError;
            }
            [host.rejected, strictError].join("|");
            """
        );

        Assert.Multiple(() =>
        {
            Assert.That(result.AsString(), Is.EqualTo("1|true"));
            Assert.That(host.SetAttempts, Is.EqualTo(2));
        });
    }

    [Test]
    public void SymbolsAndIndexKeysStayOnTheOrdinaryPropertyPath()
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.MainRealm;
        var host = new DynamicCollectionHostObject(realm);
        realm.Global["host"] = JsValue.FromObject(host);

        var result = realm.Eval(
            """
            const symbol = Symbol("marker");
            const objectKey = { toString() { return "objectKey"; } };
            host[objectKey] = 12;
            host[symbol] = 13;
            host[7] = 14;
            [
              host.objectKey,
              host[symbol],
              host[7],
              symbol in host,
              Object.getOwnPropertySymbols(host)[0] === symbol,
              Object.keys(host).join(",")
            ].join("|");
            """
        );

        Assert.Multiple(() =>
        {
            Assert.That(result.AsString(), Is.EqualTo("12|13|14|true|true|0,7,objectKey"));
            Assert.That(host.ObservedNames, Does.Contain("objectKey"));
            Assert.That(host.ObservedNames, Does.Not.Contain("7"));
        });
    }

    private sealed class DynamicCollectionHostObject : JsIndexedObject
    {
        private readonly List<string> names = [];
        private readonly Dictionary<string, JsValue> values = new(StringComparer.Ordinal);

        public DynamicCollectionHostObject(JsRealm realm, JsObject? prototype = null)
            : base(realm, prototype) { }

        public int GetCount { get; private set; }
        public int SetAttempts { get; private set; }
        public HashSet<string> ObservedNames { get; } = new(StringComparer.Ordinal);

        protected override int IndexedElementCount => names.Count;

        public void SetInitial(string name, JsValue value)
        {
            if (!values.ContainsKey(name))
                names.Add(name);
            values[name] = value;
        }

        protected override bool TryGetIndexedValue(uint index, out JsValue value)
        {
            if (index < (uint)names.Count)
            {
                value = names[(int)index];
                return true;
            }

            value = JsValue.Undefined;
            return false;
        }

        protected override bool TryGetDynamicNamedProperty(string name, out JsValue value)
        {
            ObservedNames.Add(name);
            GetCount++;
            return values.TryGetValue(name, out value);
        }

        protected override JsDynamicNamedPropertySetResult SetDynamicNamedProperty(
            string name,
            JsValue value
        )
        {
            ObservedNames.Add(name);
            SetAttempts++;
            if (name == "rejected")
                return JsDynamicNamedPropertySetResult.Rejected;

            SetInitial(name, value);
            return JsDynamicNamedPropertySetResult.Succeeded;
        }

        protected override bool HasDynamicNamedProperty(string name)
        {
            ObservedNames.Add(name);
            return values.ContainsKey(name);
        }

        protected override void CollectDynamicNamedPropertyNames(List<string> namesOut)
        {
            namesOut.AddRange(names);
        }

        protected override JsShapePropertyFlags GetDynamicNamedPropertyFlags(string name)
        {
            _ = name;
            return JsShapePropertyFlags.Writable | JsShapePropertyFlags.Enumerable;
        }
    }
}
