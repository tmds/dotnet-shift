using System.CommandLine;

sealed class DotnetShiftCommand : RootCommand
{
    public DotnetShiftCommand()
    {
        Add(new LoginCommand());
        Add(new DeployCommand());
        Add(new ListCommand());
        Add(new DeleteCommand());
        Add(new NamespaceCommand());
        Add(new SecretCommand());
    }
}
