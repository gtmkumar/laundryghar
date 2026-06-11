namespace laundryghar.Mcp.Infrastructure.Http;

/// <summary>
/// DelegatingHandler that reads the inbound customer JWT from IHttpContextAccessor
/// and forwards it as the Authorization header on every outbound HttpClient request.
/// Injected into each named HttpClient via AddHttpMessageHandler.
/// </summary>
public sealed class TokenForwardingHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TokenForwardingHandler(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is not null)
        {
            var authHeader = httpContext.Request.Headers.Authorization.ToString();
            if (!string.IsNullOrWhiteSpace(authHeader))
                request.Headers.Authorization = AuthenticationHeaderValue.Parse(authHeader);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
