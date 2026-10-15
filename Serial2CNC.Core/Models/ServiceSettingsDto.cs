namespace Serial2CNC.Models;

public sealed class ServiceSettingsDto
{
    public string WorkingDirectory { get; init; } = string.Empty;

    public string FileExtensionFilter { get; init; } = "*.*";

    public string TcpHost { get; init; } = "192.168.1.50";

    public int TcpPort { get; init; } = 23;

    public bool TcpUseXonXoff { get; init; } = true;

    public string SerialPortName { get; init; } = "COM1";

    public int SerialBaudRate { get; init; } = 9600;

    public int SerialDataBits { get; init; } = 8;

    public string SerialParity { get; init; } = "None";

    public string SerialStopBits { get; init; } = "One";

    public string SerialHandshake { get; init; } = "None";

    public int SendDelayMs { get; init; }

    public bool UseOsp7000lFraming { get; init; } = true;
}
