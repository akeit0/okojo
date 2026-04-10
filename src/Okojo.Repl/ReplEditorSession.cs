namespace Okojo.Repl;

public sealed class ReplEditorSession(
    Func<string, bool> isInputComplete,
    ReplHistoryStore history,
    string primaryPrompt = "> ",
    string continuationPrompt = "| ")
{
    private int? desiredColumn;
    private bool hasOrigin;
    private int originTop;
    private int previousRows;

    public ReplEditorBuffer Buffer { get; } = new();

    public void Render(IReplConsole console)
    {
        if (!hasOrigin)
        {
            originTop = console.CursorTop;
            hasOrigin = true;
        }

        var width = Math.Max(20, console.BufferWidth);
        var bufferHeight = Math.Max(1, console.BufferHeight);
        var layout =
            ReplConsoleLayout.Create(Buffer.Text, Buffer.CursorIndex, width, primaryPrompt, continuationPrompt);
        var rowsToClear = Math.Min(
            Math.Max(previousRows, layout.TotalRows),
            Math.Max(1, bufferHeight - originTop));

        var cursorVisible = console.CursorVisible;
        console.CursorVisible = false;
        try
        {
            for (var i = 0; i < rowsToClear; i++)
            {
                console.SetCursorPosition(0, originTop + i);
                console.Write(new(' ', Math.Max(1, width - 1)));
            }

            console.SetCursorPosition(0, originTop);
            var scrollRows = Math.Max(0, originTop + layout.TotalRows - bufferHeight);
            console.Write(layout.Text);
            if (scrollRows != 0)
                originTop = Math.Max(0, originTop - scrollRows);
            console.SetCursorPosition(layout.CursorColumn, originTop + layout.CursorRow);
            previousRows = layout.TotalRows;
        }
        finally
        {
            console.CursorVisible = cursorVisible;
        }
    }

    public bool HandleKey(ConsoleKeyInfo key, out string? submitted)
    {
        submitted = null;
        switch (key.Key)
        {
            case ConsoleKey.Enter:
                desiredColumn = null;
                if (isInputComplete(Buffer.Text))
                {
                    submitted = Buffer.Text;
                    return true;
                }

                Buffer.Insert('\n');
                return false;
            case ConsoleKey.Backspace:
                desiredColumn = null;
                Buffer.Backspace();
                return false;
            case ConsoleKey.Delete:
                desiredColumn = null;
                Buffer.Delete();
                return false;
            case ConsoleKey.LeftArrow:
                desiredColumn = null;
                Buffer.MoveLeft();
                return false;
            case ConsoleKey.RightArrow:
                desiredColumn = null;
                Buffer.MoveRight();
                return false;
            case ConsoleKey.Home:
                desiredColumn = null;
                Buffer.MoveToLineStart();
                return false;
            case ConsoleKey.End:
                desiredColumn = null;
                Buffer.MoveToLineEnd();
                return false;
            case ConsoleKey.UpArrow:
                desiredColumn ??= Buffer.GetCurrentColumn();
                if (Buffer.TryMoveUp(desiredColumn.Value))
                    return false;
                if (history.TryMovePrevious(Buffer.Text, out var previous))
                    Buffer.SetText(previous);
                return false;
            case ConsoleKey.DownArrow:
                desiredColumn ??= Buffer.GetCurrentColumn();
                if (Buffer.TryMoveDown(desiredColumn.Value))
                    return false;
                if (history.TryMoveNext(out var next))
                    Buffer.SetText(next);
                return false;
            default:
                desiredColumn = null;
                if (key.KeyChar == '\u0000' || char.IsControl(key.KeyChar))
                    return false;
                Buffer.Insert(key.KeyChar);
                return false;
        }
    }
}
