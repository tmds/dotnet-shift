using System.Text;
using Xunit.Abstractions;
using Spectre.Console;
using Spectre.Console.Testing;
using SimpleExec;
using Cli;

public class TestRunnerBase : TestCleanupBase
{
    protected TestRunnerBase(ITestOutputHelper output) : base(output)
    {
    }

    protected async Task<string[]> RunAsync(string program, params string[] args)
    {
        const string Prefix = "> ";

        Output.WriteLine($"Running '{program} {string.Join(' ', args)}':");

        if (program == "dotnet-shift")
        {
            List<string> standardOutput = new();
            var outputWriter = new TestOutputTextWriter(Output, Prefix, standardOutput);
            var commandLine = AppCommandLine.Create(
                parseResult =>
                {
                    var ansiConsoleSettings = new AnsiConsoleSettings();
                    ansiConsoleSettings.Out = new TestConsoleOutput(outputWriter);
                    var services = new AppServices(ansiConsoleSettings);
                    var context = new Cli.AppContext(parseResult, services);
                    return context;
                });

            int rv = await commandLine.InvokeAsync(args);
            outputWriter.Dispose();

            if (rv != 0)
            {
                throw new ExitCodeReadException(rv, string.Join(Environment.NewLine, standardOutput), "");
            }

            return standardOutput.ToArray();
        }
        else
        {
            var result = await SimpleExec.Command.ReadAsync(program, args);
            Output.WriteLine(PrefixLines(result.StandardOutput, Prefix));
            Output.WriteLine(PrefixLines(result.StandardError, Prefix));
            return SplitLines(result.StandardOutput);
        }

        static string[] SplitLines(string s)
        {
            s = s.Replace("\r\n", "\n");
            return s.Split('\n');
        }

        static string PrefixLines(string s, string prefix)
        {
            s = s.Replace("\r\n", "\n");
            StringBuilder sb = new();
            foreach (var line in s.Split('\n'))
            {
                sb.AppendLine($"{prefix}{line}");
            }
            return sb.ToString();
        }
    }
}