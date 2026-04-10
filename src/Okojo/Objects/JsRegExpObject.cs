using System.Runtime.InteropServices;
using Okojo.RegExp;

namespace Okojo.Objects;

internal sealed class JsRegExpObject : JsObject
{
    private Dictionary<int, PropertyDescriptor>? shadowedHiddenOwnProperties;

    internal JsRegExpObject(
        JsRealm realm,
        string pattern,
        string flags,
        bool isGlobal,
        bool ignoreCase,
        bool multiline,
        bool sticky,
        bool unicode,
        bool dotAll,
        IRegExpEngine? engine = null)
        : base(realm)
    {
        Prototype = realm.RegExpPrototype;
        Engine = engine ?? realm.Engine.Options.RegExpEngine;
        CompiledPattern = Engine is not null
            ? CompileWithEngine(Engine, pattern, flags)
            : JsRegExpRuntime.CompilePattern(pattern, flags);
        Pattern = pattern;
        Flags = CompiledPattern.Flags;
        Global = CompiledPattern.ParsedFlags.Global;
        IgnoreCase = CompiledPattern.ParsedFlags.IgnoreCase;
        Multiline = CompiledPattern.ParsedFlags.Multiline;
        Sticky = CompiledPattern.ParsedFlags.Sticky;
        Unicode = CompiledPattern.ParsedFlags.Unicode;
        DotAll = CompiledPattern.ParsedFlags.DotAll;
        InitializeStorageFromCachedShape(realm.RegExpOwnShape);
        SetNamedSlotUnchecked(JsRealm.RegExpOwnSourceSlot, JsValue.FromString(pattern));
        SetNamedSlotUnchecked(JsRealm.RegExpOwnFlagsSlot, JsValue.FromString(flags));
        SetNamedSlotUnchecked(JsRealm.RegExpOwnGlobalSlot, isGlobal ? JsValue.True : JsValue.False);
        SetNamedSlotUnchecked(JsRealm.RegExpOwnIgnoreCaseSlot, ignoreCase ? JsValue.True : JsValue.False);
        SetNamedSlotUnchecked(JsRealm.RegExpOwnMultilineSlot, multiline ? JsValue.True : JsValue.False);
        SetNamedSlotUnchecked(JsRealm.RegExpOwnLastIndexSlot, JsValue.FromInt32(0));
        SetNamedSlotUnchecked(JsRealm.RegExpOwnStickySlot, sticky ? JsValue.True : JsValue.False);
        SetNamedSlotUnchecked(JsRealm.RegExpOwnUnicodeSlot, unicode ? JsValue.True : JsValue.False);
        SetNamedSlotUnchecked(JsRealm.RegExpOwnDotAllSlot, dotAll ? JsValue.True : JsValue.False);
    }

    internal IRegExpEngine? Engine { get; }
    internal RegExpCompiledPattern CompiledPattern { get; }
    internal string Pattern { get; }
    internal string Flags { get; }
    internal bool Global { get; }
    internal bool IgnoreCase { get; }
    internal bool Multiline { get; }
    internal bool Sticky { get; }
    internal bool Unicode { get; }
    internal bool DotAll { get; }
    internal string ExecutionPattern => CompiledPattern.ExecutionPattern;
    internal string[] NamedGroupNames => CompiledPattern.NamedGroupNames;

    private static bool IsHiddenRegExpOwnAtom(int atom)
    {
        return atom is IdSource or IdFlags or IdGlobal or IdIgnoreCase or
            IdMultiline or IdSticky or IdUnicode or IdDotAll;
    }

    private bool HasShadowedHiddenOwnProperty(int atom)
    {
        return shadowedHiddenOwnProperties is not null && shadowedHiddenOwnProperties.ContainsKey(atom);
    }

    private bool TryGetShadowedHiddenOwnProperty(int atom, out PropertyDescriptor descriptor)
    {
        if (shadowedHiddenOwnProperties is not null &&
            shadowedHiddenOwnProperties.TryGetValue(atom, out descriptor))
            return true;

        descriptor = default;
        return false;
    }

    private static RegExpCompiledPattern CompileWithEngine(IRegExpEngine engine, string pattern, string flags)
    {
        try
        {
            return engine.Compile(pattern, flags);
        }
        catch (ArgumentException ex)
        {
            throw new JsRuntimeException(JsErrorKind.SyntaxError, ex.Message, "REGEXP_INVALID_PATTERN");
        }
    }

