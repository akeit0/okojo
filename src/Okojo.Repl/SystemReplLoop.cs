using System.Text;

namespace Okojo.Repl;

public sealed class ReplLoopOptions
{
    public required Func<string, Task<bool>> HandleInputAsync { get; init; }
    public required Func<string, bool> IsInputComplete { get; init; }
    public required Func<bool> PumpTurn { get; init; }
    public ReplHistoryStore? History { get; init; }
    public IReplConsole? Console { get; init; }
    public string PrimaryPrompt { get; init; } = "> ";
    public string ContinuationPrompt { get; init; } = "| ";
    public string InterruptMessage { get; init; } = "To exit, press Ctrl+C again or Ctrl+D or type .exit";
}

public static class SystemReplLoop
{
    public static async Task RunAsync(ReplLoopOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var history = options.History ?? ReplHistoryStore.CreateEphemeral();
        while (true)
        {
            var input = ReadInput(options, history);
            if (input is null)
                return;

            if (!await options.HandleInputAsync(input).ConfigureAwait(false))
                return;
        }
    }

    private static string? ReadInput(ReplLoopOptions options, ReplHistoryStore history)
    {
        return Console.IsInputRedirected
            ? ReadRedirectedInput(options)
            : ReadInteractiveInput(options, history);
    }

    private static string? ReadRedirectedInput(ReplLoopOptions options)
    {
        var builder = new StringBuilder();
        while (true)
        {
            var line = ReadRedirectedConsoleLine(options);
            if (line is null)
                return builder.Length == 0 ? null : builder.ToString();

            if (builder.Length != 0)
                builder.AppendLine();
            builder.Append(line);

            if (options.IsInputComplete(builder.ToString()))
                return builder.ToString();
        }
    }

    private static string? ReadInteractiveInput(ReplLoopOptions options, ReplHistoryStore history)
    {
        var console = options.Console ?? new SystemReplConsole();
        var session = new ReplEditorSession(
            options.IsInputComplete,
            history,
            options.PrimaryPrompt,
            options.ContinuationPrompt);
        var exitRequested = false;
        var interruptRequested = false;
        var pendingExitConfirmation = false;
        ConsoleCancelEventHandler? cancelHandler = null;
        cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            if (pendingExitConfirmation)
            {
                exitRequested = true;
                return;
            }

            pendingExitConfirmation = true;
            interruptRequested = true;
        };

        Console.CancelKeyPress += cancelHandler;
        session.Render(console);
        try
        {
            while (true)
            {
                if (exitRequested)
                    return null;

                if (interruptRequested)
                {
                    interruptRequested = false;
                    session = new(
                        options.IsInputComplete,
                        history,
                        options.PrimaryPrompt,
                        options.ContinuationPrompt);
                    console.WriteLine();
                    Console.WriteLine(options.InterruptMessage);
                    session.Render(console);
                }

                _ = options.PumpTurn();

                while (console.KeyAvailable)
                {
                    var key = console.ReadKey(true);
                    if ((key.Modifiers & ConsoleModifiers.Control) != 0 &&
                        key.Key == ConsoleKey.D)
                        return null;

                    pendingExitConfirmation = false;
                    if (session.HandleKey(key, out var submitted))
                    {
                        if (!string.IsNullOrWhiteSpace(submitted) &&
                            !submitted.StartsWith(".", StringComparison.Ordinal))
                            history.Record(submitted);
                        console.WriteLine();
                        return submitted;
                    }

                    session.Render(console);
                }
            }
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static string? ReadRedirectedConsoleLine(ReplLoopOptions options)
    {
        var readTask = Task.Run(Console.In.ReadLine);
        while (!readTask.Wait(10))
            _ = options.PumpTurn();
        return readTask.GetAwaiter().GetResult();
    }
}
