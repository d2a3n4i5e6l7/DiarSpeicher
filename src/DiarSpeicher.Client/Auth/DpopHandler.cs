using System.Runtime.InteropServices.JavaScript;
using DiarSpeicher.Client.Interop;

namespace DiarSpeicher.Client.Auth;

public sealed class DpopHandler : DelegatingHandler
{
    private const string HeaderName = "DPoP";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is { } uri)
        {
            var proof = await TrySignAsync(request.Method.Method, uri.IsAbsoluteUri ? uri.PathAndQuery : uri.OriginalString);
            if (proof is not null)
            {
                request.Headers.TryAddWithoutValidation(HeaderName, proof);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private static async Task<string?> TrySignAsync(string method, string path)
    {
        try
        {
            return await DpopJs.SignRequestProof(method, path);
        }
        catch (JSException)
        {
            return null;
        }
    }
}
