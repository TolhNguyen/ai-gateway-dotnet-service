namespace AiGateway.Api.Options;

public sealed class AdminAuthOptions
{
    public bool Enabled { get; init; } = true;

    public string? ApiKeyHash { get; init; }

    public bool AllowInDevelopmentWithoutKey { get; init; } = false;
}
