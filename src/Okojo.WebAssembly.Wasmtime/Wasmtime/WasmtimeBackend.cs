using Wasmtime;

namespace Okojo.WebAssembly.Wasmtime;

public sealed class WasmtimeBackend : IWasmBackend
{
    public bool Validate(ReadOnlySpan<byte> wasm)
    {
        try
        {
            using var engine = new Engine();
            using var _ = Module.FromBytes(engine, "module", wasm);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public IWasmCompiledModule Compile(ReadOnlySpan<byte> wasm)
    {
        try
        {
            var engine = new Engine();
            var module = Module.FromBytes(engine, "module", wasm);
            return new WasmtimeCompiledModule(engine, module);
        }
        catch (Exception ex)
        {
            throw new WasmCompileException("Failed to compile WebAssembly module.", ex);
        }
    }

    public IWasmInstance Instantiate(IWasmCompiledModule module, IWasmImportResolver imports)
    {
        ArgumentNullException.ThrowIfNull(imports);
        if (module is not WasmtimeCompiledModule wasmtimeModule)
            throw new ArgumentException("Module was not compiled by WasmtimeBackend.", nameof(module));

        try
        {
            var linker = new Linker(wasmtimeModule.Engine);
            var store = new Store(wasmtimeModule.Engine);

            foreach (var import in wasmtimeModule.Imports)
            {
                if (!imports.TryResolveImport(import, out var wasmExtern))
                    throw new WasmLinkException($"Missing import '{import.ModuleName}.{import.Name}'.");

                switch (wasmExtern)
                {
                    case WasmtimeFunctionWrapper function:
                        linker.Define(import.ModuleName, import.Name, function.GetFunctionForStore(store));
                        break;
                    case WasmtimeMemoryWrapper memory:
                        linker.Define(import.ModuleName, import.Name, memory.Memory);
                        break;
                    case WasmtimeTableWrapper table:
                        linker.Define(import.ModuleName, import.Name, table.Table);
                        break;
                    case WasmtimeGlobalWrapper global:
                        linker.Define(import.ModuleName, import.Name, global.Global);
                        break;
                    default:
                        throw new WasmLinkException(
                            $"Unsupported import extern implementation: {wasmExtern.GetType().FullName}");
                }
            }

            var instance = linker.Instantiate(store, wasmtimeModule.Module);
            return new WasmtimeInstanceWrapper(store, instance, CreateExports(store, instance, wasmtimeModule.Exports));
        }
        catch (WasmtimeException ex)
        {
            throw new WasmLinkException($"Failed to instantiate WebAssembly module: {ex.Message}", ex);
        }
    }

    public IWasmFunction CreateFunction(WasmFunctionType type, WasmHostFunctionCallback callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var store = new Store(new());
        return new WasmtimeFunctionWrapper(store, type, callback);
    }

    public IWasmMemory CreateMemory(WasmMemoryType type)
    {
        var store = new Store(new());
        return new WasmtimeMemoryWrapper(new(store, type.MinimumPages, type.MaximumPages, type.Is64Bit), type);
    }

    public IWasmTable CreateTable(WasmTableType type, object? initialValue = null)
    {
        var store = new Store(new());
        var tableKind = type.ElementKind switch
        {
            WasmValueKind.FuncRef => TableKind.FuncRef,
            WasmValueKind.ExternRef => TableKind.ExternRef,
            _ => throw new NotSupportedException($"Unsupported wasm table element kind: {type.ElementKind}")
        };

        return new WasmtimeTableWrapper(
            new(store, tableKind, initialValue, type.MinimumElements, type.MaximumElements ?? 0),
            type);
    }

    public IWasmGlobal CreateGlobal(WasmGlobalType type, WasmValue initialValue)
    {
        var store = new Store(new());
        return new WasmtimeGlobalWrapper(
            new(
                store,
                WasmtimeValueMapper.ToWasmtime(type.ValueKind),
                initialValue.Kind switch
                {
                    WasmValueKind.Int32 => initialValue.Int32Value,
                    WasmValueKind.Int64 => initialValue.Int64Value,
                    WasmValueKind.Float32 => initialValue.Float32Value,
                    WasmValueKind.Float64 => initialValue.Float64Value,
                    _ => initialValue.ObjectValue
                },
                type.Mutability == WasmMutability.Var ? Mutability.Mutable : Mutability.Immutable),
            type);
    }

    private static IReadOnlyDictionary<string, IWasmExtern> CreateExports(
        Store store,
        Instance instance,
        IReadOnlyList<WasmExportDescriptor> descriptors)
    {
        var exports = new Dictionary<string, IWasmExtern>(StringComparer.Ordinal);
        foreach (var descriptor in descriptors)
            switch (descriptor.Kind)
            {
                case WasmExternalKind.Function:
                    {
                        var fn = instance.GetFunction(descriptor.Name);
                        if (fn is not null)
                            exports[descriptor.Name] = new WasmtimeFunctionWrapper(fn, store);
                        break;
                    }
                case WasmExternalKind.Memory:
                    {
                        var memory = instance.GetMemory(descriptor.Name);
                        if (memory is not null)
                            exports[descriptor.Name] = new WasmtimeMemoryWrapper(memory, (WasmMemoryType)descriptor.Type);
                        break;
                    }
                case WasmExternalKind.Table:
                    {
                        var table = instance.GetTable(descriptor.Name);
                        if (table is not null)
                            exports[descriptor.Name] = new WasmtimeTableWrapper(table, (WasmTableType)descriptor.Type);
                        break;
                    }
                case WasmExternalKind.Global:
                    {
                        var global = instance.GetGlobal(descriptor.Name);
                        if (global is not null)
                            exports[descriptor.Name] = new WasmtimeGlobalWrapper(global, (WasmGlobalType)descriptor.Type);
                        break;
                    }
            }

        return exports;
    }
}
