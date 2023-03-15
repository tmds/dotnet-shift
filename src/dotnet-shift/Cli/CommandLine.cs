using System.CommandLine;
using System.CommandLine.Invocation;

namespace Cli;

delegate TContext ContextFactory<TContext>(InvocationContext context);

class CommandLine<TContext>
{
    internal protected delegate Task<int> Handler(TContext context, CancellationToken cancellationToken);

    private readonly ContextFactory<TContext> _contextFactory;
    private readonly IConsole _console;

    protected System.CommandLine.Command RootCommand { get; set; }

    protected CommandLine(ContextFactory<TContext> contextFactory, IConsole console)
    {
        RootCommand = null!;
        _console = console;
        _contextFactory = contextFactory;
    }

    protected CommandHandlerBuilder CreateHandlerBuilder()
        => new CommandHandlerBuilder();

    protected Command CreateCommand(string name, string? description = null)
    {
        return new Command(_contextFactory, name, description);
    }

    public Task<int> InvokeAsync(string[] args, CancellationToken cancellationToken = default)
        => RootCommand.InvokeAsync(args, _console, cancellationToken);

    protected class Command : System.CommandLine.Command, ICommandHandler
    {
        private readonly ContextFactory<TContext> _contextFactory;

        public Command(ContextFactory<TContext> contextFactory, string name, string? description = null) :
            base(name, description)
        {
            base.Handler = this;
            _contextFactory = contextFactory;
        }

        public int Invoke(InvocationContext context)
        {
            throw new System.NotImplementedException();
        }

        public new Handler? Handler { get; set; }

        public async Task<int> InvokeAsync(InvocationContext context, CancellationToken cancellationToken = default)
        {
            TContext commandContext = _contextFactory(context);

            return await Handler!(commandContext, cancellationToken);
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
