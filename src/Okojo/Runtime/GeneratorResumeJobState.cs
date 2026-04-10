namespace Okojo.Runtime;

internal sealed class GeneratorResumeJobState(
    JsRealm realm,
    JsGeneratorObject generator,
    GeneratorResumeMode mode,
    JsValue value)
{
    public readonly JsGeneratorObject Generator = generator;
    public readonly GeneratorResumeMode Mode = mode;
    public readonly JsRealm Realm = realm;
    public readonly JsValue Value = value;
}
