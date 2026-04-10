using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace Okojo.Node.Tests;

internal sealed class NodeCliProcess : IAsyncDisposable
{
    private readonly CancellationTokenSource cancellationTokenSource = new();
    private readonly Process process;
    private readonly StringBuilder stderrBuilder = new();
    private readonly object stderrGate = new();
    private readonly BlockingCollection<string> stderrLines = new();
    private readonly Task stderrPump;
    private readonly StringBuilder stdoutBuilder = new();
    private readonly object stdoutGate = new();
    private readonly BlockingCollection<string> stdoutLines = new();
    private readonly Task stdoutPump;

    private NodeCliProcess(Process process)
    {
        this.process = process;
        stdoutPump = PumpAsync(process.StandardOutput, stdoutLines, stdoutBuilder, stdoutGate,
            cancellationTokenSource.Token);
        stderrPump = PumpAsync(process.StandardError, stderrLines, stderrBuilder, stderrGate,
            cancellationTokenSource.Token);
    }

    public async ValueTask DisposeAsync()
    {
        cancellationTokenSource.Cancel();

        try
        {
            process.StandardInput.Close();
        }
        catch
        {
        }

        if (!process.HasExited)
            try
            {
                process.Kill(true);
            }
            catch
            {
            }

        stdoutLines.CompleteAdding();
        stderrLines.CompleteAdding();
        await Task.WhenAll(stdoutPump, stderrPump).ConfigureAwait(false);

        process.Dispose();
        cancellationTokenSource.Dispose();
        stdoutLines.Dispose();
        stderrLines.Dispose();
    }

    public static NodeCliProcess Start(params string[] args)
    {
        var repoRoot = FindRepoRoot();
        var configuration = GetBuildConfiguration();
        var targetFramework = GetTargetFramework();
        var cliDll = Path.Combine(repoRoot, "src", "Okojo.Node.Cli", "bin", configuration, targetFramework,
            "okojonode.dll");
        if (!File.Exists(cliDll))
            throw new FileNotFoundException($"Expected Okojo.Node.Cli build output at '{cliDll}'.", cliDll);

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(cliDll);
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        var process = Process.Start(startInfo) ??
                      throw new InvalidOperationException("Failed to start Okojo.Node.Cli.");
        return new(process);
    }

    public void SendCommand(string text)
    {
        process.StandardInput.WriteLine(text);
        process.StandardInput.Flush();
    }

    public async Task<string> WaitForStdoutLineAsync(Func<string, bool> predicate, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var remaining = deadline - DateTimeOffset.UtcNow;
            var line = ReadLine(stdoutLines, remaining);
            if (line is null)
                break;
            if (predicate(line))
                return line;
        }

        throw new TimeoutException(
            $"Timed out waiting for stdout line. Current stdout:{Environment.NewLine}{GetStdout()}");
    }

    public async Task WaitForExitAsync(TimeSpan timeout)
    {
        var waitTask = process.WaitForExitAsync();
        if (await Task.WhenAny(waitTask, Task.Delay(timeout)).ConfigureAwait(false) != waitTask)
            throw new TimeoutException("Okojo.Node.Cli did not exit within the timeout.");
        await waitTask.ConfigureAwait(false);
    }

    public string GetStdout()
    {
        lock (stdoutGate)
        {
            return stdoutBuilder.ToString();
        }
    }

    public string GetStderr()
    {
        lock (stderrGate)
        {
            return stderrBuilder.ToString();
        }
    }

    private static async Task PumpAsync(
        TextReader reader,
        BlockingCollection<string> lines,
        StringBuilder builder,
        object gate,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                    break;

                lock (gate)
                {
                    builder.AppendLine(line);
                }

                lines.Add(line, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            lines.CompleteAdding();
        }
    }

    private static string? ReadLine(BlockingCollection<string> lines, TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            return null;

        return lines.TryTake(out var line, (int)Math.Min(timeout.TotalMilliseconds, int.MaxValue))
            ? line
            : null;
    }

    private static string FindRepoRoot()
    {
        var current = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                      ?? throw new InvalidOperationException("Unable to resolve test assembly path.");
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Okojo.slnx")))
                return current;

            current = Path.GetDirectoryName(current) ?? string.Empty;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static string GetBuildConfiguration()
    {
        var directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                        ?? throw new InvalidOperationException("Unable to resolve test assembly path.");
        return Directory.GetParent(directory)?.Name
               ?? throw new InvalidOperationException("Could not determine test build configuration.");
    }

    private static string GetTargetFramework()
    {
        return Path.GetFileName(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location))
               ?? throw new InvalidOperationException("Could not determine target framework.");
    }
}
