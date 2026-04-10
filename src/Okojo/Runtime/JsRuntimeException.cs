namespace Okojo.Runtime;

public class JsRuntimeException(
    JsErrorKind kind,
    string message,
    string? detailCode = null,
    JsValue? thrownValue = null,
    Exception? innerException = null,
    JsRealm? errorRealm = null)
    : Exception(message, innerException)
{
    private string? resolvedMessage;
    private IReadOnlyList<StackFrameInfo>? stackFrames;

    public JsErrorKind Kind { get; } = kind;
    public JsValue? ThrownValue { get; } = thrownValue;
    public string? DetailCode { get; } = detailCode;
    public JsRealm? ErrorRealm { get; } = errorRealm;
    public IReadOnlyList<StackFrameInfo> StackFrames => stackFrames ?? [];
    public override string Message => resolvedMessage ?? base.Message;

    public void ResolveMessageIfMissing(string message)
    {
        if (!string.IsNullOrEmpty(base.Message) || !string.IsNullOrEmpty(resolvedMessage))
            return;
        resolvedMessage = message;
    }

    public void SetStackFramesIfMissing(IReadOnlyList<StackFrameInfo> frames)
    {
        if (stackFrames is not null)
            return;
        stackFrames = frames;
    }

    public string FullMessageWithStack()
    {
        if (StackFrames.Count == 0)
            return Message;

        if (InnerException is not null)
            return
                $"{Message}{Environment.NewLine}Inner exception: {InnerException}{Environment.NewLine}Stack trace:{Environment.NewLine}{FormatOkojoStackTrace()}";

        return $"{Message}{Environment.NewLine}Stack trace:{Environment.NewLine}{FormatOkojoStackTrace()}";
    }

    public override string ToString()
    {
        if (StackFrames.Count == 0)
            return base.ToString();

        if (InnerException is not null)
            return
                $"{base.ToString()}{Environment.NewLine}Inner exception: {InnerException}{Environment.NewLine}Stack trace:{Environment.NewLine}{FormatOkojoStackTrace()}";

        return $"{base.ToString()}{Environment.NewLine}Stack trace:{Environment.NewLine}{FormatOkojoStackTrace()}";
    }

    public string FormatOkojoStackTrace(int maxRepeatedFramesToShow = 12)
    {
        if (StackFrames.Count == 0)
            return string.Empty;

        var lines = new List<string>(StackFrames.Count);
        string? previousLine = null;
        var repeatedCount = 0;

        void FlushRepeatedFrames()
        {
            if (previousLine is null)
                return;

            var shownCount = Math.Min(repeatedCount, maxRepeatedFramesToShow);
            for (var j = 0; j < shownCount; j++)
                lines.Add(previousLine);

            var omittedCount = repeatedCount - shownCount;
            if (omittedCount > 0)
                lines.Add($"... repeated {omittedCount} more frame(s)");
        }

        for (var i = 0; i < StackFrames.Count; i++)
        {
            var line = FormatStackFrame(StackFrames[i]);
            if (line == previousLine)
            {
                repeatedCount++;
                continue;
            }

            FlushRepeatedFrames();
            previousLine = line;
            repeatedCount = 1;
        }

        FlushRepeatedFrames();
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatStackFrame(StackFrameInfo f)
    {
        var locationSuffix = string.Empty;
        if (f.HasSourceLocation)
            locationSuffix = f.SourcePath is { Length: > 0 }
                ? $" @ {f.SourcePath}:{f.SourceLine}:{f.SourceColumn}"
                : $" @ {f.SourceLine}:{f.SourceColumn}";

        return f.HasGeneratorState
            ? $"at {f.FunctionName}{locationSuffix} (pc:{f.ProgramCounter}, kind:{f.FrameKind}, gen:{f.GeneratorState}, suspend:{f.GeneratorSuspendId})"
            : $"at {f.FunctionName}{locationSuffix} (pc:{f.ProgramCounter}, kind:{f.FrameKind})";
    }
}
