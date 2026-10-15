namespace Serial2CNC.Web;

public sealed class ApiConnectionHandler(ILogger<ApiConnectionHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await base.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Cannot reach API for {Method} {Uri}.", request.Method, request.RequestUri);
            throw new HttpRequestException("API unavailable. Check that the API service is running and reachable.", ex);
        }
    }
}
