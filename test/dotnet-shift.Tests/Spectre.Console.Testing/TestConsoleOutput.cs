namespace Spectre.Console.Testing;

using System.Text;

public class TestConsoleOutput : IAnsiConsoleOutput
{
    public TestConsoleOutput(TextWriter writer)
    {
        Writer = writer;
        IsTerminal = false;
        Width = 240; // avoid wrapping lines.
        Height = 40;
    }

    public TextWriter Writer { get; }

    public bool IsTerminal { get; }

    public int Width { get; }

    public int Height { get; }

    public void SetEncoding(Encoding encoding)
    { }
}