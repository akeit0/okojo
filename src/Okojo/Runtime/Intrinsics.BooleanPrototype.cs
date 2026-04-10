namespace Okojo.Runtime;

public partial class Intrinsics
{
    private void InstallBooleanPrototypeBuiltins()
    {
        var valueOfFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var thisValue = info.ThisValue;
                if (thisValue.IsBool)
                    return thisValue;
                if (thisValue.TryGetObject(out var obj) && obj is JsBooleanObject boxed)
                    return boxed.Value ? JsValue.True : JsValue.False;
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Boolean.prototype.valueOf requires that 'this' be a Boolean");
            }, "valueOf", 0);

        var toStringFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var thisValue = info.ThisValue;
                if (thisValue.IsBool)
                    return thisValue.IsTrue ? "true" : "false";
                if (thisValue.TryGetObject(out var obj) && obj is JsBooleanObject boxed)
                    return boxed.Value ? "true" : "false";
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Boolean.prototype.toString requires that 'this' be a Boolean");
            }, "toString", 0);

        Span<PropertyDefinition> defs =
        [
            PropertyDefinition.Mutable(IdConstructor, JsValue.FromObject(BooleanConstructor)),
            PropertyDefinition.Mutable(IdValueOf, JsValue.FromObject(valueOfFn)),
            PropertyDefinition.Mutable(IdToString, JsValue.FromObject(toStringFn))
        ];
        BooleanPrototype.DefineNewPropertiesNoCollision(Realm, defs);
        BooleanConstructor.InitializePrototypeProperty(BooleanPrototype);
    }
}
