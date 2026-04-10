using Wasmtime;

namespace Okojo.WebAssembly.Wasmtime;

internal sealed class WasmtimeInstanceWrapper(
    Store store,
    Instance instance,
    IReadOnlyDictionary<string, IWasmExtern> exports)
    : IWasmInstance
{
    public Instance Instance { get; } = instance;

    public IReadOnlyDictionary<string, IWasmExtern> Exports { get; } = exports;

    public bool TryGetExport(string name, out IWasmExtern wasmExtern)
    {
        return Exports.TryGetValue(name, out wasmExtern!);
    }

    public void Dispose()
    {
        store.Dispose();
    }
}
