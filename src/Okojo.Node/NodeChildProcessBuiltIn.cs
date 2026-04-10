using System.Diagnostics;
using System.Text;
using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Node;

internal sealed class NodeChildProcessBuiltIn(NodeRuntime runtime)
{
    private const int ExecFileSyncSlot = 0;
    private const int ExecSyncSlot = 1;

    private int atomExecFileSync = -1;
    private int atomExecSync = -1;
    private JsPlainObject? moduleObject;
    private StaticNamedPropertyLayout? moduleShape;

    public JsPlainObject GetModule()
    {
        if (moduleObject is not null)
            return moduleObject;

        var realm = runtime.MainRealm;
        var shape = moduleShape ??= CreateModuleShape(realm);
        var module = new JsPlainObject(shape);
        module.SetNamedSlotUnchecked(ExecFileSyncSlot, JsValue.FromObject(CreateExecFileSyncFunction(realm)));
        module.SetNamedSlotUnchecked(ExecSyncSlot, JsValue.FromObject(CreateExecSyncFunction(realm)));
        moduleObject = module;
        return module;
    }

    private StaticNamedPropertyLayout CreateModuleShape(JsRealm realm)
    {
        EnsureAtoms(realm);
        var shape = realm.EmptyShape.GetOrAddTransition(atomExecFileSync, JsShapePropertyFlags.Open,
            out var execFileSyncInfo);
        shape = shape.GetOrAddTransition(atomExecSync, JsShapePropertyFlags.Open, out var execSyncInfo);
        Debug.Assert(execFileSyncInfo.Slot == ExecFileSyncSlot);
        Debug.Assert(execSyncInfo.Slot == ExecSyncSlot);
        return shape;
    }

    private void EnsureAtoms(JsRealm realm)
    {
        atomExecFileSync = EnsureAtom(realm, atomExecFileSync, "execFileSync");
        atomExecSync = EnsureAtom(realm, atomExecSync, "execSync");
    }

    private static int EnsureAtom(JsRealm realm, int atom, string text)
    {
        return atom >= 0 ? atom : realm.Atoms.InternNoCheck(text);
    }

    private static JsHostFunction CreateExecFileSyncFunction(JsRealm realm)
    {
        return new(realm, "execFileSync", 3, static (in info) =>
        {
            var fileName = info.GetArgumentString(0);
            var args = info.Arguments.Length > 1 ? ReadArgumentList(info.Realm, info.Arguments[1]) : [];
            var options = info.Arguments.Length > 2
                ? ReadOptions(info.Realm, info.Arguments[2])
                : ChildProcessOptions.Default;
            return ExecuteSync(info.Realm, fileName, args, options);
        }, false);
    }

    private static JsHostFunction CreateExecSyncFunction(JsRealm realm)
    {
        return new(realm, "execSync", 2, static (in info) =>
        {
            var command = info.GetArgumentString(0);
            var options = info.Arguments.Length > 1
                ? ReadOptions(info.Realm, info.Arguments[1])
                : ChildProcessOptions.Default;
            options = options with { Shell = true };
            return ExecuteSync(info.Realm, command, [], options);
        }, false);
    }

    private static JsValue ExecuteSync(JsRealm realm, string fileName, string[] args, ChildProcessOptions options)
    {
        using var process = new Process();
        process.StartInfo = CreateStartInfo(fileName, args, options);
        process.Start();

        if (!process.WaitForExit(options.TimeoutMs))
        {
            try
            {
                process.Kill(true);
            }
            catch
            {
            }

            throw new InvalidOperationException(
                $"child_process command timed out after {options.TimeoutMs} ms: {fileName}");
        }

        var stdoutText = process.StandardOutput.ReadToEnd();
        if (!string.IsNullOrEmpty(options.Encoding) &&
            !string.Equals(options.Encoding, "buffer", StringComparison.Ordinal))
            return JsValue.FromString(stdoutText);

        return JsValue.FromString(stdoutText);
    }

