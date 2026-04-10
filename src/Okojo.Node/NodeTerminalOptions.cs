namespace Okojo.Node;

public sealed class NodeTerminalOptions
{
    public bool StdinIsTty { get; set; }
    public TextWriter Stdout { get; set; } = Console.Out;
    public TextWriter Stderr { get; set; } = Console.Error;
    public bool StdoutIsTty { get; set; }
    public bool StderrIsTty { get; set; }
    public int? StdoutColumns { get; set; }
    public int? StdoutRows { get; set; }
    public int? StderrColumns { get; set; }
    public int? StderrRows { get; set; }

    internal NodeTerminalOptions Clone()
    {
        return new()
        {
            StdinIsTty = StdinIsTty,
            Stdout = Stdout,
            Stderr = Stderr,
            StdoutIsTty = StdoutIsTty,
            StderrIsTty = StderrIsTty,
            StdoutColumns = StdoutColumns,
            StdoutRows = StdoutRows,
            StderrColumns = StderrColumns,
            StderrRows = StderrRows
        };
    }
}
