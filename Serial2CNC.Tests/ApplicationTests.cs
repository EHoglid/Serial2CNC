using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Serial2CNC.Agent;
using Serial2CNC.Models;
using Serial2CNC.Presentation;
using Serial2CNC.Services;
using Serial2CNC.Web;
using Xunit;

namespace Serial2CNC.Tests;

public sealed class ApplicationTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"Serial2CNC-tests-{Guid.NewGuid()}");
    private WebApplication? _app;
    private HttpClient? _client;
    private HttpClient Client => _client ?? throw new InvalidOperationException("Host is not started.");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "sample.nc"), "G01 X1\nM30");
        _app = WebHostFactory.Create(["--urls", "http://127.0.0.1:0", "--DataDirectory", Path.Combine(_directory, "settings")]);
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri(_app.Urls.Single()) };
    }

    [Fact]
    public async Task ServerIsOneHealthyApplicationAndHasNoSerialUi()
    {
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/health")).StatusCode);
        var html = await Client.GetStringAsync("/");
        Assert.Contains("TCP/IP", html);
        Assert.DoesNotContain("Serial (COM)", html);
        Assert.Contains("blazor.web.js", html);
        Assert.Equal(HttpStatusCode.OK,
            (await Client.PostAsync("/hubs/transfers/negotiate?negotiateVersion=1", null)).StatusCode);
    }

    [Fact]
    public async Task SavedSettingsSurviveReloadAndRejectedChangesDoNotReplaceThem()
    {
        await SaveSettingsAsync();
        var store = new ServiceSettingsStore(Path.Combine(_directory, "settings"), NullLogger<ServiceSettingsStore>.Instance);
        Assert.Equal(_directory, store.Get().WorkingDirectory);
        Assert.Equal("*.nc", store.Get().FileExtensionFilter);
        Assert.Equal("127.0.0.1", store.Get().TcpHost);

        var rejected = await Client.PutAsJsonAsync("/api/settings", new UpdateServiceSettingsRequest
        {
            WorkingDirectory = Path.Combine(_directory, "missing")
        });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(_directory, (await Client.GetFromJsonAsync<ServiceSettingsDto>("/api/settings"))?.WorkingDirectory);
    }

    [Fact]
    public async Task DockerHostRejectsSerialAndUnsafePaths()
    {
        await SaveSettingsAsync();
        foreach (var request in new[]
        {
            Request(ConnectionMode.Serial, "sample.nc"),
            Request(ConnectionMode.Tcp, "../outside.nc"),
            Request(ConnectionMode.Tcp, "blocked.txt")
        })
        {
            var response = await Client.PostAsJsonAsync("/api/transfers/start", request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Client.GetAsync("/api/files/children?path=..")).StatusCode);
    }

    [Fact]
    public async Task TcpTransferUsesSavedDestinationAndCompletesAfterHttpRequestEnds()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        await SaveSettingsAsync(((IPEndPoint)listener.LocalEndpoint).Port);
        var connection = listener.AcceptTcpClientAsync();
        var response = await Client.PostAsJsonAsync("/api/transfers/start", Request(ConnectionMode.Tcp, "sample.nc"));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var receiver = await connection.WaitAsync(TimeSpan.FromSeconds(10));
        using var reader = new StreamReader(receiver.GetStream());
        Assert.Equal("G01 X1\r\nM30\r\n", await reader.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        var state = await WaitForCompletionAsync();
        Assert.Equal("Transfer complete.", state.LastStatus);
    }

    [Theory]
    [InlineData("sample.nc")]
    [InlineData("Parts/nested.nc")]
    public async Task BrowserSelectedFileCanBeTransferredUsingItsRelativePath(string relativePath)
    {
        var fullPath = Path.Combine(_directory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, "G01 X1\nM30");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        await SaveSettingsAsync(((IPEndPoint)listener.LocalEndpoint).Port);

        var page = new Serial2CNC.Web.Components.Pages.Home();
        var pageType = page.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        pageType.GetProperty("WorkingDirectory", flags)!.SetValue(page, _directory);
        pageType.GetMethod("SelectFile", flags)!.Invoke(page, [new FileTreeNodeDto
        {
            Name = Path.GetFileName(fullPath),
            RelativePath = relativePath,
            IsDirectory = false,
            HasChildren = false
        }]);
        var selectedPath = Assert.IsType<string>(pageType.GetProperty("FilePath", flags)!.GetValue(page));
        Assert.Equal(relativePath, selectedPath);

        var connection = listener.AcceptTcpClientAsync();
        var response = await Client.PostAsJsonAsync("/api/transfers/start", Request(ConnectionMode.Tcp, selectedPath));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var receiver = await connection.WaitAsync(TimeSpan.FromSeconds(10));
        using var reader = new StreamReader(receiver.GetStream());
        Assert.Equal("G01 X1\r\nM30\r\n", await reader.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("Transfer complete.", (await WaitForCompletionAsync()).LastStatus);
    }

    [Fact]
    public async Task CancelStopsRunningTransferAndBothInterfacesShareTransferGuard()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        await SaveSettingsAsync(((IPEndPoint)listener.LocalEndpoint).Port, delay: 60000);
        var response = await Client.PostAsJsonAsync("/api/transfers/start", Request(ConnectionMode.Tcp, "sample.nc"));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var receiver = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(10));
        var duplicate = await Client.PostAsJsonAsync("/api/transfers/start", Request(ConnectionMode.Tcp, "sample.nc"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new TransferRunner().RunAsync(
            Request(ConnectionMode.Tcp, Path.Combine(_directory, "sample.nc")), _ => { }, null, CancellationToken.None));
        Assert.Equal(HttpStatusCode.Accepted, (await Client.PostAsync("/api/transfers/cancel", null)).StatusCode);
        Assert.Equal("Transfer canceled.", (await WaitForCompletionAsync()).LastStatus);
    }

    [Fact]
    public void CorruptSettingsAreReportedInsteadOfSilentlyReset()
    {
        var path = Path.Combine(_directory, "corrupt");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "service-settings.json"), "not JSON");
        Assert.Throws<JsonException>(() => new ServiceSettingsStore(path, NullLogger<ServiceSettingsStore>.Instance));
        Assert.Equal("not JSON", File.ReadAllText(Path.Combine(path, "service-settings.json")));
    }

    [Fact]
    public void DesktopSettingsRoundTripAllConnectionAndWebPreferences()
    {
        var settings = new DesktopSettings
        {
            Transfer = new TransferFormValues
            {
                UseTcp = false,
                FilePath = @"C:\Programs\part.nc",
                ProgramName = "PART.MIL",
                SendDelay = "25",
                TcpHost = "192.168.1.12",
                TcpPort = "1234",
                TcpUseXonXoff = true,
                ComPort = "COM7",
                BaudRate = "19200",
                DataBits = "7",
                Parity = "Even",
                StopBits = "Two",
                Handshake = "XOnXOff",
                UseOsp7000lFraming = true
            },
            Theme = ThemePreference.Dark,
            WebEnabled = true,
            WebPort = 62898
        };
        var json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<DesktopSettings>(json);
        Assert.NotNull(restored);
        Assert.Equal(json, JsonSerializer.Serialize(restored));
        Assert.Equal("COM7", restored.Transfer?.ComPort);
        Assert.True(restored.WebEnabled);
        Assert.Equal(ThemePreference.Dark, restored.Theme);
    }

    [Fact]
    public async Task LiveLogsUseInternalApplicationAddress()
    {
        await using var scope = _app!.Services.CreateAsyncScope();
        var stream = scope.ServiceProvider.GetRequiredService<LiveLogStreamClient>();
        await stream.StartAsync();
        for (var attempt = 0; attempt < 100 && !stream.IsConnected; attempt++)
        {
            await Task.Delay(20);
        }
        Assert.True(stream.IsConnected);
    }

    [Fact]
    public async Task EmbeddedHostOffersSerialAndCancelsTransferOnDisposal()
    {
        await using var desktop = WebHostFactory.Create([], Path.Combine(_directory, "desktop"), desktopPort: 0);
        await desktop.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(desktop.Urls.Single().Replace("0.0.0.0", "127.0.0.1")) };
        Assert.True(desktop.Services.GetRequiredService<HostCapabilities>().AllowSerial);
        Assert.Contains("Serial (COM)", await client.GetStringAsync("/"));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var settings = await client.PutAsJsonAsync("/api/settings", new UpdateServiceSettingsRequest
        {
            WorkingDirectory = _directory, TcpHost = "127.0.0.1",
            TcpPort = ((IPEndPoint)listener.LocalEndpoint).Port,
            TcpUseXonXoff = false, UseOsp7000lFraming = false, SendDelayMs = 60000
        });
        settings.EnsureSuccessStatusCode();
        var response = await client.PostAsJsonAsync("/api/transfers/start", Request(ConnectionMode.Tcp, "sample.nc"));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var receiver = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(10));
        var session = desktop.Services.GetRequiredService<TransferSessionService>();
        await desktop.StopAsync();
        await session.DisposeAsync();
        Assert.False(session.GetState().IsRunning);
        Assert.Equal("Transfer canceled.", session.GetState().LastStatus);
    }

    private async Task SaveSettingsAsync(int port = 23, int delay = 0)
    {
        var response = await Client.PutAsJsonAsync("/api/settings", new UpdateServiceSettingsRequest
        {
            WorkingDirectory = _directory,
            FileExtensionFilter = "*.nc",
            TcpHost = "127.0.0.1",
            TcpPort = port,
            TcpUseXonXoff = false,
            UseOsp7000lFraming = false,
            SendDelayMs = delay
        });
        response.EnsureSuccessStatusCode();
    }

    private static TransferRequest Request(ConnectionMode mode, string file) => new()
    {
        Mode = mode,
        Tcp = new TcpConnectionOptions { Host = "invalid.example", Port = 1 },
        Transfer = new TransferOptions { FilePath = file }
    };

    private async Task<TransferApiClient.TransferStateSnapshot> WaitForCompletionAsync()
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var state = await Client.GetFromJsonAsync<TransferApiClient.TransferStateSnapshot>("/api/transfers/state");
            if (state is { IsRunning: false })
            {
                return state;
            }
            await Task.Delay(20);
        }
        throw new TimeoutException("Transfer did not stop.");
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
        Directory.Delete(_directory, recursive: true);
    }
}
