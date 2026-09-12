using System.Reflection;
using System.Runtime.InteropServices;
using Okojo.JavaScript.Execution;

namespace Okojo.Tests;

public class AtomStringTableTests
{
    [Test]
    public void PredefinedIdsSymbolsAndGrowthRemainStable()
    {
        var table = new AtomTable();
        var predefined = (string[])
            typeof(AtomTable)
                .GetField("PredefinedAtoms", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetValue(null)!;
        for (int i = 0; i < predefined.Length; i++)
        {
            Assert.That(table.InternNoCheck(predefined[i]), Is.EqualTo(i));
            Assert.That(table.AtomToString(i), Is.EqualTo(predefined[i]));
        }
        int symbol = table.InternSymbolString("same"),
            otherSymbol = table.InternSymbolString("same");
        int name = table.InternNoCheck("same");
        Assert.That(symbol, Is.LessThan(0));
        Assert.That(otherSymbol, Is.Not.EqualTo(symbol));
        Assert.That(name, Is.GreaterThanOrEqualTo(0));
        for (int i = 0; i < 10000; i++)
            table.InternNoCheck(("grow_" + i).AsSpan());
        Assert.That(table.InternNoCheck("same".AsSpan()), Is.EqualTo(name));
        Assert.That(table.AtomToString(symbol), Is.EqualTo("same"));
        Assert.That(table.TryGetSymbolByAtom(symbol, out var value), Is.True);
        Assert.That(value.Description, Is.EqualTo("same"));
        Assert.That(table.TryGetInterned("not-interned", out var missing), Is.False);
        Assert.That(missing, Is.Zero);
        Assert.Throws<KeyNotFoundException>(() => table.AtomToString(AtomTable.InvalidAtom));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RealBucketCollisionsPromoteAndKeepIds(bool useSpan)
    {
        var table = new AtomStringTable(512);
        var names = new List<string>();
        // Distinct full hashes sharing the real 521-bucket initial table.
        for (int i = 0; names.Count < 102; i++)
        {
            string name = "collision_" + i;
            if (AtomStringHash.Hash(name) % 521 == 0)
                names.Add(name);
        }
        for (int i = 0; i < 101; i++)
            Assert.That(table.Intern(names[i]), Is.EqualTo(i));
        Assert.That(table.IsRandomized, Is.False);
        Assert.That(
            useSpan ? table.Intern(names[101].AsSpan()) : table.Intern(names[101]),
            Is.EqualTo(101)
        );
        Assert.That(table.IsRandomized, Is.True);
        for (int i = 0; i < 100000; i++)
            table.Intern(("later_" + i).AsSpan());
        Assert.That(table.Count, Is.EqualTo(100102));
        // Check newly added IDs across several resizes after the seeded rehash.
        for (int i = 0; i < 100000; i += 97)
        {
            Assert.That(table.Intern("later_" + i), Is.EqualTo(102 + i));
            Assert.That(table.Name(102 + i), Is.EqualTo("later_" + i));
        }
        for (int i = 0; i < names.Count; i++)
        {
            Assert.That(table.Intern(names[i].AsSpan()), Is.EqualTo(i));
            Assert.That(table.TryGetValue(names[i], out var id), Is.True);
            Assert.That(id, Is.EqualTo(i));
            Assert.That(table.Name(i), Is.EqualTo(names[i]));
            if (!useSpan || i != 101)
                Assert.That(table.Name(i), Is.SameAs(names[i]));
        }
    }

    [Test]
    public void MixedUtf16SpansRetainCanonicalStringsWithoutHitAllocations()
    {
        var table = new AtomStringTable();
        var reference = new Dictionary<string, int>(StringComparer.Ordinal);
        var random = new Random(835);
        for (int i = 0; i < 3000; i++)
        {
            char[] chars = new char[random.Next(0, 257)];
            for (int j = 0; j < chars.Length; j++)
                chars[j] = (char)random.Next(65536);
            string name = new(chars);
            if (!reference.TryGetValue(name, out int id))
                reference.Add(name, id = reference.Count);
            string backing = "!" + name + "x";
            Assert.That(table.Intern(backing.AsSpan(1, name.Length)), Is.EqualTo(id));
            Assert.That(table.Intern(name), Is.EqualTo(id));
            Assert.That(table.Name(id), Is.EqualTo(name));
        }
        string canonical = new string("canonical-name".AsSpan());
        int canonicalId = table.Intern(canonical);
        Assert.That(table.Name(canonicalId), Is.SameAs(canonical));
        table.Intern(canonical.AsSpan());
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++)
            table.Intern(canonical.AsSpan());
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
        Assert.Throws<ArgumentNullException>(() => table.Intern((string)null!));
    }

    private delegate int RuntimeMarvin(ReadOnlySpan<byte> bytes, ulong seed);

    [Test]
    public void SeededFallbackMatchesRuntimeMarvinForEveryTailAndSeed()
    {
        var method = typeof(string)
            .Assembly.GetType("System.Marvin")!
            .GetMethod(
                "ComputeHash32",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                [typeof(ReadOnlySpan<byte>), typeof(ulong)]
            )!;
        var oracle = method.CreateDelegate<RuntimeMarvin>();
        var random = new Random(950);
        foreach (ulong seed in new ulong[] { 0, ulong.MaxValue, 0x123456789abcdef0 })
            for (int length = 0; length < 513; length++)
            {
                char[] chars = new char[length];
                for (int i = 0; i < length; i++)
                    chars[i] = (char)random.Next(65536);
                Assert.That(
                    SeededOrdinalComparer.Hash(chars, seed),
                    Is.EqualTo(oracle(MemoryMarshal.AsBytes(chars.AsSpan()), seed))
                );
            }
    }
}
