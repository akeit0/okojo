namespace Okojo.Runtime;

[Flags]
public enum CallFrameFlag
{
    None = 0,
    IsConstructorCall = 1 << 0,
    IsDerivedConstructorCall = 1 << 1
}
