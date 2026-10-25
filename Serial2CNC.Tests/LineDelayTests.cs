using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Serial2CNC.Models;
using Serial2CNC.Presentation;
using Serial2CNC.Services;
using Xunit;

namespace Serial2CNC.Tests;

[Collection("Transfer timing")]
public sealed class LineDelayTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"Serial2CNC-delay-{Guid.NewGuid()}.nc");

    public LineDelayTests()
    {
        File.WriteAllText(_file, "G01 X1\nG01 X2\nM30");
    }

    [Theory]
    [InlineData(20, false)]
    [InlineData(20, true)]
    [InlineData(100, false)]
    public async Task EngineWaitsConfiguredDelayBetweenEveryLine(int delayMs, bool framing)
    {
        await using var transport = new RecordingTransport();
        await new CncTransferEngine().SendAsync(transport, new TransferOptions
        {
            FilePath = _file, SendDelayMs = delayMs, UseOsp7000lFraming = framing, ProgramName = "PART.MIL"
        }, _ => { }, CancellationToken.None);

        string[] expected = framing ? ["$PART.MIL%", "G01 X1", "G01 X2", "M30", "%"] : ["G01 X1", "G01 X2", "M30"];
        Assert.Equal(expected, transport.Lines);
        for (var index = 1; index < transport.Timestamps.Count; index++)
        {
            var elapsed = Stopwatch.GetElapsedTime(transport.Timestamps[index - 1], transport.Timestamps[index]);
            Assert.True(elapsed.TotalMilliseconds >= delayMs,
                $"Line {index + 1} followed after {elapsed.TotalMilliseconds:F2} ms; configured delay is {delayMs} ms.");
        }
    }

    [Fact]
    public async Task DesktopFormDelayIsAppliedToTcpTransfer()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var request = new TransferRequestFactory().Create(new TransferFormValues
        {
            UseTcp = true, FilePath = _file, SendDelay = "100",
            TcpHost = "127.0.0.1", TcpPort = ((IPEndPoint)listener.LocalEndpoint).Port.ToString(),
            ComPort = "COM1", BaudRate = "9600", DataBits = "8", Parity = "None",
            StopBits = "One", Handshake = "None", UseOsp7000lFraming = false
        });
        Assert.Equal(100, request.Transfer.SendDelayMs);
        var acceptance = listener.AcceptTcpClientAsync(timeout.Token);
        var start = Stopwatch.GetTimestamp();
        var sending = new TransferExecutionService().RunAsync(request, _ => { }, null, timeout.Token);
        using var receiver = await acceptance;
        using var reader = new StreamReader(receiver.GetStream());
        Assert.Equal("G01 X1\r\nG01 X2\r\nM30\r\n", await reader.ReadToEndAsync(timeout.Token));
        await sending;
        Assert.True(Stopwatch.GetElapsedTime(start).TotalMilliseconds >= 198,
            "A three-line desktop TCP transfer must include two 100 ms delays.");
    }

    [Fact]
    public async Task OspFramingWaitsAfterHeaderBeforeProgramLines()
    {
        await using var transport = new RecordingTransport();
        await new CncTransferEngine().SendAsync(transport, new TransferOptions
        {
            FilePath = _file, SendDelayMs = 20, UseOsp7000lFraming = true, ProgramName = "PART.MIL"
        }, _ => { }, CancellationToken.None);

        var headerGap = Stopwatch.GetElapsedTime(transport.Timestamps[0], transport.Timestamps[1]);
        Assert.True(headerGap.TotalMilliseconds >= CncTransferEngine.OspHeaderDelayMs,
            $"First program line followed the header after {headerGap.TotalMilliseconds:F2} ms.");
        var lineGap = Stopwatch.GetElapsedTime(transport.Timestamps[1], transport.Timestamps[2]);
        Assert.True(lineGap.TotalMilliseconds < CncTransferEngine.OspHeaderDelayMs,
            "The header delay must not apply to normal program lines.");
    }

    [Fact]
    public async Task TcpTransferHoldsProgramLinesWhileOkumaSendsXoffAfterHeader()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var options = new TcpConnectionOptions
        {
            Host = "127.0.0.1", Port = ((IPEndPoint)listener.LocalEndpoint).Port, UseXonXoff = true
        };
        var acceptance = listener.AcceptTcpClientAsync(timeout.Token);
        await using var transport = new TcpLineTransport(options);
        var sending = new CncTransferEngine().SendAsync(transport, new TransferOptions
        {
            FilePath = _file, SendDelayMs = 0, UseOsp7000lFraming = true, ProgramName = "PART.MIL"
        }, _ => { }, timeout.Token);

        using var machine = await acceptance;
        var stream = machine.GetStream();
        var header = new byte[12];
        await stream.ReadExactlyAsync(header, timeout.Token);
        Assert.Equal("$PART.MIL%\r\n", System.Text.Encoding.ASCII.GetString(header));
        stream.WriteByte(0x13);

        var overwritePrompt = Task.Delay(1500, timeout.Token);
        var firstByte = new byte[1];
        var earlyRead = stream.ReadAsync(firstByte, timeout.Token).AsTask();
        Assert.Same(overwritePrompt, await Task.WhenAny(overwritePrompt, earlyRead));

        stream.WriteByte(0x11);
        Assert.Equal(1, await earlyRead);
        const string expected = "G01 X1\r\nG01 X2\r\nM30\r\n%\r\n";
        var rest = new byte[expected.Length - 1];
        await stream.ReadExactlyAsync(rest, timeout.Token);
        await sending;
        Assert.Equal(expected, (char)firstByte[0] + System.Text.Encoding.ASCII.GetString(rest));
    }

    public void Dispose() => File.Delete(_file);

    private sealed class RecordingTransport : ILineTransport
    {
        public List<string> Lines { get; } = [];
        public List<long> Timestamps { get; } = [];

        public Task OpenAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SendLineAsync(string line, CancellationToken cancellationToken)
        {
            Lines.Add(line);
            Timestamps.Add(Stopwatch.GetTimestamp());
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

[CollectionDefinition("Transfer timing", DisableParallelization = true)]
public sealed class TransferTimingCollection;
