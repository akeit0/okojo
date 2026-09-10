using Okojo.JavaScript;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;
using Okojo.JavaScript.Values;

namespace Okojo.DebugServer;

public static class OkojoDebugConsole
{
    public static void Install(JsRealm realm, Action<string, string>? output = null)
    {
        ArgumentNullException.ThrowIfNull(realm);

        var console = new JsPlainObject(realm);
        InstallMethod(realm, console, output, "log");
        InstallMethod(realm, console, output, "info");
        InstallMethod(realm, console, output, "warn");
        InstallMethod(realm, console, output, "error");
        InstallMethod(realm, console, output, "debug");
        realm.GlobalObject.DefineDataProperty(
            "console",
            JsValue.FromObject(console),
            JsShapePropertyFlags.Writable | JsShapePropertyFlags.Configurable
        );
    }

    private static void InstallMethod(
        JsRealm realm,
        JsPlainObject console,
        Action<string, string>? output,
        string name
    )
    {
        var method = new JsHostFunction(
            realm,
            (in info) =>
            {
                var text = FormatConsoleArguments(realm, info.Arguments);
                if (output is null)
                    Console.Error.WriteLine(string.IsNullOrEmpty(text) ? name : text);
                else
                    output(
                        name is "warn" or "error" ? "stderr" : "stdout",
                        text + Environment.NewLine
                    );
                return JsValue.Undefined;
            },
            name,
            0,
            isConstructor: false
        );

        console.DefineDataProperty(
            name,
            JsValue.FromObject(method),
            JsShapePropertyFlags.Writable | JsShapePropertyFlags.Configurable
        );
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
