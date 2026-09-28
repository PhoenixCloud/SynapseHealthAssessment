namespace OrderRouter.Core.Domain;

internal static class Guard
{
    public static string NotBlank(string? value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{name} must not be blank.", name)
            : value;
}
