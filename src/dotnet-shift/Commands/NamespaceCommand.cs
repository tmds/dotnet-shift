using System.CommandLine;

sealed class NamespaceCommand : Command
{
    public NamespaceCommand() : base("namespace", "Operate on the namespace")
    {
        Add(new NamespaceCreateCommand());
        Add(new NamespaceListCommand());
        Add(new NamespaceSetCommand());
    }
}
