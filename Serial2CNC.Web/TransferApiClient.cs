using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Serial2CNC.Models;

namespace Serial2CNC.Web;

public sealed class TransferApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public TransferApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<TransferStateSnapshot> GetStateAsync(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("api");
        using var response = await client.GetAsync("/api/transfers/state", cancellationToken);
        response.EnsureSuccessStatusCode();
        var state = await response.Content.ReadFromJsonAsync<TransferStateSnapshot>(cancellationToken);
        if (state is null || string.IsNullOrWhiteSpace(state.LastStatus))
        {
            throw new JsonException("The API returned an empty or invalid transfer state.");
        }

        return state;
    }

    public sealed record TransferStateSnapshot
    {
        public required bool IsRunning { get; init; }
        public DateTimeOffset? StartedAt { get; init; }
        public required string LastStatus { get; init; }
    }

    public async Task<string> StartAsync(TransferRequest request, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("api");
        var response = await client.PostAsJsonAsync("/api/transfers/start", request, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<string> CancelAsync(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("api");
        var response = await client.PostAsync("/api/transfers/cancel", null, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<ServiceSettingsDto?> GetSettingsAsync(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("api");
        var response = await client.GetAsync("/api/settings", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ServiceSettingsDto>(cancellationToken);
    }

    public async Task<ServiceSettingsDto?> UpdateSettingsAsync(UpdateServiceSettingsRequest request, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("api");
        using var response = await client.PutAsJsonAsync("/api/settings", request, cancellationToken);
        await EnsureSuccessAsync(response, "Unable to save settings", cancellationToken);

        return await response.Content.ReadFromJsonAsync<ServiceSettingsDto>(cancellationToken);
    }

    public async Task<IReadOnlyList<FileTreeNodeDto>> GetDirectoryChildrenAsync(string? relativePath, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("api");
        var url = string.IsNullOrWhiteSpace(relativePath)
            ? "/api/files/children"
            : $"/api/files/children?path={Uri.EscapeDataString(relativePath)}";

        using var response = await client.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, "Unable to browse the requested folder", cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<List<FileTreeNodeDto>>(cancellationToken);
        return result ?? throw new JsonException("The API returned an empty folder listing response.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var message = $"{operation} (HTTP {(int)response.StatusCode}).";
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is "application/problem+json" or "application/json")
            {
                ProblemDetails? problem;
                try
                {
                    problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken);
                }
                catch (JsonException ex)
                {
                    throw new HttpRequestException(message, ex, response.StatusCode);
                }

                var detail = problem?.Detail ?? problem?.Title;
                if (string.IsNullOrWhiteSpace(detail)
                    && problem?.Extensions.TryGetValue("message", out var legacyMessage) == true)
                {
                    detail = legacyMessage?.ToString();
                }

                if (!string.IsNullOrWhiteSpace(detail))
                {
                    message = detail;
                }
            }

            throw new HttpRequestException(message, null, response.StatusCode);
        }

    }
}