    private static ProcessStartInfo CreateStartInfo(string fileName, string[] args, ChildProcessOptions options)
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (options.Shell)
        {
            if (OperatingSystem.IsWindows())
            {
                startInfo.FileName = "cmd.exe";
                startInfo.ArgumentList.Add("/d");
                startInfo.ArgumentList.Add("/s");
                startInfo.ArgumentList.Add("/c");
                startInfo.ArgumentList.Add(BuildCommandString(fileName, args));
            }
            else
            {
                startInfo.FileName = "/bin/sh";
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add(BuildCommandString(fileName, args));
            }
        }
        else
        {
            startInfo.FileName = fileName;
            foreach (var arg in args)
                startInfo.ArgumentList.Add(arg);
        }

        if (options.EnvironmentVariables is not null)
        {
            startInfo.Environment.Clear();
            foreach (var pair in options.EnvironmentVariables)
                startInfo.Environment[pair.Key] = pair.Value;
        }

        return startInfo;
    }

    private static string BuildCommandString(string fileName, string[] args)
    {
        if (args.Length == 0)
            return fileName;

        var builder = new StringBuilder(fileName.Length + args.Sum(static x => x.Length + 1));
        builder.Append(fileName);
        for (var i = 0; i < args.Length; i++)
        {
            builder.Append(' ');
            builder.Append(args[i]);
        }

        return builder.ToString();
    }

    private static string[] ReadArgumentList(JsRealm realm, in JsValue value)
    {
        if (!value.TryGetObject(out var obj))
            return [];

        var length = realm.GetArrayLikeLengthLong(obj);
        if (length <= 0)
            return [];

        var args = new string[length];
        for (var i = 0; i < length; i++)
            if (obj.TryGetProperty(i.ToString(), out var element))
                args[i] = element.IsString ? element.AsString() : realm.ToJsStringSlowPath(element);
            else
                args[i] = string.Empty;

        return args;
    }

    private static ChildProcessOptions ReadOptions(JsRealm realm, in JsValue value)
    {
        if (!value.TryGetObject(out var obj))
            return ChildProcessOptions.Default;

        string? encoding = null;
        var timeoutMs = Timeout.Infinite;
        var shell = false;
        Dictionary<string, string>? env = null;

        if (obj.TryGetProperty("encoding", out var encodingValue) && !encodingValue.IsUndefined &&
            !encodingValue.IsNull)
            encoding = encodingValue.IsString ? encodingValue.AsString() : realm.ToJsStringSlowPath(encodingValue);

        if (obj.TryGetProperty("timeout", out var timeoutValue) && !timeoutValue.IsUndefined)
        {
            var timeoutNumber = realm.ToIntegerOrInfinity(timeoutValue);
            if (!double.IsNaN(timeoutNumber) && timeoutNumber >= 0 && !double.IsInfinity(timeoutNumber))
                timeoutMs = (int)Math.Min(int.MaxValue, timeoutNumber);
        }

        if (obj.TryGetProperty("shell", out var shellValue) && !shellValue.IsUndefined && !shellValue.IsNull)
            shell = shellValue.IsTrue || (shellValue.IsString && shellValue.AsString().Length != 0);

        if (obj.TryGetProperty("env", out var envValue) && envValue.TryGetObject(out var envObj))
        {
            env = [];
            var keysValue = realm.InvokeObjectConstructorMethod("keys", [JsValue.FromObject(envObj)]);
            if (keysValue.TryGetObject(out var keysObj))
            {
                var length = realm.GetArrayLikeLengthLong(keysObj);
                for (var i = 0; i < length; i++)
                {
                    if (!keysObj.TryGetProperty(i.ToString(), out var keyValue))
                        continue;
                    var key = keyValue.IsString ? keyValue.AsString() : realm.ToJsStringSlowPath(keyValue);
                    if (!envObj.TryGetProperty(key, out var propertyValue))
                        continue;
                    env[key] = propertyValue.IsString
                        ? propertyValue.AsString()
                        : realm.ToJsStringSlowPath(propertyValue);
                }
            }
        }

        return new(encoding, timeoutMs, shell, env);
    }

    private readonly record struct ChildProcessOptions(
        string? Encoding,
        int TimeoutMs,
        bool Shell,
        Dictionary<string, string>? EnvironmentVariables)
    {
        public static ChildProcessOptions Default { get; } =
            new("utf8", Timeout.Infinite, false, null);
    }
}
