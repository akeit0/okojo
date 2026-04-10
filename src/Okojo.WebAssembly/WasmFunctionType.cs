namespace Okojo.WebAssembly;

public sealed class WasmFunctionType(IReadOnlyList<WasmValueKind> parameters, IReadOnlyList<WasmValueKind> results)
{
    public IReadOnlyList<WasmValueKind> Parameters { get; } =
        parameters ?? throw new ArgumentNullException(nameof(parameters));

    public IReadOnlyList<WasmValueKind> Results { get; } = results ?? throw new ArgumentNullException(nameof(results));
    public int ParameterCount => Parameters.Count;
    public int ResultCount => Results.Count;
}
