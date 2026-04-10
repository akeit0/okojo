using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Tests;

public class NativeErrorObjectTests
{
    [Test]
    public void CreateErrorObjectFromException_Preserves_Native_Exception_And_Error_Surface()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var inner = new IndexOutOfRangeException("native boom");
        var ex = new JsRuntimeException(JsErrorKind.InternalError, "wrapped boom", innerException: inner);

        var value = realm.CreateErrorObjectFromException(ex);

        Assert.That(value.TryGetObject(out var obj), Is.True);
        Assert.That(obj, Is.TypeOf<JsNativeErrorObject>());

        var nativeError = (JsNativeErrorObject)obj!;
        Assert.That(nativeError.NativeException, Is.SameAs(inner));
        Assert.That(nativeError.Prototype, Is.SameAs(realm.Intrinsics.ErrorPrototype));
        Assert.That(nativeError.TryGetProperty("name", out var name), Is.True);
        Assert.That(name.AsString(), Is.EqualTo("Error"));
        Assert.That(nativeError.TryGetProperty("message", out var message), Is.True);
        Assert.That(message.AsString(), Is.EqualTo("wrapped boom"));
        Assert.That(nativeError.TryGetProperty("stack", out var stack), Is.True);
        Assert.That(stack.AsString(), Does.Contain("Error: wrapped boom"));
    }
}
