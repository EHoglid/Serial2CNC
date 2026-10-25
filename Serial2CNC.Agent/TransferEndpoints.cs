using Serial2CNC.Agent.Middleware;
using Serial2CNC.Models;

namespace Serial2CNC.Agent;

public static class TransferEndpoints
{
    public static void MapTransferEndpoints(this WebApplication application, bool allowSerial)
    {
        var app = application.MapGroup("/api");
        app.MapGet("/settings", (ServiceSettingsStore settingsStore) =>
            Results.Ok(settingsStore.Get()));

        app.MapPut("/settings", async (UpdateServiceSettingsRequest request, ServiceSettingsStore settingsStore, HttpContext context, CancellationToken cancellationToken) =>
        {
            context.Items[FileAccessExceptionHandler.RequestedPathKey] = request.WorkingDirectory?.Trim();
            if (string.IsNullOrWhiteSpace(request.WorkingDirectory))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid working directory", detail: "Choose an existing, accessible working directory before saving.");
            }

            try
            {
                var rootPath = Path.GetFullPath(request.WorkingDirectory.Trim());
                if (!Path.IsPathFullyQualified(request.WorkingDirectory.Trim()))
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                        title: "Invalid working directory", detail: "The working directory must be an absolute path.");
                }

                if (!Directory.Exists(rootPath))
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                        title: "Invalid working directory", detail: $"The directory does not exist or is not accessible: {rootPath}");
                }

                _ = BuildChildren(rootPath, rootPath, BuildFilter(request.FileExtensionFilter));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or DirectoryNotFoundException)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid working directory", detail: $"The working directory is invalid or no longer available. {ex.Message}");
            }

            var updated = await settingsStore.UpdateAsync(request, cancellationToken);
            return Results.Ok(updated);
        })
        .Produces<ServiceSettingsDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapGet("/files/children", (string? path, ServiceSettingsStore settingsStore, HttpContext context) =>
        {
            var settings = settingsStore.Get();
            if (string.IsNullOrWhiteSpace(settings.WorkingDirectory))
            {
                return Results.BadRequest(new { message = "WorkingDirectory is not configured in settings." });
            }

            var rootPath = Path.GetFullPath(settings.WorkingDirectory);
            if (!Directory.Exists(rootPath))
            {
                return Results.NotFound(new { message = $"Working directory does not exist: {rootPath}" });
            }

            if (!TryResolvePath(rootPath, path, out var resolvedPath, out var resolveError))
            {
                return Results.BadRequest(new { message = resolveError });
            }

            if (!Directory.Exists(resolvedPath))
            {
                return Results.NotFound(new { message = $"Directory does not exist: {resolvedPath}" });
            }

            context.Items[FileAccessExceptionHandler.RequestedPathKey] = resolvedPath;
            var extensionFilter = BuildFilter(settings.FileExtensionFilter);
            var children = BuildChildren(rootPath, resolvedPath, extensionFilter);
            return Results.Ok(children);
        })
        .Produces<IReadOnlyList<FileTreeNodeDto>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapGet("/transfers/state", (TransferSessionService session) =>
            Results.Ok(session.GetState()));

        app.MapPost("/transfers/start", async (TransferRequest request, TransferSessionService session, ServiceSettingsStore settingsStore, HttpContext context, CancellationToken cancellationToken) =>
        {
            var settings = settingsStore.Get();
            if (!Enum.IsDefined(request.Mode) || (!allowSerial && request.Mode != ConnectionMode.Tcp)
                || string.IsNullOrWhiteSpace(settings.WorkingDirectory))
            {
                return Results.Problem(statusCode: 400, title: "Invalid transfer",
                    detail: "Choose an available transfer mode and configure the working directory first. The Docker application supports TCP/IP only.");
            }
            var root = Path.GetFullPath(settings.WorkingDirectory);
            if (!TryResolvePath(root, request.Transfer.FilePath, out var filePath, out var error))
            {
                return Results.Problem(statusCode: 400, title: "Invalid transfer path", detail: error);
            }
            var filter = BuildFilter(settings.FileExtensionFilter);
            if (!File.Exists(filePath) || (filter.Count > 0 && !filter.Contains(Path.GetExtension(filePath).ToLowerInvariant())))
            {
                return Results.Problem(statusCode: 400, title: "Invalid transfer file",
                    detail: "Select an existing file with an allowed extension in the configured working directory.");
            }
            context.Items[FileAccessExceptionHandler.RequestedPathKey] = filePath;
            // Remote operators select files, not arbitrary network destinations or serial-port configurations.
            var approvedRequest = new TransferRequest
            {
                Mode = request.Mode,
                Tcp = new TcpConnectionOptions { Host = settings.TcpHost, Port = settings.TcpPort, UseXonXoff = settings.TcpUseXonXoff },
                Serial = new SerialConnectionOptions
                {
                    PortName = settings.SerialPortName,
                    BaudRate = settings.SerialBaudRate,
                    DataBits = settings.SerialDataBits,
                    Parity = Enum.Parse<System.IO.Ports.Parity>(settings.SerialParity),
                    StopBits = Enum.Parse<System.IO.Ports.StopBits>(settings.SerialStopBits),
                    Handshake = Enum.Parse<System.IO.Ports.Handshake>(settings.SerialHandshake)
                },
                Transfer = new TransferOptions
                {
                    FilePath = filePath,
                    ProgramName = request.Transfer.ProgramName,
                    SendDelayMs = settings.SendDelayMs,
                    UseOsp7000lFraming = settings.UseOsp7000lFraming
                }
            };
            var started = await session.TryStartAsync(approvedRequest, cancellationToken);
            return started
                ? Results.Accepted("/api/transfers/state", session.GetState())
                : Results.Conflict(new { message = "A transfer is already running." });
        });

        app.MapPost("/transfers/cancel", (TransferSessionService session) =>
        {
            return session.TryCancel()
                ? Results.Accepted("/api/transfers/state", session.GetState())
                : Results.Conflict(new { message = "No transfer is currently running." });
        });

        application.MapHub<TransferHub>("/hubs/transfers");
    }

    static bool TryResolvePath(string rootPath, string? relativePath, out string resolvedPath, out string error)
    {
        var safeRelativePath = (relativePath ?? string.Empty)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .Trim();
        if (Path.IsPathRooted(safeRelativePath)
            || safeRelativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Any(part => part == ".."))
        {
            resolvedPath = string.Empty;
            error = "The requested path must be relative and cannot contain parent-directory components.";
            return false;
        }

        try
        {
            resolvedPath = Path.GetFullPath(Path.Combine(rootPath, safeRelativePath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            resolvedPath = string.Empty;
            error = "The requested path is invalid.";
            return false;
        }

        var normalizedRoot = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedResolved = resolvedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!normalizedResolved.StartsWith(normalizedRoot,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            error = "The requested path is outside of the configured working directory.";
            return false;
        }
        var relative = Path.GetRelativePath(rootPath, resolvedPath);
        var candidate = rootPath;
        foreach (var part in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            candidate = Path.Combine(candidate, part);
            if (Path.Exists(candidate) && (File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
            {
                error = "Links and junctions inside the working directory cannot be browsed or transferred.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    static IReadOnlyList<FileTreeNodeDto> BuildChildren(string rootPath, string parentPath, HashSet<string> extensionFilter)
    {
        var children = new List<FileTreeNodeDto>();

        foreach (var directoryPath in Directory.EnumerateDirectories(parentPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            children.Add(new FileTreeNodeDto
            {
                Name = Path.GetFileName(directoryPath),
                RelativePath = ToRelativePath(rootPath, directoryPath),
                IsDirectory = true,
                HasChildren = DirectoryHasVisibleChildren(directoryPath, extensionFilter)
            });
        }

        foreach (var filePath in Directory.EnumerateFiles(parentPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (extensionFilter.Count > 0 && !extensionFilter.Contains(extension))
            {
                continue;
            }

            children.Add(new FileTreeNodeDto
            {
                Name = Path.GetFileName(filePath),
                RelativePath = ToRelativePath(rootPath, filePath),
                IsDirectory = false,
                HasChildren = false
            });
        }

        return children;
    }

    static bool DirectoryHasVisibleChildren(string directoryPath, HashSet<string> extensionFilter)
    {
        if (Directory.EnumerateDirectories(directoryPath).Any())
        {
            return true;
        }

        if (extensionFilter.Count == 0)
        {
            return Directory.EnumerateFiles(directoryPath).Any();
        }

        return Directory.EnumerateFiles(directoryPath)
            .Select(filePath => Path.GetExtension(filePath).ToLowerInvariant())
            .Any(extensionFilter.Contains);
    }

    static string ToRelativePath(string rootPath, string fullPath)
    {
        return Path.GetRelativePath(rootPath, fullPath)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    static HashSet<string> BuildFilter(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return [];
        }

        var rawParts = filter.Split([';', ',', '|', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (rawParts.Any(part => part is "*" or "*.*"))
        {
            return [];
        }

        return rawParts
            .Select(value => value.Trim().ToLowerInvariant())
            .Select(value => value switch
            {
                _ when value.StartsWith("*.") => $".{value[2..]}",
                _ when value.StartsWith('.') => value,
                _ => $".{value.TrimStart('*', '.')}"
            })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
