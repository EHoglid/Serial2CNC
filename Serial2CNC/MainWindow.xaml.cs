using System.IO;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using Serial2CNC.Models;
using Serial2CNC.Presentation;
using Serial2CNC.Services;

namespace Serial2CNC
{
    public partial class MainWindow : Window
    {
        private CancellationTokenSource? _transferCts;
        private readonly Dictionary<Control, ControlValidationState> _validationStateByControl = [];
        private readonly Dictionary<Control, TextBlock> _errorTextByControl = [];
        private readonly TransferValidationService _validationService = new();
        private readonly TransferRequestFactory _requestFactory = new();
        private readonly TransferExecutionService _executionService = new();

        public MainWindow()
        {
            InitializeComponent();
            SourceInitialized += (_, _) => ApplyTitleBarTheme();
            Loaded += (_, _) => SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
            Closed += (_, _) => SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            HookValidationClearHandlers();
            ApplyThemeSelectionFromApp();
            LoadAvailableComPorts();
        }

        private void ApplyThemeSelectionFromApp()
        {
            if (Application.Current is not App app)
            {
                return;
            }

            SetThemeRadioChecked("ThemeSystemRadio", app.CurrentThemePreference == ThemePreference.System);
            SetThemeRadioChecked("ThemeLightRadio", app.CurrentThemePreference == ThemePreference.Light);
            SetThemeRadioChecked("ThemeDarkRadio", app.CurrentThemePreference == ThemePreference.Dark);
        }

        private void SetThemeRadioChecked(string radioName, bool isChecked)
        {
            if (FindName(radioName) is RadioButton radio)
            {
                radio.IsChecked = isChecked;
            }
        }

        private void HookValidationClearHandlers()
        {
            _errorTextByControl[TcpIpTextBox] = TcpIpErrorText;
            _errorTextByControl[TcpPortTextBox] = TcpPortErrorText;
            _errorTextByControl[FilePathTextBox] = FilePathErrorText;

            TcpIpTextBox.TextChanged += (_, _) => ClearControlError(TcpIpTextBox);
            TcpPortTextBox.TextChanged += (_, _) => ClearControlError(TcpPortTextBox);
            FilePathTextBox.TextChanged += (_, _) => ClearControlError(FilePathTextBox);
            SendDelayTextBox.TextChanged += (_, _) => ClearControlError(SendDelayTextBox);
            ComPortComboBox.SelectionChanged += (_, _) => ClearControlError(ComPortComboBox);
            ComPortComboBox.LostFocus += (_, _) => ClearControlError(ComPortComboBox);
        }

        private TransferFormValues CaptureFormValues()
        {
            return new TransferFormValues
            {
                UseTcp = TcpModeRadio.IsChecked == true,
                FilePath = FilePathTextBox.Text,
                ProgramName = ProgramNameTextBox.Text,
                SendDelay = SendDelayTextBox.Text,
                TcpHost = TcpIpTextBox.Text,
                TcpPort = TcpPortTextBox.Text,
                TcpUseXonXoff = TcpXonXoffCheckBox.IsChecked == true,
                ComPort = ComPortComboBox.Text,
                BaudRate = BaudRateComboBox.Text,
                DataBits = DataBitsTextBox.Text,
                Parity = ParityComboBox.Text,
                StopBits = StopBitsComboBox.Text,
                Handshake = HandshakeComboBox.Text,
                UseOsp7000lFraming = UseOspCheckBox.IsChecked == true
            };
        }

        private void MarkValidationErrors(IEnumerable<FormValidationError> errors)
        {
            foreach (var error in errors)
            {
                switch (error.Field)
                {
                    case TransferFormFields.FilePath:
                        MarkControlError(FilePathTextBox, error.Message);
                        break;
                    case TransferFormFields.SendDelay:
                        MarkControlError(SendDelayTextBox, error.Message);
                        break;
                    case TransferFormFields.TcpHost:
                        MarkControlError(TcpIpTextBox, error.Message);
                        break;
                    case TransferFormFields.TcpPort:
                        MarkControlError(TcpPortTextBox, error.Message);
                        break;
                    case TransferFormFields.ComPort:
                        MarkControlError(ComPortComboBox, error.Message);
                        break;
                }
            }
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

        private void ModeChanged(object sender, RoutedEventArgs e)
        {
            if (TcpSettingsGroup is null || SerialSettingsGroup is null)
            {
                return;
            }

            var useTcp = TcpModeRadio.IsChecked == true;
            TcpSettingsGroup.IsEnabled = useTcp;
            SerialSettingsGroup.IsEnabled = !useTcp;

            if (!useTcp)
            {
                LoadAvailableComPorts();
            }
        }

        private void AdvancedSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (AdvancedSettingsPanel is null)
            {
                return;
            }

            AdvancedSettingsPanel.Visibility = AdvancedSettingsPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void ThemeModeChanged(object sender, RoutedEventArgs e)
        {
            if (Application.Current is not App app)
            {
                return;
            }

            if (sender is not RadioButton radio || radio.IsChecked != true)
            {
                return;
            }

            if (radio.Name == "ThemeDarkRadio")
            {
                app.SetThemePreference(ThemePreference.Dark);
                return;
            }

            if (radio.Name == "ThemeLightRadio")
            {
                app.SetThemePreference(ThemePreference.Light);
                return;
            }

            app.SetThemePreference(ThemePreference.System);
        }

        private void ProgramNameTextBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ProgramNameTextBox.IsReadOnly = false;
            ProgramNameTextBox.Focus();
            ProgramNameTextBox.SelectAll();
        }

