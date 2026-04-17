namespace Okojo.Runtime;

internal enum ModuleExecutionCompletionKind : byte
{
    Normal = 0,
    Throw = 2
}

internal readonly record struct ModuleExecutionCompletion(
    ModuleExecutionCompletionKind Kind,
    JsValue Value,
    JsRuntimeException? Failure)
{
    public bool IsAbrupt => Kind != ModuleExecutionCompletionKind.Normal;
}
