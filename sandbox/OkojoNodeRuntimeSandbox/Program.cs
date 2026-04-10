namespace OkojoNodeRuntimeSandbox;

internal static class Program
{
    public static void Main()
    {
        var runner = new NodeSandboxRunner(AppContext.BaseDirectory);
        runner.RunAll();
    }
}
