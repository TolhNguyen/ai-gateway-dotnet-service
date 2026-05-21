namespace AiGateway.Api.Domain;

public sealed record User
{
    public long Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string PasswordHash { get; init; } = string.Empty;
    public string Role { get; init; } = "user";       // user | admin
    public string Status { get; init; } = "active";   // active | disabled
    public string? DisplayName { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record PersonalAccessToken
{
    public long Id { get; init; }
    public long UserId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string TokenHash { get; init; } = string.Empty;
    public string TokenPrefix { get; init; } = string.Empty;
    public DateTimeOffset? LastUsedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
