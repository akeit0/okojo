using System.Net;
using System.Text;

namespace OkojoHostEventLoopSandbox;

internal sealed class DemoFetchHandler(IReadOnlyDictionary<string, string> payloads) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var uri = request.RequestUri?.ToString() ?? string.Empty;
        if (!payloads.TryGetValue(uri, out var payload))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                RequestMessage = request,
                Content = new StringContent("""{"kind":"missing"}""", Encoding.UTF8, "application/json")
            });

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        });
    }
}
