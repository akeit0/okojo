using Okojo.Compiler;

namespace Okojo.Runtime;

public sealed partial class JsAgent
{
    private static void InstallLinkedReExports(
        JsRealm realm,
        JsPlainObject importsObject,
        JsModuleNamespaceObject exportsObject,
        IReadOnlyList<ExportFromBindingResolved> exportFromBindings,
        IReadOnlyList<ExportNamespaceFromBindingResolved> exportNamespaceFromBindings,
        IReadOnlyList<string> exportStars,
        HashSet<string>? ambiguousStarExportNames)
    {
        for (var i = 0; i < exportFromBindings.Count; i++)
        {
            var from = exportFromBindings[i];
            if (!TryResolveDependencyNamespace(realm, importsObject, from.ResolvedDependencyId, out var depNamespace))
                continue;

            var getter = new JsHostFunction(realm, static (in info) =>
            {
                var getterRealm = info.Realm;
                var getterCallee = (JsHostFunction)info.Function;
                var capture = (ExportFromGetterCapture)getterCallee.UserData!;
                if (TryReadNamespaceExportByName(getterRealm, capture.Dependency, capture.ImportedName, out var value))
                    return value;
                return JsValue.Undefined;
            }, string.Empty, 0)
            {
                UserData = new ExportFromGetterCapture(depNamespace, from.ImportedName)
            };
            DefineLiveExportAccessor(realm, exportsObject, from.ExportedName, getter);
        }

        for (var i = 0; i < exportNamespaceFromBindings.Count; i++)
        {
            var from = exportNamespaceFromBindings[i];
            if (!TryResolveDependencyNamespace(realm, importsObject, from.ResolvedDependencyId, out var depNamespace))
                continue;

            var getter = new JsHostFunction(realm,
                static (in info) =>
                {
                    var getterCallee = (JsHostFunction)info.Function;
                    return ((NamespaceExportGetterCapture)getterCallee.UserData!).NamespaceValue;
                }, string.Empty, 0)
            {
                UserData = new NamespaceExportGetterCapture(depNamespace)
            };
            DefineLiveExportAccessor(realm, exportsObject, from.ExportedName, getter);
        }

        for (var i = 0; i < exportStars.Count; i++)
        {
            if (!TryResolveDependencyNamespace(realm, importsObject, exportStars[i], out var depNamespace))
                continue;

            foreach (var entry in depNamespace.Shape.EnumerateSlotInfos())
            {
                var atom = entry.Key;
                if (atom < 0)
                    continue;
                var flags = entry.Value.Flags;
                if ((flags & JsShapePropertyFlags.Enumerable) == 0)
                    continue;

                var key = realm.Atoms.AtomToString(atom);
                if (string.Equals(key, "default", StringComparison.Ordinal))
                    continue;
                if (ambiguousStarExportNames is not null && ambiguousStarExportNames.Contains(key))
                    continue;
                if (exportsObject.TryGetOwnPropertySlotInfoAtom(atom, out _))
                    continue;

                var getter = new JsHostFunction(realm, static (in info) =>
                {
                    var getterRealm = info.Realm;
                    var getterCallee = (JsHostFunction)info.Function;
                    var capture = (ExportStarGetterCapture)getterCallee.UserData!;
                    if (capture.Dependency.TryGetPropertyAtom(getterRealm, capture.Atom, out var value, out _))
                        return value;
                    return JsValue.Undefined;
                }, string.Empty, 0)
                {
                    UserData = new ExportStarGetterCapture(depNamespace, atom)
                };

                exportsObject.DefineAccessorPropertyAtom(realm, atom, getter, null,
                    JsShapePropertyFlags.Enumerable | JsShapePropertyFlags.Configurable |
                    JsShapePropertyFlags.HasGetter);
            }
        }
    }

    private static bool TryResolveDependencyNamespace(JsRealm realm, JsPlainObject importsObject, string resolvedId,
        out JsObject dependencyNamespace)
    {
        dependencyNamespace = null!;
        if (!importsObject.TryGetPropertyAtom(realm, realm.Atoms.InternNoCheck(resolvedId), out var depNsValue, out _))
            return false;
        if (!depNsValue.TryGetObject(out var depObj) || depObj is not JsObject depNamespaceObj)
            return false;
        dependencyNamespace = depNamespaceObj;
        return true;
    }

    private static bool TryReadNamespaceExportByName(JsRealm realm, JsObject ns, string exportName,
        out JsValue value)
    {
        if (TryGetArrayIndexFromCanonicalString(exportName, out var idx))
            return ns.TryGetElement(idx, out value);

        var atom = realm.Atoms.InternNoCheck(exportName);
        return ns.TryGetPropertyAtom(realm, atom, out value, out _);
    }

