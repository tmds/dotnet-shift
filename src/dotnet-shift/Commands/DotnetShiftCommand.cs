using System.CommandLine;

sealed class DotnetShiftCommand : RootCommand
{
    public DotnetShiftCommand()
    {
        AddCommand(new LoginCommand());
        AddCommand(new DeploymentsCommand());
    }
}
