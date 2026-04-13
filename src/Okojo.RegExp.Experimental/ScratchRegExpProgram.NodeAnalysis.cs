namespace Okojo.RegExp.Experimental;

internal sealed partial class ScratchRegExpProgram
{
    internal static bool TryGetSequenceMinMatchLength(IReadOnlyList<Node>? terms, int index, out int minLength)
    {
        if (terms is null || index < 0 || index >= terms.Count)
        {
            minLength = 0;
            return true;
        }

        long sum = 0;
        for (var i = index; i < terms.Count; i++)
        {
            if (!TryGetNodeMinMatchLength(terms[i], out var termLength))
            {
                minLength = default;
                return false;
            }

            sum += termLength;
            if (sum > int.MaxValue)
            {
                minLength = int.MaxValue;
                return true;
            }
        }

        minLength = (int)sum;
        return true;
    }

    internal static bool TryGetNodeMinMatchLength(Node node, out int minLength)
    {
        switch (node)
        {
            case EmptyNode:
            case AnchorNode:
            case BoundaryNode:
            case LookaheadNode:
            case LookbehindNode:
                minLength = 0;
                return true;
            case LiteralNode:
            case DotNode:
            case ClassNode:
            case PropertyEscapeNode:
                minLength = 1;
                return true;
            case CaptureNode capture:
                return TryGetNodeMinMatchLength(capture.Child, out minLength);
            case ScopedModifiersNode scoped:
                return TryGetNodeMinMatchLength(scoped.Child, out minLength);
            case BackReferenceNode:
            case NamedBackReferenceNode:
                minLength = default;
                return false;
            case SequenceNode sequence:
                return TryGetSequenceMinMatchLength(sequence.Terms, 0, out minLength);
            case AlternationNode alternation:
            {
                if (alternation.Alternatives.Length == 0)
                {
                    minLength = 0;
                    return true;
                }

                var best = int.MaxValue;
                for (var i = 0; i < alternation.Alternatives.Length; i++)
                {
                    if (!TryGetNodeMinMatchLength(alternation.Alternatives[i], out var alternativeLength))
                    {
                        minLength = default;
                        return false;
                    }

                    if (alternativeLength < best)
                        best = alternativeLength;
                }

                minLength = best;
                return true;
            }
            case QuantifierNode quantifier:
            {
                if (!TryGetNodeMinMatchLength(quantifier.Child, out var childLength))
                {
                    minLength = default;
                    return false;
                }

                var total = (long)childLength * quantifier.Min;
                minLength = total > int.MaxValue ? int.MaxValue : (int)total;
                return true;
            }
            default:
                minLength = default;
                return false;
        }
    }

    internal static bool CanUseForwardLookbehindVm(Node node)
    {
        switch (node)
        {
            case EmptyNode:
            case LiteralNode:
            case DotNode:
            case AnchorNode:
            case BoundaryNode:
            case ClassNode:
            case PropertyEscapeNode:
                return true;
            case SequenceNode sequence:
                for (var i = 0; i < sequence.Terms.Length; i++)
                    if (!CanUseForwardLookbehindVm(sequence.Terms[i]))
                        return false;
                return true;
            case AlternationNode alternation:
                for (var i = 0; i < alternation.Alternatives.Length; i++)
                    if (!CanUseForwardLookbehindVm(alternation.Alternatives[i]))
                        return false;
                return true;
            case QuantifierNode quantifier:
                return CanUseForwardLookbehindVm(quantifier.Child);
            case ScopedModifiersNode scoped:
                return CanUseForwardLookbehindVm(scoped.Child);
            case CaptureNode:
            case BackReferenceNode:
            case NamedBackReferenceNode:
            case LookaheadNode:
            case LookbehindNode:
            default:
                return false;
        }
    }
}