    private static void DefineLiveExportAccessor(JsRealm realm, JsModuleNamespaceObject exportsObject,
        string exportName,
        JsFunction getter)
    {
        if (AtomTable.TryGetArrayIndexFromCanonicalString(exportName, out var idx))
        {
            exportsObject.DefineElementDescriptor(idx,
                new(getter, null, JsShapePropertyFlags.Enumerable | JsShapePropertyFlags.HasGetter));
        }
        else
        {
            var atom = realm.Atoms.InternNoCheck(exportName);
            exportsObject.DefineAccessorPropertyAtom(realm, atom, getter, null,
                JsShapePropertyFlags.Enumerable | JsShapePropertyFlags.HasGetter);
        }
    }

    private static void InstallLocalSlotBackedLiveExports(
        JsRealm realm,
        string moduleResolvedId,
        JsModuleNamespaceObject exportsObject,
        IReadOnlyDictionary<string, string> exportLocalByName,
        IReadOnlyDictionary<string, ModuleVariableBinding> moduleVariableBindings,
        ModuleExecutionBindings moduleExecutionBindings,
        HashSet<string>? defaultNameEligibleLocals)
    {
        if (exportLocalByName.Count == 0 || moduleVariableBindings.Count == 0)
            return;

        foreach (var pair in exportLocalByName)
        {
            if (!moduleVariableBindings.TryGetValue(pair.Value, out var binding))
                continue;
            if (!IsValidModuleCellIndex(moduleExecutionBindings, binding.CellIndex))
                continue;

            var getter = new JsHostFunction(realm, static (in info) =>
            {
                var getterCallee = (JsHostFunction)info.Function;
                var capture = (LocalExportSlotGetterCapture)getterCallee.UserData!;
                var value = LoadModuleVariableFromBindings(capture.Realm, capture.Bindings, capture.CellIndex);
                if (capture.ShouldSetDefaultName &&
                    value.TryGetObject(out var obj) &&
                    obj is JsFunction fn)
                    if (ShouldSetModuleDefaultExportName(capture.Realm, fn))
                    {
                        const int nameAtom = IdName;
                        fn.DefineDataPropertyAtom(capture.Realm, nameAtom, "default",
                            JsShapePropertyFlags.Configurable);
                    }

                return value;
            }, string.Empty, 0)
            {
                UserData = new LocalExportSlotGetterCapture(
                    realm,
                    moduleResolvedId,
                    moduleExecutionBindings,
                    binding.CellIndex,
                    pair.Key,
                    defaultNameEligibleLocals is not null && defaultNameEligibleLocals.Contains(pair.Value))
            };
            DefineLiveExportAccessor(realm, exportsObject, pair.Key, getter);
        }
    }

    private static (
        Dictionary<string, ModuleVariableBinding> BindingByName,
        ModuleVariableSlot[] RegularExports,
        ModuleVariableSlot[] RegularImports)
        BuildModuleVariableSlots(
            IReadOnlyList<JsResolvedImportBinding> importBindings,
            IReadOnlyDictionary<string, string> exportLocalByName,
            IReadOnlySet<string> preinitializedLocalExportNames)
    {
        var regularExports = new List<ModuleVariableSlot>(exportLocalByName.Count);
        var regularImports = new List<ModuleVariableSlot>(importBindings.Count);
        var map = new Dictionary<string, ModuleVariableBinding>(importBindings.Count + exportLocalByName.Count,
            StringComparer.Ordinal);

        for (var i = 0; i < importBindings.Count; i++)
        {
            var binding = importBindings[i];
            if (map.ContainsKey(binding.LocalName))
                continue;

            if (regularImports.Count >= 127)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Module variable slot limit exceeded (255)", "MODULE_SLOT_LIMIT");

            var cellIndex = unchecked((sbyte)-(regularImports.Count + 1));
            map.Add(binding.LocalName, new(cellIndex, 0, true));
            regularImports.Add(binding.Kind == ModuleImportBindingKind.Namespace
                ? new(ModuleVariableSlotKind.NamespaceImport,
                    binding.ResolvedDependencyId, isReadOnly: true)
                : new ModuleVariableSlot(ModuleVariableSlotKind.NamedImport,
                    binding.ResolvedDependencyId, binding.ImportedName,
                    true));
        }

        foreach (var pair in exportLocalByName)
        {
            var localName = pair.Value;
            if (map.ContainsKey(localName))
                continue;

            if (regularExports.Count >= 127)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Module variable slot limit exceeded (255)", "MODULE_SLOT_LIMIT");

            var cellIndex = unchecked((sbyte)(regularExports.Count + 1));
            map.Add(localName, new(cellIndex, 0, false));
            var slot = new ModuleVariableSlot(ModuleVariableSlotKind.Local);
            if (preinitializedLocalExportNames.Contains(localName))
                slot.LocalValue = JsValue.Undefined;
            regularExports.Add(slot);
        }

        return (map, regularExports.ToArray(), regularImports.ToArray());
    }

