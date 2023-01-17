using System.CommandLine;

sealed class NamespaceSetCommand : Command
{
    public static readonly Argument<string> NamespaceArgument =
        new Argument<string>("NAMESPACE");

    public NamespaceSetCommand() : base("set", "Set the current namespace")
    {
        Add(NamespaceArgument);

        this.SetHandler((ns) => HandleAsync(ns), NamespaceArgument);
    }

    public static async Task<int> HandleAsync(string ns)
    {
        LoginContext context = KubernetesClientConfigFile.GetDefaultContext();

        context.Namespace = ns;

        // TODO: verify the namespace exists.

        KubernetesClientConfigFile.Update(context);

        Console.WriteLine($"Using namespace '{ns}'.");

        return 0;
    }
}
