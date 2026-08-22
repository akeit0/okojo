using System.Globalization;
using System.Text;

namespace Okojo.Text.RegularExpressions.Internal;

internal enum OpCode : byte
{
    Match,
    Fail,
    Character,
    CharacterClass,
    Any,
    Scan,
    Jump,
    Split,
    Save,
    RepeatInit,
    RepeatDecision,
    RepeatBody,
    RepeatNext,
    AssertStart,
    AssertEnd,
    WordBoundary,
    Backreference,
    BackreferenceSet,
    ClassSet,
    Assertion,
}

internal readonly struct Instruction
{
    internal Instruction(OpCode op, int a = 0, int b = 0)
    {
        Op = op;
        A = a;
        B = b;
    }

    internal OpCode Op { get; }
    internal int A { get; }
    internal int B { get; }

    internal Instruction WithA(int value) => new(Op, value, B);

    internal Instruction WithTargets(int a, int b) => new(Op, a, b);
}

internal enum ScanAtomKind : byte
{
    Character,
    CharacterClass,
    Any,
}

internal readonly record struct ScanInfo(
    ScanAtomKind Kind,
    int Value,
    NodeOptions Options,
    int Minimum,
    int Maximum,
    bool Greedy
);

internal sealed class ProgramSegment
{
    internal required Instruction[] Code { get; init; }
    internal required int Direction { get; init; }
    internal required int FirstCapture { get; init; }
    internal required int LastCapture { get; init; }
}

/// <summary>
/// A /v character class that can match multi-code-point strings, addressed by
/// <see cref="OpCode.ClassSet"/>. <see cref="Trie"/> encodes the string members
/// as a code-point trie; leaf nodes store the matched code-unit width.
/// </summary>
internal sealed class ClassSetInfo
{
    internal required CharSet CodePoints { get; init; }

    /// <summary>Members of at least two code points.</summary>
    internal required string[] Strings { get; init; }

    /// <summary>Compiled code-point trie over <see cref="Strings"/>.</summary>
    internal required StringTrie Trie { get; init; }
}

/// <summary>
/// Immutable code-point trie with flat edge storage. A node is identified by an
/// integer; node zero is the root. <see cref="Terminals"/> marks, per node, the
/// code-unit width of the longest string ending there, or -1.
/// </summary>
internal sealed class StringTrie
{
    internal required int[] ChildOffsets { get; init; }
    internal required int[] EdgeCodePoints { get; init; }
    internal required int[] EdgeNexts { get; init; }
    internal required int[] Terminals { get; init; }
    internal required int NodeCount { get; init; }
}

internal readonly record struct RepeatInfo(
    int Segment,
    int StateSlot,
    int Minimum,
    int Maximum,
    bool Greedy,
    int DecisionPc,
    int BodyPc,
    int ExitPc,
    int FirstCapture,
    int LastCapture
);

internal sealed class RegexProgram
{
    internal required ProgramSegment[] Segments { get; init; }
    internal required CharSet[] Classes { get; init; }
    internal required RepeatInfo[] Repeats { get; init; }
    internal required ScanInfo[] Scans { get; init; }

    /// <summary>
    /// Capture-index sets for named backreferences that refer to duplicate
    /// named groups, addressed by <see cref="OpCode.BackreferenceSet"/>.
    /// </summary>
    internal required int[][] NameGroupSets { get; init; }

    /// <summary>String-capable /v character classes, addressed by <see cref="OpCode.ClassSet"/>.</summary>
    internal required ClassSetInfo[] ClassSets { get; init; }
    internal required RegExpFlags Flags { get; init; }

    /// <summary>Explicit capture count, excluding group zero.</summary>
    internal required int CaptureCount { get; init; }
    internal required int StateSlotCount { get; init; }
    internal required SearchPlan SearchPlan { get; init; }

