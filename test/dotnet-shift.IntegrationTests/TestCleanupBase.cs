using System.Text;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;
using Spectre.Console;
using CommandHandlers;
using OpenShift;

public class TestCleanupBase : TestOutputBase, IDisposable
{
    private readonly List<string> _appsToRemove = new();
    private readonly List<string> _directoriesToDelete = new();

    protected TestCleanupBase(ITestOutputHelper output) : base(output)
    { }

    protected void AddDirectoryToDelete(string directory) => _directoriesToDelete.Add(directory);

    protected void AddAppToRemove(string app) => _appsToRemove.Add(app);

    protected string GenerateAppName([CallerMemberName]string? caller = null)
    {                                                      
        string name = $"shift-test-{caller!.ToLowerInvariant()}-{RandomString(6)}";
        AddAppToRemove(name);
        return name;
    }

    protected string GenerateDirectoryName([CallerMemberName]string? caller = null)
    {
        string name = Path.Combine(Path.GetTempPath(), $"shift-{caller!.ToLowerInvariant()}-{RandomString(6)}");
        AddDirectoryToDelete(name);
        return name;
    }

    private static string RandomString(int length)
    {
        StringBuilder sb = new();
        for (int i = 0; i < length; i++)
        {
            sb.Append((char)('a' + Random.Shared.Next('z' - 'a')));
        }
        return sb.ToString();
    }

    public void Dispose()
    {
        DisposeAsync().GetAwaiter().GetResult();
    }

    private async ValueTask DisposeAsync()
    {
        foreach (var dir in _directoriesToDelete)
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            { }
        }

        try
        {
            LoginContext context = TestContext.LoginContext;
            foreach (var app in _appsToRemove)
            {
                DeleteHandler handler = new DeleteHandler(AnsiConsole.Console, NullLogger.Instance, new OpenShiftClientFactory());
                try
                {
                    await handler.ExecuteAsync(context, app, force: true, default);
                }
                catch (Exception ex)
                {
                    Output.WriteLine($"Exception while deleting application {app}:" + Environment.NewLine + ex);
                }
            }
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Exception while deleting apps:" + Environment.NewLine + ex);
        }
    }
}