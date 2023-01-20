using System.CommandLine;

sealed class NamespaceCommand : Command
{
    public NamespaceCommand() : base("namespace", "Operate on namespaces")
    {
        Add(new NamespaceCreateCommand());
        Add(new NamespaceListCommand());
        Add(new NamespaceSetCommand());
    }
}
