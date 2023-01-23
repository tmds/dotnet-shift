using System.CommandLine;

sealed class DeleteCommand : Command
{
    public DeleteCommand() : base("delete", "Delete a deployed application")
    {
        Add(NameArgument);

        this.SetHandler((string name) => HandleAsync(name), NameArgument);
    }

    public static async Task HandleAsync(string appName)
    {
        var client = new OpenShiftClient();

        // TODO: move logic into the handler to give feedback to the user on what gets deleted.
        await client.DeleteApplicationAsync(appName);
    }

    public static readonly Argument<string> NameArgument =
        new Argument<string>("APP", "Name of the application to delete");
}