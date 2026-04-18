using System.Runtime.CompilerServices;
using System.Text;
using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Diagnostics;

public sealed class ReplFormatter(JsRealm realm, int? indent = null)
{
    private readonly int? indentSize = indent is > 0 ? indent : null;
    private readonly Dictionary<JsObject, int> refIds = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<JsObject> stackSet = new(ReferenceEqualityComparer.Instance);
    private int nextRefId = 1;

    public string Format(in JsValue value)
    {
        return FormatValue(value, true, 0);
    }

    private string FormatValue(in JsValue value, bool isRoot, int depth)
    {
        if (value.IsString)
            return $"'{JsObject.EscapeSingleQuotedString(value.AsString())}'";
        if (value.IsFloat64)
        {
            if (value.U == Unsafe.BitCast<double, ulong>(double.NegativeZero))
                return "-0";
            return value.ToString();
        }

        if (value.IsUndefined || value.IsNull || value.IsBool || value.IsNumber || value.IsSymbol)
            return value.ToString() ?? string.Empty;
        if (!value.TryGetObject(out var obj))
            return value.ToString() ?? string.Empty;
        return FormatObject(obj, isRoot, depth);
    }

    private string FormatObject(JsObject obj, bool isRoot, int depth)
    {
        var displayTag = TryGetDisplayTag(obj);
        if (obj is JsFunction fn)
            return displayTag is not null ? $"[{displayTag}]" : fn.ToString() ?? "[Function]";

        if (!stackSet.Add(obj))
        {
            var id = GetOrAssignRefId(obj);
            return $"[Circular *{id}]";
        }

        string rendered;
        bool hasEntries;
        if (obj is JsArray array)
            rendered = FormatArray(array, depth, out hasEntries);
        else
            rendered = FormatPlainObject(obj, depth, out hasEntries);
        stackSet.Remove(obj);

        if (displayTag is not null && !hasEntries)
            rendered = $"[{displayTag}]";

        if (isRoot && refIds.TryGetValue(obj, out var rootId))
            return $"<ref *{rootId}> {rendered}";
        return rendered;
    }

    private string FormatArray(JsArray array, int depth, out bool hasEntries)
    {
        var parts = new List<string>((int)Math.Min(array.Length, 64));
        var currentEmptyCount = 0;
        for (uint i = 0; i < array.Length; i++)
        {
            if (!array.TryGetElement(i, out var element))
            {
                currentEmptyCount++;
                continue;
            }

            if (currentEmptyCount != 0)
            {
                parts.Add($"<{currentEmptyCount} empty item{(currentEmptyCount > 1 ? "s" : "")}>");
                currentEmptyCount = 0;
            }

            parts.Add(FormatValue(element, false, depth + 1));
        }

        var namedAtoms = new List<int>(8);
        array.CollectOwnNamedPropertyAtoms(realm, namedAtoms, true);
        for (var i = 0; i < namedAtoms.Count; i++)
        {
            var atom = namedAtoms[i];
            if (atom < 0)
                continue;
            if (!array.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out var descriptor))
                continue;

            var key = JsObject.FormatDisplayPropertyKey(realm.Atoms.AtomToString(atom));
            parts.Add($"{key}: {FormatDescriptorValue(descriptor, depth + 1)}");
        }

        hasEntries = parts.Count != 0;
        return FormatCollection(parts, depth, '[', ']');
    }

    private string FormatPlainObject(JsObject obj, int depth, out bool hasEntries)
    {
        var parts = new List<string>(8);

        if (obj.IndexedProperties is not null && obj.IndexedProperties.Count != 0)
        {
            var indices = obj.IndexedProperties.Keys.ToArray();
            Array.Sort(indices);
            for (var i = 0; i < indices.Length; i++)
            {
                var index = indices[i];
                var descriptor = obj.IndexedProperties[index];
                if (!descriptor.Enumerable)
                    continue;
                parts.Add($"{index}: {FormatDescriptorValue(descriptor, depth + 1)}");
            }
        }

        var namedAtoms = new List<int>(8);
        obj.CollectOwnNamedPropertyAtoms(realm, namedAtoms, true);
        for (var i = 0; i < namedAtoms.Count; i++)
        {
            var atom = namedAtoms[i];
            if (atom < 0)
                continue;
            if (!obj.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out var descriptor))
                continue;

            var key = JsObject.FormatDisplayPropertyKey(realm.Atoms.AtomToString(atom));
            parts.Add($"{key}: {FormatDescriptorValue(descriptor, depth + 1)}");
        }

        if (obj is JsGlobalObject global)
            foreach (var entry in global.EnumerateNamedGlobalDescriptors())
            {
                var atom = entry.Key;
                if (atom < 0)
                    continue;
                var descriptor = entry.Value;
                if (!descriptor.Enumerable)
                    continue;
                var key = JsObject.FormatDisplayPropertyKey(realm.Atoms.AtomToString(atom));
                parts.Add($"{key}: {FormatDescriptorValue(descriptor, depth + 1)}");
            }

        hasEntries = parts.Count != 0;
        return FormatCollection(parts, depth, '{', '}');
    }

    private string FormatDescriptorValue(in PropertyDescriptor descriptor, int depth)
    {
        var hasGetter = descriptor.HasGetter;
        var hasSetter = descriptor.HasSetter;
        if (hasGetter && hasSetter)
            return "[Getter/Setter]";
        if (hasGetter)
            return "[Getter]";
        if (hasSetter)
            return "[Setter]";
        return FormatValue(descriptor.Value, false, depth);
    }

    private int GetOrAssignRefId(JsObject obj)
    {
        if (refIds.TryGetValue(obj, out var id))
            return id;
        id = nextRefId++;
        refIds[obj] = id;
        return id;
    }

    private string? TryGetDisplayTag(JsObject obj)
    {
        if (obj.TryGetPropertyAtom(realm, AtomTable.IdSymbolToStringTag, out var value, out _) && value.IsString)
            return value.AsString();
        return null;
    }

    private string FormatCollection(List<string> parts, int depth, char open, char close)
    {
        if (parts.Count == 0)
            return $"{open}{close}";
        if (indentSize is not { } size)
            return $"{open} {string.Join(", ", parts)} {close}";

        var sb = new StringBuilder();
        sb.Append(open);
        for (var i = 0; i < parts.Count; i++)
        {
            sb.AppendLine();
            sb.Append(new string(' ', (depth + 1) * size));
            sb.Append(parts[i]);
            if (i != parts.Count - 1)
                sb.Append(',');
        }

        sb.AppendLine();
        sb.Append(new string(' ', depth * size));
        sb.Append(close);
        return sb.ToString();
    }
}
