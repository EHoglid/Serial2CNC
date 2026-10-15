namespace Serial2CNC.Web;

public sealed record LiveLogEntry(DateTimeOffset Timestamp, string Source, string Level, string Message);
