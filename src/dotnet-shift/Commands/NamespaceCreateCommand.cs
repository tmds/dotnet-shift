using System.CommandLine;

sealed class NamespaceCreateCommand : Command
{
    public static readonly Argument<string> NamespaceArgument =
        new Argument<string>("NAMESPACE");

    public NamespaceCreateCommand() : base("create", "Create a new namespace and use it")
    {
        Add(NamespaceArgument);

        this.SetHandler((ns) => HandleAsync(ns), NamespaceArgument);
    }

    public static async Task<int> HandleAsync(string ns)
    {
        LoginContext context = KubernetesClientConfigFile.GetDefaultContext();

        // Create the project.
        var client = new OpenShiftClient();
        await client.CreateProjectAsync(ns);

        // Use it as the current namespace.
        context.Namespace = ns;
        KubernetesClientConfigFile.Update(context);

        Console.WriteLine($"Using namespace '{ns}'.");

        return 0;
    }
}
