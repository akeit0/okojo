using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Tests;

public class OkojoShapeTests
{
    [Test]
    public void TransitionShape_FifteenthProperty_StaysLinear()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var shape = realm.EmptyShape;

        for (var i = 0; i < 15; i++) shape = shape.GetOrAddTransition(realm.Atoms.InternNoCheck($"p{i}"), out _);

        Assert.That(shape.Kind, Is.EqualTo(NamedPropertyLayoutKind.LinearStatic));
        Assert.That(shape.PropertyCount, Is.EqualTo(15));
    }

    [Test]
    public void TransitionShape_SixteenthProperty_PromotesToMap()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var shape = realm.EmptyShape;

        for (var i = 0; i < 16; i++) shape = shape.GetOrAddTransition(realm.Atoms.InternNoCheck($"p{i}"), out _);

        Assert.That(shape.Kind, Is.EqualTo(NamedPropertyLayoutKind.MapStatic));
        Assert.That(shape.PropertyCount, Is.EqualTo(16));
    }

    [Test]
    public void DeleteProperty_RebuildsSixteenPropertyShape_BackToLinearLookup()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var obj = new JsPlainObject(realm);

        for (var i = 0; i < 16; i++) obj.DefineDataProperty($"p{i}", JsValue.FromInt32(i), JsShapePropertyFlags.Open);

        var deleted = obj.DeletePropertyAtom(realm, realm.Atoms.InternNoCheck("p0"));

        Assert.That(deleted, Is.True);
        Assert.That(obj.Shape.Kind, Is.EqualTo(NamedPropertyLayoutKind.LinearStatic));
        Assert.That(obj.Shape.PropertyCount, Is.EqualTo(15));
        Assert.That(obj.TryGetProperty("p1", out var value), Is.True);
        Assert.That(value.Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void DynamicShape_SmallSet_StaysLinear()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var shape = new DynamicNamedPropertyLayout(realm);
        for (var i = 0; i < 15; i++) shape.SetSlotInfo(i + 1, new(i, JsShapePropertyFlags.Open));

        Assert.That(shape.Kind, Is.EqualTo(NamedPropertyLayoutKind.DynamicLinear));
        Assert.That(shape.Count, Is.EqualTo(15));
    }

    [Test]
    public void DynamicShape_SixteenthEntry_PromotesToMap()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var shape = new DynamicNamedPropertyLayout(realm);
        for (var i = 0; i < 16; i++) shape.SetSlotInfo(i + 1, new(i, JsShapePropertyFlags.Open));

        Assert.That(shape.Kind, Is.EqualTo(NamedPropertyLayoutKind.DynamicMap));
        Assert.That(shape.Count, Is.EqualTo(16));
    }

    [Test]
    public void DynamicShape_DeleteBackToFifteen_RebuildsToLinear()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var shape = new DynamicNamedPropertyLayout(realm);
        for (var i = 0; i < 16; i++) shape.SetSlotInfo(i + 1, new(i, JsShapePropertyFlags.Open));

        var removed = shape.Remove(1, out _);

        Assert.That(removed, Is.True);
        Assert.That(shape.Kind, Is.EqualTo(NamedPropertyLayoutKind.DynamicLinear));
        Assert.That(shape.Count, Is.EqualTo(15));
        Assert.That(shape.TryGetSlotInfo(2, out var slotInfo), Is.True);
        Assert.That(slotInfo.Slot, Is.EqualTo(1));
    }
}
