namespace Serial2CNC.Presentation;

public static class TransferFormFields
{
    public const string FilePath = "FilePath";
    public const string SendDelay = "SendDelay";
    public const string TcpHost = "TcpHost";
    public const string TcpPort = "TcpPort";
    public const string ComPort = "ComPort";
}

public sealed record FormValidationError(string Field, string Message);

public sealed class TransferValidationService
{
    public IReadOnlyList<FormValidationError> Validate(TransferFormValues values)
    {
        var errors = new List<FormValidationError>();

        if (string.IsNullOrWhiteSpace(values.FilePath))
        {
            errors.Add(new FormValidationError(TransferFormFields.FilePath, "NC file is required."));
        }

        if (!int.TryParse(values.SendDelay, out var delayMs) || delayMs < 0)
        {
            errors.Add(new FormValidationError(TransferFormFields.SendDelay, "Delay must be a whole number >= 0."));
        }

        if (values.UseTcp)
        {
            if (string.IsNullOrWhiteSpace(values.TcpHost))
            {
                errors.Add(new FormValidationError(TransferFormFields.TcpHost, "TCP IP address is required."));
            }

            if (!int.TryParse(values.TcpPort, out var tcpPort) || tcpPort <= 0)
            {
                errors.Add(new FormValidationError(TransferFormFields.TcpPort, "TCP port must be a whole number > 0."));
            }
        }
        else if (string.IsNullOrWhiteSpace(values.ComPort))
        {
            errors.Add(new FormValidationError(TransferFormFields.ComPort, "COM port is required."));
        }

        return errors;
    }
}
