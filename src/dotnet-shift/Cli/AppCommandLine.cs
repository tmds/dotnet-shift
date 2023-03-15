using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;

namespace Cli;

sealed partial class AppCommandLine : CommandLine<AppContext>
{
    public static AppCommandLine Create(IConsole? console = null, ContextFactory<AppContext>? contextFactory = null)
    {
        console ??= new SystemConsole();
        contextFactory ??= DefaultContextFactory;
        return new AppCommandLine(contextFactory, console);
    }

    private AppCommandLine(ContextFactory<AppContext> contextFactory, IConsole console) :
        base(contextFactory, console)
    {
        RootCommand = CreateRootCommand();
    }

    private static AppContext DefaultContextFactory(InvocationContext invocationContext)
        => new AppContext(invocationContext.ParseResult, new AppServices());
}