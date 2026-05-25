using System.ComponentModel.DataAnnotations;

namespace AiGateway.Api.Contracts;

public sealed record RegisterRequest
{
    [Required, EmailAddress, StringLength(255)]
    public required string Email { get; init; }

    [Required, StringLength(128, MinimumLength = 8)]
    public required string Password { get; init; }

    [StringLength(255)]
    public string? DisplayName { get; init; }
}

public sealed record LoginRequest
{
    [Required, EmailAddress, StringLength(255)]
    public required string Email { get; init; }

    [Required, StringLength(128, MinimumLength = 1)]
    public required string Password { get; init; }
}

public sealed record LoginResponse
{
    public required string AccessToken { get; init; }
    public required string TokenType { get; init; }
    public required int ExpiresInSeconds { get; init; }
    public required UserDto User { get; init; }
}

public sealed record UserDto
{
    public long Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string Role { get; init; } = "user";
    public string Status { get; init; } = "active";
    public string? DisplayName { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record CreatePatRequest
{
    [Required, StringLength(255, MinimumLength = 1)]
    public required string Name { get; init; }

    [Range(0, 3650)]
    public int? ExpiresInDays { get; init; }

    public bool UseCaveman { get; init; }
}

public sealed record CreatePatResponse
{
    public required long Id { get; init; }
    public required string Name { get; init; }
    /// <summary>Raw token. Returned ONLY at creation.</summary>
    public required string Token { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public bool UseCaveman { get; init; }
    public string ResponseStyle { get; init; } = "normal";
}

public sealed record PatDto
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string TokenPrefix { get; init; } = string.Empty;
    public DateTimeOffset? LastUsedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public bool UseCaveman { get; init; }
    public string ResponseStyle { get; init; } = "normal";
}
