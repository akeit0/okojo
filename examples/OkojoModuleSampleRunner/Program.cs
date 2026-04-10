using Okojo;
using Okojo.Objects;
using Okojo.Runtime;

var engine = JsRuntime.Create();
var agent = engine.MainAgent;
var realm = agent.MainRealm;
InstallConsole(realm);

var defaultEntryPath = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory,
    "..", "..", "..", "..", "..",
    "examples", "OkojoModuleSample", "node-demo.mjs"));

var entryPath = Path.GetFullPath(args.Length > 0 ? args[0] : defaultEntryPath);

_ = agent.Modules.Evaluate(realm, entryPath);

static void InstallConsole(JsRealm realm)
{
    var consoleObject = new JsPlainObject(realm);
    var logFunction = new JsHostFunction(realm, "log", 1, static (in info) =>
    {
        var args = info.Arguments;
        if (args.Length == 0)
        {
            Console.WriteLine();
            return JsValue.Undefined;
        }

        var parts = new string[args.Length];
        for (var i = 0; i < args.Length; i++)
            if (args[i].TryGetObject(out var obj))
                parts[i] = obj.ToDisplayString(4);
            else
                parts[i] = args[i].ToString();

        Console.WriteLine(string.Join(" ", parts));
        return JsValue.Undefined;
    });

    consoleObject.DefineDataProperty("log", JsValue.FromObject(logFunction), JsShapePropertyFlags.Open);
    realm.Global["console"] = JsValue.FromObject(consoleObject);
}
