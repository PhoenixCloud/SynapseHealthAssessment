using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using OrderRouter.Api.Contracts;
using OrderRouter.Core.Routing;

namespace OrderRouter.Api;

/// <summary>
/// POST /api/route. Always returns HTTP 200; the body's "feasible" field says whether routing
/// succeeded. Takes only <see cref="HttpContext"/>, so the framework never binds or rejects the
/// body itself: no automatic 400 or 415 responses. Error messages are always short, fixed,
/// human-friendly text. Exception details are logged, never returned.
/// </summary>
public static class RouteOrderEndpoint
{
    public const string Path = "/api/route";

    public static async Task<Ok<RoutingResult>> HandleAsync(HttpContext context)
    {
        try
        {
            string body;
            try
            {
                using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
                body = await reader.ReadToEndAsync(context.RequestAborted);
            }
            catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                Logger(context).LogWarning("Request body for {Path} exceeded the server's size limit.", Path);
                return TypedResults.Ok(RoutingResult.Failure([RoutingErrors.BodyTooLarge]));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger(context).LogWarning(ex, "Could not read the request body for {Path}.", Path);
                return TypedResults.Ok(RoutingResult.Failure([RoutingErrors.UnreadableBody]));
            }

            var parsed = OrderRequestParser.Parse(body);
            if (!parsed.IsValid)
            {
                return TypedResults.Ok(RoutingResult.Failure(parsed.Errors));
            }

            var service = context.RequestServices.GetRequiredService<RoutingService>();
            var decision = service.Route(parsed.Order!);
            return TypedResults.Ok(RoutingResult.From(decision));
        }
        catch (Exception ex)
        {
            Logger(context).LogError(ex, "Unexpected error while routing an order.");
            return TypedResults.Ok(RoutingResult.Failure([RoutingErrors.InternalError]));
        }
    }

    /// <summary>
    /// Keeps every response on /api/route at HTTP 200:
    /// <list type="bullet">
    /// <item>Any method other than POST (GET, PUT, HEAD, custom methods...) gets a 200 failure
    /// body saying to use POST, instead of ASP.NET's 405.</item>
    /// <item>For POST, anything thrown outside the handler's own try/catch (for example while
    /// writing the response) becomes the same 200 failure body, if nothing has been sent yet.</item>
    /// </list>
    /// Other paths pass through untouched.
    /// </summary>
    public static async Task AlwaysOkMiddleware(HttpContext context, RequestDelegate next)
    {
        if (!IsRoutePath(context.Request.Path))
        {
            await next(context);
            return;
        }

        if (!HttpMethods.IsPost(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            if (HttpMethods.IsHead(context.Request.Method))
            {
                // HTTP forbids a body on HEAD responses: status and headers only.
                context.Response.ContentType = "application/json; charset=utf-8";
                return;
            }

            await context.Response.WriteAsJsonAsync(RoutingResult.Failure([RoutingErrors.UsePost]));
            return;
        }

        try
        {
            await next(context);
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            Logger(context).LogError(ex, "Unexpected error in the {Path} pipeline.", Path);
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsJsonAsync(RoutingResult.Failure([RoutingErrors.InternalError]));
        }
    }

    /// <summary>
    /// The paths ASP.NET routing sends to this endpoint: "/api/route" and "/api/route/",
    /// ignoring case. Both must be covered, or a request like GET /api/route/ would get a 405.
    /// </summary>
    public static bool IsRoutePath(PathString path) =>
        path.Equals(Path, StringComparison.OrdinalIgnoreCase)
        || path.Equals(Path + "/", StringComparison.OrdinalIgnoreCase);

    private static ILogger Logger(HttpContext context) =>
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(RouteOrderEndpoint).FullName!);
}
