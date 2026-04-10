namespace Okojo.Repl;

public sealed class ReplEditorBuffer
{
    public string Text { get; private set; } = string.Empty;

    public int CursorIndex { get; private set; }

    public void SetText(string text)
    {
        Text = text ?? string.Empty;
        CursorIndex = Text.Length;
    }

    public void Insert(char value)
    {
        Text = Text.Insert(CursorIndex, value.ToString());
        CursorIndex++;
    }

    public void Backspace()
    {
        if (CursorIndex == 0)
            return;

        Text = Text.Remove(CursorIndex - 1, 1);
        CursorIndex--;
    }

    public void Delete()
    {
        if (CursorIndex >= Text.Length)
            return;

        Text = Text.Remove(CursorIndex, 1);
    }

    public void MoveLeft()
    {
        if (CursorIndex > 0)
            CursorIndex--;
    }

    public void MoveRight()
    {
        if (CursorIndex < Text.Length)
            CursorIndex++;
    }

    public void MoveToLineStart()
    {
        var start = Text.LastIndexOf('\n', Math.Max(0, CursorIndex - 1));
        CursorIndex = start + 1;
    }

    public void MoveToLineEnd()
    {
        var end = Text.IndexOf('\n', CursorIndex);
        CursorIndex = end >= 0 ? end : Text.Length;
    }

    public int GetCurrentColumn()
    {
        return CursorIndex - GetCurrentLineStart();
    }

    public bool TryMoveUp(int desiredColumn)
    {
        var currentLineStart = GetCurrentLineStart();
        if (currentLineStart == 0)
            return false;

        var previousLineEnd = currentLineStart - 1;
        var previousLineStart = Text.LastIndexOf('\n', Math.Max(0, previousLineEnd - 1)) + 1;
        var previousLineLength = previousLineEnd - previousLineStart;
        CursorIndex = previousLineStart + Math.Min(desiredColumn, previousLineLength);
        return true;
    }

    public bool TryMoveDown(int desiredColumn)
    {
        var currentLineEnd = Text.IndexOf('\n', CursorIndex);
        if (currentLineEnd < 0)
            return false;

        var nextLineStart = currentLineEnd + 1;
        var nextLineEnd = Text.IndexOf('\n', nextLineStart);
        if (nextLineEnd < 0)
            nextLineEnd = Text.Length;
        var nextLineLength = nextLineEnd - nextLineStart;
        CursorIndex = nextLineStart + Math.Min(desiredColumn, nextLineLength);
        return true;
    }

    private int GetCurrentLineStart()
    {
        return Text.LastIndexOf('\n', Math.Max(0, CursorIndex - 1)) + 1;
    }
}
