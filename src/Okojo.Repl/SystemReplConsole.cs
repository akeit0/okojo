namespace Okojo.Repl;

public sealed class SystemReplConsole : IReplConsole
{
    public int BufferWidth => Console.BufferWidth;

    public int BufferHeight => Console.BufferHeight;

    public int CursorLeft => Console.CursorLeft;

    public int CursorTop => Console.CursorTop;

    public bool KeyAvailable => Console.KeyAvailable;

    public bool CursorVisible
    {
        get => !OperatingSystem.IsWindows() || Console.CursorVisible;
        set
        {
            if (OperatingSystem.IsWindows())
                Console.CursorVisible = value;
        }
    }

    public ConsoleKeyInfo ReadKey(bool intercept)
    {
        return Console.ReadKey(intercept);
    }

    public void SetCursorPosition(int left, int top)
    {
        Console.SetCursorPosition(left, top);
    }

    public void Write(string text)
    {
        Console.Write(text);
    }

    public void WriteLine()
    {
        Console.WriteLine();
    }
}
