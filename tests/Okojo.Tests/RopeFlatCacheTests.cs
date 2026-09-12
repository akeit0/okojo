using Okojo.JavaScript.Values;

namespace Okojo.Tests;

public class RopeFlatCacheTests
{
    [Test]
    public void FlattenedNodesRetainSnapshotsAndCanBeExtended()
    {
        var left = JsString.Concat(new string('a', 128), new string('b', 128));
        var snapshot = left;
        var extended = JsString.Concat(left, new string('c', 128));
        var flat = left.Flatten();
        Assert.That(snapshot.TryGetFlatString(out var cached), Is.True);
        Assert.That(cached, Is.SameAs(flat));
        Assert.That(extended.Flatten(), Is.EqualTo(flat + new string('c', 128)));
        Assert.That(left.Flatten(), Is.SameAs(flat));
        Assert.That(JsString.Concat(left, JsString.Empty).Flatten(), Is.SameAs(flat));
    }

    [Test]
    public void SlicesAndIterationWorkAcrossFlattenedAndUnflattenedChildren()
    {
        var left = JsString.Concat(new string('a', 128), "😀");
        var right = JsString.Concat(new string('b', 128), "tail");
        var root = JsString.Concat(left, right);
        var slice = root.Slice(120, 20);
        right.Flatten();
        var chars = new List<char>();
        foreach (var c in root)
            chars.Add(c);
        var expected = new string('a', 128) + "😀" + new string('b', 128) + "tail";
        Assert.That(new string(chars.ToArray()), Is.EqualTo(expected));
        Assert.That(slice.Flatten(), Is.EqualTo(expected.Substring(120, 20)));
        Assert.That(root.Flatten(), Is.EqualTo(expected));
    }
}
