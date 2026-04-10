namespace Okojo.Runtime;

internal static class FreshArrayOperations
{
    internal static void DefineElement(JsObject target, uint index, JsValue value)
    {
        if (target is JsArray arrayTarget)
        {
            arrayTarget.InitializeLiteralElement(index, value);
            return;
        }

        target.SetElement(index, value);
    }
}
