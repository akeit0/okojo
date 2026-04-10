using System.Diagnostics;
using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Node;

internal sealed class NodeFsBuiltIn(NodeRuntime runtime)
{
    private const int ModuleReadFileSyncSlot = 0;
    private const int ModuleWriteFileSlot = 1;
    private const int ModuleOpenSyncSlot = 2;
    private const int ModuleReaddirSyncSlot = 3;
    private const int ModuleStatSyncSlot = 4;
    private const int ModuleConstantsSlot = 5;

    private const int ConstantsONonBlockSlot = 0;
    private const int ConstantsOEvtOnlySlot = 1;

    private const int StatsIsDirectorySlot = 0;
    private const int StatsIsFileSlot = 1;
    private int atomConstants = -1;
    private int atomIsDirectory = -1;
    private int atomIsFile = -1;
    private int atomOEvtOnly = -1;
    private int atomONonBlock = -1;
    private int atomOpenSync = -1;
    private int atomReaddirSync = -1;

    private int atomReadFileSync = -1;
    private int atomStatSync = -1;
    private int atomWriteFile = -1;
    private JsPlainObject? constantsObject;
    private StaticNamedPropertyLayout? constantsShape;
    private JsPlainObject? moduleObject;
    private StaticNamedPropertyLayout? moduleShape;
    private StaticNamedPropertyLayout? statsShape;

    public JsPlainObject GetModule()
    {
        if (moduleObject is not null)
            return moduleObject;

        var realm = runtime.MainRealm;
        var shape = moduleShape ??= CreateModuleShape(realm);
        var module = new JsPlainObject(shape);
        module.SetNamedSlotUnchecked(ModuleReadFileSyncSlot, JsValue.FromObject(CreateReadFileSyncFunction(realm)));
        module.SetNamedSlotUnchecked(ModuleWriteFileSlot, JsValue.FromObject(CreateWriteFileFunction(realm)));
        module.SetNamedSlotUnchecked(ModuleOpenSyncSlot, JsValue.FromObject(CreateOpenSyncFunction(realm)));
        module.SetNamedSlotUnchecked(ModuleReaddirSyncSlot, JsValue.FromObject(CreateReaddirSyncFunction(realm)));
        module.SetNamedSlotUnchecked(ModuleStatSyncSlot, JsValue.FromObject(CreateStatSyncFunction(realm)));
        module.SetNamedSlotUnchecked(ModuleConstantsSlot, JsValue.FromObject(GetConstantsObject()));
        moduleObject = module;
        return module;
    }

    private JsPlainObject GetConstantsObject()
    {
        if (constantsObject is not null)
            return constantsObject;

        var realm = runtime.MainRealm;
        var shape = constantsShape ??= CreateConstantsShape(realm);
        var constants = new JsPlainObject(shape);
        constants.SetNamedSlotUnchecked(ConstantsONonBlockSlot, JsValue.FromInt32(GetONonBlock()));
        constants.SetNamedSlotUnchecked(ConstantsOEvtOnlySlot, JsValue.FromInt32(GetOEvtOnly()));
        constantsObject = constants;
        return constants;
    }

    private StaticNamedPropertyLayout CreateModuleShape(JsRealm realm)
    {
        EnsureAtoms(realm);
        var shape = realm.EmptyShape.GetOrAddTransition(atomReadFileSync, JsShapePropertyFlags.Open, out var readInfo);
        shape = shape.GetOrAddTransition(atomWriteFile, JsShapePropertyFlags.Open, out var writeInfo);
        shape = shape.GetOrAddTransition(atomOpenSync, JsShapePropertyFlags.Open, out var openInfo);
        shape = shape.GetOrAddTransition(atomReaddirSync, JsShapePropertyFlags.Open, out var readdirInfo);
        shape = shape.GetOrAddTransition(atomStatSync, JsShapePropertyFlags.Open, out var statInfo);
        shape = shape.GetOrAddTransition(atomConstants, JsShapePropertyFlags.Open, out var constantsInfo);
        Debug.Assert(readInfo.Slot == ModuleReadFileSyncSlot);
        Debug.Assert(writeInfo.Slot == ModuleWriteFileSlot);
        Debug.Assert(openInfo.Slot == ModuleOpenSyncSlot);
        Debug.Assert(readdirInfo.Slot == ModuleReaddirSyncSlot);
        Debug.Assert(statInfo.Slot == ModuleStatSyncSlot);
        Debug.Assert(constantsInfo.Slot == ModuleConstantsSlot);
        return shape;
    }

