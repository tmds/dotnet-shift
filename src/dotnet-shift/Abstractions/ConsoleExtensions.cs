static class ConsoleExtensions
{
    public static void WriteErrorLine(this IAnsiConsole console, string value)
    {
        console.MarkupLine($"[red]error[/]: {value}");
    }
}