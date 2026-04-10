namespace Okojo.Runtime.Interop;

internal interface IClrByRefPlaceholder
{
    Type TargetType { get; }
    bool HasValue { get; }
    bool TryPrepareByRefValue(JsRealm realm, Type parameterType, bool allowUnset, out object? value, out int score);
    void SetBoxedValue(object? value);
}
