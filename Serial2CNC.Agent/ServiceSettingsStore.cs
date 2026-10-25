using System.Text.Json;
using Serial2CNC.Models;

namespace Serial2CNC.Agent;

public sealed class ServiceSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly string _settingsFilePath;
    private ServiceSettingsDto _settings;
    private readonly ILogger<ServiceSettingsStore> _logger;

    public ServiceSettingsStore(string dataDirectory, ILogger<ServiceSettingsStore> logger)
    {
        _logger = logger;
        _settingsFilePath = Path.Combine(dataDirectory, "service-settings.json");
        _settings = LoadFromDiskOrDefault();
    }

    public ServiceSettingsDto Get()
    {
        return _settings;
    }

    public async Task<ServiceSettingsDto> UpdateAsync(UpdateServiceSettingsRequest request, CancellationToken cancellationToken)
    {
        var normalized = Normalize(request);

        await _sync.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_settingsFilePath)!;
            Directory.CreateDirectory(directory);

            var temporaryPath = _settingsFilePath + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(normalized, JsonOptions), cancellationToken);
            File.Move(temporaryPath, _settingsFilePath, overwrite: true);
            _settings = normalized;

            return _settings;
        }
        finally
        {
            _sync.Release();
        }
    }

    private ServiceSettingsDto LoadFromDiskOrDefault()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                return CreateDefault();
            }

            var json = File.ReadAllText(_settingsFilePath);
            var loaded = JsonSerializer.Deserialize<ServiceSettingsDto>(json, JsonOptions);

            if (loaded is null)
            {
                throw new JsonException("The settings file is empty.");
            }

            return Normalize(new UpdateServiceSettingsRequest
            {
                WorkingDirectory = loaded.WorkingDirectory,
                FileExtensionFilter = loaded.FileExtensionFilter,
                TcpHost = loaded.TcpHost,
                TcpPort = loaded.TcpPort,
                TcpUseXonXoff = loaded.TcpUseXonXoff,
                SerialPortName = loaded.SerialPortName,
                SerialBaudRate = loaded.SerialBaudRate,
                SerialDataBits = loaded.SerialDataBits,
                SerialParity = loaded.SerialParity,
                SerialStopBits = loaded.SerialStopBits,
                SerialHandshake = loaded.SerialHandshake,
                SendDelayMs = loaded.SendDelayMs,
                UseOsp7000lFraming = loaded.UseOsp7000lFraming
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogError(ex, "Unable to load settings from {SettingsFile}.", _settingsFilePath);
            throw;
        }
    }

    private static ServiceSettingsDto Normalize(UpdateServiceSettingsRequest request)
    {
        return new ServiceSettingsDto
        {
            WorkingDirectory = request.WorkingDirectory.Trim(),
            FileExtensionFilter = NormalizeFileExtensionFilter(request.FileExtensionFilter),
            TcpHost = string.IsNullOrWhiteSpace(request.TcpHost) ? "192.168.1.50" : request.TcpHost.Trim(),
            TcpPort = request.TcpPort > 0 ? request.TcpPort : 23,
            TcpUseXonXoff = request.TcpUseXonXoff,
            SerialPortName = string.IsNullOrWhiteSpace(request.SerialPortName) ? "COM1" : request.SerialPortName.Trim(),
            SerialBaudRate = request.SerialBaudRate > 0 ? request.SerialBaudRate : 9600,
            SerialDataBits = request.SerialDataBits > 0 ? request.SerialDataBits : 8,
            SerialParity = NormalizeChoice(request.SerialParity, "None", ["None", "Odd", "Even", "Mark", "Space"]),
            SerialStopBits = NormalizeChoice(request.SerialStopBits, "One", ["None", "One", "Two", "OnePointFive"]),
            SerialHandshake = NormalizeChoice(request.SerialHandshake, "None", ["None", "XOnXOff", "RequestToSend", "RequestToSendXOnXOff"]),
            SendDelayMs = request.SendDelayMs < 0 ? 0 : request.SendDelayMs,
            UseOsp7000lFraming = request.UseOsp7000lFraming
        };
    }

    private static ServiceSettingsDto CreateDefault()
    {
        return new ServiceSettingsDto
        {
            WorkingDirectory = string.Empty,
            FileExtensionFilter = "*.*",
            TcpHost = "192.168.1.50",
            TcpPort = 23,
            TcpUseXonXoff = true,
            SerialPortName = "COM1",
            SerialBaudRate = 9600,
            SerialDataBits = 8,
            SerialParity = "None",
            SerialStopBits = "One",
            SerialHandshake = "None",
            SendDelayMs = 0,
            UseOsp7000lFraming = true
        };
    }

    private static string NormalizeChoice(string value, string fallback, IEnumerable<string> allowed)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return allowed.FirstOrDefault(item => item.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase)) ?? fallback;
    }

    private static string NormalizeFileExtensionFilter(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return "*.*";
        }

        var normalized = filter
            .Split([';', ',', '|', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Trim().ToLowerInvariant())
            .Select(part => part switch
            {
                "*" => "*.*",
                "*.*" => "*.*",
                _ when part.StartsWith("*.") => part,
                _ when part.StartsWith('.') => $"*{part}",
                _ => $"*.{part.TrimStart('*', '.')}"
            })
            .Select(part => part.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized.Length == 0 ? "*.*" : string.Join(';', normalized);
    }

}
