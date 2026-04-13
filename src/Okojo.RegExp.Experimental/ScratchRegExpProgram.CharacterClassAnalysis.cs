using System.Runtime.InteropServices.Marshalling;

namespace Okojo.RegExp.Experimental;

internal sealed partial class ScratchRegExpProgram
{
    internal static bool TryCreateSimpleClass(ClassNode cls, out ExperimentalRegExpSimpleClass simpleClass)
    {
        if (cls.Expression is not null)
        {
            simpleClass = null!;
            return false;
        }

        var clsItems = cls.Items;
        for (var i = 0; i < clsItems.Length; i++)
        {
            var item = clsItems[i];
            if (item.Kind == ClassItemKind.StringLiteral ||
                item.Kind == ClassItemKind.PropertyEscape &&
                item.PropertyKind == PropertyEscapeKind.StringProperty)
            {
                simpleClass = null!;
                return false;
            }
        }

        var items = new ExperimentalRegExpSimpleClassItem[clsItems.Length];
        for (var i = 0; i < clsItems.Length; i++)
        {
            var item = clsItems[i];
            items[i] = item.Kind switch
            {
                ClassItemKind.Literal => new(ExperimentalRegExpSimpleClassItemKind.Literal, CodePoint: item.CodePoint),
                ClassItemKind.Range => new(ExperimentalRegExpSimpleClassItemKind.Range, RangeStart: item.RangeStart,
                    RangeEnd: item.RangeEnd),
                ClassItemKind.Digit => new(ExperimentalRegExpSimpleClassItemKind.Digit),
                ClassItemKind.NotDigit => new(ExperimentalRegExpSimpleClassItemKind.NotDigit),
                ClassItemKind.Space => new(ExperimentalRegExpSimpleClassItemKind.Space),
                ClassItemKind.NotSpace => new(ExperimentalRegExpSimpleClassItemKind.NotSpace),
                ClassItemKind.Word => new(ExperimentalRegExpSimpleClassItemKind.Word),
                ClassItemKind.NotWord => new(ExperimentalRegExpSimpleClassItemKind.NotWord),
                ClassItemKind.PropertyEscape => new(ExperimentalRegExpSimpleClassItemKind.PropertyEscape,
                    PropertyEscape: new ExperimentalRegExpPropertyEscape(item.PropertyKind,
                        item.PropertyNegated, item.PropertyCategories, item.PropertyValue)),
                _ => throw new InvalidOperationException($"Unsupported simple class item: {item.Kind}")
            };
        }

        simpleClass = new()
        {
            Items = items,
            Negated = cls.Negated
        };
        return true;
    }

    internal static bool TryGetSingleLiteralClassCodePoint(ClassNode cls, out int codePoint)
    {
        if (cls.Expression is null &&
            !cls.Negated &&
            cls.Items.Length == 1 &&
            cls.Items[0].Kind == ClassItemKind.Literal)
        {
            codePoint = cls.Items[0].CodePoint;
            return true;
        }

        codePoint = default;
        return false;
    }

    internal static bool TryGetSmallLiteralClassCodePoints(ClassNode cls, out int[] codePoints)
    {
        if (cls.Expression is not null || cls.Negated || cls.Items.Length == 0 || cls.Items.Length > MaxSearchLiteralSetSize)
        {
            codePoints = [];
            return false;
        }

        var items = cls.Items;
        for (var i = 0; i < items.Length; i++)
        {
            if (items[i].Kind != ClassItemKind.Literal)
            {
                codePoints = [];
                return false;
            }
        }

        var builder = new int[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            builder[i] = items[i].CodePoint;
        }

        codePoints = builder;
        return true;
    }

    internal static bool TryBuildAsciiClassBitmap(ClassNode cls, out ulong lowMask, out ulong highMask)
    {
        lowMask = 0;
        highMask = 0;
        if (cls.Expression is not null || cls.Negated || cls.Items.Length == 0)
            return false;

        var items = cls.Items;
        for (var i = 0; i < items.Length; i++)
        {
            if (!TryAddAsciiClassItem(items[i], ref lowMask, ref highMask))
            {
                lowMask = 0;
                highMask = 0;
                return false;
            }
        }

        return true;
    }

    private static bool TryAddAsciiClassItem(ClassItem item, ref ulong lowMask, ref ulong highMask)
    {
        switch (item.Kind)
        {
            case ClassItemKind.Literal:
                return TryAddAsciiCodePoint(item.CodePoint, ref lowMask, ref highMask);
            case ClassItemKind.Range:
                if ((uint)item.RangeEnd >= 128)
                    return false;
                for (var cp = item.RangeStart; cp <= item.RangeEnd; cp++)
                    AddAsciiCodePoint(cp, ref lowMask, ref highMask);
                return true;
            case ClassItemKind.Digit:
                AddAsciiRange('0', '9', ref lowMask, ref highMask);
                return true;
            case ClassItemKind.Word:
                AddAsciiRange('0', '9', ref lowMask, ref highMask);
                AddAsciiRange('A', 'Z', ref lowMask, ref highMask);
                AddAsciiRange('a', 'z', ref lowMask, ref highMask);
                AddAsciiCodePoint('_', ref lowMask, ref highMask);
                return true;
            case ClassItemKind.PropertyEscape:
                switch (item.PropertyKind)
                {
                    case PropertyEscapeKind.Ascii:
                        AddAsciiRange(0, 127, ref lowMask, ref highMask);
                        return true;
                    case PropertyEscapeKind.AsciiHexDigit:
                        AddAsciiRange('0', '9', ref lowMask, ref highMask);
                        AddAsciiRange('A', 'F', ref lowMask, ref highMask);
                        AddAsciiRange('a', 'f', ref lowMask, ref highMask);
                        return true;
                    default:
                        return false;
                }
            default:
                return false;
        }
    }

    private static bool TryAddAsciiCodePoint(int codePoint, ref ulong lowMask, ref ulong highMask)
    {
        if ((uint)codePoint >= 128)
            return false;

        AddAsciiCodePoint(codePoint, ref lowMask, ref highMask);
        return true;
    }

    private static void AddAsciiRange(int start, int end, ref ulong lowMask, ref ulong highMask)
    {
        for (var cp = start; cp <= end; cp++)
            AddAsciiCodePoint(cp, ref lowMask, ref highMask);
    }

    private static void AddAsciiCodePoint(int codePoint, ref ulong lowMask, ref ulong highMask)
    {
        if (codePoint < 64)
            lowMask |= 1UL << codePoint;
        else
            highMask |= 1UL << (codePoint - 64);
    }
}
