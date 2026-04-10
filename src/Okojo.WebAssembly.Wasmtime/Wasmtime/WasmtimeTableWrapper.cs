using Wasmtime;

namespace Okojo.WebAssembly.Wasmtime;

internal sealed class WasmtimeTableWrapper(Table table, WasmTableType type) : IWasmTable
{
    public Table Table { get; } = table;

    public WasmExternalKind Kind => WasmExternalKind.Table;

    public WasmTableType Type { get; } = type;

    public uint Length => checked((uint)Table.GetSize());

    public object? GetElement(uint index)
    {
        var value = Table.GetElement(index);
        if (Type.ElementKind == WasmValueKind.FuncRef && value is Function function)
            return new WasmtimeFunctionWrapper(function);
        return value;
    }

    public void SetElement(uint index, object? value)
    {
        if (Type.ElementKind == WasmValueKind.FuncRef && value is WasmtimeFunctionWrapper function)
        {
            Table.SetElement(index, function.Function);
            return;
        }

        Table.SetElement(index, value);
    }

    public uint Grow(uint delta, object? initialValue)
    {
        if (Type.ElementKind == WasmValueKind.FuncRef && initialValue is WasmtimeFunctionWrapper function)
            return checked((uint)Table.Grow(delta, function.Function));

        return checked((uint)Table.Grow(delta, initialValue));
    }
}
