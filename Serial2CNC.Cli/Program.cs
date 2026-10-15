namespace Serial2CNC.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        return await CliRunner.RunAsync(args, CancellationToken.None);
    }
}
