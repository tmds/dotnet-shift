using System.Text;
using Xunit.Abstractions;

class TestOutputTextWriter : TextWriter
{
    private readonly ITestOutputHelper _output;
    private readonly StringBuilder _lineBuffer = new();
    private readonly List<string>? _allOutput;
    private readonly string? _prefix;

    public TestOutputTextWriter(ITestOutputHelper output, string? prefix = null, List<string>? allOutput = null)
    {
        _output = output;
        _prefix = prefix;
        _allOutput = allOutput;
    }

    public override Encoding Encoding
    {
        get { return Encoding.UTF8; }
    }

    public override void WriteLine(string? message)
    {
        string line = $"{ClearLineBuffer()}{message}";
        _output.WriteLine($"{_prefix}{line}");
        _allOutput?.Add(line);
    }

    public override void WriteLine(string format, params object?[] args)
    {
        WriteLine(string.Format(format, args));
    }

    public override void Write(char value)
    {
        if (value == '\n')
        {
            WriteLine("");
        }
        else if (value == '\r')
        { }
        else
        {
            _lineBuffer.Append(value);
        }
    }

    private string ClearLineBuffer()
    {
        string line = _lineBuffer.ToString();
        _lineBuffer.Clear();
        return line;
    }

    protected override void Dispose(bool disposing)
    {
        if (_lineBuffer.Length > 0)
        {
            WriteLine(ClearLineBuffer());
        }
        base.Dispose(disposing);
    }
}