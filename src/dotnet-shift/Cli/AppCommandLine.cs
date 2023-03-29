using System.CommandLine;
using System.CommandLine.Invocation;

namespace Cli;

sealed partial class AppCommandLine : CommandLine<AppContext>
{
    public static AppCommandLine Create(ContextFactory<AppContext>? contextFactory = null)
    {
        contextFactory ??= DefaultContextFactory;
        return new AppCommandLine(contextFactory);
    }

    private AppCommandLine(ContextFactory<AppContext> contextFactory) :
        base(contextFactory)
    {
        ContextExceptionHandler = ExceptionHandler;
        Configure(
            CreateRootCommand(),
            builder =>{ });
    }

    private static AppContext DefaultContextFactory(ParseResult parseResult)
        => new AppContext(parseResult, new AppServices());
}