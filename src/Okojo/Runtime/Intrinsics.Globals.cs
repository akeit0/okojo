using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Okojo.Bytecode;
using Okojo.Compiler;
using Okojo.Parsing;

namespace Okojo.Runtime;

public partial class Intrinsics
{
    [GeneratedRegex(@"^[\+\-]?(Infinity|((([0-9]+(\.[0-9]*)?)|(\.[0-9]+))([eE][\+\-]?[0-9]+)?))")]
    private static partial Regex ParseFloatRegex { get; }

    private JsHostFunction CreateGlobalEvalFunction()
    {
        return new(Realm, (in info) =>
        {
            var realm = ((JsHostFunction)info.Function).Realm;
            var args = info.Arguments;
            if (args.Length == 0)
                return JsValue.Undefined;

            var sourceValue = args[0];
            if (!sourceValue.IsString)
                return sourceValue;

            var source = sourceValue.AsString();
            if (TryEvaluateTriviaOnlyEvalSource(source, out var triviaOnlyResult))
                return triviaOnlyResult;
            JsProgram program;
            JsScript script;
            try
            {
                program = JavaScriptParser.ParseScript(source);
                EvalEarlyErrors.ThrowIfInvalidIndirectEvalScript(program);
                script = program.StrictDeclared
                    ? JsCompiler.Compile(realm, program, new() { IsIndirectEval = true, IsStrictIndirectEval = true })
                    : JsCompiler.Compile(realm, program, new() { IsIndirectEval = true });
            }
            catch (JsParseException ex)
            {
                throw new JsRuntimeException(JsErrorKind.SyntaxError, ex.Message, "EVAL_PARSE_ERROR");
            }

            var root = new JsBytecodeFunction(realm, script, "eval");
            var useIndirectEvalGlobalBindingSemantics = !program.StrictDeclared;
            if (useIndirectEvalGlobalBindingSemantics)
                PrepareIndirectEvalDeclarationInstantiation(realm, program);
            if (useIndirectEvalGlobalBindingSemantics)
                realm.EnterIndirectEvalGlobalBindingSemantics();

            JsValue result;
            try
            {
                result = realm.InvokeBytecodeFunction(root, JsValue.Undefined, ReadOnlySpan<JsValue>.Empty,
                    JsValue.Undefined);
            }
            finally
            {
                if (useIndirectEvalGlobalBindingSemantics)
                    realm.ExitIndirectEvalGlobalBindingSemantics();
            }

            return result.IsTheHole ? JsValue.Undefined : result;
        }, "eval", 1);
    }

    private static bool TryEvaluateTriviaOnlyEvalSource(string source, out JsValue result)
    {
        var span = source.AsSpan();
        var index = 0;
        while (index < span.Length)
        {
            var ch = span[index];
            if (IsEvalTriviaChar(ch))
            {
                index++;
                continue;
            }

            if (ch == '#' && index == 0 && index + 1 < span.Length && span[index + 1] == '!')
            {
                index += 2;
                while (index < span.Length && !IsEvalLineTerminator(span[index]))
                    index++;
                continue;
            }

            if (ch != '/' || index + 1 >= span.Length)
                break;

            var next = span[index + 1];
            if (next == '/')
            {
                index += 2;
                while (index < span.Length && !IsEvalLineTerminator(span[index]))
                    index++;
                continue;
            }

            if (next != '*')
                break;

            index += 2;
            while (index + 1 < span.Length && !(span[index] == '*' && span[index + 1] == '/'))
                index++;
            if (index + 1 >= span.Length)
            {
                result = default;
                return false;
            }

            index += 2;
        }

        result = default;
        if (index < span.Length)
            return false;

        result = JsValue.Undefined;
        return true;
    }

    private static bool IsEvalTriviaChar(char ch)
    {
        return ch == ' ' ||
               ch == '\t' ||
               ch == '\v' ||
               ch == '\f' ||
               ch == '\u00A0' ||
               ch == '\uFEFF' ||
               IsEvalLineTerminator(ch);
    }

    private static bool IsEvalLineTerminator(char ch)
    {
        return ch == '\r' || ch == '\n' || ch == '\u2028' || ch == '\u2029';
    }

