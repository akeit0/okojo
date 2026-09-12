using System.Runtime.CompilerServices;
using Okojo.JavaScript.Bytecode;

namespace Okojo.JavaScript.Execution;

public sealed partial class JsRealm
{
    private readonly object linkSyncRoot = new();
    private ConditionalWeakTable<JsFunctionDescriptor, WeakReference<JsScript>>? linkedFunctions;

    // Realm operations remain agent-confined, just like the VM and shape table.
    // The weak key lets a long-lived realm drop discarded units; the weak value
    // lets a discarded unit's instances (and their realm references) be collected,
    // so retaining a unit alone never retains a realm either.
    internal JsScript LinkFunction(JsFunctionDescriptor function)
    {
        var instance = GetOrCreateFunctionInstance(function);
        instance.ValidateDeclarations();
        Agent.RegisterScript(instance);
        return instance;
    }

    internal JsScript GetOrCreateFunctionInstance(JsFunctionDescriptor function)
    {
        lock (linkSyncRoot)
        {
            linkedFunctions ??= new();
            if (
                linkedFunctions.TryGetValue(function, out var existing)
                && existing.TryGetTarget(out var live)
            )
                return live;
            var instance = new JsScript(this, function);
            linkedFunctions.Remove(function);
            linkedFunctions.Add(function, new WeakReference<JsScript>(instance));
            return instance;
        }
    }
}
