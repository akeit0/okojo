using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Objects;

namespace Okojo.JavaScript.Execution;

internal sealed record ModuleExecutionCompilation(
    JsScript Script,
    JsValue[] InitialContextSlots,
    IReadOnlyList<ModuleHoistedFunction> HoistedFunctions
);

internal readonly record struct ModuleHoistedFunction(
    JsBytecodeFunction Template,
    ModuleHoistedFunctionStorageKind StorageKind,
    int StorageIndex
);

internal enum ModuleHoistedFunctionStorageKind : byte
{
    ModuleCell,
    ContextSlot,
}
