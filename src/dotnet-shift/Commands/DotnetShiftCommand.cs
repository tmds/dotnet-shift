class DotnetShiftCommand : System.CommandLine.RootCommand
{
    public DotnetShiftCommand()
    {
        AddCommand(new DeploymentsCommand());
    }
}
