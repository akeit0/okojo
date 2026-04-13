namespace Okojo.RegExp.Experimental;

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

            using var indices = new ScratchPooledIntSet();
            switch (node)
            {
                case CaptureNode capture:
                    indices.Add(capture.Index);
                    indices.UnionWith(CollectCaptureIndices(capture.Child, map));
                    break;
                case LookaheadNode lookahead:
                    indices.UnionWith(CollectCaptureIndices(lookahead.Child, map));
                    break;
                case LookbehindNode lookbehind:
                    indices.UnionWith(CollectCaptureIndices(lookbehind.Child, map));
                    break;
                case ScopedModifiersNode scoped:
                    indices.UnionWith(CollectCaptureIndices(scoped.Child, map));
                    break;
                case SequenceNode sequence:
                    foreach (var term in sequence.Terms)
                        indices.UnionWith(CollectCaptureIndices(term, map));
                    break;
                case AlternationNode alternation:
                    foreach (var alternative in alternation.Alternatives)
                        indices.UnionWith(CollectCaptureIndices(alternative, map));
                    break;
                case QuantifierNode quantifier:
                    indices.UnionWith(CollectCaptureIndices(quantifier.Child, map));
                    break;
            }

            var array = indices.Count == 0 ? Array.Empty<int>() : new int[indices.Count];
            if (array.Length != 0)
            {
                indices.CopyTo(array);
                if (array.Length > 1)
                    Array.Sort(array);
            }

            map[node] = array;
            return array;
        }
    }
}
