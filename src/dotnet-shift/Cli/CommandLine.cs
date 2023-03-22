using System.CommandLine;
using System.CommandLine.Invocation;

namespace Cli;

delegate TContext ContextFactory<TContext>(InvocationContext context);

class CommandLine<TContext>
{
    internal protected delegate Task<int> Handler(TContext context, CancellationToken cancellationToken);

    private readonly ContextFactory<TContext> _contextFactory;
    private readonly IConsole _console;
    private CommandLineConfiguration? _config;

    private TContext CreateContext(InvocationContext context)
        => _contextFactory(context);

    protected Filter? ContextExceptionHandler { get; set; }

    protected void Configure(RootCommand command, System.Action<CommandLineBuilder> configure)
    {
        if (_config is not null)
        {
            throw new System.InvalidOperationException();
        }
        var builder = new CommandLineBuilder(command);
        configure?.Invoke(builder);
        _config = builder.Build();
    }

    protected CommandLine(ContextFactory<TContext> contextFactory, IConsole console)
    {
        _console = console;
        _contextFactory = contextFactory;
    }

    protected CommandHandlerBuilder CreateHandlerBuilder()
        => new CommandHandlerBuilder();

    protected Command CreateCommand(string name, string? description = null)
    {
        return new Command(this, name, description);
    }

    public Task<int> InvokeAsync(string[] args)
        => _config!.InvokeAsync(args, _console);

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

            public override int Invoke(InvocationContext context)
                => InvokeAsync(context, default).GetAwaiter().GetResult();

            public override Task<int> InvokeAsync(InvocationContext context, CancellationToken cancellationToken = default)
            {
                TContext ctx = _commandLine.CreateContext(context);
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