    private static void PrepareIndirectEvalDeclarationInstantiation(JsRealm realm, JsProgram program)
    {
        var varDeclaredNames = new List<int>(4);
        var functionDeclarations = new List<JsFunctionDeclaration>(4);
        CollectVarScopedDeclarations(realm, program.Statements, varDeclaredNames, functionDeclarations);

        foreach (var atom in varDeclaredNames.Distinct())
            if (realm.HasGlobalLexicalBindingAtom(atom))
            {
                var name = realm.Atoms.AtomToString(atom);
                throw new JsRuntimeException(JsErrorKind.SyntaxError,
                    $"Identifier '{name}' has already been declared",
                    "EVAL_GLOBAL_LEXICAL_CONFLICT");
            }

        var declaredFunctionNames = new HashSet<int>();
        for (var i = functionDeclarations.Count - 1; i >= 0; i--)
        {
            var atom = realm.Atoms.InternNoCheck(functionDeclarations[i].Name);
            if (!declaredFunctionNames.Add(atom))
                continue;

            if (!realm.GlobalObject.CanDeclareGlobalFunctionAtom(atom))
            {
                var name = realm.Atoms.AtomToString(atom);
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    $"Identifier '{name}' has already been declared",
                    "EVAL_GLOBAL_FUNCTION_NOT_DEFINABLE");
            }
        }

