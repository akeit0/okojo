namespace Okojo.Compiler.Experimental;

internal enum CompilerCollectedScopeKind : byte
{
    Program = 0,
    Function = 1,
    Block = 2,
    Catch = 3,
    Class = 4,
    StaticBlock = 5
}
