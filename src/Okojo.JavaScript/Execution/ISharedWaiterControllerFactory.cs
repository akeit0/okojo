namespace Okojo.JavaScript.Execution;

internal interface ISharedWaiterControllerFactory
{
    JsArrayBufferObject.ISharedWaiterController CreateController(JsRealm realm);
}
