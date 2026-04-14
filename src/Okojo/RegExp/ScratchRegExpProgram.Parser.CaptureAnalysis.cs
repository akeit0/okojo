using Okojo.Internals;

namespace Okojo.RegExp;

internal sealed partial class ScratchRegExpProgram
{
    private sealed partial class Parser
    {
        private static Dictionary<Node, int[]> BuildCaptureIndexMap(Node root)
        {
            var map = new Dictionary<Node, int[]>(ReferenceEqualityComparer.Instance);
            CollectCaptureIndices(root, map);
            return map;
        }

        private static int[] CollectCaptureIndices(Node node, Dictionary<Node, int[]> map)
        {
            if (map.TryGetValue(node, out var existing))
                return existing;

            int[] array;
            switch (node)
            {
                case CaptureNode capture:
                    array = MergeCaptureIndexArray(capture.Index, CollectCaptureIndices(capture.Child, map));
                    break;
                case LookaheadNode lookahead:
                    array = CollectCaptureIndices(lookahead.Child, map);
                    break;
                case LookbehindNode lookbehind:
                    array = CollectCaptureIndices(lookbehind.Child, map);
                    break;
                case ScopedModifiersNode scoped:
                    array = CollectCaptureIndices(scoped.Child, map);
                    break;
                case SequenceNode sequence:
                    array = MergeCaptureIndexLists(sequence.Terms, map);
                    break;
                case AlternationNode alternation:
                    array = MergeCaptureIndexLists(alternation.Alternatives, map);
                    break;
                case QuantifierNode quantifier:
                    array = CollectCaptureIndices(quantifier.Child, map);
                    break;
                default:
                    array = Array.Empty<int>();
                    break;
            }

            map[node] = array;
            return array;
        }

        private static int[] MergeCaptureIndexArray(int captureIndex, int[] childIndices)
        {
            if (childIndices.Length == 0)
                return [captureIndex];

            var insertIndex = Array.BinarySearch(childIndices, captureIndex);
            if (insertIndex >= 0)
                return childIndices;

            insertIndex = ~insertIndex;
            var merged = new int[childIndices.Length + 1];
            if (insertIndex != 0)
                Array.Copy(childIndices, 0, merged, 0, insertIndex);

            merged[insertIndex] = captureIndex;

            if (insertIndex < childIndices.Length)
            {
                Array.Copy(childIndices, insertIndex, merged, insertIndex + 1,
                    childIndices.Length - insertIndex);
            }

            return merged;
        }

        private static int[] MergeCaptureIndexLists(Node[] nodes, Dictionary<Node, int[]> map)
        {
            int[]? single = null;
            var nonEmptyCount = 0;
            var totalCount = 0;
            for (var i = 0; i < nodes.Length; i++)
            {
                var childIndices = CollectCaptureIndices(nodes[i], map);
                if (childIndices.Length == 0)
                    continue;

                totalCount += childIndices.Length;
                if (nonEmptyCount++ == 0)
                    single = childIndices;
            }

            if (nonEmptyCount == 0)
                return Array.Empty<int>();
            if (nonEmptyCount == 1)
                return single!;

            Span<int> initialBuffer = stackalloc int[Math.Min(totalCount, 16)];
            var builder = new PooledArrayBuilder<int>(initialBuffer);
            try
            {
                for (var i = 0; i < nodes.Length; i++)
                {
                    var childIndices = CollectCaptureIndices(nodes[i], map);
                    for (var j = 0; j < childIndices.Length; j++)
                        builder.Add(childIndices[j]);
                }

                return MaterializeSortedDistinct(builder.AsSpan());
            }
            finally
            {
                builder.Dispose();
            }
        }

        private static int[] MaterializeSortedDistinct(Span<int> values)
        {
            if (values.Length == 0)
                return Array.Empty<int>();
            if (values.Length == 1)
                return [values[0]];

            values.Sort();
            var sortedValues = values;

            var uniqueCount = 1;
            for (var i = 1; i < sortedValues.Length; i++)
            {
                if (sortedValues[i] == sortedValues[uniqueCount - 1])
                    continue;

                sortedValues[uniqueCount++] = sortedValues[i];
            }

            if (uniqueCount == sortedValues.Length)
                return sortedValues.ToArray();

            return sortedValues[..uniqueCount].ToArray();
        }
    }
}
