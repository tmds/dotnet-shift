using System.Net;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

public class IntegrationTests : TestRunnerBase
{
    public IntegrationTests(ITestOutputHelper output) : base(output)
    { }

    [Fact]
    public async Task Deploy()
    {
        string projectDirectory = GenerateDirectoryName();
        string componentName = GenerateAppName();

        // Create a .NET project.
        await RunAsync("dotnet", "new", "web", "-o", projectDirectory, "-n", componentName);
        File.WriteAllText(Path.Combine(projectDirectory, "Program.cs"), GenerateProgramCs("Reply1"));

        // Deploy the application.
        string[] output = await RunAsync("dotnet-shift", "deploy", "--expose", "--context", TestContext.ContextName, projectDirectory);
        string url = GetRouteUrlFromDeployOutput(output);
        // Verify we get a reply from the app.
        await VerifyApplicationReplyAsync(url, "Reply1");

        // Update the .NET project.
        File.WriteAllText(Path.Combine(projectDirectory, "Program.cs"), GenerateProgramCs("Reply2"));

        // Re-deploy the application.
        await RunAsync("dotnet-shift", "deploy", "--context", TestContext.ContextName, projectDirectory);
        // Verify we get the updated reply.
        await VerifyApplicationReplyAsync(url, "Reply2");

        // List the deployed resources.
        output = await RunAsync("dotnet-shift", "list", "--context", TestContext.ContextName);
        Assert.Contains(output, line => line.Contains(componentName));

        // Delete the deployment.
        await RunAsync("dotnet-shift", "delete", "--context", TestContext.ContextName, componentName);

        // List the deployed resources.
        output = await RunAsync("dotnet-shift", "list", "--context", TestContext.ContextName);
        Assert.DoesNotContain(output, line => line.Contains(componentName));
    }

    private async Task VerifyApplicationReplyAsync(string url, string expectedReply)
    {
        await RetryHelper.ExecuteAsync(async () =>
            {
                using var httpClient = new HttpClient();
                var response = await httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                Assert.Equal(expectedReply, await response.Content.ReadAsStringAsync());
            },
            retryWhen: ex => ex is HttpRequestException re && re.StatusCode == HttpStatusCode.ServiceUnavailable,
            maxAttempts: 10);
    }

    private static string GenerateProgramCs(string reply)
    {
        return $$"""
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();

        app.MapGet("/", () => "{{reply}}");

        app.Run();
        """;
    }

    private static string GetRouteUrlFromDeployOutput(string[] output)
    {
        string line = output.Last();
        Assert.Contains("is exposed at", line);
        var match = Regex.Match(line, "'(https?://.*)'");
        Assert.NotNull(match);
        Assert.Equal(2, match.Groups.Count);
        string url = match.Groups[1].Value;
        return url;
    }
}
