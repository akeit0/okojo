using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace Okojo.DebugServer.Tests;

internal sealed class DebugServerProcess : IAsyncDisposable
{
    private readonly Process process;
    private readonly CancellationTokenSource cancellationTokenSource = new();
    private readonly Task stdoutPump;
    private readonly Task stderrPump;
    private readonly BlockingCollection<string> stdoutLines = new();
    private readonly BlockingCollection<string> stderrLines = new();

    private DebugServerProcess(Process process)
    {
        this.process = process;
        stdoutPump = PumpAsync(process.StandardOutput, stdoutLines, cancellationTokenSource.Token);
        stderrPump = PumpAsync(process.StandardError, stderrLines, cancellationTokenSource.Token);
    }

    public static DebugServerProcess Start(string scriptPath, IReadOnlyList<string> extraArgs)
    {
        string repoRoot = FindRepoRoot();
        string project = Path.GetFullPath(Path.Combine(repoRoot, "src", "Okojo.DebugServer", "Okojo.DebugServer.csproj"));

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(project);
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("--script");
        startInfo.ArgumentList.Add(scriptPath);

        foreach (var arg in extraArgs)
            startInfo.ArgumentList.Add(arg);

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start debug server.");
        return new DebugServerProcess(process);
    }

    public void SendCommand(string command)
    {
        process.StandardInput.WriteLine(command);
        process.StandardInput.Flush();
    }

    public async Task<JsonElement> WaitForJsonEventAsync(string eventName, TimeSpan timeout)
        => await Task.FromResult(WaitForJsonEvent(eventName, timeout)).ConfigureAwait(false);

    public async Task WaitForExitAsync(TimeSpan timeout)
    {
        var waitTask = process.WaitForExitAsync();
        if (await Task.WhenAny(waitTask, Task.Delay(timeout)).ConfigureAwait(false) != waitTask)
            throw new TimeoutException("Debug server process did not exit within the timeout.");
        await waitTask.ConfigureAwait(false);
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
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
        }

        stdoutLines.CompleteAdding();
        stderrLines.CompleteAdding();
        await Task.WhenAll(stdoutPump, stderrPump).ConfigureAwait(false);

        process.Dispose();
        cancellationTokenSource.Dispose();
        stdoutLines.Dispose();
    }

    public string GetStderr()
    {
        return string.Join(Environment.NewLine, stderrLines.ToArray());
    }

    private JsonElement WaitForJsonEvent(string eventName, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var remaining = deadline - DateTimeOffset.UtcNow;
            var line = ReadLine(remaining);
            if (line is null)
                break;

            if (TryParseEvent(line, out var payload) &&
                string.Equals(GetString(payload, "event"), eventName, StringComparison.Ordinal))
            {
                return payload;
            }
        }

        throw new TimeoutException(BuildTimeoutMessage(eventName));
    }

    private static async Task PumpAsync(TextReader reader, BlockingCollection<string> lines, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                    break;

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

    private string? ReadLine(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            return null;

        try
        {
            return stdoutLines.TryTake(out var line, (int)Math.Min(timeout.TotalMilliseconds, int.MaxValue),
                cancellationTokenSource.Token)
                ? line
                : null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static bool TryParseEvent(string line, out JsonElement payload)
    {
        payload = default;
        if (!line.TrimStart().StartsWith('{'))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(line);
            if (!doc.RootElement.TryGetProperty("event", out _))
                return false;

            payload = doc.RootElement.Clone();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetString(JsonElement payload, string propertyName)
    {
        return payload.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private string BuildTimeoutMessage(string eventName)
    {
        var stderr = GetStderr();
        return string.IsNullOrWhiteSpace(stderr)
            ? $"Timed out waiting for debug server event '{eventName}'."
            : $"Timed out waiting for debug server event '{eventName}'. Stderr:{Environment.NewLine}{stderr}";
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "src", "Okojo.DebugServer", "Okojo.DebugServer.csproj");
            if (File.Exists(candidate))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }
}
