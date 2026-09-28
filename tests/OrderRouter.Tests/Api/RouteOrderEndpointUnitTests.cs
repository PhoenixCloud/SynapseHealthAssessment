using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using OrderRouter.Api;
using OrderRouter.Api.Contracts;
using OrderRouter.Core.Domain;
using OrderRouter.Core.Routing;

namespace OrderRouter.Tests.Api;

/// <summary>Failure paths that are hard to trigger over HTTP: unreadable bodies and pipeline errors.</summary>
public class RouteOrderEndpointUnitTests
{
    private static DefaultHttpContext Context(Stream body)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(new RoutingService(new ReferenceData([], [])))
            .BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = RouteOrderEndpoint.Path;
        context.Request.Body = body;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private sealed class FailingStream : MemoryStream
    {
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            throw new IOException("SECRET-DETAIL: socket reset");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new IOException("SECRET-DETAIL: socket reset");

        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("SECRET-DETAIL: socket reset");
    }

    [Test]
    public async Task UnreadableBodyGivesAFriendlyFailure()
    {
        Ok<RoutingResult> result = await RouteOrderEndpoint.HandleAsync(Context(new FailingStream()));

        Assert.That(result.StatusCode, Is.EqualTo(200));
        Assert.That(result.Value!.Feasible, Is.False);
        Assert.That(result.Value.Errors, Is.EqualTo(new[] { "Request body could not be read." }));
    }

    private sealed class TooLargeStream : MemoryStream
    {
        private static Exception TooLarge() =>
            new BadHttpRequestException("Request body too large. The max request body size is 30000000 bytes.", 413);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => throw TooLarge();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw TooLarge();
        public override int Read(byte[] buffer, int offset, int count) => throw TooLarge();
    }

    [Test]
    public async Task OversizedBodyGivesAFriendlyFailure()
    {
        var result = await RouteOrderEndpoint.HandleAsync(Context(new TooLargeStream()));

        Assert.That(result.StatusCode, Is.EqualTo(200));
        Assert.That(result.Value!.Errors, Is.EqualTo(new[] { "Request body is too large." }));
    }

    [TestCase("/api/route", true)]
    [TestCase("/api/route/", true)]
    [TestCase("/API/Route/", true)]
    [TestCase("/api/route//", false)]
    [TestCase("/api/routes", false)]
    [TestCase("/api", false)]
    public void RoutePathMatching(string path, bool expected) =>
        Assert.That(RouteOrderEndpoint.IsRoutePath(path), Is.EqualTo(expected));

    [Test]
    public async Task InvalidUtf8IsTreatedAsInvalidJson()
    {
        var result = await RouteOrderEndpoint.HandleAsync(Context(new MemoryStream([0xFF, 0xFE, 0x00, 0x7B])));

        Assert.That(result.Value!.Errors, Is.EqualTo(new[] { "Request body must be a JSON object." }));
    }

    [Test]
    public async Task MiddlewareTurnsAPipelineErrorInto200()
    {
        var context = Context(new MemoryStream());

        await RouteOrderEndpoint.AlwaysOkMiddleware(context,
            _ => throw new InvalidOperationException("SECRET-DETAIL: stack goes here"));

        context.Response.Body.Position = 0;
        var text = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();
        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(text, Is.EqualTo("""{"feasible":false,"errors":["Internal error while routing order."]}"""));
    }

    [Test]
    public void MiddlewareDoesNotTouchOtherRequests()
    {
        var context = Context(new MemoryStream());
        context.Request.Path = "/swagger/index.html";
        context.Request.Method = HttpMethods.Get;

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            RouteOrderEndpoint.AlwaysOkMiddleware(context, _ => throw new InvalidOperationException()));
    }

    [Test]
    public async Task MiddlewarePassesSuccessfulResponsesThrough()
    {
        var context = Context(new MemoryStream());

        await RouteOrderEndpoint.AlwaysOkMiddleware(context, ctx =>
        {
            ctx.Response.StatusCode = 200;
            return ctx.Response.WriteAsync("ok");
        });

        context.Response.Body.Position = 0;
        Assert.That(await new StreamReader(context.Response.Body).ReadToEndAsync(), Is.EqualTo("ok"));
    }

    [Test]
    public void ResultForAFailedDecisionHasNoRouting()
    {
        var result = RoutingResult.From(RoutingDecision.Failure(["x"]));

        Assert.That(result.Routing, Is.Null);
        Assert.That(JsonSerializer.Serialize(result), Is.EqualTo("""{"feasible":false,"errors":["x"]}"""));
    }

    [Test]
    public void ResultForASuccessfulDecisionHasNoErrors()
    {
        var decision = RoutingDecision.Success(
            [new Shipment("SUP-1", "Acme", [new RoutedItem("WC-1", 2, "wheelchair", FulfillmentMode.MailOrder)])]);

        Assert.That(JsonSerializer.Serialize(RoutingResult.From(decision)), Is.EqualTo(
            """{"feasible":true,"routing":[{"supplier_id":"SUP-1","supplier_name":"Acme","items":[{"product_code":"WC-1","quantity":2,"category":"wheelchair","fulfillment_mode":"mail_order"}]}]}"""));
    }
}
