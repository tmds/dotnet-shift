using Spectre.Console;
using Spectre.Console.Rendering;

sealed class MockConsole : IAnsiConsole
{
    public Spectre.Console.Profile Profile => throw new NotImplementedException();

    public IAnsiConsoleCursor Cursor => throw new NotImplementedException();

    public IAnsiConsoleInput Input => throw new NotImplementedException();

    public IExclusivityMode ExclusivityMode => throw new NotImplementedException();

    public RenderPipeline Pipeline => throw new NotImplementedException();

    public void Clear(bool home)
    { }

    public void Write(IRenderable renderable)
    { }
}