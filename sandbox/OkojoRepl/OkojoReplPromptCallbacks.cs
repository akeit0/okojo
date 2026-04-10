using Okojo.Parsing;
using PrettyPrompt;
using PrettyPrompt.Completion;
using PrettyPrompt.Consoles;
using PrettyPrompt.Documents;
using PrettyPrompt.Highlighting;

namespace OkojoRepl;

internal sealed class OkojoReplPromptCallbacks : PromptCallbacks
{
    private const int IndentSize = 2;

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "true", "false", "null", "var", "let", "const", "if", "else", "return", "function",
        "for", "while", "do", "break", "continue", "debugger", "typeof", "void", "delete",
        "switch", "case", "default", "throw", "try", "catch", "finally", "with", "in",
        "instanceof", "of", "new", "this", "class", "enum", "extends", "super", "export",
        "import", "await", "async", "yield"
    };

    private static readonly IReadOnlyList<CompletionItem> ReplCommandItems =
    [
        CreateCommandItem("help"),
        CreateCommandItem("q"),
        CreateCommandItem("quit"),
        CreateCommandItem("exit")
    ];

    private readonly KeyBindings keyBindings;
    private readonly bool suggestionsEnabled;

    internal OkojoReplPromptCallbacks(KeyBindings keyBindings, bool suggestionsEnabled = true)
    {
        this.keyBindings = keyBindings;
        this.suggestionsEnabled = suggestionsEnabled;
    }

    private static CompletionItem CreateCommandItem(string name)
    {
        return new(
            name,
            new(name, new FormatSpan(0, name.Length, AnsiColor.Magenta)),
            name);
    }

    protected override Task<KeyPress> TransformKeyPressAsync(
        string text,
        int caret,
        KeyPress keyPress,
        CancellationToken cancellationToken)
    {
        if (keyBindings.NewLine.Matches(keyPress.ConsoleKeyInfo))
        {
            var indentation = GetIndentationString(text, caret);
            return Task.FromResult(indentation.Length == 0
                ? keyPress
                : NewLineWithIndentation(indentation));
        }

        if (keyBindings.SubmitPrompt.Matches(keyPress.ConsoleKeyInfo) &&
            !IsInputComplete(text))
            return Task.FromResult(NewLineWithIndentation(GetIndentationString(text, caret)));

        return base.TransformKeyPressAsync(text, caret, keyPress, cancellationToken);
    }

    protected override Task<(string Text, int Caret)> FormatInput(
        string text,
        int caret,
        KeyPress keyPress,
        CancellationToken cancellationToken)
    {
        if (keyPress.ConsoleKeyInfo.KeyChar != '}')
            return base.FormatInput(text, caret, keyPress, cancellationToken);

        var lineStart = GetLineStart(text, caret);
        var lineEnd = GetLineEnd(text, caret);
        var lineText = text[lineStart..lineEnd];
        var trimmedLine = lineText.Trim();
        if (trimmedLine.Length != 0 && trimmedLine != "}")
            return base.FormatInput(text, caret, keyPress, cancellationToken);

        var level = GetSmartIndentationLevel(text, lineStart);
        var indent = new string(' ', Math.Max(0, level - 1) * IndentSize);
        var newLine = indent + "}";
        var newText = text.Remove(lineStart, lineEnd - lineStart).Insert(lineStart, newLine);
        var newCaret = lineStart + newLine.Length;
        return Task.FromResult((newText, newCaret));
    }

    protected override Task<bool> ShouldOpenCompletionWindowAsync(
        string text,
        int caret,
        KeyPress keyPress,
        CancellationToken cancellationToken)
    {
        if (!suggestionsEnabled)
            return Task.FromResult(false);
        if (char.IsControl(keyPress.ConsoleKeyInfo.KeyChar))
            return Task.FromResult(false);
        if (char.IsLetterOrDigit(keyPress.ConsoleKeyInfo.KeyChar) ||
            keyPress.ConsoleKeyInfo.KeyChar is '_' or '$' or ':')
            return Task.FromResult(true);
        return base.ShouldOpenCompletionWindowAsync(text, caret, keyPress, cancellationToken);
    }

    protected override Task<TextSpan> GetSpanToReplaceByCompletionAsync(
        string text,
        int caret,
        CancellationToken cancellationToken)
    {
        var start = caret;
        while (start > 0)
        {
            var c = text[start - 1];
            if (char.IsLetterOrDigit(c) || c == '_' || c == '$')
                start--;
            else
                break;
        }

        return Task.FromResult(new TextSpan(start, caret - start));
    }

    protected override Task<IReadOnlyList<CompletionItem>> GetCompletionItemsAsync(
        string text,
        int caret,
        TextSpan spanToBeReplaced,
        CancellationToken cancellationToken)
    {
        if (!suggestionsEnabled)
            return Task.FromResult<IReadOnlyList<CompletionItem>>(Array.Empty<CompletionItem>());

        var isCommand = spanToBeReplaced.Start > 0 && text[spanToBeReplaced.Start - 1] == ':';
        if (isCommand)
            return Task.FromResult(ReplCommandItems);

        var items = new List<CompletionItem>(Keywords.Count);
        foreach (var keyword in Keywords)
        {
            var replacement = NeedsTrailingSpace(keyword) ? keyword + " " : keyword;
            items.Add(new(
                replacement,
                new(keyword, new FormatSpan(0, keyword.Length, AnsiColor.BrightBlue)),
                keyword));
        }

        return Task.FromResult<IReadOnlyList<CompletionItem>>(items);
    }

    protected override Task<IReadOnlyCollection<FormatSpan>> HighlightCallbackAsync(
        string text,
        CancellationToken cancellationToken)
    {
        var spans = new List<FormatSpan>();
        var inString = false;
        var quote = '\0';
        var stringStart = 0;
        var inLineComment = false;
        var inBlockComment = false;
        var commentStart = 0;
        var wordStart = -1;

        void FlushWord(int endExclusive)
        {
            if (wordStart < 0)
                return;
            var len = endExclusive - wordStart;
            var word = text.Substring(wordStart, len);
            if (Keywords.Contains(word))
                spans.Add(new(wordStart, len, AnsiColor.BrightBlue));
            wordStart = -1;
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inLineComment)
            {
                if (c == '\n')
                {
                    spans.Add(new(commentStart, i - commentStart, AnsiColor.BrightGreen));
                    inLineComment = false;
                }

                continue;
            }

            if (inBlockComment)
            {
                if (c == '*' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    i++;
                    spans.Add(new(commentStart, i - commentStart + 1, AnsiColor.BrightGreen));
                    inBlockComment = false;
                }

                continue;
            }

            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                    continue;
                }

                if (c == quote)
                {
                    spans.Add(new(stringStart, i - stringStart + 1, AnsiColor.Yellow));
                    inString = false;
                }

                continue;
            }

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                FlushWord(i);
                inLineComment = true;
                commentStart = i;
                i++;
                continue;
            }

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                FlushWord(i);
                inBlockComment = true;
                commentStart = i;
                i++;
                continue;
            }

            if (c is '"' or '\'' or '`')
            {
                FlushWord(i);
                inString = true;
                quote = c;
                stringStart = i;
                continue;
            }

            if (char.IsLetterOrDigit(c) || c is '_' or '$')
            {
                if (wordStart < 0)
                    wordStart = i;
                continue;
            }

            FlushWord(i);
        }

        FlushWord(text.Length);
        if (inLineComment)
            spans.Add(new(commentStart, text.Length - commentStart, AnsiColor.BrightGreen));
        if (inBlockComment)
            spans.Add(new(commentStart, text.Length - commentStart, AnsiColor.BrightGreen));
        if (inString)
            spans.Add(new(stringStart, text.Length - stringStart, AnsiColor.Yellow));

        return Task.FromResult<IReadOnlyCollection<FormatSpan>>(spans);
    }

    private static string GetIndentationString(string text, int caret)
    {
        var level = GetSmartIndentationLevel(text, caret);
        return new(' ', level * IndentSize);
    }

    private static int GetSmartIndentationLevel(string text, int caret)
    {
        var openBraces = 0;
        var inLineComment = false;
        var inBlockComment = false;
        var inString = false;
        var inTemplateText = false;
        var quote = '\0';
        var escape = false;
        var templateExpressionBraceStack = new Stack<int>();

        for (var i = 0; i < Math.Min(text.Length, caret); i++)
        {
            var c = text[i];
            var prev = i > 0 ? text[i - 1] : '\0';
            var next = i + 1 < Math.Min(text.Length, caret) ? text[i + 1] : '\0';
            if (inLineComment)
            {
                if (c == '\n')
                    inLineComment = false;
                continue;
            }

            if (inBlockComment)
            {
                if (prev == '*' && c == '/')
                    inBlockComment = false;
                continue;
            }

            if (inTemplateText)
            {
                if (!escape && c == '`')
                {
                    inTemplateText = false;
                    continue;
                }

                if (!escape && c == '$' && next == '{')
                {
                    templateExpressionBraceStack.Push(openBraces);
                    openBraces++;
                    inTemplateText = false;
                    i++;
                    escape = false;
                    continue;
                }

                escape = c == '\\' && !escape;
                continue;
            }

            if (inString)
            {
                if (!escape && c == quote)
                    inString = false;
                escape = c == '\\' && !escape;
                continue;
            }

            if (prev == '/' && c == '/')
            {
                inLineComment = true;
            }
            else if (prev == '/' && c == '*')
            {
                inBlockComment = true;
            }
            else if (c is '"' or '\'')
            {
                inString = true;
                quote = c;
                escape = false;
            }
            else if (c == '`')
            {
                inTemplateText = true;
                escape = false;
            }
            else if (c == '{')
            {
                openBraces++;
            }
            else if (c == '}')
            {
                openBraces--;
                if (templateExpressionBraceStack.Count != 0 &&
                    openBraces == templateExpressionBraceStack.Peek())
                {
                    templateExpressionBraceStack.Pop();
                    inTemplateText = true;
                }
            }
        }

        return Math.Max(0, openBraces);
    }

    private static bool IsInputComplete(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return true;
        if (!HasBalancedDelimiters(input))
            return false;

        try
        {
            JavaScriptParser.ParseScript(input);
            return true;
        }
        catch (JsParseException ex)
        {
            var msg = ex.Message;
            if (msg.Contains("found Eof", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("unexpected end of input", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("unterminated", StringComparison.OrdinalIgnoreCase) ||
                IsLikelyEndOfInputParse(ex, input))
                return false;
            return true;
        }
    }

    private static bool HasBalancedDelimiters(string text)
    {
        var paren = 0;
        var brace = 0;
        var bracket = 0;
        var inLineComment = false;
        var inBlockComment = false;
        var inString = false;
        var inTemplateText = false;
        var quote = '\0';
        var escape = false;
        var templateExpressionBraceStack = new Stack<int>();

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';
            if (inLineComment)
            {
                if (c == '\n')
                    inLineComment = false;
                continue;
            }

            if (inBlockComment)
            {
                if (c == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }

                continue;
            }

            if (inTemplateText)
            {
                if (!escape && c == '`')
                {
                    inTemplateText = false;
                    continue;
                }

                if (!escape && c == '$' && next == '{')
                {
                    templateExpressionBraceStack.Push(brace);
                    brace++;
                    inTemplateText = false;
                    i++;
                    escape = false;
                    continue;
                }

                escape = c == '\\' && !escape;
                continue;
            }

            if (inString)
            {
                if (!escape && c == quote)
                    inString = false;
                escape = c == '\\' && !escape;
                continue;
            }

            if (c == '/' && next == '/')
            {
                inLineComment = true;
                i++;
                continue;
            }

            if (c == '/' && next == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }

            if (c is '"' or '\'')
            {
                inString = true;
                quote = c;
                escape = false;
                continue;
            }

            if (c == '`')
            {
                inTemplateText = true;
                escape = false;
                continue;
            }

            switch (c)
            {
                case '(':
                    paren++;
                    break;
                case ')':
                    paren--;
                    if (paren < 0)
                        return true;
                    break;
                case '{':
                    brace++;
                    break;
                case '}':
                    brace--;
                    if (brace < 0)
                        return true;
                    if (templateExpressionBraceStack.Count != 0 &&
                        brace == templateExpressionBraceStack.Peek())
                    {
                        templateExpressionBraceStack.Pop();
                        inTemplateText = true;
                    }

                    break;
                case '[':
                    bracket++;
                    break;
                case ']':
                    bracket--;
                    if (bracket < 0)
                        return true;
                    break;
            }
        }

        return !inString && !inTemplateText && !inBlockComment && paren == 0 && brace == 0 && bracket == 0 &&
               templateExpressionBraceStack.Count == 0;
    }

    private static bool NeedsTrailingSpace(string keyword)
    {
        return keyword is "var" or "let" or "const" or "if" or "else" or "return" or "function" or "for" or "while" or
            "do" or "typeof" or "void" or "delete" or "switch" or "case" or "throw" or "try" or "catch" or "finally" or
            "with" or "in" or "instanceof" or "of" or "new" or "class" or "enum" or "extends" or "export" or "import" or
            "await" or "async" or "yield";
    }

    private static bool IsLikelyEndOfInputParse(JsParseException ex, string input)
    {
        return ex.Position >= input.Length &&
               ex.Message.Contains("Unexpected token", StringComparison.OrdinalIgnoreCase);
    }

    private static KeyPress NewLineWithIndentation(string indentation)
    {
        return new(
            ConsoleKey.Insert.ToKeyInfo('\0', true),
            "\n" + indentation);
    }

    private static int GetLineStart(string text, int index)
    {
        if (index <= 0)
            return 0;
        var i = index - 1;
        while (i >= 0 && text[i] != '\n')
            i--;
        return i + 1;
    }

    private static int GetLineEnd(string text, int index)
    {
        var i = Math.Min(index, text.Length);
        while (i < text.Length && text[i] != '\n')
            i++;
        return i;
    }
}
