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

    protected class Command : System.CommandLine.Command
    {
        private readonly ContextFactory<TContext> _contextFactory;

        public Command(ContextFactory<TContext> contextFactory, string name, string? description = null) :
            base(name, description)
        {
            _contextFactory = contextFactory;
        }

        public Handler? Handler
        {
            get => (base.Action as MyCliAction)?.Handler;
            set => base.Action = value is null ? null : new MyCliAction(_contextFactory, value);
        }

        private async Task<int> InvokeAsync(InvocationContext context, CancellationToken cancellationToken = default)
        {
            TContext commandContext = _contextFactory(context);

            return await Handler!(commandContext, cancellationToken);
        }

        private sealed class MyCliAction : CliAction
        {
            private readonly ContextFactory<TContext> _contextFactory;
            public Handler Handler { get; }

            public MyCliAction(ContextFactory<TContext> contextFactory, Handler handler)
                => (_contextFactory, Handler) = (contextFactory, handler);

            public override int Invoke(InvocationContext context)
                => InvokeAsync(context, default).GetAwaiter().GetResult();

            public override Task<int> InvokeAsync(InvocationContext context, CancellationToken cancellationToken = default)
                => Handler(_contextFactory(context), cancellationToken);
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
