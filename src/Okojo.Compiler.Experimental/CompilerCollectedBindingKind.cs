namespace Okojo.Compiler.Experimental;

internal enum CompilerCollectedBindingKind : byte
{
    Parameter = 0,
    Var = 1,
    Lexical = 2,
    FunctionDeclaration = 3,
    ClassDeclaration = 4,
    Import = 5,
    FunctionNameSelf = 6,
    BlockAlias = 7,
    LoopHeadAlias = 8,
    CatchAlias = 9,
    ClassLexicalAlias = 10
}
