namespace Okojo.Runtime;

internal sealed class AsyncGeneratorSettledResolveState(
    JsRealm realm,
    JsGeneratorObject generator,
    JsPromiseObject requestPromise,
    JsPromiseObject.PromiseState settledState,
    JsValue settledResult,
    bool done)
{
    public readonly bool Done = done;
    public readonly JsGeneratorObject Generator = generator;
    public readonly JsRealm Realm = realm;
    public readonly JsPromiseObject RequestPromise = requestPromise;
    public readonly JsValue SettledResult = settledResult;
    public readonly JsPromiseObject.PromiseState SettledState = settledState;
}
