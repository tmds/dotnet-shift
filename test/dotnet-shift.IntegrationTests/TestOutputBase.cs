using Xunit.Abstractions;

public class TestOutputBase
{
    protected ITestOutputHelper Output { get; }

    protected TestOutputBase(ITestOutputHelper output)
        => Output = output;
}