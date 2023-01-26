using System.CommandLine;

sealed class DeploymentCommand : Command
{
    public DeploymentCommand() : base("deployment", "Operate on deployments")
    {
        Add(new DeploymentAddBuildWebHookCommand());
        Add(new DeploymentListCommand());
    }
}
