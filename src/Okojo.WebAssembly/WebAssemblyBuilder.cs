using System.Runtime.CompilerServices;
using Okojo.Runtime;

namespace Okojo.WebAssembly;

public sealed class WebAssemblyBuilder
{
    private static readonly ConditionalWeakTable<JsRuntimeOptions, State> States = new();

    private readonly JsRuntimeOptions options;
    private readonly State state;

    internal WebAssemblyBuilder(JsRuntimeOptions options)
    {
        this.options = options;
        state = States.GetValue(options, static _ => new());
    }

    public WebAssemblyBuilder UseBackend(Func<IWasmBackend> backendFactory)
    {
        ArgumentNullException.ThrowIfNull(backendFactory);
        state.BackendFactory = backendFactory;
        return this;
    }

    public WebAssemblyBuilder InstallGlobals()
    {
        options.UseRealmSetup(realm =>
        {
            var backendFactory = state.BackendFactory
                                 ?? throw new InvalidOperationException(
                                     "WebAssembly globals require a configured wasm backend. Call UseBackend(...) first.");
            WebAssemblyInstaller.Install(realm, backendFactory());
        });

        return this;
    }

    public WebAssemblyBuilder UseGlobal(string name, Func<JsRealm, JsValue> valueFactory)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(valueFactory);
        options.UseRealmSetup(realm => realm.Global[name] = valueFactory(realm));
        return this;
    }

    private sealed class State
    {
        public Func<IWasmBackend>? BackendFactory;
    }
}
