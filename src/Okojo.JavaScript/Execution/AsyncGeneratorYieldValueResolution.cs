namespace Okojo.JavaScript.Execution;

internal sealed class AsyncGeneratorYieldValueResolution(
    JsGeneratorObject generator,
    JsPromiseObject promise
)
{
    public readonly JsGeneratorObject Generator = generator;
    public readonly JsPromiseObject Promise = promise;
}
