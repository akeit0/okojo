namespace Okojo.Runtime;

internal static class FrameLayout
{
    public const int HeaderSize = 4;
    public const int OffsetCallData = 0;
    public const int OffsetCurrentContext = 1;
    public const int OffsetThisValue = 2;
    public const int OffsetExtra0 = 3; // construct/host-exit new.target or active generator object for generator frames

    public const int BitOffsetCallerFp = 48;
    public const int BitOffsetArgCount = 32;
    public const int BitOffsetCallerPc = 0;

    public const int BitOffsetFrameFlags = 32;
    public const int BitOffsetFrameKind = 0;
}
