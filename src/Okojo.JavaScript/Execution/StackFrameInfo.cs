namespace Okojo.JavaScript.Execution;

public readonly record struct StackFrameInfo(
    string FunctionName,
    int ProgramCounter,
    CallFrameKind FrameKind,
    CallFrameFlag Flags,
    bool HasGeneratorState,
    GeneratorState GeneratorState,
    int GeneratorSuspendId,
    bool HasSourceLocation,
    int SourceLine,
    int SourceColumn,
    string? SourcePath
);
