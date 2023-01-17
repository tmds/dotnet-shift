using System.CommandLine;

sealed class DotnetShiftCommand : RootCommand
{
    public DotnetShiftCommand()
    {
        AddCommand(new LoginCommand());
        AddCommand(new DeployCommand());
        AddCommand(new ListCommand());
        AddCommand(new DeleteCommand());
        AddCommand(new NamespaceCommand());
    }
}
