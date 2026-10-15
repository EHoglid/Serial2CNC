using Serial2CNC.Models;

namespace Serial2CNC.Cli;

public sealed class CliOptions
{
    public bool ShowHelp { get; init; }

    public required TransferRequest Request { get; init; }

    public static CliOptions CreateDefault() => new()
    {
        ShowHelp = false,
        Request = new TransferRequest
        {
            Mode = ConnectionMode.Tcp,
            Tcp = new TcpConnectionOptions(),
            Serial = new SerialConnectionOptions(),
            Transfer = new TransferOptions
            {
                FilePath = string.Empty,
                SendDelayMs = 0,
                UseOsp7000lFraming = true,
                ProgramName = null
            }
        }
    };

    public static string HelpText =>
        "Serial2CNC usage:\n" +
        "  --mode tcp|serial\n" +
        "  --file <path-to-nc>\n" +
        "  --delay <milliseconds>\n" +
        "  --osp7000l true|false\n" +
        "  --name <program-name>\n" +
        "\nTCP options:\n" +
        "  --ip <address>\n" +
        "  --port <number>\n" +
        "  --xonxoff true|false\n" +
        "\nSerial options:\n" +
        "  --com <port-name>\n" +
        "  --baud <rate>\n" +
        "  --parity None|Odd|Even|Mark|Space\n" +
        "  --databits <5-8>\n" +
        "  --stopbits None|One|Two|OnePointFive\n" +
        "  --handshake None|XOnXOff|RequestToSend|RequestToSendXOnXOff\n" +
        "  --help";
}
