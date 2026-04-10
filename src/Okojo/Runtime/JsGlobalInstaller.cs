namespace Okojo.Runtime;

/// <summary>
///     Explicit helper for installing host globals into a realm without introducing
///     reflection-heavy host binding policy into core Okojo.
/// </summary>
public sealed class JsGlobalInstaller
{
    internal JsGlobalInstaller(JsRealm realm)
    {
        this.Realm = realm;
    }

    public JsRealm Realm { get; }

    public JsGlobalInstaller Value(string name, JsValue value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Realm.Global[name] = value;
        return this;
    }

    public JsGlobalInstaller Value(string name, Func<JsRealm, JsValue> valueFactory)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(valueFactory);
        Realm.Global[name] = valueFactory(Realm);
        return this;
    }

    public JsGlobalInstaller Function(string name, int length, JsHostFunctionBody body, bool isConstructor = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(body);
        var fn = new JsHostFunction(Realm, body, name, length, isConstructor);
        Realm.Global[name] = JsValue.FromObject(fn);
        return this;
    }

    public JsGlobalInstaller Function(string name, int length, Func<CallInfo, JsValue> body, bool isConstructor = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(body);
        return Function(name, length, (in info) => body(info), isConstructor);
    }

    public JsGlobalInstaller Property(PropertyDefinition definition)
    {
        Realm.GlobalObject.DefineNewPropertiesNoCollision(Realm, [definition]);
        return this;
    }

    public JsGlobalInstaller Properties(IEnumerable<PropertyDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        if (definitions is PropertyDefinition[] array)
        {
            if (array.Length != 0)
                Realm.GlobalObject.DefineNewPropertiesNoCollision(Realm, array);
            return this;
        }

        var buffer = new List<PropertyDefinition>();
        foreach (var definition in definitions)
            buffer.Add(definition);

        if (buffer.Count != 0)
            Realm.GlobalObject.DefineNewPropertiesNoCollision(Realm, buffer.ToArray());
        return this;
    }
}