    private StaticNamedPropertyLayout CreateConstantsShape(JsRealm realm)
    {
        EnsureAtoms(realm);
        var shape = realm.EmptyShape.GetOrAddTransition(atomONonBlock, JsShapePropertyFlags.Open, out var nonBlockInfo);
        shape = shape.GetOrAddTransition(atomOEvtOnly, JsShapePropertyFlags.Open, out var evtOnlyInfo);
        Debug.Assert(nonBlockInfo.Slot == ConstantsONonBlockSlot);
        Debug.Assert(evtOnlyInfo.Slot == ConstantsOEvtOnlySlot);
        return shape;
    }

    private StaticNamedPropertyLayout CreateStatsShape(JsRealm realm)
    {
        EnsureAtoms(realm);
        var shape = realm.EmptyShape.GetOrAddTransition(atomIsDirectory, JsShapePropertyFlags.Open,
            out var isDirectoryInfo);
        shape = shape.GetOrAddTransition(atomIsFile, JsShapePropertyFlags.Open, out var isFileInfo);
        Debug.Assert(isDirectoryInfo.Slot == StatsIsDirectorySlot);
        Debug.Assert(isFileInfo.Slot == StatsIsFileSlot);
        return shape;
    }

    private void EnsureAtoms(JsRealm realm)
    {
        atomReadFileSync = EnsureAtom(realm, atomReadFileSync, "readFileSync");
        atomWriteFile = EnsureAtom(realm, atomWriteFile, "writeFile");
        atomOpenSync = EnsureAtom(realm, atomOpenSync, "openSync");
        atomReaddirSync = EnsureAtom(realm, atomReaddirSync, "readdirSync");
        atomStatSync = EnsureAtom(realm, atomStatSync, "statSync");
        atomConstants = EnsureAtom(realm, atomConstants, "constants");
        atomONonBlock = EnsureAtom(realm, atomONonBlock, "O_NONBLOCK");
        atomOEvtOnly = EnsureAtom(realm, atomOEvtOnly, "O_EVTONLY");
        atomIsDirectory = EnsureAtom(realm, atomIsDirectory, "isDirectory");
        atomIsFile = EnsureAtom(realm, atomIsFile, "isFile");
    }

    private static int EnsureAtom(JsRealm realm, int atom, string text)
    {
        return atom >= 0 ? atom : realm.Atoms.InternNoCheck(text);
    }

