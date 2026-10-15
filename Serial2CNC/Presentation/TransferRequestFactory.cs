using System.IO.Ports;
using Serial2CNC.Models;

namespace Serial2CNC.Presentation;

public sealed class TransferRequestFactory
{
    public TransferRequest Create(TransferFormValues values)
    {
        var mode = values.UseTcp ? ConnectionMode.Tcp : ConnectionMode.Serial;

        return new TransferRequest
        {
            Mode = mode,
            Tcp = new TcpConnectionOptions
            {
                Host = values.TcpHost.Trim(),
                Port = ParseInt(values.TcpPort, "TCP Port"),
                UseXonXoff = values.TcpUseXonXoff
            },
            Serial = new SerialConnectionOptions
            {
                PortName = ParseRequired(values.ComPort, "COM Port"),
                BaudRate = ParseInt(values.BaudRate, "Baud Rate"),
                DataBits = ParseInt(values.DataBits, "Data Bits"),
                Parity = ParseEnum<Parity>(values.Parity, "Parity"),
                StopBits = ParseEnum<StopBits>(values.StopBits, "Stop Bits"),
                Handshake = ParseEnum<Handshake>(values.Handshake, "Handshake")
            },
            Transfer = new TransferOptions
            {
                FilePath = ParseRequired(values.FilePath, "NC file"),
                SendDelayMs = ParseInt(values.SendDelay, "Delay"),
                UseOsp7000lFraming = values.UseOsp7000lFraming,
                    ProgramName = values.UseOsp7000lFraming && !string.IsNullOrWhiteSpace(values.ProgramName)
                        ? values.ProgramName.Trim()
                        : null
            }
        };
    }

    private static int ParseInt(string value, string fieldName)
    {
        if (!int.TryParse(value, out var result))
        {
            throw new InvalidOperationException($"{fieldName} must be a whole number.");
        }

        return result;
    }

    private static string ParseRequired(string value, string fieldName)
    {
        var result = value?.Trim();
        if (string.IsNullOrWhiteSpace(result))
        {
            throw new InvalidOperationException($"{fieldName} is required.");
        }

        return result;
    }

    private static TEnum ParseEnum<TEnum>(string value, string fieldName)
        where TEnum : struct, Enum
    {
        if (!Enum.TryParse<TEnum>(value, true, out var result))
        {
            throw new InvalidOperationException($"{fieldName} value '{value}' is invalid.");
        }

        return result;
    }
}
