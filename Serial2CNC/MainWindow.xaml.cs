using System.IO;
using System.IO.Ports;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using Serial2CNC.Models;
using Serial2CNC.Presentation;
using Serial2CNC.Services;
using Serial2CNC.Web;
using Microsoft.AspNetCore.Builder;

namespace Serial2CNC
{
    public partial class MainWindow : Window
    {
        private CancellationTokenSource? _transferCts;
        private readonly TransferValidationService _validationService = new();
        private readonly TransferRequestFactory _requestFactory = new();
        private readonly TransferExecutionService _executionService = new();
        private WebApplication? _webApplication;
        private Task _webHostingTask = Task.CompletedTask;
        private Task _activeTransferTask = Task.CompletedTask;
        private bool _closingAfterShutdown;
        private bool _settingsLoaded;
        private MainWindowViewModel ViewModel { get; }

        public MainWindow()
        {
            ViewModel = new MainWindowViewModel(StartAsync, () => _transferCts?.Cancel(), BrowseFile, LoadAvailableComPorts, OpenWeb);
            DataContext = ViewModel;
            InitializeComponent();
            SourceInitialized += (_, _) => ApplyTitleBarTheme();
            Loaded += (_, _) => SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
            Closed += (_, _) => SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            if (Application.Current is App app)
            {
                ViewModel.Theme = app.CurrentThemePreference;
            }
            LoadAvailableComPorts();
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = DesktopSettings.Load();
                if (settings.Transfer is { } form)
                {
                    ViewModel.LoadFormValues(form);
                }
                if (Application.Current is App app)
                {
                    app.SetThemePreference(settings.Theme);
                    ViewModel.Theme = settings.Theme;
                }
                ViewModel.WebPort = settings.WebPort.ToString();
                ViewModel.WebEnabled = settings.WebEnabled;
                _settingsLoaded = true;
                _webHostingTask = ApplyWebHostingAsync();
                await _webHostingTask;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                MessageBox.Show(this, $"Unable to load settings. The saved file has not been changed.\n{ex.Message}",
                    "Settings error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveSettings()
        {
            if (!_settingsLoaded)
            {
                throw new InvalidOperationException("Settings were not loaded. The saved settings will not be overwritten.");
            }
            if (!int.TryParse(ViewModel.WebPort, out var port) || port is < 1 or > 65535)
            {
                throw new InvalidOperationException("The web port must be between 1 and 65535.");
            }
            new DesktopSettings
            {
                Transfer = ViewModel.CaptureFormValues(),
                Theme = ViewModel.Theme,
                WebEnabled = ViewModel.WebEnabled,
                WebPort = port
            }.Save();
        }

        private async void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.Theme) && Application.Current is App app)
            {
                app.SetThemePreference(ViewModel.Theme);
            }
            if (e.PropertyName == nameof(MainWindowViewModel.UseTcp) && ViewModel.UseSerial)
            {
                LoadAvailableComPorts();
            }
            if (e.PropertyName == nameof(MainWindowViewModel.Log))
            {
                _ = Dispatcher.BeginInvoke(new Action(LogTextBox.ScrollToEnd));
            }
            if (e.PropertyName != nameof(MainWindowViewModel.WebEnabled)
                || !_settingsLoaded || ViewModel.IsWebTransitioning || ViewModel.IsShuttingDown)
            {
                return;
            }
            _webHostingTask = ApplyWebHostingAsync();
            await _webHostingTask;
            if (!ViewModel.IsShuttingDown)
            {
                TrySaveSettings();
            }
        }

        private async Task ApplyWebHostingAsync()
        {
            ViewModel.IsWebTransitioning = true;
            try
            {
                if (ViewModel.WebEnabled && _webApplication is null)
                {
                    if (!int.TryParse(ViewModel.WebPort, out var port) || port is < 1 or > 65535)
                    {
                        throw new InvalidOperationException("The web port must be between 1 and 65535.");
                    }
                    var host = WebHostFactory.Create([], Path.Combine(DesktopSettings.DataDirectory, "Web"), port);
                    try
                    {
                        await host.StartAsync();
                    }
                    catch
                    {
                        await host.DisposeAsync();
                        throw;
                    }
                    _webApplication = host;
                    ViewModel.WebStatus = $"Web app running: http://{Environment.MachineName}:{port} (allow private-network access in Windows Firewall).";
                }
                else if (!ViewModel.WebEnabled && _webApplication is not null)
                {
                    await _webApplication.StopAsync();
                    await _webApplication.DisposeAsync();
                    _webApplication = null;
                    ViewModel.WebStatus = "Web application disabled.";
                }
            }
            catch (Exception ex)
            {
                ViewModel.WebEnabled = _webApplication is not null;
                ViewModel.WebStatus = $"Web application error: {ex.Message}";
                AppendLog(ViewModel.WebStatus);
            }
            finally
            {
                ViewModel.IsWebRunning = _webApplication is not null;
                ViewModel.IsWebTransitioning = false;
            }
        }

