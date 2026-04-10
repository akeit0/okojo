using Wasmtime;

namespace Okojo.WebAssembly.Wasmtime;

internal sealed class WasmtimeCompiledModule(Engine engine, Module module) : IWasmCompiledModule
{
    public Engine Engine { get; } = engine;

    public Module Module { get; } = module;

    public IReadOnlyList<WasmImportDescriptor> Imports { get; } = module.Imports.Select(CreateImport).ToArray();

    public IReadOnlyList<WasmExportDescriptor> Exports { get; } = module.Exports.Select(CreateExport).ToArray();

    public void Dispose()
    {
        Module.Dispose();
        Engine.Dispose();
    }

    private static WasmImportDescriptor CreateImport(Import import)
    {
        return import switch
        {
            FunctionImport functionImport => new(
                import.ModuleName,
                import.Name,
                WasmExternalKind.Function,
                new WasmFunctionType(
                    functionImport.Parameters.Select(WasmtimeValueMapper.ToOkojo).ToArray(),
                    functionImport.Results.Select(WasmtimeValueMapper.ToOkojo).ToArray())),
            MemoryImport memoryImport => new(
                import.ModuleName,
                import.Name,
                WasmExternalKind.Memory,
                new WasmMemoryType(memoryImport.Minimum, memoryImport.Maximum, false, memoryImport.Is64Bit)),
            TableImport tableImport => new(
                import.ModuleName,
                import.Name,
                WasmExternalKind.Table,
                new WasmTableType(WasmtimeValueMapper.ToOkojo(tableImport.Kind), tableImport.Minimum,
                    tableImport.Maximum)),
            GlobalImport globalImport => new(
                import.ModuleName,
                import.Name,
                WasmExternalKind.Global,
                new WasmGlobalType(
                    WasmtimeValueMapper.ToOkojo(globalImport.Kind),
                    globalImport.Mutability == Mutability.Mutable ? WasmMutability.Var : WasmMutability.Const)),
            _ => throw new NotSupportedException($"Unsupported Wasmtime import type: {import.GetType().FullName}")
        };
    }

    private static WasmExportDescriptor CreateExport(Export export)
    {
        return export switch
        {
            FunctionExport functionExport => new(
                export.Name,
                WasmExternalKind.Function,
                new WasmFunctionType(
                    functionExport.Parameters.Select(WasmtimeValueMapper.ToOkojo).ToArray(),
                    functionExport.Results.Select(WasmtimeValueMapper.ToOkojo).ToArray())),
            MemoryExport memoryExport => new(
                export.Name,
                WasmExternalKind.Memory,
                new WasmMemoryType(memoryExport.Minimum, memoryExport.Maximum, false, memoryExport.Is64Bit)),
            TableExport tableExport => new(
                export.Name,
                WasmExternalKind.Table,
                new WasmTableType(WasmtimeValueMapper.ToOkojo(tableExport.Kind), tableExport.Minimum,
                    tableExport.Maximum)),
            GlobalExport globalExport => new(
                export.Name,
                WasmExternalKind.Global,
                new WasmGlobalType(
                    WasmtimeValueMapper.ToOkojo(globalExport.Kind),
                    globalExport.Mutability == Mutability.Mutable ? WasmMutability.Var : WasmMutability.Const)),
            _ => throw new NotSupportedException($"Unsupported Wasmtime export type: {export.GetType().FullName}")
        };
    }
}
