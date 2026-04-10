namespace Okojo.Runtime;

internal sealed class AsyncGeneratorYieldDelegateAwaitState(
    JsGeneratorObject generator,
    GeneratorResumeMode originalMode)
{
    public readonly JsGeneratorObject Generator = generator;
    public readonly GeneratorResumeMode OriginalMode = originalMode;
}
