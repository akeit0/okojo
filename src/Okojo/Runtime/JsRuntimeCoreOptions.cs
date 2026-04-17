using System.Reflection;
using Okojo.RegExp;

namespace Okojo.Runtime;

/// <summary>
///     Engine-core runtime configuration.
///     Keep this for embedding choices that affect engine semantics rather than host integration.
/// </summary>
public sealed class JsRuntimeCoreOptions
{
    private readonly List<Assembly> clrAssemblies = new();
    private readonly List<IRealmApiModule> realmApiModules = new();
    private int clrAssembliesVersion;

    public bool ClrAccessEnabled { get; private set; }
    public IRegExpEngine? RegExpEngine { get; private set; } = RegExp.RegExpEngine.Default;
    public IReadOnlyList<Assembly> ClrAssemblies => clrAssemblies;
    public IReadOnlyList<IRealmApiModule> RealmApiModules => realmApiModules;
    internal IClrAccessProvider? ClrAccessProvider { get; private set; }
    internal int ClrAssembliesVersion => clrAssembliesVersion;

    internal JsRuntimeCoreOptions EnableClrAccess(IClrAccessProvider? provider = null)
    {
        ClrAccessEnabled = true;
        ClrAccessProvider ??= provider;
        return this;
    }

    internal JsRuntimeCoreOptions UseClrAccessProvider(IClrAccessProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ClrAccessProvider = provider;
        return this;
    }

    public JsRuntimeCoreOptions AddRealmApiModule(IRealmApiModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (!realmApiModules.Contains(module))
            realmApiModules.Add(module);
        return this;
    }

    public JsRuntimeCoreOptions UseRealmSetup(Action<JsRealm> setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        realmApiModules.Add(new DelegateRealmApiModule(setup));
        return this;
    }

    public JsRuntimeCoreOptions UseGlobals(Action<JsGlobalInstaller> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return UseRealmSetup(realm => realm.InstallGlobals(configure));
    }

    public JsRuntimeCoreOptions UseRegExpEngine(IRegExpEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        RegExpEngine = engine;
        return this;
    }

    public JsRuntimeCoreOptions AddClrAssembly(params Assembly[] assemblies)
    {
        AddClrAssembliesCore(assemblies);
        return this;
    }

    internal bool AddClrAssembliesCore(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        var changed = false;
        for (var i = 0; i < assemblies.Length; i++)
        {
            var assembly = assemblies[i] ?? throw new ArgumentNullException(nameof(assemblies));
            if (!clrAssemblies.Contains(assembly))
            {
                clrAssemblies.Add(assembly);
                changed = true;
            }
        }

        if (changed)
            clrAssembliesVersion++;
        return changed;
    }

    internal JsRuntimeCoreOptions Clone()
    {
        var clone = new JsRuntimeCoreOptions
        {
            ClrAccessEnabled = ClrAccessEnabled,
            RegExpEngine = RegExpEngine,
            ClrAccessProvider = ClrAccessProvider,
            clrAssembliesVersion = clrAssembliesVersion
        };
        clone.clrAssemblies.AddRange(clrAssemblies);
        clone.realmApiModules.AddRange(realmApiModules);
        return clone;
    }
}
