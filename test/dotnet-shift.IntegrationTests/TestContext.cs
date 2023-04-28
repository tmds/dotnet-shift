using Microsoft.Build.Locator;

static class TestContext
{
    static TestContext()
    {
        MSBuildLocator.RegisterDefaults();
    }

    private static Lazy<LoginContext> _context = new(() =>
    {
        string? contextName = Environment.GetEnvironmentVariable("TEST_CONTEXT");
        if (contextName is null)
        {
            throw new InvalidOperationException("TEST_CONTEXT is not set.");
        }
        LoginContext? context = new Kubectl.KubernetesConfigFile().GetContext(contextName);
        if (context == null)
        {
            throw new InvalidOperationException($"TEST_CONTEXT {contextName} is not found.");
        }
        return context!;
    });

    public static string ContextName => LoginContext.Name;

    public static LoginContext LoginContext => _context.Value;
}