    internal override bool TryGetPropertyAtomWithReceiverValue(JsRealm realm, in JsValue receiverValue, int atom,
        out JsValue value, out SlotInfo slotInfo)
    {
        if (TryGetShadowedHiddenOwnProperty(atom, out var descriptor))
        {
            slotInfo = SlotInfo.Invalid;
            if (descriptor.IsAccessor)
            {
                if (descriptor.Getter is null)
                {
                    value = JsValue.Undefined;
                    return true;
                }

                value = InvokeAccessorFunction(realm, receiverValue, descriptor.Getter, ReadOnlySpan<JsValue>.Empty);
                return true;
            }

            value = descriptor.Value;
            return true;
        }

        if (IsHiddenRegExpOwnAtom(atom))
        {
            if (Prototype is not null && Prototype != this)
            {
                var found = Prototype.TryGetPropertyAtomWithReceiverValue(realm, receiverValue, atom, out value, out _);
                slotInfo = SlotInfo.Invalid;
                return found;
            }

            slotInfo = SlotInfo.Invalid;
            value = JsValue.Undefined;
            return false;
        }

        return base.TryGetPropertyAtomWithReceiverValue(realm, receiverValue, atom, out value, out slotInfo);
    }

    internal override bool TryGetOwnNamedPropertyDescriptorAtom(JsRealm realm, int atom,
        out PropertyDescriptor descriptor,
        bool needDescriptor = true)
    {
        if (TryGetShadowedHiddenOwnProperty(atom, out descriptor))
        {
            if (!needDescriptor)
                descriptor = default;
            return true;
        }

        if (IsHiddenRegExpOwnAtom(atom))
        {
            descriptor = default;
            return false;
        }

        return base.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out descriptor, needDescriptor);
    }

    internal override void CollectOwnNamedPropertyAtoms(JsRealm realm, List<int> atomsOut, bool enumerableOnly)
    {
        var scratch = new List<int>(8);
        base.CollectOwnNamedPropertyAtoms(realm, scratch, enumerableOnly);
        for (var i = 0; i < scratch.Count; i++)
        {
            var atom = scratch[i];
            if (!IsHiddenRegExpOwnAtom(atom))
                atomsOut.Add(atom);
        }

        if (shadowedHiddenOwnProperties is null)
            return;

        foreach (var pair in shadowedHiddenOwnProperties)
            if (!enumerableOnly || pair.Value.Enumerable)
                atomsOut.Add(pair.Key);
    }

    internal override void DefineDataPropertyAtom(JsRealm realm, int atom, JsValue value, JsShapePropertyFlags flags)
    {
        if (IsHiddenRegExpOwnAtom(atom))
        {
            (shadowedHiddenOwnProperties ??= [])[atom] = new(value, null, flags);
            return;
        }

        base.DefineDataPropertyAtom(realm, atom, value, flags);
    }

    internal override bool DefineOwnDataPropertyExact(JsRealm realm, int atom, JsValue value,
        JsShapePropertyFlags flags)
    {
        if (IsHiddenRegExpOwnAtom(atom))
        {
            (shadowedHiddenOwnProperties ??= [])[atom] = new(value, null, flags);
            return true;
        }

        return base.DefineOwnDataPropertyExact(realm, atom, value, flags);
    }

    internal override bool DefineOwnAccessorPropertyExact(JsRealm realm, int atom, JsFunction? getter,
        JsFunction? setter, JsShapePropertyFlags flags)
    {
        if (IsHiddenRegExpOwnAtom(atom))
        {
            (shadowedHiddenOwnProperties ??= [])[atom] = new(
                getter is null ? JsValue.Undefined : JsValue.FromObject(getter),
                setter,
                flags);
            return true;
        }

        return base.DefineOwnAccessorPropertyExact(realm, atom, getter, setter, flags);
    }

    internal override bool SetPropertyAtomWithReceiver(JsRealm realm, JsObject receiver, int atom, JsValue value,
        out SlotInfo slotInfo)
    {
        if (shadowedHiddenOwnProperties is not null &&
            shadowedHiddenOwnProperties.TryGetValue(atom, out var descriptor))
        {
            slotInfo = SlotInfo.Invalid;
            if (descriptor.IsAccessor)
            {
                if (descriptor.Setter is null)
                    return false;

                var arg = MemoryMarshal.CreateReadOnlySpan(in value, 1);
                _ = InvokeAccessorFunction(realm, receiver, descriptor.Setter, arg);
                return true;
            }

            if (!descriptor.Writable)
                return false;

            if (!ReferenceEquals(this, receiver))
                return receiver.DefineOwnDataPropertyExact(realm, atom, value, JsShapePropertyFlags.Open);

            shadowedHiddenOwnProperties[atom] = new(value, null, descriptor.Flags);
            return true;
        }

        return base.SetPropertyAtomWithReceiver(realm, receiver, atom, value, out slotInfo);
    }

    internal override bool DeletePropertyAtom(JsRealm realm, int atom)
    {
        if (shadowedHiddenOwnProperties is not null &&
            shadowedHiddenOwnProperties.TryGetValue(atom, out var descriptor))
        {
            if (!descriptor.Configurable)
                return false;
            shadowedHiddenOwnProperties.Remove(atom);
            return true;
        }

        return base.DeletePropertyAtom(realm, atom);
    }
}