    private static JsValue LoadModuleVariableFromBindings(JsRealm realm, ModuleExecutionBindings bindings,
        int cellIndex)
    {
        if (cellIndex > 0)
        {
            var exportIndex = cellIndex - 1;
            if ((uint)exportIndex >= (uint)bindings.RegularExports.Length)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    $"Module export cell index out of range: {cellIndex}", "MODULE_SLOT_OOB");
            return ThrowIfModuleBindingUninitialized(bindings.RegularExports[exportIndex].LocalValue);
        }

        var importIndex = -cellIndex - 1;
        if ((uint)importIndex >= (uint)bindings.RegularImports.Length)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                $"Module import cell index out of range: {cellIndex}", "MODULE_SLOT_OOB");

        var cell = bindings.RegularImports[importIndex];
        switch (cell.Kind)
        {
            case ModuleVariableSlotKind.NamespaceImport:
                if (cell.ResolvedDependencyId is null)
                    return JsValue.TheHole;
                if (bindings.Imports.TryGetObject(out var importsObjNs) &&
                    importsObjNs is JsObject importsNs &&
                    importsNs.TryGetPropertyAtom(realm, realm.Atoms.InternNoCheck(cell.ResolvedDependencyId),
                        out var depNsValue, out _))
                    return ThrowIfModuleBindingUninitialized(depNsValue);

                return JsValue.TheHole;

            case ModuleVariableSlotKind.NamedImport:
                if (cell.ResolvedDependencyId is null || cell.ImportedName is null)
                    return JsValue.TheHole;
                if (bindings.Imports.TryGetObject(out var importsObj) &&
                    importsObj is JsObject imports &&
                    imports.TryGetPropertyAtom(realm, realm.Atoms.InternNoCheck(cell.ResolvedDependencyId),
                        out var depNs, out _) &&
                    depNs.TryGetObject(out var depNsObj) &&
                    depNsObj is JsObject depObj &&
                    TryReadNamespaceExportByName(realm, depObj, cell.ImportedName, out var imported))
                    return ThrowIfModuleBindingUninitialized(imported);

                return JsValue.TheHole;

            default:
                return JsValue.TheHole;
        }
    }

    private static bool IsValidModuleCellIndex(ModuleExecutionBindings bindings, int cellIndex)
    {
        if (cellIndex > 0)
        {
            var exportIndex = cellIndex - 1;
            return (uint)exportIndex < (uint)bindings.RegularExports.Length;
        }

        var importIndex = -cellIndex - 1;
        return (uint)importIndex < (uint)bindings.RegularImports.Length;
    }

    private static JsValue ThrowIfModuleBindingUninitialized(in JsValue value)
    {
        if (value.IsTheHole)
            throw new JsRuntimeException(JsErrorKind.ReferenceError, string.Empty, "TDZ_READ_BEFORE_INIT");
        return value;
    }

    private sealed class LocalExportSlotGetterCapture(
        JsRealm realm,
        string moduleResolvedId,
        ModuleExecutionBindings bindings,
        int cellIndex,
        string exportedName,
        bool shouldSetDefaultName)
    {
        public JsRealm Realm { get; } = realm;
        public string ModuleResolvedId { get; } = moduleResolvedId;
        public ModuleExecutionBindings Bindings { get; } = bindings;
        public int CellIndex { get; } = cellIndex;
        public string ExportedName { get; } = exportedName;
        public bool ShouldSetDefaultName { get; } = shouldSetDefaultName;
    }

    private sealed class ExportStarGetterCapture(JsObject dependency, int atom)
    {
        public JsObject Dependency { get; } = dependency;
        public int Atom { get; } = atom;
    }

    private sealed class ExportFromGetterCapture(JsObject dependency, string importedName)
    {
        public JsObject Dependency { get; } = dependency;
        public string ImportedName { get; } = importedName;
    }

    private sealed class NamespaceExportGetterCapture(JsValue namespaceValue)
    {
        public JsValue NamespaceValue { get; } = namespaceValue;
    }
}
