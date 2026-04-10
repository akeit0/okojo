using System.Runtime.InteropServices;

namespace Okojo.WebAssembly;

[StructLayout(LayoutKind.Explicit)]
public readonly struct WasmValue
{
    [FieldOffset(0)] private readonly int int32Value;

    [FieldOffset(0)] private readonly long int64Value;

    [FieldOffset(0)] private readonly float float32Value;

    [FieldOffset(0)] private readonly double float64Value;

    [FieldOffset(8)] private readonly object? objectValue;

    private WasmValue(WasmValueKind kind, int value)
    {
        int64Value = default;
        float32Value = default;
        float64Value = default;
        objectValue = null;
        int32Value = value;
        this.Kind = kind;
    }

    private WasmValue(WasmValueKind kind, long value)
    {
        int32Value = default;
        float32Value = default;
        float64Value = default;
        objectValue = null;
        int64Value = value;
        this.Kind = kind;
    }

    private WasmValue(WasmValueKind kind, float value)
    {
        int32Value = default;
        int64Value = default;
        float64Value = default;
        objectValue = null;
        float32Value = value;
        this.Kind = kind;
    }

    private WasmValue(WasmValueKind kind, double value)
    {
        int32Value = default;
        int64Value = default;
        float32Value = default;
        objectValue = null;
        float64Value = value;
        this.Kind = kind;
    }

    public WasmValue(WasmValueKind kind, object? value)
    {
        int32Value = default;
        int64Value = default;
        float32Value = default;
        float64Value = default;
        objectValue = value;
        this.Kind = kind;
    }

    [field: FieldOffset(16)] public WasmValueKind Kind { get; }

    public int Int32Value => Kind == WasmValueKind.Int32
        ? int32Value
        : throw CreateKindMismatchException(WasmValueKind.Int32);

    public long Int64Value => Kind == WasmValueKind.Int64
        ? int64Value
        : throw CreateKindMismatchException(WasmValueKind.Int64);

    public float Float32Value => Kind == WasmValueKind.Float32
        ? float32Value
        : throw CreateKindMismatchException(WasmValueKind.Float32);

    public double Float64Value => Kind == WasmValueKind.Float64
        ? float64Value
        : throw CreateKindMismatchException(WasmValueKind.Float64);

    public object? ObjectValue => Kind is WasmValueKind.FuncRef or WasmValueKind.ExternRef or WasmValueKind.V128
        ? objectValue
        : null;

    public static WasmValue FromInt32(int value)
    {
        return new(WasmValueKind.Int32, value);
    }

    public static WasmValue FromInt64(long value)
    {
        return new(WasmValueKind.Int64, value);
    }

    public static WasmValue FromFloat32(float value)
    {
        return new(WasmValueKind.Float32, value);
    }

    public static WasmValue FromFloat64(double value)
    {
        return new(WasmValueKind.Float64, value);
    }

    public static WasmValue FromExternRef(object? value)
    {
        return new(WasmValueKind.ExternRef, value);
    }

    private InvalidOperationException CreateKindMismatchException(WasmValueKind expected)
    {
        return new($"WasmValue kind is {Kind}, not {expected}.");
    }
}
