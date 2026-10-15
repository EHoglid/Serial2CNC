namespace Serial2CNC.Models;

public sealed class TcpConnectionOptions
{
    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 23;

    public bool UseXonXoff { get; set; } = true;
}
