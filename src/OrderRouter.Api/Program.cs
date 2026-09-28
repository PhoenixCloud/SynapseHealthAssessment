using Microsoft.OpenApi;
using OrderRouter.Api;
using OrderRouter.Api.Swagger;
using OrderRouter.Core.Data;
using OrderRouter.Core.Domain;
using OrderRouter.Core.Routing;

var builder = WebApplication.CreateBuilder(args);

// Reference data: DATA_DIR if set, otherwise the app folder, where the CSVs are copied at build.
var dataDirectory = builder.Configuration["DATA_DIR"] is { Length: > 0 } configured
    ? configured
    : AppContext.BaseDirectory;
var loaded = ReferenceDataLoader.Load(dataDirectory);

builder.Services.AddSingleton<ReferenceData>(loaded.Data);
builder.Services.AddSingleton<RoutingService>(sp => new RoutingService(sp.GetRequiredService<ReferenceData>()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Multi-Supplier Order Router",
        Version = "v1",
        Description = "Routes an order to suppliers. POST /api/route always returns HTTP 200; " +
                      "the \"feasible\" field says whether routing succeeded.",
    });
    options.OperationFilter<RouteOperationFilter>();
});

var app = builder.Build();

app.Logger.LogInformation("Loaded {Suppliers} suppliers and {Products} products from {Directory}.",
    loaded.Data.Suppliers.Count, loaded.Data.Products.Count, dataDirectory);
foreach (var warning in loaded.Warnings)
{
    app.Logger.LogWarning("Reference data: {Warning}", warning.ToString());
}

// Swagger is on in every environment so the container can be tested by hand.
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Order Router v1");
    options.RoutePrefix = "swagger";
    options.DocumentTitle = "Order Router";
    options.EnableTryItOutByDefault();
    options.DisplayRequestDuration();
});

app.Use(RouteOrderEndpoint.AlwaysOkMiddleware);

// Cast to Delegate: a handler taking only HttpContext would otherwise bind as a raw
// RequestDelegate, which discards the returned result and writes an empty body.
app.MapPost(RouteOrderEndpoint.Path, (Delegate)RouteOrderEndpoint.HandleAsync)
    .WithName("RouteOrder")
    .WithTags("Routing")
    .WithSummary("Route an order to suppliers")
    .WithDescription(
        "Always returns HTTP 200. On success \"feasible\" is true and \"routing\" lists each supplier " +
        "with its items. On failure \"feasible\" is false and \"errors\" lists every problem. " +
        "Pick a request from the Examples list to try it.");

app.Run();

/// <summary>Exposed for WebApplicationFactory in the endpoint tests.</summary>
public partial class Program;
