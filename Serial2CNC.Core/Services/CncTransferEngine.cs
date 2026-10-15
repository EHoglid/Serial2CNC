using System.Text;
using Serial2CNC.Models;
using System.IO;

namespace Serial2CNC.Services;

public sealed class CncTransferEngine
{
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

            report(new TransferProgress
            {
                Message = $"Sending line {i + 1}/{payloadLines.Count}",
                Current = i + 1,
                Total = payloadLines.Count,
                Line = line
            });

            if (transfer.SendDelayMs > 0 && i < payloadLines.Count - 1)
            {
                await Task.Delay(transfer.SendDelayMs, cancellationToken);
            }
        }

        report(new TransferProgress
        {
            Message = "Transfer complete.",
            Current = payloadLines.Count,
            Total = payloadLines.Count
        });
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
