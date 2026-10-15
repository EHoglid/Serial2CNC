namespace Serial2CNC.Presentation;

public sealed class TransferFormValues
{
    public bool UseTcp { get; init; }

    public string FilePath { get; init; } = string.Empty;

    public string ProgramName { get; init; } = string.Empty;

    public string SendDelay { get; init; } = string.Empty;

    public string TcpHost { get; init; } = string.Empty;

    public string TcpPort { get; init; } = string.Empty;

    public bool TcpUseXonXoff { get; init; }

    public string ComPort { get; init; } = string.Empty;

    public string BaudRate { get; init; } = string.Empty;

    public string DataBits { get; init; } = string.Empty;

    public string Parity { get; init; } = string.Empty;

    public string StopBits { get; init; } = string.Empty;

    public string Handshake { get; init; } = string.Empty;

    public bool UseOsp7000lFraming { get; init; }
}
