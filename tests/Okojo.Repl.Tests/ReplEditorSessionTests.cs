using System.Text;

namespace Okojo.Repl.Tests;

public class ReplEditorSessionTests
{
    [Test]
    public void Session_Uses_Node_Style_Continuation_Prompt_And_Tracks_Cursor()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "Okojo.Repl.Tests", Guid.NewGuid().ToString("N"));
        var historyPath = Path.Combine(tempRoot, "repl-history.json");

        try
        {
            using var history = ReplHistoryStore.Load(historyPath);
            var session = new ReplEditorSession(input => ReplInputParser.IsInputComplete(input), history);
            var console = new FakeReplConsole();

            session.Render(console);
            _ = session.HandleKey(Key('{'), out _);
            session.Render(console);
            _ = session.HandleKey(Enter(), out _);
            session.Render(console);

            Assert.That(console.GetDisplayText(), Does.Contain("> {"));
            Assert.That(console.GetDisplayText(), Does.Contain("\n|"));
            Assert.That(console.CursorTop, Is.EqualTo(1));
            Assert.That(console.CursorLeft, Is.EqualTo(2));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    [Test]
    public void Session_Uses_History_On_Up_And_Down()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "Okojo.Repl.Tests", Guid.NewGuid().ToString("N"));
        var historyPath = Path.Combine(tempRoot, "repl-history.json");

        try
        {
            using var history = ReplHistoryStore.Load(historyPath);
            history.Record("first()");
            history.Record("second()");

            var session = new ReplEditorSession(input => ReplInputParser.IsInputComplete(input), history);
            _ = session.HandleKey(Key('w'), out _);
            _ = session.HandleKey(Key('i'), out _);
            _ = session.HandleKey(Key('p'), out _);

            _ = session.HandleKey(Arrow(ConsoleKey.UpArrow), out _);
            Assert.That(session.Buffer.Text, Is.EqualTo("second()"));

            _ = session.HandleKey(Arrow(ConsoleKey.UpArrow), out _);
            Assert.That(session.Buffer.Text, Is.EqualTo("first()"));

            _ = session.HandleKey(Arrow(ConsoleKey.DownArrow), out _);
            Assert.That(session.Buffer.Text, Is.EqualTo("second()"));

            _ = session.HandleKey(Arrow(ConsoleKey.DownArrow), out _);
            Assert.That(session.Buffer.Text, Is.EqualTo("wip"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    [Test]
    public void Session_Uses_Up_Down_For_Multiline_Cursor_Movement_Before_History()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "Okojo.Repl.Tests", Guid.NewGuid().ToString("N"));
        var historyPath = Path.Combine(tempRoot, "repl-history.json");

        try
        {
            using var history = ReplHistoryStore.Load(historyPath);
            history.Record("history()");

            var session = new ReplEditorSession(input => ReplInputParser.IsInputComplete(input), history);
            session.Buffer.SetText("line1\nline2");

            _ = session.HandleKey(Arrow(ConsoleKey.UpArrow), out _);
            Assert.That(session.Buffer.Text, Is.EqualTo("line1\nline2"));
            Assert.That(session.Buffer.CursorIndex, Is.EqualTo(5));

            _ = session.HandleKey(Arrow(ConsoleKey.DownArrow), out _);
            Assert.That(session.Buffer.Text, Is.EqualTo("line1\nline2"));
            Assert.That(session.Buffer.CursorIndex, Is.EqualTo(11));

            _ = session.HandleKey(Arrow(ConsoleKey.UpArrow), out _);
            Assert.That(session.Buffer.CursorIndex, Is.EqualTo(5));

            _ = session.HandleKey(Arrow(ConsoleKey.UpArrow), out _);
            Assert.That(session.Buffer.Text, Is.EqualTo("history()"));

            _ = session.HandleKey(Arrow(ConsoleKey.DownArrow), out _);
            Assert.That(session.Buffer.Text, Is.EqualTo("line1\nline2"));
            Assert.That(session.Buffer.CursorIndex, Is.EqualTo(11));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    [Test]
    public void HistoryStore_Persists_Multiline_Entries()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "Okojo.Repl.Tests", Guid.NewGuid().ToString("N"));
        var historyPath = Path.Combine(tempRoot, "repl-history.json");

        try
        {
            using (var store = ReplHistoryStore.Load(historyPath))
            {
                store.Record("line1()\nline2()");
            }

            using var reloaded = ReplHistoryStore.Load(historyPath);
            Assert.That(reloaded.TryMovePrevious(string.Empty, out var entry), Is.True);
            Assert.That(entry, Is.EqualTo("line1()\nline2()"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    [Test]
    public void Session_Rendering_Multiline_History_Does_Not_Overwrite_Previous_Content()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "Okojo.Repl.Tests", Guid.NewGuid().ToString("N"));
        var historyPath = Path.Combine(tempRoot, "repl-history.json");

        try
        {
            using var history = ReplHistoryStore.Load(historyPath);
            history.Record("line1()\nline2()");

            var session = new ReplEditorSession(input => ReplInputParser.IsInputComplete(input), history);
            var console = new FakeReplConsole();

            session.Render(console);
            _ = session.HandleKey(Key('x'), out _);
            session.Render(console);
            _ = session.HandleKey(Arrow(ConsoleKey.UpArrow), out _);
            session.Render(console);

            Assert.That(console.GetDisplayText(), Is.EqualTo("> line1()\n| line2()"));
            Assert.That(console.CursorTop, Is.EqualTo(1));
            Assert.That(console.CursorLeft, Is.EqualTo(9));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    [Test]
    public void Session_Rendering_Multiline_History_Preserves_Preexisting_Output_Above_Prompt()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "Okojo.Repl.Tests", Guid.NewGuid().ToString("N"));
        var historyPath = Path.Combine(tempRoot, "repl-history.json");

        try
        {
            using var history = ReplHistoryStore.Load(historyPath);
            history.Record("function f(v){\n   console.log(vvvv);\n}");

            var session = new ReplEditorSession(input => ReplInputParser.IsInputComplete(input), history);
            var console = new FakeReplConsole();

            console.Write("Welcome to okojonode 0.1.0-local.");
            console.WriteLine();
            console.Write("Type \".help\" for more information.");
            console.WriteLine();

            session.Render(console);
            _ = session.HandleKey(Arrow(ConsoleKey.UpArrow), out _);
            session.Render(console);

            Assert.That(
                console.GetDisplayText(),
                Is.EqualTo(
                    "Welcome to okojonode 0.1.0-local.\n" +
                    "Type \".help\" for more information.\n" +
                    "> function f(v){\n" +
                    "|    console.log(vvvv);\n" +
                    "| }"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    [Test]
    public void Session_Rendering_Multiline_History_Preserves_Output_When_Buffer_Is_Tight()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "Okojo.Repl.Tests", Guid.NewGuid().ToString("N"));
        var historyPath = Path.Combine(tempRoot, "repl-history.json");

        try
        {
            using var history = ReplHistoryStore.Load(historyPath);
            history.Record("function f(v){\n   console.log(vvvv);\n}");

            var session = new ReplEditorSession(input => ReplInputParser.IsInputComplete(input), history);
            var console = new FakeReplConsole
            {
                BufferHeight = 4
            };

            console.Write("Welcome to okojonode 0.1.0-local.");
            console.WriteLine();
            console.Write("Type \".help\" for more information.");
            console.WriteLine();

            session.Render(console);
            _ = session.HandleKey(Arrow(ConsoleKey.UpArrow), out _);
            session.Render(console);

            Assert.That(
                console.GetDisplayText(),
                Is.EqualTo(
                    "Type \".help\" for more information.\n" +
                    "> function f(v){\n" +
                    "|    console.log(vvvv);\n" +
                    "| }"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    [Test]
    public void Session_Render_Clamps_To_Buffer_Height()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "Okojo.Repl.Tests", Guid.NewGuid().ToString("N"));
        var historyPath = Path.Combine(tempRoot, "repl-history.json");

        try
        {
            using var history = ReplHistoryStore.Load(historyPath);
            var session = new ReplEditorSession(input => ReplInputParser.IsInputComplete(input), history);
            var console = new FakeReplConsole
            {
                BufferWidth = 40,
                BufferHeight = 3
            };

            console.SetCursorPosition(0, 2);
            session.Render(console);
            _ = session.HandleKey(Key('{'), out _);
            session.Render(console);
            _ = session.HandleKey(Enter(), out _);

            Assert.DoesNotThrow(() => session.Render(console));
            Assert.That(console.CursorTop, Is.InRange(0, 2));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    private static ConsoleKeyInfo Key(char value)
    {
        return new(value, 0, false, false, false);
    }

    private static ConsoleKeyInfo Enter()
    {
        return new('\r', ConsoleKey.Enter, false, false, false);
    }

    private static ConsoleKeyInfo Arrow(ConsoleKey key)
    {
        return new('\0', key, false, false, false);
    }

    private sealed class FakeReplConsole : IReplConsole
    {
        private readonly Dictionary<int, StringBuilder> rows = new();

        public int BufferWidth { get; set; } = 80;

        public int BufferHeight { get; set; } = 300;

        public int CursorLeft { get; private set; }

        public int CursorTop { get; private set; }

        public bool KeyAvailable => false;

        public bool CursorVisible { get; set; } = true;

        public ConsoleKeyInfo ReadKey(bool intercept)
        {
            throw new NotSupportedException();
        }

        public void SetCursorPosition(int left, int top)
        {
            if (left < 0 || left >= BufferWidth)
                throw new ArgumentOutOfRangeException(nameof(left));
            if (top < 0 || top >= BufferHeight)
                throw new ArgumentOutOfRangeException(nameof(top));
            CursorLeft = left;
            CursorTop = top;
        }

        public void Write(string text)
        {
            foreach (var ch in text)
            {
                if (ch == '\r')
                    continue;
                if (ch == '\n')
                {
                    MoveToNextLine();
                    CursorLeft = 0;
                    continue;
                }

                var row = rows.GetValueOrDefault(CursorTop);
                if (row is null)
                {
                    row = new();
                    rows[CursorTop] = row;
                }

                while (row.Length <= CursorLeft)
                    row.Append(' ');
                row[CursorLeft] = ch;
                CursorLeft++;
            }
        }

        public void WriteLine()
        {
            MoveToNextLine();
            CursorLeft = 0;
        }

        private void MoveToNextLine()
        {
            if (CursorTop < BufferHeight - 1)
            {
                CursorTop++;
                return;
            }

            var shifted = new Dictionary<int, StringBuilder>();
            foreach (var pair in rows)
            {
                if (pair.Key == 0)
                    continue;

                shifted[pair.Key - 1] = pair.Value;
            }

            rows.Clear();
            foreach (var pair in shifted)
                rows[pair.Key] = pair.Value;

            CursorTop = Math.Max(0, BufferHeight - 1);
        }

        public string GetDisplayText()
        {
            if (rows.Count == 0)
                return string.Empty;

            var maxRow = rows.Keys.Max();
            var builder = new StringBuilder();
            for (var i = 0; i <= maxRow; i++)
            {
                if (i != 0)
                    builder.Append('\n');
                if (rows.TryGetValue(i, out var row))
                    builder.Append(row.ToString().TrimEnd());
            }

            return builder.ToString();
        }
    }
}
