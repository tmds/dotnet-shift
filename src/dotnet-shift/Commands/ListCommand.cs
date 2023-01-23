using System.CommandLine;

sealed class ListCommand : Command
{
    public ListCommand() : base("list", "Lists deployed .NET applications")
    {
        this.SetHandler(() => HandleAsync());
    }

    public static async Task HandleAsync()
    {
        var client = new OpenShiftClient();

        var deployments = await client.ListDotnetApplicationsAsync();

        const int NameColumn = 0;
        const int AppColumn = 1;

        string[,] table = new string[2, deployments.Count + 1];
        table[NameColumn, 0] = "NAME";
        table[AppColumn, 0] = "APP";

        for (int i = 0; i < deployments.Count; i++)
        {
            Deployment deployment = deployments[i];
            table[NameColumn, i + 1] = deployment.Labels[ResourceLabels.Name];
            table[AppColumn, i + 1] = deployment.Labels[ResourceLabels.PartOf];
        }

        PrintTable(table);
    }

    private static void PrintTable(string[,] table)
    {
        const int Pad = 3;

        int[] columWidths = new int[table.GetLength(1) - 1];
        for (int x = 0; x < table.GetLength(0) - 1; x++)
        {
            for (int y = 0; y < table.GetLength(1); y++)
            {
                columWidths[x] = Math.Max(columWidths[x], table[x, y].Length);
            }
        }

        for (int y = 0; y < table.GetLength(1); y++)
        {
            for (int x = 0; x < table.GetLength(0); x++)
            {
                Console.Write(table[x, y]);
                if (x < table.GetLength(0) - 1)
                {
                    Console.Write(new string(' ', columWidths[x] + Pad - table[x, y].Length));
                }
            }
            Console.WriteLine();
        }
    }
}