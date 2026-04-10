namespace Okojo.Repl;

public interface IReplConsole
{
    int BufferWidth { get; }
    int BufferHeight { get; }
    int CursorLeft { get; }
    int CursorTop { get; }
    bool KeyAvailable { get; }
    bool CursorVisible { get; set; }

    ConsoleKeyInfo ReadKey(bool intercept);
    void SetCursorPosition(int left, int top);
    void Write(string text);
    void WriteLine();
}
