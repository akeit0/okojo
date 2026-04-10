#!/usr/bin/env dotnet
using System.Diagnostics;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: KEY=VALUE ... command [args...]");
    return 1;
}

var env = new Dictionary<string, string>();
int cmdIndex = -1;

// KEY=VALUE の抽出
for (int i = 0; i < args.Length; i++)
{
    var arg = args[i];
    var eqIndex = arg.IndexOf('=');

    if (eqIndex > 0)
    {
        var key = arg.Substring(0, eqIndex);
        var value = arg.Substring(eqIndex + 1);
        env[key] = value;
    }
    else
    {
        cmdIndex = i;
        break;
    }
}

if (cmdIndex < 0)
{
    Console.Error.WriteLine("No command specified.");
    return 1;
}

// コマンドと引数
var command = args[cmdIndex];
var cmdArgs = args.Skip(cmdIndex + 1).ToArray();

// Process設定
var psi = new ProcessStartInfo
{
    FileName = command,
    UseShellExecute = false
};

// 環境変数を設定（既存 + 上書き）
foreach (var kv in env)
{
    psi.Environment[kv.Key] = kv.Value;
    Console.WriteLine($"Set env: {kv.Key}={kv.Value}");
}

// 引数追加
foreach (var a in cmdArgs)
{
    psi.ArgumentList.Add(a);
}

var process = Process.Start(psi);
process!.WaitForExit();

return process.ExitCode;