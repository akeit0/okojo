using System.Runtime.CompilerServices;
using Okojo.Bytecode;

namespace Okojo.Runtime;

public sealed partial class JsRealm
{
    private int indirectEvalGlobalBindingSemanticsDepth;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryGetGlobalLexicalBindingValue(int atom, out JsValue value)
    {
        if (GlobalObject.TryGetLexicalBinding(atom, out var context, out var slot, out _))
        {
            value = ThrowIfTheHole(context!.Slots[slot]);
            return true;
        }

        value = JsValue.Undefined;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryGetGlobalLexicalBindingValue(int atom, out JsValue value, out JsContext? context, out int slot,
        out bool isConst)
    {
        if (GlobalObject.TryGetLexicalBinding(atom, out context, out slot, out isConst))
        {
            value = ThrowIfTheHole(context!.Slots[slot]);
            return true;
        }

        context = null;
        slot = -1;
        isConst = false;
        value = JsValue.Undefined;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryGetGlobalBindingByAtom(JsScript script, int icSlot, int atom, out JsValue value)
    {
        var globalBindingIcEntries = script.GlobalBindingIcEntries;
#if DEBUG
        if (globalBindingIcEntries is null)
            throw new InvalidOperationException("Global binding IC entries are required for IC-backed global loads.");
        if ((uint)icSlot >= (uint)globalBindingIcEntries.Length)
            throw new InvalidOperationException("Global binding feedback slot is out of range.");
#endif

        ref var entry = ref globalBindingIcEntries![icSlot];
        if (entry.Kind != GlobalBindingIcKind.Uninitialized)
        {
            if (entry.Kind is GlobalBindingIcKind.NonLexical)
            {
                if (GlobalObject.TryGetCachedGlobalValue(entry.Slot, entry.Version, out value))
                    return true;
            }
            else
            {
                value = ThrowIfTheHole(entry.LexicalContext!.Slots[entry.Slot]);
                return true;
            }
        }

        return TryGetGlobalBindingByAtomSlow(ref entry, atom, out value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool TryGetGlobalBindingByAtomSlow(ref GlobalBindingIcEntry entry, int atom, out JsValue value)
    {
        if (TryGetGlobalLexicalBindingValue(atom, out value, out var lexicalContext, out var lexicalSlot,
                out var isConst))
        {
            entry.Kind = isConst ? GlobalBindingIcKind.LexicalConst : GlobalBindingIcKind.Lexical;
            entry.NameAtom = atom;
            entry.LexicalContext = lexicalContext;
            entry.Slot = lexicalSlot;
            entry.Version = 0;
            return true;
        }

        if (GlobalObject.TryGetPropertyAtomForGlobalCache(this, atom, out value, out var globalSlot,
                out var globalVersion))
        {
            entry.Kind = globalSlot >= 0 ? GlobalBindingIcKind.NonLexical : GlobalBindingIcKind.Uninitialized;
            entry.NameAtom = atom;
            entry.LexicalContext = null;
            entry.Slot = globalSlot;
            entry.Version = globalVersion;
            return true;
        }

        entry.Kind = GlobalBindingIcKind.Uninitialized;
        entry.NameAtom = atom;
        entry.LexicalContext = null;
        entry.Slot = -1;
        entry.Version = 0;
        return false;
    }

    internal bool HasGlobalLexicalBindingAtom(int atom)
    {
        return GlobalObject.HasLexicalBindingAtom(atom);
    }

    private void RegisterGlobalLexicalBindings(JsScript script, JsContext context)
    {
        GlobalObject.RegisterLexicalBindings(script, context);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void StoreGlobalByAtom(
        JsScript script,
        int icSlot,
        int atom,
        bool isInitializationStore,
        bool useFunctionDeclarationSemantics,
        bool strict)
    {
        var globalBindingIcEntries = script.GlobalBindingIcEntries;
#if DEBUG
        if (globalBindingIcEntries is null)
            throw new InvalidOperationException("Global binding IC entries are required for IC-backed global stores.");
        if ((uint)icSlot >= (uint)globalBindingIcEntries.Length)
            throw new InvalidOperationException("Global binding feedback slot is out of range.");
#endif

        ref var entry = ref globalBindingIcEntries![icSlot];
        if (entry.Kind != GlobalBindingIcKind.Uninitialized)
        {
            if (entry.Kind is GlobalBindingIcKind.NonLexical)
            {
                if (!isInitializationStore &&
                    !useFunctionDeclarationSemantics &&
                    GlobalObject.TrySetCachedGlobalValue(entry.Slot, entry.Version, acc))
                    return;
            }
            else
            {
                if (entry.Kind == GlobalBindingIcKind.LexicalConst && !isInitializationStore)
                    ThrowConstAssignError(atom);

                entry.LexicalContext!.Slots[entry.Slot] = acc;
                return;
            }
        }

        StoreGlobalByAtomSlow(ref entry, atom, isInitializationStore, useFunctionDeclarationSemantics, strict);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ThrowConstAssignError(int atom)
    {
        ThrowTypeError("GLOBAL_CONST_ASSIGN", $"{Atoms.AtomToString(atom)} is read-only");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void StoreGlobalByAtomSlow(
        ref GlobalBindingIcEntry entry,
        int atom,
        bool isInitializationStore,
        bool useFunctionDeclarationSemantics,
        bool strict)
    {
        if (GlobalObject.TryGetLexicalBinding(atom, out var lexicalContext, out var lexicalSlot, out var isConst))
        {
            entry.Kind = isConst ? GlobalBindingIcKind.LexicalConst : GlobalBindingIcKind.Lexical;
            entry.NameAtom = atom;
            entry.LexicalContext = lexicalContext;
            entry.Slot = lexicalSlot;
            entry.Version = 0;

            if (isConst && !isInitializationStore)
                ThrowTypeError("GLOBAL_CONST_ASSIGN", $"{Atoms.AtomToString(atom)} is read-only");

            lexicalContext!.Slots[lexicalSlot] = acc;
            return;
        }

        entry.Kind = GlobalBindingIcKind.NonLexical;
        entry.NameAtom = atom;
        entry.LexicalContext = null;
        StoreGlobalByAtomNonLexical(atom, isInitializationStore, useFunctionDeclarationSemantics, strict);

        if (GlobalObject.TryGetOwnWritableDataGlobalSlot(atom, out var globalSlot, out var globalVersion))
        {
            entry.Slot = globalSlot;
            entry.Version = globalVersion;
        }
        else
        {
            entry.Kind = GlobalBindingIcKind.Uninitialized;
            entry.Slot = -1;
            entry.Version = 0;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void StoreGlobalByAtomNonLexical(int atom, bool isInitializationStore, bool useFunctionDeclarationSemantics,
        bool strict)
    {
        var result = GlobalObject.StoreGlobalAtom(
            this,
            atom,
            acc,
            strict,
            isInitializationStore,
            useFunctionDeclarationSemantics,
            indirectEvalGlobalBindingSemanticsDepth > 0);
        if (result == GlobalStoreResult.Success)
            return;

        if (!strict && result != GlobalStoreResult.FunctionNotDefinable)
            return;

        ThrowError(this, atom, result);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void ThrowError(JsRealm realm, int atom, GlobalStoreResult result)
        {
            var name = realm.Atoms.AtomToString(atom);
            if (result == GlobalStoreResult.Unresolvable)
                ThrowReferenceError("GLOBAL_NOT_DEFINED", $"{name} is not defined");
            if (result == GlobalStoreResult.FunctionNotDefinable)
                ThrowTypeError("GLOBAL_FUNCTION_NOT_DEFINABLE", $"Identifier '{name}' has already been declared");
            realm.ThrowConstAssignError(atom);
        }
    }

    internal void EnterIndirectEvalGlobalBindingSemantics()
    {
        indirectEvalGlobalBindingSemanticsDepth++;
    }

    internal void ExitIndirectEvalGlobalBindingSemantics()
    {
        indirectEvalGlobalBindingSemanticsDepth--;
    }
}
