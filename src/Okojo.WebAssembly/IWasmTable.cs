namespace Okojo.WebAssembly;

public interface IWasmTable : IWasmExtern
{
    WasmTableType Type { get; }
    uint Length { get; }
    object? GetElement(uint index);
    void SetElement(uint index, object? value);
    uint Grow(uint delta, object? initialValue);
}
