using Okojo.Objects;
using Okojo.Runtime;
using Okojo.Values;

namespace Okojo.DebugServer;

public static class OkojoDebugConsole
{
    public static void Install(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);

        var console = new JsPlainObject(realm);
        InstallMethod(realm, console, "log");
        InstallMethod(realm, console, "info");
        InstallMethod(realm, console, "warn");
        InstallMethod(realm, console, "error");
        InstallMethod(realm, console, "debug");
        realm.GlobalObject.DefineDataProperty("console", JsValue.FromObject(console),
            JsShapePropertyFlags.Writable | JsShapePropertyFlags.Configurable);
    }

    private static void InstallMethod(JsRealm realm, JsPlainObject console, string name)
    {
        var method = new JsHostFunction(realm, (in info) =>
        {
            var text = FormatConsoleArguments(realm, info.Arguments);
            Console.Error.WriteLine(string.IsNullOrEmpty(text) ? name : text);
            return JsValue.Undefined;
        }, name, 0, isConstructor: false);

        console.DefineDataProperty(name, JsValue.FromObject(method),
            JsShapePropertyFlags.Writable | JsShapePropertyFlags.Configurable);
    }

    private static string FormatConsoleArguments(JsRealm realm, ReadOnlySpan<JsValue> args)
    {
        if (args.Length == 0)
            return string.Empty;

        var parts = new string[args.Length];
        for (int i = 0; i < args.Length; i++)
            parts[i] = args[i].ToString(realm);
        return string.Join(" ", parts);
    }
}
