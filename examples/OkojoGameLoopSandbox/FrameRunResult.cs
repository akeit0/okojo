namespace OkojoGameLoopSandbox;

internal readonly record struct FrameRunResult(
    bool Completed,
    int FrameCount,
    string? ErrorCode,
    string? ErrorMessage);
