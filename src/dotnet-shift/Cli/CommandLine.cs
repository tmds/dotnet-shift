using System.CommandLine;
using System.CommandLine.Invocation;

namespace Cli;

delegate TContext ContextFactory<TContext>(ParseResult parseResult);

class CommandLine<TContext>
{
    internal protected delegate Task<int> Handler(TContext context, CancellationToken cancellationToken);

    private readonly ContextFactory<TContext> _contextFactory;
    private CommandLineConfiguration? _config;

    private TContext CreateContext(ParseResult parseResult)
        => _contextFactory(parseResult);

    protected Filter? ContextExceptionHandler { get; set; }

    protected void Configure(RootCommand command, System.Action<CommandLineConfiguration> configure)
    {
        if (_config is not null)
        {
            throw new System.InvalidOperationException();
        }
        _config = new CommandLineConfiguration(command);
        configure?.Invoke(_config);
    }

    protected CommandLine(ContextFactory<TContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    protected CommandHandlerBuilder CreateHandlerBuilder()
        => new CommandHandlerBuilder();

    protected Command CreateCommand(string name, string? description = null)
    {
        return new Command(this, name, description);
    }

    public Task<int> InvokeAsync(string[] args)
        => _config!.InvokeAsync(args);

    protected class Command : System.CommandLine.Command
    {
        private readonly CommandLine<TContext> _commandLine;

        public Command(CommandLine<TContext> commandLine, string name, string? description = null) :
            base(name, description)
        {
            _commandLine = commandLine;
        }

        public Handler? Handler
        {
            get => (base.Action as MyCliAction)?.Handler;
            set => base.Action = value is null ? null : new MyCliAction(_commandLine, value);
        }

        private sealed class MyCliAction : CliAction
        {
            private readonly CommandLine<TContext> _commandLine;
            public Handler Handler { get; }

            public MyCliAction(CommandLine<TContext> commandLine, Handler handler)
                => (_commandLine, Handler) = (commandLine, handler);

            public override int Invoke(ParseResult parseResult)
                => InvokeAsync(parseResult, default).GetAwaiter().GetResult();

            public override Task<int> InvokeAsync(ParseResult parseResult, CancellationToken cancellationToken = default)
            {
                TContext ctx = _commandLine.CreateContext(parseResult);
                if (_commandLine.ContextExceptionHandler is { } eh)
                    return eh(ctx, (c, ct) => Handler(c, ct), cancellationToken);
                else
                    return Handler(ctx, cancellationToken);
            }
        }
    }

    protected delegate Task<int> Filter(TContext context, Handler next, CancellationToken cancellationToken);

    protected class CommandHandlerBuilder
    {
        private List<Filter> _filters = new();
        private Handler? _handler;

        public CommandHandlerBuilder Filter(Filter filter)
        {
            _filters ??= new();
            _filters.Add(filter);
            return this;
        }

        public CommandHandlerBuilder Handle(Handler handler)
        {
            _handler = handler;
            return this;
        }

        public Handler Build()
        {
            Handler handler = _handler ?? throw new System.InvalidOperationException("Handler is not set.");

            if (_filters is not null)
            {
                for (int i = _filters.Count - 1; i >= 0; i--)
                {
                    Filter filter = _filters[i];
                    Handler previousHandler = handler;
                    handler = (c, ct) => filter(c, previousHandler, ct);
                }
            }

            return handler;
        }
    }
}
