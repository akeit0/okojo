using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Bytecode;

/// <summary>
/// In-memory, realm-independent compilation output. Retaining a unit cannot keep
/// a realm alive. Linking is memoized by the target realm with weak references
/// on both sides, so neither side retains the other by itself.
/// </summary>
public sealed class JsCompilationUnit
{
    private readonly JsFunctionDescriptor[] additionalRoots;
    private JsFunctionDescriptor[]? functions;

    internal JsCompilationUnit(
        JsFunctionDescriptor entry,
        JsFunctionDescriptor[]? additionalFunctions = null
    )
    {
        Entry = entry;
        additionalRoots = additionalFunctions ?? [];
    }

    public JsFunctionDescriptor Entry { get; }
    public SourceCode? SourceCode => Entry.Code.SourceCode;
    public ReadOnlySpan<JsFunctionDescriptor> Functions => GetFunctions();

    /// <summary>
    /// Resolves names and layouts and obtains the realm-local feedback graph.
    /// Global declaration checks run against the target even on a cache hit.
    /// Execution rechecks them because globals can change after linking.
    /// </summary>
    public JsScript Link(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);
        var entry = realm.LinkFunction(Entry);
        for (var i = 0; i < additionalRoots.Length; i++)
            realm.LinkFunction(additionalRoots[i]);
        return entry;
    }

    private JsFunctionDescriptor[] GetFunctions()
    {
        var cached = Volatile.Read(ref functions);
        if (cached is not null)
            return cached;
        var pending = new List<JsFunctionDescriptor> { Entry };
        pending.AddRange(additionalRoots);
        var seen = new HashSet<JsFunctionDescriptor>(ReferenceEqualityComparer.Instance);
        var result = new List<JsFunctionDescriptor>();
        for (var i = 0; i < pending.Count; i++)
        {
            var function = pending[i];
            if (!seen.Add(function))
                continue;
            result.Add(function);
            foreach (var value in function.Code.ConstantDescriptors)
                if (value is JsFunctionDescriptor child)
                    pending.Add(child);
        }
        var created = result.ToArray();
        return Interlocked.CompareExchange(ref functions, created, null) ?? created;
    }
}
