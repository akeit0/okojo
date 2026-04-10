using System.Buffers;
using Wasmtime;

namespace Okojo.WebAssembly.Wasmtime;

internal sealed class WasmtimeFunctionWrapper : IWasmFunction
{
    private readonly Func<Store, Function>? functionFactory;
    private readonly Store? store;

    public WasmtimeFunctionWrapper(Function function, Store? store = null)
    {
        this.store = store;
        Function = function;
        functionFactory = null;
        Type = new(
            function.Parameters.Select(WasmtimeValueMapper.ToOkojo).ToArray(),
            function.Results.Select(WasmtimeValueMapper.ToOkojo).ToArray());
    }

    public WasmtimeFunctionWrapper(Store store, WasmFunctionType type, WasmHostFunctionCallback callback)
    {
        this.store = store;
        Type = type;
        functionFactory = targetStore => CreateHostFunction(targetStore, type, callback);
        Function = functionFactory(store);
    }

    public Function Function { get; }

    public WasmExternalKind Kind => WasmExternalKind.Function;

    public WasmFunctionType Type { get; }

    public void Invoke(ReadOnlySpan<WasmValue> arguments, Span<WasmValue> returnValues)
    {
        if (arguments.Length != Type.ParameterCount)
            throw new WasmRuntimeTrapException("Incorrect WebAssembly function arity.");
        if (returnValues.Length != Type.ResultCount)
            throw new WasmRuntimeTrapException("Incorrect WebAssembly function result buffer length.");

        ValueBox[]? rentedArgs = null;
        var mappedArgs = arguments.Length == 0
            ? Array.Empty<ValueBox>()
            : rentedArgs = ArrayPool<ValueBox>.Shared.Rent(arguments.Length);

        try
        {
            for (var i = 0; i < arguments.Length; i++)
                mappedArgs[i] = WasmtimeValueMapper.ToWasmtime(arguments[i]);

            var result = Function.Invoke(mappedArgs.AsSpan(0, arguments.Length));
            WriteResults(result, returnValues);
        }
        catch (Exception ex)
        {
            var detail = string.IsNullOrWhiteSpace(ex.Message)
                ? ex.GetType().Name
                : $"{ex.GetType().Name}: {ex.Message}";
            throw new WasmRuntimeTrapException($"Failed to invoke WebAssembly function. {detail}", ex);
        }
        finally
        {
            if (rentedArgs is not null)
                ArrayPool<ValueBox>.Shared.Return(rentedArgs, true);
        }
    }

    public Function GetFunctionForStore(Store targetStore)
    {
        if (functionFactory is null || ReferenceEquals(targetStore, store))
            return Function;

        return functionFactory(targetStore);
    }

    private void WriteResults(object? result, Span<WasmValue> returnValues)
    {
        if (Type.ResultCount == 0)
            return;

        if (Type.ResultCount == 1)
        {
            returnValues[0] = MapSingleResult(result, Type.Results[0]);
            return;
        }

        if (result is not object[] values || values.Length != Type.ResultCount)
            throw new WasmRuntimeTrapException("Unexpected multi-value WebAssembly result shape.");

        for (var i = 0; i < values.Length; i++)
            returnValues[i] = MapSingleResult(values[i], Type.Results[i]);
    }

    private WasmValue MapSingleResult(object? result, WasmValueKind kind)
    {
        return kind switch
        {
            WasmValueKind.Int32 => WasmValue.FromInt32((int)result!),
            WasmValueKind.Int64 => WasmValue.FromInt64((long)result!),
            WasmValueKind.Float32 => WasmValue.FromFloat32((float)result!),
            WasmValueKind.Float64 => WasmValue.FromFloat64((double)result!),
            WasmValueKind.FuncRef => new(WasmValueKind.FuncRef, result),
            WasmValueKind.ExternRef => WasmValue.FromExternRef(result),
            WasmValueKind.V128 => new(WasmValueKind.V128, (V128)result!),
            _ => throw new NotSupportedException($"Unsupported wasm result kind: {kind}")
        };
    }

    private static Function CreateHostFunction(Store targetStore, WasmFunctionType type,
        WasmHostFunctionCallback callback)
    {
        return Function.FromCallback(
            targetStore,
            (_, arguments, results) =>
            {
                WasmValue[]? rentedArgs = null;
                WasmValue[]? rentedResults = null;
                var mappedArgs = arguments.Length == 0
                    ? Array.Empty<WasmValue>()
                    : rentedArgs = ArrayPool<WasmValue>.Shared.Rent(arguments.Length);
                var mappedResults = type.ResultCount == 0
                    ? Array.Empty<WasmValue>()
                    : rentedResults = ArrayPool<WasmValue>.Shared.Rent(type.ResultCount);

                try
                {
                    for (var i = 0; i < arguments.Length; i++)
                        mappedArgs[i] = WasmtimeValueMapper.ToOkojo(arguments[i], type.Parameters[i], targetStore);

                    callback(mappedArgs.AsSpan(0, arguments.Length), mappedResults.AsSpan(0, type.ResultCount));

                    for (var i = 0; i < type.ResultCount; i++)
                        results[i] = WasmtimeValueMapper.ToWasmtime(mappedResults[i]);
                }
                finally
                {
                    if (rentedArgs is not null)
                        ArrayPool<WasmValue>.Shared.Return(rentedArgs, true);
                    if (rentedResults is not null)
                        ArrayPool<WasmValue>.Shared.Return(rentedResults, true);
                }
            },
            type.Parameters.Select(WasmtimeValueMapper.ToWasmtime).ToArray(),
            type.Results.Select(WasmtimeValueMapper.ToWasmtime).ToArray());
    }
}