        private void ProgramNameTextBox_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            ProgramNameTextBox.IsReadOnly = true;
        }

        private void RefreshPorts_Click(object sender, RoutedEventArgs e)
        {
            LoadAvailableComPorts();
        }

        private void BrowseFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "NC Files (*.nc)|*.nc|All Files (*.*)|*.*"
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            FilePathTextBox.Text = dialog.FileName;
            ProgramNameTextBox.Text = $"{Path.GetFileNameWithoutExtension(dialog.FileName)}.MIL".ToUpperInvariant();
        }

        private async void Start_Click(object sender, RoutedEventArgs e)
        {
            if (_transferCts is not null)
            {
                return;
            }

            var formValues = CaptureFormValues();

            ClearValidationErrors();
            var validationErrors = _validationService.Validate(formValues);
            if (validationErrors.Count > 0)
            {
                MarkValidationErrors(validationErrors);
                StatusTextBlock.Text = "Invalid settings.";
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
                StatusTextBlock.Text = "Invalid settings.";
                AppendLog($"Invalid settings: {ex.Message}");
                return;
            }

            StartButton.IsEnabled = false;
            CancelButton.IsEnabled = true;
            LogTextBox.Clear();
            StatusTextBlock.Text = "Starting transfer...";
            TransferProgressBar.Value = 0;

            _transferCts = new CancellationTokenSource();

            try
            {
                await _executionService.RunAsync(
                    request,
                    progress => Dispatcher.Invoke(() => ReportProgress(progress)),
                    status => Dispatcher.Invoke(() => AppendLog(status)),
                    _transferCts.Token);

                StatusTextBlock.Text = "Transfer complete.";
            }
            catch (OperationCanceledException)
            {
                StatusTextBlock.Text = "Transfer canceled.";
                AppendLog("Transfer canceled.");
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Transfer failed.";
                AppendLog($"Transfer failed: {ex.Message}");
            }
            finally
            {
                _transferCts.Dispose();
                _transferCts = null;
                StartButton.IsEnabled = true;
                CancelButton.IsEnabled = false;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _transferCts?.Cancel();
        }

        private void ReportProgress(TransferProgress progress)
        {
            StatusTextBlock.Text = progress.Message;

            if (progress.Total > 0)
            {
                TransferProgressBar.Value = (progress.Current * 100.0) / progress.Total;
            }
            else
            {
                TransferProgressBar.Value = 0;
            }

            if (!string.IsNullOrWhiteSpace(progress.Line))
            {
                AppendLog(progress.Line);
            }
        }

        private void AppendLog(string line)
        {
            LogTextBox.AppendText(line + Environment.NewLine);
            LogTextBox.ScrollToEnd();
        }

        private void LoadAvailableComPorts()
        {
            var selected = ComPortComboBox.Text;
            var ports = GetAvailableComPorts();

            ComPortComboBox.Items.Clear();
            foreach (var port in ports)
            {
                ComPortComboBox.Items.Add(port);
            }

            if (!string.IsNullOrWhiteSpace(selected) && ports.Contains(selected, StringComparer.OrdinalIgnoreCase))
            {
                ComPortComboBox.Text = selected;
            }
            else if (ports.Length > 0)
            {
                ComPortComboBox.SelectedIndex = 0;
            }
            else
            {
                ComPortComboBox.Text = "COM1";
            }
        }

        private static string[] GetAvailableComPorts()
        {
            var ports = SerialPort.GetPortNames();
            Array.Sort(ports, StringComparer.OrdinalIgnoreCase);
            return ports;
        }

        private void MarkControlError(Control control, string message)
        {
            if (!_validationStateByControl.ContainsKey(control))
            {
                _validationStateByControl[control] = new ControlValidationState(
                    control.BorderBrush,
                    control.BorderThickness,
                    ToolTipService.GetToolTip(control));
            }

            control.BorderBrush = Brushes.OrangeRed;
            control.BorderThickness = new Thickness(0, 0, 0, 2);
            ToolTipService.SetToolTip(control, message);

            if (_errorTextByControl.TryGetValue(control, out var errorText))
            {
                errorText.Text = message;
                errorText.Visibility = Visibility.Visible;
            }
        }

        private void ClearControlError(Control control)
        {
            if (!_validationStateByControl.TryGetValue(control, out var previousState))
            {
                return;
            }

            control.BorderBrush = previousState.BorderBrush;
            control.BorderThickness = previousState.BorderThickness;
            ToolTipService.SetToolTip(control, previousState.ToolTip);

            if (_errorTextByControl.TryGetValue(control, out var errorText))
            {
                errorText.Text = string.Empty;
                errorText.Visibility = Visibility.Collapsed;
            }

            _validationStateByControl.Remove(control);
        }

        private void ClearValidationErrors()
        {
            var controls = _validationStateByControl.Keys.ToArray();
            foreach (var control in controls)
            {
                ClearControlError(control);
            }
        }

        private sealed record ControlValidationState(Brush BorderBrush, Thickness BorderThickness, object? ToolTip);
    }
}