        foreach (var atom in varDeclaredNames.Distinct())
        {
            if (declaredFunctionNames.Contains(atom))
                continue;

            if (!realm.GlobalObject.CanDeclareGlobalVarAtom(atom))
            {
                var name = realm.Atoms.AtomToString(atom);
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    $"Identifier '{name}' has already been declared",
                    "EVAL_GLOBAL_VAR_NOT_DEFINABLE");
            }
        }
    }

    public static void PrepareGlobalScriptDeclarationInstantiation(JsRealm realm, JsProgram program)
    {
        for (var i = 0; i < program.TopLevelLexicalNames.Count; i++)
        {
            var atom = realm.Atoms.InternNoCheck(program.TopLevelLexicalNames[i]);
            if (realm.HasGlobalLexicalBindingAtom(atom) || realm.GlobalObject.HasRestrictedGlobalPropertyAtom(atom))
            {
                var name = realm.Atoms.AtomToString(atom);
                throw new JsRuntimeException(JsErrorKind.SyntaxError,
                    $"Identifier '{name}' has already been declared",
                    "SCRIPT_GLOBAL_LEXICAL_CONFLICT");
            }
        }

        var varDeclaredNames = new List<int>(4);
        var functionDeclarations = new List<JsFunctionDeclaration>(4);
        CollectVarScopedDeclarations(realm, program.Statements, varDeclaredNames, functionDeclarations);

        foreach (var atom in varDeclaredNames.Distinct())
            if (realm.HasGlobalLexicalBindingAtom(atom))
            {
                var name = realm.Atoms.AtomToString(atom);
                throw new JsRuntimeException(JsErrorKind.SyntaxError,
                    $"Identifier '{name}' has already been declared",
                    "SCRIPT_GLOBAL_VAR_LEXICAL_CONFLICT");
            }

        var declaredFunctionNames = new HashSet<int>();
        for (var i = functionDeclarations.Count - 1; i >= 0; i--)
        {
            var atom = realm.Atoms.InternNoCheck(functionDeclarations[i].Name);
            if (!declaredFunctionNames.Add(atom))
                continue;

            if (!realm.GlobalObject.CanDeclareGlobalFunctionAtom(atom))
            {
                var name = realm.Atoms.AtomToString(atom);
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    $"Identifier '{name}' has already been declared",
                    "SCRIPT_GLOBAL_FUNCTION_NOT_DEFINABLE");
            }
        }

        foreach (var atom in varDeclaredNames.Distinct())
        {
            if (declaredFunctionNames.Contains(atom))
                continue;

            if (!realm.GlobalObject.CanDeclareGlobalVarAtom(atom))
            {
                var name = realm.Atoms.AtomToString(atom);
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    $"Identifier '{name}' has already been declared",
                    "SCRIPT_GLOBAL_VAR_NOT_DEFINABLE");
            }
        }
    }

    private static void CollectVarScopedDeclarations(
        JsRealm realm,
        IReadOnlyList<JsStatement> statements,
        List<int> varDeclaredNames,
        List<JsFunctionDeclaration> functionDeclarations)
    {
        foreach (var statement in statements)
            switch (statement)
            {
                case JsVariableDeclarationStatement varStatement
                    when varStatement.Kind == JsVariableDeclarationKind.Var:
                    foreach (var declarator in varStatement.Declarators)
                        varDeclaredNames.Add(realm.Atoms.InternNoCheck(declarator.Name));
                    break;
                case JsFunctionDeclaration functionDeclaration:
                    functionDeclarations.Add(functionDeclaration);
                    varDeclaredNames.Add(realm.Atoms.InternNoCheck(functionDeclaration.Name));
                    break;
                case JsBlockStatement block:
                    CollectVarScopedDeclarations(realm, block.Statements, varDeclaredNames, functionDeclarations);
                    break;
                case JsIfStatement ifStatement:
                    CollectVarScopedDeclarations(realm, [ifStatement.Consequent], varDeclaredNames,
                        functionDeclarations);
                    if (ifStatement.Alternate is not null)
                        CollectVarScopedDeclarations(realm, [ifStatement.Alternate], varDeclaredNames,
                            functionDeclarations);
                    break;
                case JsWhileStatement whileStatement:
                    CollectVarScopedDeclarations(realm, [whileStatement.Body], varDeclaredNames, functionDeclarations);
                    break;
                case JsDoWhileStatement doWhileStatement:
                    CollectVarScopedDeclarations(realm, [doWhileStatement.Body], varDeclaredNames,
                        functionDeclarations);
                    break;
                case JsForStatement forStatement:
                    if (forStatement.Init is JsVariableDeclarationStatement initDeclaration &&
                        initDeclaration.Kind == JsVariableDeclarationKind.Var)
                        foreach (var declarator in initDeclaration.Declarators)
                            varDeclaredNames.Add(realm.Atoms.InternNoCheck(declarator.Name));

                    CollectVarScopedDeclarations(realm, [forStatement.Body], varDeclaredNames, functionDeclarations);
                    break;
                case JsForInOfStatement forInOfStatement:
                    if (forInOfStatement.Left is JsVariableDeclarationStatement leftDeclaration &&
                        leftDeclaration.Kind == JsVariableDeclarationKind.Var)
                        foreach (var declarator in leftDeclaration.Declarators)
                            varDeclaredNames.Add(realm.Atoms.InternNoCheck(declarator.Name));

                    CollectVarScopedDeclarations(realm, [forInOfStatement.Body], varDeclaredNames,
                        functionDeclarations);
                    break;
                case JsTryStatement tryStatement:
                    CollectVarScopedDeclarations(realm, [tryStatement.Block], varDeclaredNames, functionDeclarations);
                    if (tryStatement.Handler is not null)
                        CollectVarScopedDeclarations(realm, [tryStatement.Handler.Body], varDeclaredNames,
                            functionDeclarations);
                    if (tryStatement.Finalizer is not null)
                        CollectVarScopedDeclarations(realm, [tryStatement.Finalizer], varDeclaredNames,
                            functionDeclarations);
                    break;
                case JsSwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                        CollectVarScopedDeclarations(realm, switchCase.Consequent, varDeclaredNames,
                            functionDeclarations);
                    break;
                case JsLabeledStatement labeledStatement:
                    CollectVarScopedDeclarations(realm, [labeledStatement.Statement], varDeclaredNames,
                        functionDeclarations);
                    break;
            }
    }

    private JsHostFunction CreateGlobalIsNaNFunction()
    {
        return new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var value = args.Length == 0 ? JsValue.Undefined : args[0];
            return double.IsNaN(realm.ToNumber(value)) ? JsValue.True : JsValue.False;
        }, "isNaN", 1);
    }

    private JsHostFunction CreateGlobalIsFiniteFunction()
    {
        return new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var value = args.Length == 0 ? JsValue.Undefined : args[0];
            var number = realm.ToNumber(value);
            return !double.IsNaN(number) && !double.IsInfinity(number) ? JsValue.True : JsValue.False;
        }, "isFinite", 1);
    }

    private JsHostFunction CreateGlobalParseIntFunction()
    {
        return new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var input = args.Length == 0 ? "undefined" : realm.ToJsStringSlowPath(args[0]);
            var radix = 0;
            if (args.Length > 1 && !args[1].IsUndefined)
                radix = JsRealm.ToInt32SlowPath(realm, args[1]);

            var index = 0;
            while (index < input.Length && char.IsWhiteSpace(input[index]))
                index++;

            var negative = false;
            if (index < input.Length && (input[index] == '+' || input[index] == '-'))
            {
                negative = input[index] == '-';
                index++;
            }

            if (radix != 0 && (radix < 2 || radix > 36))
                return JsValue.NaN;

            if (radix == 0)
            {
                radix = 10;
                if (index + 1 < input.Length && input[index] == '0' &&
                    (input[index + 1] == 'x' || input[index + 1] == 'X'))
                {
                    radix = 16;
                    index += 2;
                }
            }
            else if (radix == 16 && index + 1 < input.Length && input[index] == '0' &&
                     (input[index + 1] == 'x' || input[index + 1] == 'X'))
            {
                index += 2;
            }

            double value = 0;
            var parsedAny = false;
            while (index < input.Length)
            {
                var digit = ParseIntDigit(input[index]);
                if (digit < 0 || digit >= radix)
                    break;
                parsedAny = true;
                value = value * radix + digit;
                index++;
            }

            if (!parsedAny)
                return JsValue.NaN;

            return new(negative ? -value : value);
        }, "parseInt", 2);
    }

    private JsHostFunction CreateGlobalEncodeUriFunction(bool escapeReserved)
    {
        var name = escapeReserved ? "encodeURIComponent" : "encodeURI";
        return new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var input = args.Length == 0 ? "undefined" : realm.ToJsStringSlowPath(args[0]);
            return JsValue.FromString(EncodeUriString(realm, input, escapeReserved));
        }, name, 1);
    }

    private JsHostFunction CreateGlobalDecodeUriFunction(bool preserveReserved)
    {
        var name = preserveReserved ? "decodeURI" : "decodeURIComponent";
        return new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var input = args.Length == 0 ? "undefined" : realm.ToJsStringSlowPath(args[0]);
            return JsValue.FromString(DecodeUriString(realm, input, preserveReserved));
        }, name, 1);
    }

    private JsHostFunction CreateGlobalParseFloatFunction()
    {
        return new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var input = args.Length == 0 ? "undefined" : realm.ToJsStringSlowPath(args[0]);
            input = input.TrimStart();
            if (input.Length == 0)
                return JsValue.NaN;

            var match = ParseFloatRegex.Match(input);
            if (!match.Success)
                return JsValue.NaN;

            var token = match.Value;
            if (token == "Infinity" || token == "+Infinity")
                return new(double.PositiveInfinity);
            if (token == "-Infinity")
                return new(double.NegativeInfinity);

            return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? new(value)
                : JsValue.NaN;
        }, "parseFloat", 1);
    }

    private static int ParseIntDigit(char ch)
    {
        if (ch >= '0' && ch <= '9')
            return ch - '0';
        if (ch >= 'a' && ch <= 'z')
            return ch - 'a' + 10;
        if (ch >= 'A' && ch <= 'Z')
            return ch - 'A' + 10;
        return -1;
    }

    private static string EncodeUriString(JsRealm realm, string input, bool escapeReserved)
    {
        var sb = new StringBuilder(input.Length);
        Span<byte> utf8 = stackalloc byte[4];
        for (var i = 0; i < input.Length; i++)
        {
            var ch = input[i];
            if (IsUnescapedUriCodeUnit(ch, escapeReserved))
            {
                sb.Append(ch);
                continue;
            }

            Rune rune;
            if (char.IsHighSurrogate(ch))
            {
                if (i + 1 >= input.Length || !char.IsLowSurrogate(input[i + 1]))
                    ThrowUriMalformed(realm);

                rune = new(ch, input[i + 1]);
                i++;
            }
            else if (char.IsLowSurrogate(ch))
            {
                ThrowUriMalformed(realm);
                return string.Empty;
            }
            else
            {
                rune = new(ch);
            }

            var written = rune.EncodeToUtf8(utf8);
            for (var j = 0; j < written; j++)
            {
                sb.Append('%');
                sb.Append(utf8[j].ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return sb.ToString();
    }

    private static bool IsUnescapedUriCodeUnit(char ch, bool escapeReserved)
    {
        if ((ch >= 'A' && ch <= 'Z') ||
            (ch >= 'a' && ch <= 'z') ||
            (ch >= '0' && ch <= '9'))
            return true;

        return ch switch
        {
            '-' or '_' or '.' or '!' or '~' or '*' or '\'' or '(' or ')' => true,
            ';' or '/' or '?' or ':' or '@' or '&' or '=' or '+' or '$' or ',' or '#' => !escapeReserved,
            _ => false
        };
    }

    private static void ThrowUriMalformed(JsRealm realm)
    {
        var error = new JsPlainObject(realm.ErrorObjectShape, false)
        {
            Prototype = realm.UriErrorPrototype
        };
        error.SetNamedSlotUnchecked(0, "URIError");
        error.SetNamedSlotUnchecked(1, "URI malformed");
        throw new JsRuntimeException(
            JsErrorKind.InternalError,
            "URI malformed",
            "URI_MALFORMED",
            JsValue.FromObject(error),
            errorRealm: realm);
    }

    private static JsRuntimeException UriMalformed(JsRealm realm)
    {
        var error = new JsPlainObject(realm.ErrorObjectShape, false)
        {
            Prototype = realm.UriErrorPrototype
        };
        error.SetNamedSlotUnchecked(0, "URIError");
        error.SetNamedSlotUnchecked(1, "URI malformed");
        return new(
            JsErrorKind.InternalError,
            "URI malformed",
            "URI_MALFORMED",
            JsValue.FromObject(error),
            errorRealm: realm);
    }

    private static string DecodeUriString(JsRealm realm, string input, bool preserveReserved)
    {
        var sb = new StringBuilder(input.Length);
        Span<byte> bytes = stackalloc byte[4];
        for (var i = 0; i < input.Length; i++)
        {
            var ch = input[i];
            if (ch != '%')
            {
                sb.Append(ch);
                continue;
            }

            byte firstByte = 0;
            if (i + 2 >= input.Length || !TryDecodeHexByte(input, i + 1, out firstByte))
                goto fail;

            var byteCount = GetUtf8SequenceLength(firstByte);
            if (byteCount == 0)
                goto fail;

            if (byteCount == 1)
            {
                if (preserveReserved && IsReservedUriCodeUnit((char)firstByte))
                    sb.Append(input, i, 3);
                else
                    AppendDecodedScalar(sb, firstByte, preserveReserved);
                i += 2;
                continue;
            }

            bytes[0] = firstByte;
            var cursor = i + 3;
            for (var j = 1; j < byteCount; j++)
            {
                if (cursor + 2 >= input.Length || input[cursor] != '%' ||
                    !TryDecodeHexByte(input, cursor + 1, out bytes[j]))
                    goto fail;
                cursor += 3;
            }

            if (Rune.DecodeFromUtf8(bytes[..byteCount], out var rune, out var consumed) != OperationStatus.Done ||
                consumed != byteCount ||
                rune.Value is >= 0xD800 and <= 0xDFFF)
                goto fail;

            if (preserveReserved && rune.IsAscii && IsReservedUriCodeUnit((char)rune.Value))
                sb.Append(input, i, cursor - i);
            else
                AppendDecodedScalar(sb, rune, preserveReserved);
            i = cursor - 1;
        }

        return sb.ToString();
        fail: ;
        throw UriMalformed(realm);
    }

    private static void AppendDecodedScalar(StringBuilder sb, int scalar, bool preserveReserved)
    {
        AppendDecodedScalar(sb, new Rune(scalar), preserveReserved);
    }

    private static void AppendDecodedScalar(StringBuilder sb, Rune rune, bool preserveReserved)
    {
        if (preserveReserved &&
            rune.IsAscii &&
            IsReservedUriCodeUnit((char)rune.Value))
        {
            Span<byte> utf8 = stackalloc byte[4];
            var written = rune.EncodeToUtf8(utf8);
            for (var i = 0; i < written; i++)
            {
                sb.Append('%');
                sb.Append(utf8[i].ToString("X2", CultureInfo.InvariantCulture));
            }

            return;
        }

        Span<char> chars = stackalloc char[2];
        var charCount = rune.EncodeToUtf16(chars);
        sb.Append(chars[..charCount]);
    }

    private static bool TryDecodeHexByte(string text, int start, out byte value)
    {
        value = 0;
        if (start + 1 >= text.Length)
            return false;

        var high = ParseHexDigit(text[start]);
        var low = ParseHexDigit(text[start + 1]);
        if (high < 0 || low < 0)
            return false;

        value = (byte)((high << 4) | low);
        return true;
    }

    private static int ParseHexDigit(char ch)
    {
        if (ch >= '0' && ch <= '9')
            return ch - '0';
        if (ch >= 'a' && ch <= 'f')
            return ch - 'a' + 10;
        if (ch >= 'A' && ch <= 'F')
            return ch - 'A' + 10;
        return -1;
    }

    private static int GetUtf8SequenceLength(byte firstByte)
    {
        if ((firstByte & 0x80) == 0)
            return 1;
        if ((firstByte & 0xE0) == 0xC0)
            return 2;
        if ((firstByte & 0xF0) == 0xE0)
            return 3;
        if ((firstByte & 0xF8) == 0xF0)
            return 4;
        return 0;
    }

    private static bool IsReservedUriCodeUnit(char ch)
    {
        return ch is ';' or '/' or '?' or ':' or '@' or '&' or '=' or '+' or '$' or ',' or '#';
    }
}
