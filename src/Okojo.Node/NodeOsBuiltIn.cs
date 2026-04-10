using System.Diagnostics;
using System.Runtime.InteropServices;
using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Node;

internal sealed class NodeOsBuiltIn(NodeRuntime runtime)
{
    private const int ReleaseSlot = 0;
    private const int PlatformSlot = 1;
    private const int ArchSlot = 2;
    private const int HomedirSlot = 3;
    private const int TmpdirSlot = 4;
    private const int EolSlot = 5;
    private int atomArch = -1;
    private int atomEol = -1;
    private int atomHomedir = -1;
    private int atomPlatform = -1;

    private int atomRelease = -1;
    private int atomTmpdir = -1;
    private JsPlainObject? moduleObject;
    private StaticNamedPropertyLayout? moduleShape;

    public JsPlainObject GetModule()
    {
        if (moduleObject is not null)
            return moduleObject;

        var realm = runtime.MainRealm;
        var shape = moduleShape ??= CreateModuleShape(realm);
        var module = new JsPlainObject(shape);
        module.SetNamedSlotUnchecked(ReleaseSlot, JsValue.FromObject(CreateReleaseFunction(realm)));
        module.SetNamedSlotUnchecked(PlatformSlot, JsValue.FromObject(CreatePlatformFunction(realm)));
        module.SetNamedSlotUnchecked(ArchSlot, JsValue.FromObject(CreateArchFunction(realm)));
        module.SetNamedSlotUnchecked(HomedirSlot, JsValue.FromObject(CreateHomedirFunction(realm)));
        module.SetNamedSlotUnchecked(TmpdirSlot, JsValue.FromObject(CreateTmpdirFunction(realm)));
        module.SetNamedSlotUnchecked(EolSlot, JsValue.FromString(Environment.NewLine));
        moduleObject = module;
        return module;
    }

    private StaticNamedPropertyLayout CreateModuleShape(JsRealm realm)
    {
        EnsureAtoms(realm);
        var shape = realm.EmptyShape.GetOrAddTransition(atomRelease, JsShapePropertyFlags.Open, out var releaseInfo);
        shape = shape.GetOrAddTransition(atomPlatform, JsShapePropertyFlags.Open, out var platformInfo);
        shape = shape.GetOrAddTransition(atomArch, JsShapePropertyFlags.Open, out var archInfo);
        shape = shape.GetOrAddTransition(atomHomedir, JsShapePropertyFlags.Open, out var homedirInfo);
        shape = shape.GetOrAddTransition(atomTmpdir, JsShapePropertyFlags.Open, out var tmpdirInfo);
        shape = shape.GetOrAddTransition(atomEol, JsShapePropertyFlags.Open, out var eolInfo);
        Debug.Assert(releaseInfo.Slot == ReleaseSlot);
        Debug.Assert(platformInfo.Slot == PlatformSlot);
        Debug.Assert(archInfo.Slot == ArchSlot);
        Debug.Assert(homedirInfo.Slot == HomedirSlot);
        Debug.Assert(tmpdirInfo.Slot == TmpdirSlot);
        Debug.Assert(eolInfo.Slot == EolSlot);
        return shape;
    }

    private void EnsureAtoms(JsRealm realm)
    {
        atomRelease = EnsureAtom(realm, atomRelease, "release");
        atomPlatform = EnsureAtom(realm, atomPlatform, "platform");
        atomArch = EnsureAtom(realm, atomArch, "arch");
        atomHomedir = EnsureAtom(realm, atomHomedir, "homedir");
        atomTmpdir = EnsureAtom(realm, atomTmpdir, "tmpdir");
        atomEol = EnsureAtom(realm, atomEol, "EOL");
    }

    private static int EnsureAtom(JsRealm realm, int atom, string text)
    {
        return atom >= 0 ? atom : realm.Atoms.InternNoCheck(text);
    }

    private static JsHostFunction CreateReleaseFunction(JsRealm realm)
    {
        return new(realm, "release", 0,
            static (in _) => { return JsValue.FromString(Environment.OSVersion.Version.ToString()); }, false);
    }

    private static JsHostFunction CreatePlatformFunction(JsRealm realm)
    {
        return new(realm, "platform", 0, static (in _) => { return JsValue.FromString(GetPlatformString()); }, false);
    }

    private static JsHostFunction CreateArchFunction(JsRealm realm)
    {
        return new(realm, "arch", 0, static (in _) => { return JsValue.FromString(GetArchString()); }, false);
    }

    private static JsHostFunction CreateHomedirFunction(JsRealm realm)
    {
        return new(realm, "homedir", 0,
            static (in _) =>
            {
                return JsValue.FromString(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            }, false);
    }

    private static JsHostFunction CreateTmpdirFunction(JsRealm realm)
    {
        return new(realm, "tmpdir", 0,
            static (in _) =>
            {
                return JsValue.FromString(Path.GetTempPath()
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }, false);
    }

    private static string GetPlatformString()
    {
        if (OperatingSystem.IsWindows())
            return "win32";
        if (OperatingSystem.IsMacOS())
            return "darwin";
        if (OperatingSystem.IsLinux())
            return "linux";
        return "unknown";
    }

    private static string GetArchString()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "ia32",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
        };
    }
}
