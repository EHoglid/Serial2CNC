using System.Globalization;
using System.IO.Ports;
using Serial2CNC.Models;

namespace Serial2CNC.Cli;

public static class CliParser
{
    public static CliOptions Parse(string[] args)
    {
        var options = CliOptions.CreateDefault();

        if (args.Length == 0)
        {
            return options;
        }

        var mode = options.Request.Mode;
        var tcp = options.Request.Tcp;
        var serial = options.Request.Serial;

        var filePath = options.Request.Transfer.FilePath;
        var delayMs = options.Request.Transfer.SendDelayMs;
        var useOsp = options.Request.Transfer.UseOsp7000lFraming;
        var programName = options.Request.Transfer.ProgramName;

        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];

            if (!key.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            if (key.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("/?", StringComparison.OrdinalIgnoreCase))
            {
                return new CliOptions
                {
                    ShowHelp = true,
                    Request = options.Request
                };
            }

            if (i + 1 >= args.Length)
            {
                throw new ArgumentException($"Missing value for {key}.");
            }

            var value = args[++i];

            switch (key.ToLowerInvariant())
            {
                case "--mode":
                    mode = value.Equals("serial", StringComparison.OrdinalIgnoreCase)
                        ? ConnectionMode.Serial
                        : ConnectionMode.Tcp;
                    break;
                case "--file":
                    filePath = value;
                    break;
                case "--delay":
                    delayMs = int.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "--osp7000l":
                    useOsp = bool.Parse(value);
                    break;
                case "--name":
                    programName = value;
                    break;

                case "--ip":
                    tcp.Host = value;
                    break;
                case "--port":
                    tcp.Port = int.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "--xonxoff":
                    tcp.UseXonXoff = bool.Parse(value);
                    break;

                case "--com":
                    serial.PortName = value;
                    break;
                case "--baud":
                    serial.BaudRate = int.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "--parity":
                    serial.Parity = Enum.Parse<Parity>(value, true);
                    break;
                case "--databits":
                    serial.DataBits = int.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "--stopbits":
                    serial.StopBits = Enum.Parse<StopBits>(value, true);
                    break;
                case "--handshake":
                    serial.Handshake = Enum.Parse<Handshake>(value, true);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {key}");
            }
        }

        var request = new TransferRequest
        {
            Mode = mode,
            Tcp = tcp,
            Serial = serial,
            Transfer = new TransferOptions
            {
                FilePath = filePath,
                SendDelayMs = delayMs,
                UseOsp7000lFraming = useOsp,
                ProgramName = programName
            }
        };

        return new CliOptions
        {
            ShowHelp = false,
            Request = request
        };
    }
}
