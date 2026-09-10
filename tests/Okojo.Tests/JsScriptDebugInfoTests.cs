using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Parsing;

namespace Okojo.Tests;

public class JsScriptDebugInfoTests
{
    private static JsScript ScriptWithLocations()
    {
        // Source "aa\nbb\ncc" (line starts 0, 3, 6); debug entries map
        // pc 10 -> offset 5 (line 2) and pc 20 -> offset 15 (line 3).
        return new JsScript(
            [(byte)JsOpCode.Return],
            Array.Empty<ulong>(),
            Array.Empty<object>(),
            0,
            Array.Empty<int>(),
            DebugPcOffsets: [10, 20],
            DebugSourceOffsets: [5, 15],
            SourceCode: new SourceCode("aa\nbb\ncc", null)
        );
    }

    [Test]
    public void SourceLocation_Before_First_Entry_Clamps_To_First()
    {
        var script = ScriptWithLocations();

        Assert.That(
            JsScriptDebugInfo.TryGetSourceLocation(script, 0, out var line, out var column),
            Is.True
        );
        Assert.That(line, Is.EqualTo(2));
        Assert.That(column, Is.EqualTo(3));
    }

    [Test]
    public void SourceLocation_Exact_And_Between_Entries_Resolve()
    {
        var script = ScriptWithLocations();

        Assert.That(
            JsScriptDebugInfo.TryGetSourceLocation(script, 10, out var line, out _),
            Is.True
        );
        Assert.That(line, Is.EqualTo(2));

        Assert.That(
            JsScriptDebugInfo.TryGetSourceLocation(script, 15, out var betweenLine, out _),
            Is.True
        );
        Assert.That(betweenLine, Is.EqualTo(2));

        Assert.That(
            JsScriptDebugInfo.TryGetSourceLocation(script, 100, out var lastLine, out _),
            Is.True
        );
        Assert.That(lastLine, Is.EqualTo(3));
    }

    [Test]
    public void SourceLocation_Without_Tables_Fails()
    {
        var script = new JsScript(
            [(byte)JsOpCode.Return],
            Array.Empty<ulong>(),
            Array.Empty<object>(),
            0,
            Array.Empty<int>()
        );

        Assert.That(JsScriptDebugInfo.TryGetSourceLocation(script, 0, out _, out _), Is.False);
    }
}
