using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using OrderRouter.Api.Contracts;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace OrderRouter.Api.Swagger;

/// <summary>
/// Documents the request body and adds the request and response samples to POST /api/route.
/// The body is documented here rather than with endpoint metadata such as <c>Accepts</c>,
/// because that metadata makes routing reject other content types with 415, which would break
/// the always-200 rule.
/// </summary>
public sealed class RouteOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!string.Equals(context.ApiDescription.RelativePath, RouteOrderEndpoint.Path.TrimStart('/'),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var requestSchema = context.SchemaGenerator.GenerateSchema(typeof(RouteRequest), context.SchemaRepository);

        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Description = "The order to route. Pick a sample from the Examples list.",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new()
                {
                    Schema = requestSchema,
                    Examples = RouteExamples.All.ToDictionary(
                        example => example.Key,
                        example => (IOpenApiExample)new OpenApiExample
                        {
                            Summary = example.Summary,
                            Description = example.Description,
                            Value = JsonNode.Parse(example.Body),
                        }),
                },
            },
        };

        if (operation.Responses?.TryGetValue("200", out var response) == true
            && response.Content?.TryGetValue("application/json", out var mediaType) == true)
        {
            response.Description = "Always returned, for success and failure alike. Check \"feasible\".";
            mediaType.Examples = new Dictionary<string, IOpenApiExample>
            {
                ["success"] = new OpenApiExample
                {
                    Summary = "Routed (feasible)",
                    Value = JsonNode.Parse(RouteExamples.SuccessResponse),
                },
                ["failure"] = new OpenApiExample
                {
                    Summary = "Not routed (not feasible)",
                    Value = JsonNode.Parse(RouteExamples.FailureResponse),
                },
            };
        }
    }
}
