namespace Okojo.RegExp;

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

    internal static bool TryGetSequenceMaxMatchLength(IReadOnlyList<Node>? terms, int index, out int maxLength)
    {
        if (terms is null || index < 0 || index >= terms.Count)
        {
            maxLength = 0;
            return true;
        }

        long sum = 0;
        for (var i = index; i < terms.Count; i++)
        {
            if (!TryGetNodeMaxMatchLength(terms[i], out var termLength))
            {
                maxLength = default;
                return false;
            }

            sum += termLength;
            if (sum > int.MaxValue)
            {
                maxLength = int.MaxValue;
                return true;
            }
        }

        maxLength = (int)sum;
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
                minLength = 1;
                return true;
            case ClassNode cls:
                return TryGetClassMinMatchLength(cls, out minLength);
            case PropertyEscapeNode propertyEscape:
                return TryGetPropertyEscapeMinMatchLength(propertyEscape, out minLength);
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

    internal static bool TryGetNodeMaxMatchLength(Node node, out int maxLength)
    {
        switch (node)
        {
            case EmptyNode:
            case AnchorNode:
            case BoundaryNode:
            case LookaheadNode:
            case LookbehindNode:
                maxLength = 0;
                return true;
            case LiteralNode:
            case DotNode:
                maxLength = 1;
                return true;
            case ClassNode cls:
                return TryGetClassMaxMatchLength(cls, out maxLength);
            case PropertyEscapeNode propertyEscape:
                return TryGetPropertyEscapeMaxMatchLength(propertyEscape, out maxLength);
            case CaptureNode capture:
                return TryGetNodeMaxMatchLength(capture.Child, out maxLength);
            case ScopedModifiersNode scoped:
                return TryGetNodeMaxMatchLength(scoped.Child, out maxLength);
            case BackReferenceNode:
            case NamedBackReferenceNode:
                maxLength = default;
                return false;
            case SequenceNode sequence:
                return TryGetSequenceMaxMatchLength(sequence.Terms, 0, out maxLength);
            case AlternationNode alternation:
            {
                if (alternation.Alternatives.Length == 0)
                {
                    maxLength = 0;
                    return true;
                }

                var best = 0;
                for (var i = 0; i < alternation.Alternatives.Length; i++)
                {
                    if (!TryGetNodeMaxMatchLength(alternation.Alternatives[i], out var alternativeLength))
                    {
                        maxLength = default;
                        return false;
                    }

                    if (alternativeLength > best)
                        best = alternativeLength;
                }

                maxLength = best;
                return true;
            }
            case QuantifierNode quantifier:
            {
                if (quantifier.Max == int.MaxValue ||
                    !TryGetNodeMaxMatchLength(quantifier.Child, out var childLength))
                {
                    maxLength = default;
                    return false;
                }

                var total = (long)childLength * quantifier.Max;
                maxLength = total > int.MaxValue ? int.MaxValue : (int)total;
                return true;
            }
            default:
                maxLength = default;
                return false;
        }
    }

    private static bool TryGetPropertyEscapeMinMatchLength(PropertyEscapeNode propertyEscape, out int minLength)
    {
        if (propertyEscape.Kind == PropertyEscapeKind.StringProperty &&
            propertyEscape.PropertyValue is not null &&
            ScratchUnicodeStringPropertyTables.TryGetLengthRange(propertyEscape.PropertyValue, out minLength, out _))
            return true;

        minLength = 1;
        return true;
    }

    private static bool TryGetPropertyEscapeMaxMatchLength(PropertyEscapeNode propertyEscape, out int maxLength)
    {
        if (propertyEscape.Kind == PropertyEscapeKind.StringProperty &&
            propertyEscape.PropertyValue is not null &&
            ScratchUnicodeStringPropertyTables.TryGetLengthRange(propertyEscape.PropertyValue, out _, out maxLength))
            return true;

        maxLength = 1;
        return true;
    }

    private static bool TryGetClassMinMatchLength(ClassNode cls, out int minLength)
    {
        if (cls.Expression is not null)
            return TryGetClassSetExpressionMinMatchLength(cls.Expression, out minLength);

        if (cls.Items.Length == 0)
        {
            minLength = 0;
            return true;
        }

        var best = int.MaxValue;
        for (var i = 0; i < cls.Items.Length; i++)
        {
            if (!TryGetClassItemMinMatchLength(cls.Items[i], out var itemMinLength))
            {
                minLength = default;
                return false;
            }

            if (itemMinLength < best)
                best = itemMinLength;
        }

        minLength = best;
        return true;
    }

    private static bool TryGetClassMaxMatchLength(ClassNode cls, out int maxLength)
    {
        if (cls.Expression is not null)
            return TryGetClassSetExpressionMaxMatchLength(cls.Expression, out maxLength);

        var best = 0;
        for (var i = 0; i < cls.Items.Length; i++)
        {
            if (!TryGetClassItemMaxMatchLength(cls.Items[i], out var itemMaxLength))
            {
                maxLength = default;
                return false;
            }

            if (itemMaxLength > best)
                best = itemMaxLength;
        }

        maxLength = best;
        return true;
    }

    private static bool TryGetClassItemMinMatchLength(ClassItem item, out int minLength)
    {
        switch (item.Kind)
        {
            case ClassItemKind.StringLiteral:
                return TryGetStringSetMinMatchLength(item.Strings, out minLength);
            case ClassItemKind.PropertyEscape when item.PropertyKind == PropertyEscapeKind.StringProperty &&
                                                    item.PropertyValue is not null:
                return ScratchUnicodeStringPropertyTables.TryGetLengthRange(item.PropertyValue, out minLength, out _);
            default:
                minLength = 1;
                return true;
        }
    }

    private static bool TryGetClassItemMaxMatchLength(ClassItem item, out int maxLength)
    {
        switch (item.Kind)
        {
            case ClassItemKind.StringLiteral:
                return TryGetStringSetMaxMatchLength(item.Strings, out maxLength);
            case ClassItemKind.PropertyEscape when item.PropertyKind == PropertyEscapeKind.StringProperty &&
                                                    item.PropertyValue is not null:
                return ScratchUnicodeStringPropertyTables.TryGetLengthRange(item.PropertyValue, out _, out maxLength);
            default:
                maxLength = 1;
                return true;
        }
    }

    private static bool TryGetClassSetExpressionMinMatchLength(ClassSetExpression expression, out int minLength)
    {
        switch (expression)
        {
            case ClassSetItemExpression itemExpression:
                return TryGetClassItemMinMatchLength(itemExpression.Item, out minLength);
            case ClassSetNestedExpression nested:
                return TryGetClassMinMatchLength(nested.Class, out minLength);
            case ClassSetUnionExpression union:
            {
                if (union.Terms.Length == 0)
                {
                    minLength = 0;
                    return true;
                }

                var best = int.MaxValue;
                for (var i = 0; i < union.Terms.Length; i++)
                {
                    if (!TryGetClassSetExpressionMinMatchLength(union.Terms[i], out var termMinLength))
                    {
                        minLength = default;
                        return false;
                    }

                    if (termMinLength < best)
                        best = termMinLength;
                }

                minLength = best;
                return true;
            }
            case ClassSetIntersectionExpression intersection:
            {
                if (!TryGetClassSetExpressionMinMatchLength(intersection.Left, out var leftMinLength) ||
                    !TryGetClassSetExpressionMinMatchLength(intersection.Right, out var rightMinLength))
                {
                    minLength = default;
                    return false;
                }

                minLength = Math.Min(leftMinLength, rightMinLength);
                return true;
            }
            case ClassSetDifferenceExpression difference:
                return TryGetClassSetExpressionMinMatchLength(difference.Left, out minLength);
            default:
                minLength = default;
                return false;
        }
    }

    private static bool TryGetClassSetExpressionMaxMatchLength(ClassSetExpression expression, out int maxLength)
    {
        switch (expression)
        {
            case ClassSetItemExpression itemExpression:
                return TryGetClassItemMaxMatchLength(itemExpression.Item, out maxLength);
            case ClassSetNestedExpression nested:
                return TryGetClassMaxMatchLength(nested.Class, out maxLength);
            case ClassSetUnionExpression union:
            {
                var best = 0;
                for (var i = 0; i < union.Terms.Length; i++)
                {
                    if (!TryGetClassSetExpressionMaxMatchLength(union.Terms[i], out var termMaxLength))
                    {
                        maxLength = default;
                        return false;
                    }

                    if (termMaxLength > best)
                        best = termMaxLength;
                }

                maxLength = best;
                return true;
            }
            case ClassSetIntersectionExpression intersection:
            {
                if (!TryGetClassSetExpressionMaxMatchLength(intersection.Left, out var leftMaxLength) ||
                    !TryGetClassSetExpressionMaxMatchLength(intersection.Right, out var rightMaxLength))
                {
                    maxLength = default;
                    return false;
                }

                maxLength = Math.Min(leftMaxLength, rightMaxLength);
                return true;
            }
            case ClassSetDifferenceExpression difference:
                return TryGetClassSetExpressionMaxMatchLength(difference.Left, out maxLength);
            default:
                maxLength = default;
                return false;
        }
    }

    private static bool TryGetStringSetMinMatchLength(string[]? strings, out int minLength)
    {
        if (strings is null || strings.Length == 0)
        {
            minLength = 0;
            return true;
        }

        var best = int.MaxValue;
        for (var i = 0; i < strings.Length; i++)
        {
            if (strings[i].Length < best)
                best = strings[i].Length;
        }

        minLength = best;
        return true;
    }

    private static bool TryGetStringSetMaxMatchLength(string[]? strings, out int maxLength)
    {
        if (strings is null || strings.Length == 0)
        {
            maxLength = 0;
            return true;
        }

        var best = 0;
        for (var i = 0; i < strings.Length; i++)
        {
            if (strings[i].Length > best)
                best = strings[i].Length;
        }

        maxLength = best;
        return true;
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

    internal static bool CanUseCaptureAwareForwardLookbehindVm(Node node)
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
            case CaptureNode capture:
                return CanUseCaptureAwareForwardLookbehindVm(capture.Child);
            case SequenceNode sequence:
                for (var i = 0; i < sequence.Terms.Length; i++)
                    if (!CanUseCaptureAwareForwardLookbehindVm(sequence.Terms[i]))
                        return false;
                return true;
            case AlternationNode alternation:
                for (var i = 0; i < alternation.Alternatives.Length; i++)
                    if (!CanUseCaptureAwareForwardLookbehindVm(alternation.Alternatives[i]))
                        return false;
                return true;
            case QuantifierNode quantifier:
                if (ContainsCaptureNode(quantifier.Child))
                    return false;
                return CanUseCaptureAwareForwardLookbehindVm(quantifier.Child);
            case ScopedModifiersNode scoped:
                return CanUseCaptureAwareForwardLookbehindVm(scoped.Child);
            case BackReferenceNode:
            case NamedBackReferenceNode:
            case LookaheadNode:
            case LookbehindNode:
            default:
                return false;
        }
    }

    private static bool ContainsCaptureNode(Node node)
    {
        switch (node)
        {
            case CaptureNode:
                return true;
            case SequenceNode sequence:
                for (var i = 0; i < sequence.Terms.Length; i++)
                    if (ContainsCaptureNode(sequence.Terms[i]))
                        return true;
                return false;
            case AlternationNode alternation:
                for (var i = 0; i < alternation.Alternatives.Length; i++)
                    if (ContainsCaptureNode(alternation.Alternatives[i]))
                        return true;
                return false;
            case QuantifierNode quantifier:
                return ContainsCaptureNode(quantifier.Child);
            case ScopedModifiersNode scoped:
                return ContainsCaptureNode(scoped.Child);
            case LookaheadNode lookahead:
                return ContainsCaptureNode(lookahead.Child);
            case LookbehindNode lookbehind:
                return ContainsCaptureNode(lookbehind.Child);
            default:
                return false;
        }
    }
}
