using Serial2CNC.Services;

namespace Serial2CNC.Cli;

public static class CliRunner
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        CliOptions options;
        try
        {
            options = CliParser.Parse(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.WriteLine(CliOptions.HelpText);
            return 1;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(CliOptions.HelpText);
            return 0;
        }

        if (string.IsNullOrWhiteSpace(options.Request.Transfer.FilePath))
        {
            Console.Error.WriteLine("--file is required in command mode.");
            return 1;
        }

        try
        {
            var runner = new TransferRunner();
            await runner.RunAsync(
                options.Request,
                progress =>
                {
                    if (!string.IsNullOrWhiteSpace(progress.Line))
                    {
                        Console.WriteLine(progress.Line);
                    }

                    var percent = progress.Total <= 0
                        ? 0
                        : (progress.Current * 100.0) / progress.Total;

                    Console.WriteLine($"{progress.Message} ({percent:F1}%)");
                },
                status => Console.WriteLine(status),
                cancellationToken);

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Transfer failed: {ex.Message}");
            return 2;
        }
    }
}
