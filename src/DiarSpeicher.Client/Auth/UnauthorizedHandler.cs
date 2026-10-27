using System.Net;

namespace DiarSpeicher.Client.Auth;

public sealed class UnauthorizedHandler(UnauthorizedSignal signal) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            signal.Raise();
        }

        return response;
    }
}
