using Okojo.Parsing;

namespace Okojo.Repl;

public static class ReplInputParser
{
    public static bool IsInputComplete(string input, bool allowTopLevelAwait = false)
    {
        if (string.IsNullOrWhiteSpace(input))
            return true;
        if (!HasBalancedDelimiters(input))
            return false;

        try
        {
            JavaScriptParser.ParseScript(input, false, false, allowTopLevelAwait);
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

    private static bool IsLikelyEndOfInputParse(JsParseException ex, string input)
    {
        return ex.Position >= input.Length &&
               ex.Message.Contains("Unexpected token", StringComparison.OrdinalIgnoreCase);
    }
}
