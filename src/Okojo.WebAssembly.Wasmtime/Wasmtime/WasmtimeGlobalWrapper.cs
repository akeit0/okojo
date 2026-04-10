using Wasmtime;

namespace Okojo.WebAssembly.Wasmtime;

internal sealed class WasmtimeGlobalWrapper(Global global, WasmGlobalType type) : IWasmGlobal
{
    public Global Global { get; } = global;

    public WasmExternalKind Kind => WasmExternalKind.Global;

    public WasmGlobalType Type { get; } = type;

    public WasmValue GetValue()
    {
        var rawValue = Global.GetValue();
        return Type.ValueKind switch
        {
            WasmValueKind.Int32 => WasmValue.FromInt32((int)rawValue!),
            WasmValueKind.Int64 => WasmValue.FromInt64((long)rawValue!),
            WasmValueKind.Float32 => WasmValue.FromFloat32((float)rawValue!),
            WasmValueKind.Float64 => WasmValue.FromFloat64((double)rawValue!),
            _ => new(Type.ValueKind, rawValue)
        };
    }

    public void SetValue(WasmValue value)
    {
        Global.SetValue(value.Kind switch
        {
            WasmValueKind.Int32 => value.Int32Value,
            WasmValueKind.Int64 => value.Int64Value,
            WasmValueKind.Float32 => value.Float32Value,
            WasmValueKind.Float64 => value.Float64Value,
            _ => value.ObjectValue
        });
    }
}