        private void OpenWeb()
        {
            if (_webApplication is null)
            {
                ViewModel.WebStatus = "Enable the web application first.";
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo($"http://localhost:{ViewModel.WebPort}") { UseShellExecute = true });
            }
            catch (Win32Exception ex)
            {
                ViewModel.WebStatus = $"Unable to open browser: {ex.Message}";
            }
        }

        private bool TrySaveSettings()
        {
            try
            {
                SaveSettings();
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                MessageBox.Show(this, $"Unable to save settings.\n{ex.Message}", "Settings error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private async void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (_closingAfterShutdown)
            {
                return;
            }
            e.Cancel = true;
            if (ViewModel.IsShuttingDown)
            {
                return;
            }
            if (!TrySaveSettings() && MessageBox.Show(this, "Close without saving settings?", "Settings error",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }
            ViewModel.IsShuttingDown = true;
            _transferCts?.Cancel();
            try
            {
                try
                {
                    await _activeTransferTask;
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    AppendLog($"Transfer ended with an error during shutdown: {ex.Message}");
                }
                await _webHostingTask;
                if (_webApplication is not null)
                {
                    await _webApplication.StopAsync();
                    await _webApplication.DisposeAsync();
                    _webApplication = null;
                    ViewModel.IsWebRunning = false;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Unable to stop the web application cleanly.\n{ex.Message}", "Web application error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            _closingAfterShutdown = true;
            _ = Dispatcher.BeginInvoke(new Action(Close));
        }

        private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            Dispatcher.Invoke(ApplyTitleBarTheme);
        }

        private void ApplyTitleBarTheme()
        {
            var isDark = !IsWindowsLightTheme();
            var useDark = isDark ? 1 : 0;
            var hwnd = new WindowInteropHelper(this).Handle;

            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            _ = DwmSetWindowAttribute(hwnd, DwmaUseImmersiveDarkMode, ref useDark, sizeof(int));
            _ = DwmSetWindowAttribute(hwnd, DwmaUseImmersiveDarkModeBefore20H1, ref useDark, sizeof(int));
        }

        private static bool IsWindowsLightTheme()
        {
            const string personalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
            const string valueName = "AppsUseLightTheme";

            using var key = Registry.CurrentUser.OpenSubKey(personalizeKeyPath);
            return key?.GetValue(valueName) is int value ? value != 0 : true;
        }

        private const int DwmaUseImmersiveDarkMode = 20;
        private const int DwmaUseImmersiveDarkModeBefore20H1 = 19;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        private void ProgramNameTextBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ViewModel.IsProgramNameReadOnly = false;
            ProgramNameTextBox.Focus();
            ProgramNameTextBox.SelectAll();
        }

        private void ProgramNameTextBox_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            ViewModel.IsProgramNameReadOnly = true;
        }

        private void BrowseFile()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "NC Files (*.nc)|*.nc|All Files (*.*)|*.*"
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            ViewModel.FilePath = dialog.FileName;
            ViewModel.ProgramName = $"{Path.GetFileNameWithoutExtension(dialog.FileName)}.MIL".ToUpperInvariant();
        }

        private async Task StartAsync()
        {
            if (_transferCts is not null)
            {
                return;
            }

            var formValues = ViewModel.CaptureFormValues();
            if (!TrySaveSettings())
            {
                return;
            }

            var validationErrors = _validationService.Validate(formValues);
            ViewModel.SetValidationErrors(validationErrors);
            if (validationErrors.Count > 0)
            {
                ViewModel.Status = "Invalid settings.";
                foreach (var error in validationErrors)
                {
                    AppendLog($"Invalid settings: {error.Message}");
                }

                return;
            }

            TransferRequest request;
            try
            {
                request = _requestFactory.Create(formValues);
            }
            catch (Exception ex)
            {
                ViewModel.Status = "Invalid settings.";
                AppendLog($"Invalid settings: {ex.Message}");
                return;
            }

            ViewModel.IsTransferring = true;
            ViewModel.Log = string.Empty;
            AppendLog($"Line delay: {request.Transfer.SendDelayMs} ms between lines."
                + (request.Mode == ConnectionMode.Tcp ? " TCP Nagle buffering disabled." : string.Empty));
            ViewModel.Status = "Starting transfer...";
            ViewModel.Progress = 0;

            _transferCts = new CancellationTokenSource();

            try
            {
                _activeTransferTask = _executionService.RunAsync(
                    request,
                    progress => Dispatcher.Invoke(() => ReportProgress(progress)),
                    status => Dispatcher.Invoke(() => AppendLog(status)),
                    _transferCts.Token);
                await _activeTransferTask;

                ViewModel.Status = "Transfer complete.";
            }
            catch (OperationCanceledException)
            {
                ViewModel.Status = "Transfer canceled.";
                AppendLog("Transfer canceled.");
            }
            catch (Exception ex)
            {
                ViewModel.Status = "Transfer failed.";
                AppendLog($"Transfer failed: {ex.Message}");
            }
            finally
            {
                _activeTransferTask = Task.CompletedTask;
                _transferCts.Dispose();
                _transferCts = null;
                ViewModel.IsTransferring = false;
            }
        }

        private void ReportProgress(TransferProgress progress)
        {
            ViewModel.Status = progress.Message;

            if (progress.Total > 0)
            {
                ViewModel.Progress = (progress.Current * 100.0) / progress.Total;
            }
            else
            {
                ViewModel.Progress = 0;
            }

            if (!string.IsNullOrWhiteSpace(progress.Line))
            {
                AppendLog(progress.Line);
            }
        }

        private void AppendLog(string line)
        {
            ViewModel.Log += line + Environment.NewLine;
        }

        private void LoadAvailableComPorts()
        {
            var selected = ViewModel.ComPort;
            var ports = GetAvailableComPorts();
            ViewModel.AvailableComPorts = ports;

            if (!string.IsNullOrWhiteSpace(selected))
            {
                ViewModel.ComPort = selected;
            }
            else if (ports.Length > 0)
            {
                ViewModel.ComPort = ports[0];
            }
            else
            {
                ViewModel.ComPort = "COM1";
            }
        }

        private static string[] GetAvailableComPorts()
        {
            var ports = SerialPort.GetPortNames();
            Array.Sort(ports, StringComparer.OrdinalIgnoreCase);
            return ports;
        }

    }
}