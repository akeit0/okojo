using System.Net;
using System.Text;
using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class FetchInteropTests
{
    [Test]
    public void FetchIsUndefinedWithoutWebModule()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("typeof fetch;"));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("undefined"));
    }

    [Test]
    public async Task FetchSupportsDataUrlTextAndJson()
    {
        var options = new JsRuntimeOptions().UseFetch();
        var realm = JsRuntime.Create(options).DefaultRealm;

        var text = await realm.EvalAsync("""
                                         fetch("data:text/plain,fetch%20works").then(r => r.text());
                                         """);
        Assert.That(text.IsString, Is.True);
        Assert.That(text.AsString(), Is.EqualTo("fetch works"));

        var json = await realm.EvalAsync("""
                                         (async () => {
                                           const res = await fetch("data:application/json,%7B%22x%22%3A1%2C%22ok%22%3Atrue%7D");
                                           const value = await res.json();
                                           return [res.ok, res.status, value.x, value.ok].join("|");
                                         })()
                                         """);

        Assert.That(json.IsString, Is.True);
        Assert.That(json.AsString(), Is.EqualTo("true|200|1|true"));

        var bufferInfo = await realm.EvalAsync("""
                                               (async () => {
                                                 const res = await fetch("data:text/plain,Hi");
                                                 const buf = await res.arrayBuffer();
                                                 const view = new Uint8Array(buf);
                                                 return [buf.byteLength, view[0], view[1], res.headers.get("content-type")].join("|");
                                               })()
                                               """);

        Assert.That(bufferInfo.IsString, Is.True);
        Assert.That(bufferInfo.AsString(), Is.EqualTo("2|72|105|text/plain"));
    }

    [Test]
    public async Task FetchBuilder_Can_Use_Custom_HttpClient()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(static request =>
        {
            var method = request.Method.Method;
            var header = request.Headers.TryGetValues("x-test", out var values) ? string.Join(",", values) : "";
            var body = request.Content is null ? "" : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var payload = $"method={method};header={header};body={body}";
            return AddHeader(new(HttpStatusCode.Accepted)
            {
                ReasonPhrase = "Accepted",
                RequestMessage = request,
                Content = new StringContent(payload, Encoding.UTF8, "text/plain")
            }, "x-response-test", "yes");
        }));

        var options =
            new JsRuntimeOptions().UseWebPlatform(builder => builder.UseFetch(fetch => fetch.HttpClient = httpClient));
        var realm = JsRuntime.Create(options).DefaultRealm;

        var result = await realm.EvalAsync("""
                                           (async () => {
                                             const res = await fetch("https://example.test/ping", {
                                               method: "POST",
                                               headers: { "x-test": "ok" },
                                               body: "hello"
                                             });
                                             return [
                                               res.status,
                                               res.statusText,
                                               res.headers.get("content-type"),
                                               res.headers.has("x-response-test"),
                                               await res.text()
                                             ].join("|");
                                           })()
                                           """);

        Assert.That(result.AsString(),
            Is.EqualTo("202|Accepted|text/plain; charset=utf-8|true|method=POST;header=ok;body=hello"));
    }

    [Test]
    public async Task Fetch_Respects_AbortSignal()
    {
        using var httpClient = new HttpClient(new CancelOnlyHttpMessageHandler());
        var realm = JsRuntime.Create(new JsRuntimeOptions()
                .UseWebRuntimeGlobals()
                .UseFetch(fetch => fetch.HttpClient = httpClient))
            .DefaultRealm;

        var result = await realm.EvalAsync("""
                                           (async () => {
                                             const controller = new AbortController();
                                             const pending = fetch("https://example.test/slow", { signal: controller.signal })
                                               .then(() => "resolved", reason => `${reason}`);
                                             controller.abort("bye");
                                             return await pending;
                                           })()
                                           """);

        Assert.That(result.AsString(), Is.EqualTo("bye"));
    }

    private static HttpResponseMessage AddHeader(HttpResponseMessage response, string name, string value)
    {
        response.Headers.Add(name, value);
        return response;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }

    private sealed class CancelOnlyHttpMessageHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException();
        }
    }
}
