namespace Okojo.Compiler.Experimental;

internal enum CompilerPlannedStorageKind : byte
{
    LocalRegister = 0,
    LexicalRegister = 1,
    ImportBinding = 2,
    ContextSlot = 3
}
