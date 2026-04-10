using System.Text;

namespace Okojo.Repl;

public readonly record struct ReplConsoleLayout(string Text, int CursorRow, int CursorColumn, int TotalRows)
{
    private const string ConsoleNewLine = "\r\n";

    public static ReplConsoleLayout Create(
        string input,
        int cursorIndex,
        int width,
        string primaryPrompt = "> ",
        string continuationPrompt = "| ")
    {
        width = Math.Max(20, width);
        var builder = new StringBuilder();
        var row = 0;
        var col = 0;
        int? cursorRow = null;
        int? cursorColumn = null;

        AppendPrompt(builder, ref row, ref col, primaryPrompt, width);
        if (cursorIndex == 0)
        {
            cursorRow = row;
            cursorColumn = col;
        }

        for (var i = 0; i < input.Length; i++)
        {
            var ch = input[i];
            if (ch == '\n')
            {
                builder.Append(ConsoleNewLine);
                row++;
                col = 0;
                AppendPrompt(builder, ref row, ref col, continuationPrompt, width);
            }
            else
            {
                builder.Append(ch);
                Advance(ref row, ref col, 1, width);
            }

            if (i + 1 == cursorIndex)
            {
                cursorRow = row;
                cursorColumn = col;
            }
        }

        return new(builder.ToString(), cursorRow ?? row, cursorColumn ?? col, row + 1);
    }

    private static void AppendPrompt(StringBuilder builder, ref int row, ref int col, string prompt, int width)
    {
        builder.Append(prompt);
        Advance(ref row, ref col, prompt.Length, width);
    }

    private static void Advance(ref int row, ref int col, int count, int width)
    {
        var absolute = col + count;
        row += absolute / width;
        col = absolute % width;
    }
}
