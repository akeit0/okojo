namespace Okojo.JavaScript.Bytecode;

/// <summary>Primitive values and holes owned by a realm-independent compiled array literal.</summary>
internal sealed class JsArrayLiteral(JsValue[] elements)
{
    internal ReadOnlySpan<JsValue> Elements => elements;

    /// <summary>Immutable backing store shared copy-on-write by created arrays.</summary>
    internal JsValue[] SharedElements => elements;

    public override string ToString() => $"ArrayLiteral({elements.Length})";
}
