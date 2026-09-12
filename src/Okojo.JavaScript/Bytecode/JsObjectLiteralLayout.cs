namespace Okojo.JavaScript.Bytecode;

/// <summary>Ordered, unique, non-index keys; their positions are the emitted slots.</summary>
internal sealed class JsObjectLiteralLayout(string[] names)
{
    internal static readonly JsObjectLiteralLayout Empty = new([]);
    private readonly string[] names = names;

    internal StaticNamedPropertyLayout Link(JsRealm realm)
    {
        var shape = realm.EmptyShape;
        for (var i = 0; i < names.Length; i++)
        {
            var atom = realm.Atoms.InternNoCheck(names[i]);
            shape = shape.GetOrAddTransition(atom, JsShapePropertyFlags.Open, out _);
        }
        return shape;
    }
}
