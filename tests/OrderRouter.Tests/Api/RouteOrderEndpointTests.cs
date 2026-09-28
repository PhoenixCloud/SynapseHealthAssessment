using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OrderRouter.Api.Swagger;
using OrderRouter.Core.Domain;
using OrderRouter.Core.Routing;

namespace OrderRouter.Tests.Api;

/// <summary>
/// End-to-end tests through the real HTTP pipeline (in memory). The hard requirement: every
/// response from POST /api/route is HTTP 200, with the spec's exact body contract.
/// WebApplicationFactory runs in the Development environment, where ASP.NET would normally show
/// its detailed exception page, so these tests also prove that page is never reached.
/// </summary>
public class RouteOrderEndpointTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public void StartServer()
    {
        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public void StopServer()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static async Task<JsonElement> ReadBody(HttpResponseMessage response)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "Every response must be HTTP 200.");
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private async Task<JsonElement> Post(HttpContent content, string path = "/api/route") =>
        await ReadBody(await _client.PostAsync(path, content));

    private static string[] Errors(JsonElement body) =>
        body.GetProperty("errors").EnumerateArray().Select(e => e.GetString()!).ToArray();

    private static string[] PropertyNames(JsonElement element) =>
        element.EnumerateObject().Select(p => p.Name).ToArray();

    // ---- Every Swagger sample does what its description says ----

    private static IEnumerable<TestCaseData> SwaggerExamples() =>
        RouteExamples.All.Select(e => new TestCaseData(e).SetName($"Swagger example: {e.Key}"));

    [TestCaseSource(nameof(SwaggerExamples))]
    public async Task SwaggerExampleProducesItsDocumentedResult(RouteExample example)
    {
        var body = await Post(Json(example.Body));

        Assert.That(body.GetProperty("feasible").GetBoolean(), Is.EqualTo(example.ExpectedFeasible));
        if (example.ExpectedFeasible)
        {
            var shipment = body.GetProperty("routing").EnumerateArray().Single();
            Assert.That(shipment.GetProperty("supplier_id").GetString(), Is.EqualTo(example.ExpectedSupplierId));
            Assert.That(shipment.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("fulfillment_mode").GetString()),
                Is.All.EqualTo(example.ExpectedMode));
        }
        else
        {
            Assert.That(Errors(body), Is.EqualTo(example.ExpectedErrors));
        }
    }

    // ---- Response contract (exactly the spec's shape) ----

    [Test]
    public async Task SuccessBodyHasExactlyTheSpecFields()
    {
        var body = await Post(Json(RouteExamples.All.Single(e => e.Key == "ord-001").Body));

        Assert.That(PropertyNames(body), Is.EqualTo(new[] { "feasible", "routing" }));
        var shipment = body.GetProperty("routing")[0];
        Assert.That(PropertyNames(shipment), Is.EqualTo(new[] { "supplier_id", "supplier_name", "items" }));
        var item = shipment.GetProperty("items")[0];
        Assert.That(PropertyNames(item), Is.EqualTo(new[] { "product_code", "quantity", "category", "fulfillment_mode" }));

        Assert.Multiple(() =>
        {
            Assert.That(body.GetProperty("feasible").ValueKind, Is.EqualTo(JsonValueKind.True));
            Assert.That(item.GetProperty("quantity").ValueKind, Is.EqualTo(JsonValueKind.Number));
            Assert.That(item.GetProperty("product_code").GetString(), Is.EqualTo("WC-STD-001"));
            Assert.That(item.GetProperty("category").GetString(), Is.EqualTo("wheelchair"));
            Assert.That(item.GetProperty("fulfillment_mode").GetString(), Is.EqualTo("local"));
        });
    }

    [Test]
    public async Task SuccessBodyMatchesTheSwaggerResponseSampleExactly()
    {
        var body = await Post(Json(RouteExamples.All.Single(e => e.Key == "ord-001").Body));
        var documented = JsonDocument.Parse(RouteExamples.SuccessResponse).RootElement;

        Assert.That(JsonElement.DeepEquals(body, documented), Is.True, body.ToString());
    }

    [Test]
    public async Task FailureBodyMatchesTheSpecExampleExactly()
    {
        var body = await Post(Json("""{ "customer_zip": "ABC", "items": [] }"""));
        var specExample = JsonDocument.Parse(RouteExamples.FailureResponse).RootElement;

        Assert.That(PropertyNames(body), Is.EqualTo(new[] { "feasible", "errors" }));
        Assert.That(JsonElement.DeepEquals(body, specExample), Is.True, body.ToString());
    }

    [Test]
    public async Task MailOrderModeIsWrittenAsMailOrder()
    {
        var body = await Post(Json(RouteExamples.All.Single(e => e.Key == "ord-003").Body));

        Assert.That(body.GetProperty("routing")[0].GetProperty("items")[0].GetProperty("fulfillment_mode").GetString(),
            Is.EqualTo("mail_order"));
    }

    [Test]
    public async Task QuantityAndCatalogCodeAreEchoed()
    {
        var body = await Post(Json(RouteExamples.All.Single(e => e.Key == "mail-order-missing").Body));

        var item = body.GetProperty("routing")[0].GetProperty("items")[0];
        Assert.That(item.GetProperty("product_code").GetString(), Is.EqualTo("WC-STD-001"));
        Assert.That(item.GetProperty("quantity").GetInt32(), Is.EqualTo(3));
    }

    // ---- Always 200: malformed bodies ----

    [TestCase("", TestName = "Empty body")]
    [TestCase("   ", TestName = "Whitespace body")]
    [TestCase("{", TestName = "Truncated JSON")]
    [TestCase("not json at all", TestName = "Plain text")]
    [TestCase("<order><zip>10015</zip></order>", TestName = "XML")]
    [TestCase("[]", TestName = "JSON array")]
    [TestCase("null", TestName = "JSON null")]
    [TestCase("\"text\"", TestName = "JSON string")]
    public async Task MalformedBodyReturns200WithAFriendlyError(string raw)
    {
        var body = await Post(Json(raw));

        Assert.That(body.GetProperty("feasible").GetBoolean(), Is.False);
        Assert.That(Errors(body), Is.EqualTo(new[] { "Request body must be a JSON object." }));
    }

    [Test]
    public async Task SampleOrdersFileArrayIsRejectedWith200()
    {
        var samples = await File.ReadAllTextAsync(Path.Combine(TestContext.CurrentContext.TestDirectory, "Data", "sample_orders.json"));

        var body = await Post(Json(samples));

        Assert.That(Errors(body), Is.EqualTo(new[] { "Request body must be a JSON object." }));
    }

    [Test]
    public async Task NoBodyAtAllReturns200()
    {
        var body = await ReadBody(await _client.PostAsync("/api/route", content: null));

        Assert.That(Errors(body), Is.EqualTo(new[] { "Request body must be a JSON object." }));
    }

    // ---- Always 200: content types are never enforced ----

    [TestCase("text/plain")]
    [TestCase("application/xml")]
    [TestCase("application/x-www-form-urlencoded")]
    [TestCase("application/octet-stream")]
    public async Task ValidJsonWithAnyContentTypeIsRouted(string mediaType)
    {
        var content = new StringContent(RouteExamples.All[0].Body, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);

        var body = await Post(content);

        Assert.That(body.GetProperty("feasible").GetBoolean(), Is.True);
    }

    [Test]
    public async Task ValidJsonWithNoContentTypeIsRouted()
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(RouteExamples.All[0].Body));

        var body = await Post(content);

        Assert.That(body.GetProperty("feasible").GetBoolean(), Is.True);
    }

    [TestCase("/API/ROUTE", TestName = "Upper-case path")]
    [TestCase("/api/route?debug=true", TestName = "Query string is ignored")]
    [TestCase("/api/route/", TestName = "Trailing slash")]
    public async Task PathVariantsStillReach200(string path)
    {
        var body = await Post(Json(RouteExamples.All[0].Body), path);

        Assert.That(body.GetProperty("feasible").GetBoolean(), Is.True);
    }

    // ---- Always 200: other HTTP methods on the same path ----

    [TestCase("GET")]
    [TestCase("PUT")]
    [TestCase("PATCH")]
    [TestCase("DELETE")]
    [TestCase("OPTIONS")]
    [TestCase("TRACE")]
    [TestCase("PURGE", TestName = "Non-standard method")]
    public async Task OtherMethodsReturn200AskingForPost(string method)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), "/api/route");
        if (method is "PUT" or "PATCH") request.Content = Json(RouteExamples.All[0].Body);

        var body = await ReadBody(await _client.SendAsync(request));

        Assert.That(PropertyNames(body), Is.EqualTo(new[] { "feasible", "errors" }));
        Assert.That(Errors(body), Is.EqualTo(new[] { "Use POST to route an order." }));
    }

    [TestCase("GET", "/api/route/", TestName = "GET with trailing slash")]
    [TestCase("PUT", "/API/ROUTE/", TestName = "PUT with trailing slash and upper case")]
    [TestCase("DELETE", "/api/route/?x=1", TestName = "DELETE with trailing slash and query")]
    public async Task OtherMethodsOnPathVariantsReturn200(string method, string path)
    {
        var body = await ReadBody(await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)));

        Assert.That(Errors(body), Is.EqualTo(new[] { "Use POST to route an order." }));
    }

    [TestCase("/api/route")]
    [TestCase("/api/route/")]
    public async Task HeadReturns200WithNoBody(string path)
    {
        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Head, path));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadAsByteArrayAsync(), Is.Empty);
    }

    [Test]
    public async Task OtherPathsAreNotAffected()
    {
        var response = await _client.GetAsync("/api/other");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // ---- Always 200: validation and routing failures ----

    [Test]
    public async Task ValidationErrorsReturn200AndAreAllListed()
    {
        var body = await Post(Json(RouteExamples.All.Single(e => e.Key == "wrong-types").Body));

        Assert.That(body.GetProperty("feasible").GetBoolean(), Is.False);
        Assert.That(Errors(body), Has.Length.EqualTo(3));
    }

    [Test]
    public async Task NumericZipAndNullMailOrderAreAccepted()
    {
        var body = await Post(Json("""
            { "customer_zip": 10015, "mail_order": null, "items": [ { "product_code": "WC-STD-001", "quantity": 1 } ] }
            """));

        Assert.That(body.GetProperty("feasible").GetBoolean(), Is.True);
        Assert.That(body.GetProperty("routing")[0].GetProperty("items")[0].GetProperty("fulfillment_mode").GetString(),
            Is.EqualTo("local"));
    }

    [Test]
    public async Task RoutingFailureReturns200WithoutARoutingField()
    {
        var body = await Post(Json(RouteExamples.All.Single(e => e.Key == "no-supplier").Body));

        Assert.That(PropertyNames(body), Is.EqualTo(new[] { "feasible", "errors" }));
    }

    // ---- Always 200 and no internal details: unexpected errors ----

    private sealed class ThrowingRoutingService(ReferenceData data) : RoutingService(data)
    {
        public override RoutingDecision Route(Order order) =>
            throw new InvalidOperationException("SECRET-DETAIL: connection string Server=db;Password=hunter2");
    }

    [Test]
    public async Task UnexpectedErrorReturns200WithAFriendlyMessageAndNoInternalDetails()
    {
        using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<RoutingService>(sp => new ThrowingRoutingService(sp.GetRequiredService<ReferenceData>()))));
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/route", Json(RouteExamples.All[0].Body));
        var text = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(text, Is.EqualTo("""{"feasible":false,"errors":["Internal error while routing order."]}"""));
        Assert.That(text, Does.Not.Contain("SECRET").And.Not.Contain("Exception").And.Not.Contain(" at "));
    }

    // ---- Swagger ----

    [Test]
    public async Task SwaggerUiIsServed()
    {
        var response = await _client.GetAsync("/swagger/index.html");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task OpenApiDocumentDescribesTheEndpointWithEveryExample()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var operation = document.GetProperty("paths").GetProperty("/api/route").GetProperty("post");

        var requestExamples = operation.GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("examples");
        Assert.That(requestExamples.EnumerateObject().Select(e => e.Name), Is.EquivalentTo(RouteExamples.All.Select(e => e.Key)));

        var responses = operation.GetProperty("responses");
        Assert.That(responses.EnumerateObject().Select(r => r.Name), Is.EqualTo(new[] { "200" }), "Only 200 is documented.");
        var responseExamples = responses.GetProperty("200").GetProperty("content")
            .GetProperty("application/json").GetProperty("examples");
        Assert.That(responseExamples.EnumerateObject().Select(e => e.Name), Is.EquivalentTo(new[] { "success", "failure" }));
    }

    [Test]
    public async Task OpenApiDocumentDescribesTheRequestFields()
    {
        var document = JsonDocument.Parse(await _client.GetStringAsync("/swagger/v1/swagger.json")).RootElement;
        var schema = document.GetProperty("components").GetProperty("schemas").GetProperty("RouteRequest");

        Assert.That(schema.GetProperty("properties").EnumerateObject().Select(p => p.Name),
            Is.EquivalentTo(new[] { "order_id", "customer_zip", "mail_order", "items" }));
    }
}
