using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Serial2CNC.Presentation;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDataErrorInfo
{
    private bool _useTcp = true;
    private string _filePath = string.Empty;
    private string _programName = string.Empty;
    private string _sendDelay = "0";
    private string _tcpHost = "192.168.1.50";
    private string _tcpPort = "23";
    private bool _tcpUseXonXoff = true;
    private string _comPort = string.Empty;
    private string _baudRate = "4800";
    private string _dataBits = "8";
    private string _parity = "None";
    private string _stopBits = "One";
    private string _handshake = "None";
    private bool _useOsp7000lFraming = true;
    private bool _webEnabled;
    private bool _isWebRunning;
    private bool _isWebTransitioning;
    private string _webPort = "62899";
    private string _webStatus = "Web application disabled.";
    private bool _isTransferring;
    private bool _isShuttingDown;
    private bool _showSettings;
    private bool _isProgramNameReadOnly = true;
    private ThemePreference _theme = ThemePreference.System;
    private string _status = "Ready";
    private string _log = string.Empty;
    private double _progress;
    private string[] _availableComPorts = [];
    private readonly Dictionary<string, string> _errors = [];

    public MainWindowViewModel(Func<Task> start, Action cancel, Action browse, Action refreshPorts, Action openWeb)
    {
        StartCommand = new AsyncDelegateCommand(start, () => !IsTransferring && !IsShuttingDown);
        CancelCommand = new DelegateCommand(cancel, () => IsTransferring && !IsShuttingDown);
        BrowseCommand = new DelegateCommand(browse);
        RefreshPortsCommand = new DelegateCommand(refreshPorts);
        OpenWebCommand = new DelegateCommand(openWeb, () => IsWebRunning && !IsWebTransitioning && !IsShuttingDown);
        ToggleSettingsCommand = new DelegateCommand(() => ShowSettings = !ShowSettings);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public AsyncDelegateCommand StartCommand { get; }
    public DelegateCommand CancelCommand { get; }
    public DelegateCommand BrowseCommand { get; }
    public DelegateCommand RefreshPortsCommand { get; }
    public DelegateCommand OpenWebCommand { get; }
    public DelegateCommand ToggleSettingsCommand { get; }

    public bool UseTcp { get => _useTcp; set { if (Set(ref _useTcp, value)) OnPropertyChanged(nameof(UseSerial)); } }
    public bool UseSerial { get => !UseTcp; set { if (value) UseTcp = false; } }
    public string FilePath { get => _filePath; set => Set(ref _filePath, value); }
    public string ProgramName { get => _programName; set => Set(ref _programName, value); }
    public string SendDelay { get => _sendDelay; set => Set(ref _sendDelay, value); }
    public string TcpHost { get => _tcpHost; set => Set(ref _tcpHost, value); }
    public string TcpPort { get => _tcpPort; set => Set(ref _tcpPort, value); }
    public bool TcpUseXonXoff { get => _tcpUseXonXoff; set => Set(ref _tcpUseXonXoff, value); }
    public string ComPort { get => _comPort; set => Set(ref _comPort, value); }
    public string BaudRate { get => _baudRate; set => Set(ref _baudRate, value); }
    public string DataBits { get => _dataBits; set => Set(ref _dataBits, value); }
    public string Parity { get => _parity; set => Set(ref _parity, value); }
    public string StopBits { get => _stopBits; set => Set(ref _stopBits, value); }
    public string Handshake { get => _handshake; set => Set(ref _handshake, value); }
    public bool UseOsp7000lFraming { get => _useOsp7000lFraming; set => Set(ref _useOsp7000lFraming, value); }
    public bool WebEnabled { get => _webEnabled; set => Set(ref _webEnabled, value); }
    public string WebPort { get => _webPort; set => Set(ref _webPort, value); }
    public string WebStatus { get => _webStatus; set => Set(ref _webStatus, value); }
    public bool IsWebRunning { get => _isWebRunning; set { if (Set(ref _isWebRunning, value)) RefreshState(); } }
    public bool IsWebTransitioning { get => _isWebTransitioning; set { if (Set(ref _isWebTransitioning, value)) RefreshState(); } }
    public bool IsTransferring { get => _isTransferring; set { if (Set(ref _isTransferring, value)) RefreshState(); } }
    public bool IsShuttingDown { get => _isShuttingDown; set { if (Set(ref _isShuttingDown, value)) RefreshState(); } }
    public bool CanConfigureWeb => !IsWebTransitioning && !IsShuttingDown;
    public bool CanEditWebPort => CanConfigureWeb && !IsWebRunning;
    public bool IsInteractive => !IsShuttingDown;
    public bool ShowSettings { get => _showSettings; set => Set(ref _showSettings, value); }
    public bool IsProgramNameReadOnly { get => _isProgramNameReadOnly; set => Set(ref _isProgramNameReadOnly, value); }
    public ThemePreference Theme { get => _theme; set { if (Set(ref _theme, value)) { OnPropertyChanged(nameof(IsSystemTheme)); OnPropertyChanged(nameof(IsLightTheme)); OnPropertyChanged(nameof(IsDarkTheme)); } } }
    public bool IsSystemTheme { get => Theme == ThemePreference.System; set { if (value) Theme = ThemePreference.System; } }
    public bool IsLightTheme { get => Theme == ThemePreference.Light; set { if (value) Theme = ThemePreference.Light; } }
    public bool IsDarkTheme { get => Theme == ThemePreference.Dark; set { if (value) Theme = ThemePreference.Dark; } }
    public string Status { get => _status; set => Set(ref _status, value); }
    public string Log { get => _log; set => Set(ref _log, value); }
    public double Progress { get => _progress; set => Set(ref _progress, value); }
    public string[] AvailableComPorts { get => _availableComPorts; set => Set(ref _availableComPorts, value); }
    public string[] ParityOptions { get; } = ["None", "Odd", "Even", "Mark", "Space"];
    public string[] StopBitsOptions { get; } = ["None", "One", "Two", "OnePointFive"];
    public string[] HandshakeOptions { get; } = ["None", "XOnXOff", "RequestToSend", "RequestToSendXOnXOff"];
    public string[] BaudRateOptions { get; } = ["110", "300", "600", "1200", "2400", "4800", "9600", "19200", "38400", "57600", "115200"];

    public string Error => string.Join(Environment.NewLine, _errors.Values);
    public string this[string columnName] => _errors.GetValueOrDefault(columnName, string.Empty);
    public string FilePathError => this[nameof(FilePath)];
    public string TcpHostError => this[nameof(TcpHost)];
    public string TcpPortError => this[nameof(TcpPort)];

    public void SetValidationErrors(IEnumerable<FormValidationError> errors)
    {
        _errors.Clear();
        foreach (var error in errors) _errors[error.Field] = error.Message;
        foreach (var property in new[] { nameof(FilePath), nameof(SendDelay), nameof(TcpHost), nameof(TcpPort), nameof(ComPort) })
            OnPropertyChanged(property);
        NotifyErrors();
    }

    public TransferFormValues CaptureFormValues() => new()
    {
        UseTcp = UseTcp, FilePath = FilePath, ProgramName = ProgramName, SendDelay = SendDelay,
        TcpHost = TcpHost, TcpPort = TcpPort, TcpUseXonXoff = TcpUseXonXoff, ComPort = ComPort,
        BaudRate = BaudRate, DataBits = DataBits, Parity = Parity, StopBits = StopBits,
        Handshake = Handshake, UseOsp7000lFraming = UseOsp7000lFraming
    };

    public void LoadFormValues(TransferFormValues form)
    {
        UseTcp = form.UseTcp;
        FilePath = form.FilePath;
        ProgramName = form.ProgramName;
        SendDelay = form.SendDelay;
        TcpHost = form.TcpHost;
        TcpPort = form.TcpPort;
        TcpUseXonXoff = form.TcpUseXonXoff;
        ComPort = form.ComPort;
        BaudRate = form.BaudRate;
        DataBits = form.DataBits;
        Parity = form.Parity;
        StopBits = form.StopBits;
        Handshake = form.Handshake;
        UseOsp7000lFraming = form.UseOsp7000lFraming;
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        if (_errors.Remove(propertyName)) NotifyErrors();
        OnPropertyChanged(propertyName);
        return true;
    }

    private void NotifyErrors()
    {
        OnPropertyChanged(nameof(FilePathError));
        OnPropertyChanged(nameof(TcpHostError));
        OnPropertyChanged(nameof(TcpPortError));
        OnPropertyChanged(nameof(Error));
    }

    private void RefreshState()
    {
        OnPropertyChanged(nameof(CanConfigureWeb));
        OnPropertyChanged(nameof(CanEditWebPort));
        OnPropertyChanged(nameof(IsInteractive));
        StartCommand.Refresh();
        CancelCommand.Refresh();
        OpenWebCommand.Refresh();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
