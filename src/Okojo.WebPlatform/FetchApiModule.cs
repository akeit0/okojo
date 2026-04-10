using System.Buffers;
using System.Text;
using Okojo.Objects;
using Okojo.Runtime;
using Okojo.WebPlatform.Internal;

namespace Okojo.WebPlatform;

public sealed class FetchApiModule(HttpClient? httpClient = null, HostTaskQueueKey completionQueueKey = default)
    : IRealmApiModule
{
    private static readonly HttpClient SHttpClient = new();

    private readonly HostTaskQueueKey completionQueueKey =
        completionQueueKey == default ? WebTaskQueueKeys.Network : completionQueueKey;

    private readonly HttpClient httpClient = httpClient ?? SHttpClient;

    public static FetchApiModule Shared { get; } = new();

    public void Install(JsRealm realm)
    {
        if (realm.Global.TryGetValue("fetch", out _))
            return;

        var fetchFunction = new JsHostFunction(realm, static (in info) =>
        {
            var input = info.GetArgumentOrDefault(0, JsValue.Undefined);
            var init = info.GetArgumentOrDefault(1, JsValue.Undefined);
            var url = input.IsString ? input.AsString() : input.ToString();
            var module = (FetchApiModule)((JsHostFunction)info.Function).UserData!;
            var abort = GetAbortRegistration(init);
            // Fetch completion is host work that settles a promise and then drains
            // promise jobs at the next microtask checkpoint. Route the completion through
            // a host queue first instead of resolving directly on an arbitrary thread.
            return abort.WrapTaskOnHostQueue(info.Realm, module.FetchAsync(info.Realm, url, init, abort),
                module.completionQueueKey);
        }, "fetch", 2, false)
        {
            UserData = this
        };

        realm.Global["fetch"] = JsValue.FromObject(fetchFunction);
    }

    private async Task<JsValue> FetchAsync(JsRealm realm, string url, JsValue initValue, AbortRegistration abort)
    {
        abort.Token.ThrowIfCancellationRequested();

        if (TryHandleDataUrlFetch(realm, url, out var dataResponse))
            return JsValue.FromObject(dataResponse);

        using var request = BuildFetchRequest(url, initValue);
        using var response = await httpClient.SendAsync(request, abort.Token).ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync(abort.Token).ConfigureAwait(false);
        var statusText = response.ReasonPhrase ?? string.Empty;
        var responseObject = FetchResponseFactory.Create(realm,
            (int)response.StatusCode,
            statusText,
            response.RequestMessage?.RequestUri?.ToString() ?? url,
            bytes,
            CollectResponseHeaders(response));
        return JsValue.FromObject(responseObject);
    }

    private static AbortRegistration GetAbortRegistration(JsValue initValue)
    {
        if (initValue.TryGetObject(out var initObj) &&
            initObj.TryGetProperty("signal", out var signalValue) &&
            !signalValue.IsUndefined &&
            !signalValue.IsNull)
            return AbortInterop.Link(signalValue);

        return AbortInterop.Link(JsValue.Undefined);
    }

    private static HttpRequestMessage BuildFetchRequest(string url, JsValue initValue)
    {
        var method = HttpMethod.Get;
        string? bodyText = null;
        var headers = new List<KeyValuePair<string, string>>();

        if (initValue.TryGetObject(out var initObj))
        {
            if (initObj.TryGetProperty("method", out var methodValue) && !methodValue.IsUndefined)
                method = new(methodValue.IsString ? methodValue.AsString() : methodValue.ToString());

            if (initObj.TryGetProperty("body", out var bodyValue) && !bodyValue.IsUndefined && !bodyValue.IsNull)
                bodyText = bodyValue.IsString ? bodyValue.AsString() : bodyValue.ToString();

            if (initObj.TryGetProperty("headers", out var headersValue) &&
                headersValue.TryGetObject(out var headersObj))
            {
                var names = headersObj.GetEnumerableOwnPropertyNames();
                for (var i = 0; i < names.Count; i++)
                {
                    var name = names[i];
                    if (headersObj.TryGetProperty(name, out var headerValue))
                        headers.Add(new(name, headerValue.IsString ? headerValue.AsString() : headerValue.ToString()));
                }
            }
        }

        var request = new HttpRequestMessage(method, url);
        if (bodyText is not null)
            request.Content = new StringContent(bodyText, Encoding.UTF8);

        for (var i = 0; i < headers.Count; i++)
        {
            var (name, value) = headers[i];
            if (!request.Headers.TryAddWithoutValidation(name, value))
            {
                request.Content ??= new ByteArrayContent(Array.Empty<byte>());
                _ = request.Content.Headers.TryAddWithoutValidation(name, value);
            }
        }

        return request;
    }

    private static bool TryHandleDataUrlFetch(JsRealm realm, string url, out JsPlainObject response)
    {
        response = null!;
        if (!url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return false;

        var comma = url.IndexOf(',');
        if (comma < 0)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Invalid data URL");

        var meta = url.AsSpan(5, comma - 5);
        var payload = url.AsSpan(comma + 1);
        var isBase64 = meta.EndsWith(";base64", StringComparison.OrdinalIgnoreCase);
        var bytes = isBase64
            ? ConvertFromBase64(payload)
            : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));

        response = FetchResponseFactory.Create(realm, 200, "OK", url, bytes, CreateDataUrlHeaders(meta, isBase64));
        return true;

        static byte[] ConvertFromBase64(ReadOnlySpan<char> base64)
        {
            var length = base64.Length * 3 / 4;
            var buffer = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                if (!Convert.TryFromBase64Chars(base64, buffer, out var bytesWritten))
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Invalid base64 data in data URL");
                return buffer.AsSpan(0, bytesWritten).ToArray();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    private static Dictionary<string, string> CreateDataUrlHeaders(ReadOnlySpan<char> meta, bool isBase64)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var metaText = meta.ToString();
        var semicolon = metaText.IndexOf(';');
        var mediaType = semicolon >= 0 ? metaText[..semicolon] : metaText;
        if (string.IsNullOrEmpty(mediaType))
            mediaType = "text/plain;charset=US-ASCII";
        headers["content-type"] = mediaType;
        if (isBase64)
            headers["content-transfer-encoding"] = "base64";
        return headers;
    }

    private static Dictionary<string, string> CollectResponseHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers)
            headers[header.Key.ToLowerInvariant()] = string.Join(", ", header.Value);
        foreach (var header in response.Content.Headers)
            headers[header.Key.ToLowerInvariant()] = string.Join(", ", header.Value);
        return headers;
    }
}
