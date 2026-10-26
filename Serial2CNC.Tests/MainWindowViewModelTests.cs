using System.Text.Json;
using Serial2CNC.Presentation;
using Xunit;

namespace Serial2CNC.Tests;

public sealed class MainWindowViewModelTests
{
    private static MainWindowViewModel Create() =>
        new(() => Task.CompletedTask, () => { }, () => { }, () => { }, () => { });

    [Fact]
    public void FormValuesRoundTripWithoutLosingAnySetting()
    {
        var form = new TransferFormValues
        {
            UseTcp = false, FilePath = @"C:\CNCPrograms\part.nc", ProgramName = "PART.MIL",
            SendDelay = "42", TcpHost = "192.168.1.10", TcpPort = "4001",
            TcpUseXonXoff = false, ComPort = "COM7", BaudRate = "19200", DataBits = "7",
            Parity = "Even", StopBits = "Two", Handshake = "XOnXOff", UseOsp7000lFraming = false
        };
        var model = Create();
        model.LoadFormValues(form);

        Assert.Equal(JsonSerializer.Serialize(form), JsonSerializer.Serialize(model.CaptureFormValues()));
    }

    [Fact]
    public void ConnectionAndThemeSelectionsNotifyDependentProperties()
    {
        var model = Create();
        var notifications = new List<string?>();
        model.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);

        model.UseSerial = true;
        Assert.False(model.UseTcp);
        Assert.True(model.UseSerial);
        Assert.Contains(nameof(model.UseSerial), notifications);
        model.UseTcp = true;
        Assert.False(model.UseSerial);

        model.IsDarkTheme = true;
        Assert.Equal(ThemePreference.Dark, model.Theme);
        Assert.False(model.IsSystemTheme);
        Assert.False(model.IsLightTheme);
        Assert.Contains(nameof(model.IsDarkTheme), notifications);
        model.IsLightTheme = true;
        Assert.True(model.IsLightTheme);
        Assert.False(model.IsDarkTheme);
        model.IsSystemTheme = true;
        Assert.True(model.IsSystemTheme);
    }

    [Fact]
    public void WebActionsFollowActualServiceStateNotEnableCheckbox()
    {
        var model = Create();
        var changes = 0;
        model.OpenWebCommand.CanExecuteChanged += (_, _) => changes++;
        model.WebEnabled = true;
        Assert.False(model.OpenWebCommand.CanExecute(null));
        Assert.True(model.CanEditWebPort);

        model.IsWebTransitioning = true;
        Assert.False(model.CanConfigureWeb);
        Assert.False(model.CanEditWebPort);
        model.IsWebRunning = true;
        Assert.False(model.OpenWebCommand.CanExecute(null));

        model.IsWebTransitioning = false;
        Assert.True(model.OpenWebCommand.CanExecute(null));
        Assert.True(model.CanConfigureWeb);
        Assert.False(model.CanEditWebPort);

        model.WebEnabled = false;
        model.IsWebTransitioning = true;
        Assert.False(model.OpenWebCommand.CanExecute(null));
        model.IsWebRunning = false;
        model.IsWebTransitioning = false;
        Assert.False(model.OpenWebCommand.CanExecute(null));
        Assert.True(model.CanEditWebPort);
        Assert.True(changes > 0);
    }

    [Fact]
    public void TransferAndShutdownStatesControlCommands()
    {
        var model = Create();
        Assert.True(model.StartCommand.CanExecute(null));
        Assert.False(model.CancelCommand.CanExecute(null));
        model.IsTransferring = true;
        Assert.False(model.StartCommand.CanExecute(null));
        Assert.True(model.CancelCommand.CanExecute(null));
        model.IsTransferring = false;
        Assert.True(model.StartCommand.CanExecute(null));
        Assert.False(model.CancelCommand.CanExecute(null));

        model.IsWebRunning = true;
        model.IsShuttingDown = true;
        Assert.False(model.IsInteractive);
        Assert.False(model.StartCommand.CanExecute(null));
        Assert.False(model.CancelCommand.CanExecute(null));
        Assert.False(model.OpenWebCommand.CanExecute(null));
        Assert.False(model.CanConfigureWeb);
    }

    [Fact]
    public void EditingClearsOnlyTheEditedFieldsValidationError()
    {
        var model = Create();
        model.SetValidationErrors([
            new(TransferFormFields.FilePath, "Select a file."),
            new(TransferFormFields.TcpPort, "Invalid port."),
            new(TransferFormFields.SendDelay, "Invalid delay.")
        ]);
        Assert.Equal("Select a file.", model.FilePathError);
        model.FilePath = "part.nc";
        Assert.Empty(model.FilePathError);
        Assert.Equal("Invalid port.", model.TcpPortError);
        model.SendDelay = "1";
        Assert.Empty(model[nameof(model.SendDelay)]);
        model.SetValidationErrors([]);
        Assert.Empty(model.Error);
        Assert.Empty(model.TcpPortError);
    }

    [Fact]
    public void CommandsInvokeActionsAndToggleSettings()
    {
        var invoked = new List<string>();
        var model = new MainWindowViewModel(() => Task.CompletedTask, () => invoked.Add("cancel"),
            () => invoked.Add("browse"), () => invoked.Add("refresh"), () => invoked.Add("open"));
        model.OpenWebCommand.Execute(null);
        Assert.Empty(invoked);
        model.IsWebRunning = true;
        model.OpenWebCommand.Execute(null);
        model.BrowseCommand.Execute(null);
        model.RefreshPortsCommand.Execute(null);
        model.IsTransferring = true;
        model.CancelCommand.Execute(null);
        Assert.Equal(["open", "browse", "refresh", "cancel"], invoked);
        model.ToggleSettingsCommand.Execute(null);
        Assert.True(model.ShowSettings);
        model.ToggleSettingsCommand.Execute(null);
        Assert.False(model.ShowSettings);
    }

    [Fact]
    public void AsyncCommandPreventsReentryAndReenablesAfterCompletion()
    {
        var completion = new TaskCompletionSource();
        var calls = 0;
        var command = new AsyncDelegateCommand(() => { calls++; return completion.Task; });
        command.Execute(null);
        Assert.False(command.CanExecute(null));
        command.Execute(null);
        Assert.Equal(1, calls);
        completion.SetResult();
        Assert.True(command.CanExecute(null));
    }
}
