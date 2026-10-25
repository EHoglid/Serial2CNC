using System.Diagnostics;
using System.Text;
using Serial2CNC.Models;
using System.IO;

namespace Serial2CNC.Services;

public sealed class CncTransferEngine
{
    // The OSP needs time to create the file (or prompt for overwrite and raise XOFF) after the header.
    public const int OspHeaderDelayMs = 500;

    public async Task SendAsync(
        ILineTransport transport,
        TransferOptions transfer,
        Action<TransferProgress> report,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(transfer.FilePath))
        {
            throw new FileNotFoundException("Input file not found.", transfer.FilePath);
        }

        var sourceLines = await File.ReadAllLinesAsync(transfer.FilePath, Encoding.ASCII, cancellationToken);
        var payloadLines = BuildPayloadLines(sourceLines, transfer).ToList();

        report(new TransferProgress
        {
            Message = "Connecting...",
            Current = 0,
            Total = payloadLines.Count
        });

        await transport.OpenAsync(cancellationToken);

        for (var i = 0; i < payloadLines.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = payloadLines[i];
            await transport.SendLineAsync(line, cancellationToken);
            var sentAt = Stopwatch.GetTimestamp();

            report(new TransferProgress
            {
                Message = $"Sending line {i + 1}/{payloadLines.Count}",
                Current = i + 1,
                Total = payloadLines.Count,
                Line = line
            });

            var delayMs = transfer.UseOsp7000lFraming && i == 0
                ? Math.Max(OspHeaderDelayMs, transfer.SendDelayMs)
                : transfer.SendDelayMs;

            if (delayMs > 0 && i < payloadLines.Count - 1)
            {
                await DelayAtLeastAsync(sentAt, TimeSpan.FromMilliseconds(delayMs), cancellationToken);
            }
        }

        report(new TransferProgress
        {
            Message = "Transfer complete.",
            Current = payloadLines.Count,
            Total = payloadLines.Count
        });
    }

    // Timers may fire slightly early (notably on Linux), so keep waiting until the full delay has elapsed.
    private static async Task DelayAtLeastAsync(long startedAt, TimeSpan delay, CancellationToken cancellationToken)
    {
        while (true)
        {
            var remaining = delay - Stopwatch.GetElapsedTime(startedAt);
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1, Math.Ceiling(remaining.TotalMilliseconds))), cancellationToken);
        }
    }

    private static IEnumerable<string> BuildPayloadLines(IEnumerable<string> fileLines, TransferOptions transfer)
    {
        if (transfer.UseOsp7000lFraming)
        {
            var programName = ResolveProgramName(transfer);
            yield return $"${programName}%";
        }

        foreach (var line in fileLines)
        {
            yield return line;
        }

        if (transfer.UseOsp7000lFraming)
        {
            yield return "%";
        }
    }

    private static string ResolveProgramName(TransferOptions transfer)
    {
        if (!string.IsNullOrWhiteSpace(transfer.ProgramName))
        {
            return transfer.ProgramName.Trim().ToUpperInvariant();
        }

        var name = Path.GetFileNameWithoutExtension(transfer.FilePath);
        return $"{name}.MIL".ToUpperInvariant();
    }
}
