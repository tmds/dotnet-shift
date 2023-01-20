using System.CommandLine;

sealed class SecretCommand : Command
{
    public SecretCommand() : base("secret", "Operate on secrets")
    {
        // TODO: add list and delete commands.
        Add(new SecretAddSshCommand());
    }
}