    internal string GetDebugView()
    {
        StringBuilder builder = new();
        builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"flags: {RegExpFlagParser.Format(Flags)}"
        );
        builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"capture slots: {(CaptureCount + 1) * 2}"
        );
        builder.AppendLine(CultureInfo.InvariantCulture, $"repeat-state slots: {StateSlotCount}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"search: {SearchPlan}");

        for (int segmentIndex = 0; segmentIndex < Segments.Length; segmentIndex++)
        {
            ProgramSegment segment = Segments[segmentIndex];
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"segment[{segmentIndex}] direction={(segment.Direction > 0 ? "forward" : "backward")} captures={segment.FirstCapture}..{segment.LastCapture}"
            );
            for (int pc = 0; pc < segment.Code.Length; pc++)
            {
                Instruction instruction = segment.Code[pc];
                builder
                    .Append("  ")
                    .Append(pc.ToString("D5", CultureInfo.InvariantCulture))
                    .Append("  ")
                    .Append(instruction.Op.ToString().PadRight(18));
                switch (instruction.Op)
                {
                    case OpCode.Character:
                        builder.Append(
                            CultureInfo.InvariantCulture,
                            $"U+{instruction.A:X}, options={(NodeOptions)instruction.B}"
                        );
                        break;
                    case OpCode.CharacterClass:
                        builder.Append(
                            CultureInfo.InvariantCulture,
                            $"class[{instruction.A}], options={(NodeOptions)instruction.B} {Classes![instruction.A].DebugDisplay()}"
                        );
                        break;
                    case OpCode.Any:
                    case OpCode.AssertStart:
                    case OpCode.AssertEnd:
                        builder.Append(
                            CultureInfo.InvariantCulture,
                            $"options={(NodeOptions)instruction.B}"
                        );
                        break;
                    case OpCode.Scan:
                    {
                        ScanInfo scan = Scans[instruction.A];
                        builder
                            .Append(
                                CultureInfo.InvariantCulture,
                                $"scan[{instruction.A}] {scan.Kind} value={scan.Value} count={scan.Minimum}.."
                            )
                            .Append(
                                scan.Maximum < 0
                                    ? "∞"
                                    : scan.Maximum.ToString(CultureInfo.InvariantCulture)
                            )
                            .Append(scan.Greedy ? " greedy" : " lazy")
                            .Append(CultureInfo.InvariantCulture, $", options={scan.Options}");
                        break;
                    }
                    case OpCode.Jump:
                    case OpCode.Save:
                    case OpCode.RepeatInit:
                    case OpCode.RepeatDecision:
                    case OpCode.RepeatBody:
                    case OpCode.RepeatNext:
                        builder.Append(instruction.A);
                        break;
                    case OpCode.Split:
                        builder.Append(
                            CultureInfo.InvariantCulture,
                            $"{instruction.A}, {instruction.B}"
                        );
                        break;
                    case OpCode.WordBoundary:
                    case OpCode.Backreference:
                        builder.Append(
                            CultureInfo.InvariantCulture,
                            $"{instruction.A}, options={(NodeOptions)instruction.B}"
                        );
                        break;
                    case OpCode.BackreferenceSet:
                        builder.Append(
                            CultureInfo.InvariantCulture,
                            $"groups=[{string.Join(',', NameGroupSets[instruction.A])}], options={(NodeOptions)instruction.B}"
                        );
                        break;
                    case OpCode.ClassSet:
                        builder.Append(
                            CultureInfo.InvariantCulture,
                            $"classset[{instruction.A}] cp={Classes is not null}, strings={ClassSets[instruction.A].Strings.Length}, options={(NodeOptions)instruction.B}"
                        );
                        break;
                    case OpCode.Assertion:
                        builder.Append(
                            CultureInfo.InvariantCulture,
                            $"segment={instruction.A}, positive={instruction.B != 0}"
                        );
                        break;
                }
                builder.AppendLine();
            }
        }

        if (Scans.Length != 0)
        {
            builder.AppendLine("simple scans:");
            for (int i = 0; i < Scans.Length; i++)
            {
                ScanInfo scan = Scans[i];
                builder
                    .Append("  [")
                    .Append(i)
                    .Append("] ")
                    .Append(scan.Kind)
                    .Append(" value=")
                    .Append(scan.Value)
                    .Append(" count=")
                    .Append(scan.Minimum)
                    .Append("..")
                    .Append(
                        scan.Maximum < 0 ? "∞" : scan.Maximum.ToString(CultureInfo.InvariantCulture)
                    )
                    .Append(scan.Greedy ? " greedy" : " lazy")
                    .Append(" options=")
                    .Append(scan.Options)
                    .AppendLine();
            }
        }

        if (Repeats.Length != 0)
        {
            builder.AppendLine("generic repeats:");
            for (int i = 0; i < Repeats.Length; i++)
            {
                RepeatInfo repeat = Repeats[i];
                builder
                    .Append("  [")
                    .Append(i)
                    .Append("] segment=")
                    .Append(repeat.Segment)
                    .Append(" state=")
                    .Append(repeat.StateSlot)
                    .Append(" count=")
                    .Append(repeat.Minimum)
                    .Append("..")
                    .Append(
                        repeat.Maximum < 0
                            ? "∞"
                            : repeat.Maximum.ToString(CultureInfo.InvariantCulture)
                    )
                    .Append(repeat.Greedy ? " greedy" : " lazy")
                    .Append(" decision/body/exit=")
                    .Append(repeat.DecisionPc)
                    .Append('/')
                    .Append(repeat.BodyPc)
                    .Append('/')
                    .Append(repeat.ExitPc)
                    .Append(" captures=")
                    .Append(repeat.FirstCapture)
                    .Append("..")
                    .Append(repeat.LastCapture)
                    .AppendLine();
            }
        }
        return builder.ToString();
    }
}

internal enum SearchAnchor : byte
{
    None,
    AbsoluteStart,
    LineStart,
}

internal sealed class SearchPlan
{
    internal static SearchPlan None(int minimumLength) => new() { MinimumLength = minimumLength };

    internal SearchAnchor Anchor { get; init; }
    internal string? Prefix { get; init; }

    /// <summary>
    /// Code points that can begin a match, when the pattern clearly starts with a
    /// single non-nullable consuming atom. Null when no such set can be proven.
    /// </summary>
    internal CharSet? LeadingSet { get; init; }
    internal int MinimumLength { get; init; }

    public override string ToString() =>
        Prefix is null
            ? $"anchor={Anchor}, min={MinimumLength}, leading={(LeadingSet is null ? "?" : LeadingSet.DebugDisplay())}"
            : $"anchor={Anchor}, prefix=\"{Prefix}\", min={MinimumLength}";
}
