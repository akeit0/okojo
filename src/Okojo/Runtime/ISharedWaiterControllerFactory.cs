namespace Okojo.Runtime;

internal interface ISharedWaiterControllerFactory
{
    JsArrayBufferObject.ISharedWaiterController CreateController(JsRealm realm);
}
