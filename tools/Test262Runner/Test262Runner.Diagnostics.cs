using System.Text.RegularExpressions;
using Okojo.Diagnostics;
using Okojo.Runtime;
using JsValue = Okojo.JsValue;

internal static partial class Program
{
    private static string ToDisplayPath(string repoRoot, string path, bool fullPath)
    {
        if (string.IsNullOrEmpty(path) || path[0] == '<')
            return path;
        return MakeDisplayPath(repoRoot, path, fullPath);
    }

    private static bool TryExtractLineColumnFromMessage(string message, out int line, out int column)
    {
        var match = Regex.Match(message, @"\sat\s.+:(\d+):(\d+)(?:\s|$)", RegexOptions.CultureInvariant);
        if (match.Success &&
            int.TryParse(match.Groups[1].Value, out line) &&
            int.TryParse(match.Groups[2].Value, out column))
            return true;

        line = 0;
        column = 0;
        return false;
    }

    private static bool TryExtractManagedSourceLocation(Exception ex, string repoRoot, bool fullPath,
        out string location)
    {
        location = string.Empty;
        var current = ex;
        while (current is not null)
        {
            if (TryExtractManagedSourceLocationFromStackTrace(current.StackTrace, repoRoot, fullPath, out location))
                return true;
            current = current.InnerException!;
        }

        return false;
    }

    private static bool TryExtractManagedSourceLocationForRuntimeException(
        JsRuntimeException runtimeEx,
        string repoRoot,
        bool fullPath,
        out string location)
    {
        location = string.Empty;

        var deepest = runtimeEx.InnerException;
        while (deepest?.InnerException is not null)
            deepest = deepest.InnerException;

        if (deepest is not null &&
            TryExtractManagedSourceLocationFromStackTrace(deepest.StackTrace, repoRoot, fullPath, out location))
            return true;

        return TryExtractManagedSourceLocation(runtimeEx, repoRoot, fullPath, out location);
    }

    private static bool TryExtractManagedSourceLocationFromStackTrace(string? stackTrace, string repoRoot,
        bool fullPath, out string location)
    {
        location = string.Empty;
        if (string.IsNullOrEmpty(stackTrace))
            return false;

        var matches = Regex.Matches(
            stackTrace,
            @" in (?<path>[A-Za-z]:\\[^:\r\n]+?\.cs):line (?<line>\d+)",
            RegexOptions.CultureInvariant);

        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var path = match.Groups["path"].Value;
            if (path.IndexOf("\\.nuget\\", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;

            var displayPath = ToDisplayPath(repoRoot, path.Replace('\\', '/'), fullPath);
            location = $"{displayPath}:{match.Groups["line"].Value}";
            return true;
        }

        return false;
    }

    private static string FormatRuntimeExceptionMessage(JsRuntimeException runtimeEx)
    {
        if (runtimeEx.DetailCode == "JS_THROW_VALUE" &&
            runtimeEx.ThrownValue is { } thrownValue &&
            TryFormatThrownValueMessage(thrownValue, out var thrownMessage))
            return $"JavaScript throw: {thrownMessage}";

        return $"JavaScript throw: {runtimeEx.Kind}: {runtimeEx.Message}";
    }

    private static bool TryFormatThrownValueMessage(JsValue thrownValue, out string message)
    {
        if (thrownValue.TryGetObject(out var thrownObj))
        {
            string? name = null;
            string? detail = null;
            if (thrownObj.TryGetProperty("name", out var nameValue) && nameValue.IsString)
                name = nameValue.AsString();
            if (thrownObj.TryGetProperty("message", out var messageValue) && messageValue.IsString)
                detail = messageValue.AsString();

            if (!string.IsNullOrEmpty(name) || !string.IsNullOrEmpty(detail))
            {
                message = string.IsNullOrEmpty(detail)
                    ? name ?? "Error"
                    : string.IsNullOrEmpty(name)
                        ? detail
                        : $"{name}: {detail}";
                return true;
            }
        }

        message = DebugFormatter.FormatValue(thrownValue);
        return true;
    }

    private static (string Path, int Line, int Column)? SelectRuntimeExceptionLocation(
        JsRuntimeException runtimeEx,
        string sourcePath,
        HarnessSourceBundle harnessSource,
        bool strict,
        bool isModuleCase)
    {
        var normalizedSourcePath = sourcePath.Replace('\\', '/');
        (string Path, int Line, int Column)? fallback = null;

        var frames = runtimeEx.StackFrames;
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            if (!frame.HasSourceLocation)
                continue;

            var mapped = isModuleCase
                ? (normalizedSourcePath, frame.SourceLine, frame.SourceColumn)
                : MapSourceLocation(sourcePath, harnessSource, strict, frame.SourceLine, frame.SourceColumn);
            if (mapped is null)
                continue;

            if (fallback is null)
                fallback = mapped.Value;

            if (runtimeEx.DetailCode == "JS_THROW_VALUE" &&
                string.Equals(mapped.Value.Path, normalizedSourcePath, StringComparison.Ordinal))
                return mapped.Value;
        }

        return fallback;
    }

    private static int CountLinesWhenAppendedWithAppendLine(string text)
    {
        if (string.IsNullOrEmpty(text)) return 1;

        var count = 1; // AppendLine always appends one trailing newline.
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\n')
                count++;

        return count;
    }

    private static string StripParseLocationSuffix(string message)
    {
        if (string.IsNullOrEmpty(message)) return message;

        return Regex.Replace(
            message,
            @"\s+at line \d+,\s*column \d+\s*\(position \d+\)\.?$",
            string.Empty,
            RegexOptions.CultureInvariant);
    }

    private static (string Path, int Line, int Column)? MapSourceLocation(
        string sourcePath,
        HarnessSourceBundle harnessSource,
        bool strict,
        int fullLine,
        int column)
    {
        if (fullLine <= 0)
            return null;

        var strictPreludeLines = strict ? 1 : 0;
        if (strict && fullLine == 1)
            return ("<strict-directive>", 1, column);

        var harnessStart = strictPreludeLines + 1;
        var harnessEnd = strictPreludeLines + harnessSource.TotalLines;
        if (fullLine >= harnessStart && fullLine <= harnessEnd)
        {
            var harnessRelativeLine = fullLine - strictPreludeLines;
            for (var i = 0; i < harnessSource.Segments.Count; i++)
            {
                var segment = harnessSource.Segments[i];
                if (harnessRelativeLine < segment.StartLine || harnessRelativeLine > segment.EndLine)
                    continue;

                var lineInSegment = harnessRelativeLine - segment.StartLine + 1;
                return (segment.DisplayPath, lineInSegment, column);
            }

            return ("<harness>", harnessRelativeLine, column);
        }

        var testStart = harnessEnd + 1;
        var testLine = fullLine - testStart + 1;
        if (testLine < 1)
            testLine = 1;
        return (sourcePath.Replace('\\', '/'), testLine, column);
    }
}
