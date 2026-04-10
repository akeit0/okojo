namespace Okojo.Runtime;

public enum CallFrameKind
{
    ScriptFrame = 0,
    FunctionFrame = 1,
    ConstructFrame = 2,
    HostExitFrame = 3,
    GeneratorFrame = 4
}