    private static JsHostFunction CreateReadFileSyncFunction(JsRealm realm)
    {
        return new(realm, "readFileSync", 2, static (in info) =>
        {
            var path = info.GetArgumentString(0);
            string? encoding = null;
            if (info.Arguments.Length > 1 && !info.Arguments[1].IsUndefined && !info.Arguments[1].IsNull)
                encoding = info.GetArgument(1).IsString
                    ? info.GetArgument(1).AsString()
                    : info.Realm.ToJsStringSlowPath(info.GetArgument(1));

            if (string.Equals(encoding, "utf8", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(encoding, "utf-8", StringComparison.OrdinalIgnoreCase))
                return JsValue.FromString(File.ReadAllText(path));

            var bytes = File.ReadAllBytes(path);
            return JsValue.FromObject(CreateUint8Array(info.Realm, bytes));
        }, false);
    }

    private static JsHostFunction CreateWriteFileFunction(JsRealm realm)
    {
        return new(realm, "writeFile", 4, static (in info) =>
        {
            var path = info.GetArgumentString(0);
            var content = GetWriteFileContent(info.GetArgument(1), info.Realm);
            string? encoding = null;
            JsFunction? callback = null;

            if (info.ArgumentCount > 2)
            {
                var third = info.GetArgument(2);
                if (third.TryGetObject(out var thirdObj) && thirdObj is JsFunction thirdFn)
                    callback = thirdFn;
                else if (!third.IsUndefined && !third.IsNull)
                    encoding = third.IsString ? third.AsString() : info.Realm.ToJsStringSlowPath(third);
            }

            if (info.ArgumentCount > 3)
            {
                var fourth = info.GetArgument(3);
                if (fourth.TryGetObject(out var fourthObj) && fourthObj is JsFunction fourthFn)
                    callback = fourthFn;
            }

            if (encoding is not null &&
                !string.Equals(encoding, "utf8", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(encoding, "utf-8", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Unsupported fs.writeFile encoding '{encoding}'.");

            File.WriteAllText(path, content);

            if (callback is not null)
                info.Realm.InvokeFunction(callback, JsValue.Undefined, [JsValue.Undefined]);

            return JsValue.Undefined;
        }, false);
    }

    private static JsHostFunction CreateOpenSyncFunction(JsRealm realm)
    {
        return new(realm, "openSync", 2, static (in info) =>
        {
            var path = info.GetArgumentString(0);
            if (!File.Exists(path) && !Directory.Exists(path))
                throw new InvalidOperationException($"ENOENT: no such file or directory, open '{path}'");

            return JsValue.FromInt32(1);
        }, false);
    }

    private static JsHostFunction CreateReaddirSyncFunction(JsRealm realm)
    {
        return new(realm, "readdirSync", 2, static (in info) =>
        {
            var path = info.GetArgumentString(0);
            var entries = Directory.GetFileSystemEntries(path);
            var result = new JsArray(info.Realm);
            for (var i = 0; i < entries.Length; i++)
                result[(uint)i] = JsValue.FromString(Path.GetFileName(entries[i]));
            return JsValue.FromObject(result);
        }, false);
    }

    private JsHostFunction CreateStatSyncFunction(JsRealm realm)
    {
        return new(realm, "statSync", 2, (in info) =>
        {
            var path = info.GetArgumentString(0);
            var isDirectory = Directory.Exists(path);
            var isFile = !isDirectory && File.Exists(path);
            if (!isDirectory && !isFile)
                throw new InvalidOperationException($"ENOENT: no such file or directory, stat '{path}'");

            var shape = statsShape ??= CreateStatsShape(info.Realm);
            var stats = new JsPlainObject(shape);
            stats.SetNamedSlotUnchecked(
                StatsIsDirectorySlot,
                JsValue.FromObject(CreateStatsPredicateFunction(info.Realm, "isDirectory", isDirectory)));
            stats.SetNamedSlotUnchecked(
                StatsIsFileSlot,
                JsValue.FromObject(CreateStatsPredicateFunction(info.Realm, "isFile", isFile)));
            return JsValue.FromObject(stats);
        }, false);
    }

    private static JsHostFunction CreateStatsPredicateFunction(JsRealm realm, string name, bool result)
    {
        return new(realm, name, 0, static (in info) =>
        {
            var flag = (bool)((JsHostFunction)info.Function).UserData!;
            return flag ? JsValue.True : JsValue.False;
        }, false)
        {
            UserData = result
        };
    }

    private static JsTypedArrayObject CreateUint8Array(JsRealm realm, byte[] bytes)
    {
        var array = new JsTypedArrayObject(realm, (uint)bytes.Length, TypedArrayElementKind.Uint8,
            realm.Uint8ArrayPrototype);
        for (uint i = 0; i < bytes.Length; i++)
            array.TrySetNormalizedElement(i, JsValue.FromInt32(bytes[i]));
        return array;
    }

    private static int GetONonBlock()
    {
        return OperatingSystem.IsWindows() ? 0 : 2048;
    }

    private static int GetOEvtOnly()
    {
        return OperatingSystem.IsMacOS() ? 32768 : 0;
    }

    private static string GetWriteFileContent(JsValue value, JsRealm realm)
    {
        return value.IsString ? value.AsString() : realm.ToJsStringSlowPath(value);
    }
}
