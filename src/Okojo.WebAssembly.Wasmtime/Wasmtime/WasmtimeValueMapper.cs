using Wasmtime;

namespace Okojo.WebAssembly.Wasmtime;

internal static class WasmtimeValueMapper
{
    public static WasmValueKind ToOkojo(ValueKind kind)
    {
        return kind switch
        {
            ValueKind.Int32 => WasmValueKind.Int32,
            ValueKind.Int64 => WasmValueKind.Int64,
            ValueKind.Float32 => WasmValueKind.Float32,
            ValueKind.Float64 => WasmValueKind.Float64,
            ValueKind.FuncRef => WasmValueKind.FuncRef,
            ValueKind.ExternRef => WasmValueKind.ExternRef,
            ValueKind.V128 => WasmValueKind.V128,
            _ => throw new NotSupportedException($"Unsupported Wasmtime value kind: {kind}")
        };
    }

    public static ValueKind ToWasmtime(WasmValueKind kind)
    {
        return kind switch
        {
            WasmValueKind.Int32 => ValueKind.Int32,
            WasmValueKind.Int64 => ValueKind.Int64,
            WasmValueKind.Float32 => ValueKind.Float32,
            WasmValueKind.Float64 => ValueKind.Float64,
            WasmValueKind.FuncRef => ValueKind.FuncRef,
            WasmValueKind.ExternRef => ValueKind.ExternRef,
            WasmValueKind.V128 => ValueKind.V128,
            _ => throw new NotSupportedException($"Unsupported wasm value kind: {kind}")
        };
    }

    public static ValueBox ToWasmtime(in WasmValue value)
    {
        return value.Kind switch
        {
            WasmValueKind.Int32 => value.Int32Value,
            WasmValueKind.Int64 => value.Int64Value,
            WasmValueKind.Float32 => value.Float32Value,
            WasmValueKind.Float64 => value.Float64Value,
            WasmValueKind.FuncRef => value.ObjectValue is null ? Function.Null : (Function)value.ObjectValue,
            WasmValueKind.ExternRef => value.ObjectValue is string s ? s : ValueBox.AsBox(value.ObjectValue),
            WasmValueKind.V128 => (V128)value.ObjectValue!,
            _ => throw new NotSupportedException($"Unsupported wasm value kind: {value.Kind}")
        };
    }

    public static WasmValue ToOkojo(ValueBox value, WasmValueKind kind, Store? store = null)
    {
        return kind switch
        {
            WasmValueKind.Int32 => WasmValue.FromInt32(value.AsInt32()),
            WasmValueKind.Int64 => WasmValue.FromInt64(value.AsInt64()),
            WasmValueKind.Float32 => WasmValue.FromFloat32(value.AsSingle()),
            WasmValueKind.Float64 => WasmValue.FromFloat64(value.AsDouble()),
            WasmValueKind.FuncRef => new(WasmValueKind.FuncRef, store is null ? null : value.AsFunction(store)),
            WasmValueKind.ExternRef => WasmValue.FromExternRef(value.As<object>()),
            WasmValueKind.V128 => new(WasmValueKind.V128, value.AsV128()),
            _ => throw new NotSupportedException($"Unsupported wasm value kind: {kind}")
        };
    }
}
