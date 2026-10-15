using Serial2CNC.Security;

namespace Serial2CNC.Api;

public sealed class AgentCredentialHandler(IHttpContextAccessor accessor, ILogger<AgentCredentialHandler> logger) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var actor = accessor.HttpContext?.Request.Headers[ServiceSecurity.ActorHeader].ToString();
        if (!string.IsNullOrEmpty(actor))
        {
            request.Headers.Add(ServiceSecurity.ActorHeader, actor);
        }
        if (request.Method != HttpMethod.Get)
        {
            logger.LogInformation("User {User} requested {Method} {Path}.", actor ?? "service", request.Method, request.RequestUri?.AbsolutePath);
        }
        return base.SendAsync(request, cancellationToken);
    }
}
