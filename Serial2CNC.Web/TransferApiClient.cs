using System.Net.Http.Json;
using Serial2CNC.Models;

namespace Serial2CNC.Web;

public sealed class TransferApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public TransferApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<string> GetStateAsync(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("api");
        var response = await client.GetAsync("/api/transfers/state", cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
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
        var response = await client.PutAsJsonAsync("/api/settings", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ServiceSettingsDto>(cancellationToken);
    }

    public async Task<IReadOnlyList<FileTreeNodeDto>> GetDirectoryChildrenAsync(string? relativePath, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("api");
        var url = string.IsNullOrWhiteSpace(relativePath)
            ? "/api/files/children"
            : $"/api/files/children?path={Uri.EscapeDataString(relativePath)}";

        var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var result = await response.Content.ReadFromJsonAsync<List<FileTreeNodeDto>>(cancellationToken);
        return result ?? [];
    }
}
